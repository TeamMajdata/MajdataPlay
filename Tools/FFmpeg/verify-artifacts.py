#!/usr/bin/env python3
"""Verify staged FFmpeg SHA256, target machine, Android load alignment and host ABI."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import struct

ROOT = Path(__file__).resolve().parents[2]
NATIVE = ROOT / 'Assets/Plugins/MajdataPlay/FFmpeg/Native'
HOST_TARGET = {'Windows':'win-x64', 'Linux':'linux-x64'}.get(platform.system()) if struct.calcsize('P') == 8 else None
if platform.system() == 'Darwin':
    HOST_TARGET = 'macos-arm64' if platform.machine() == 'arm64' else 'macos-x64'


def verify_macho(data, target, path, require_platform=True):
    assert data[:4] == b'\xcf\xfa\xed\xfe', (path, 'expected little-endian 64-bit Mach-O')
    expected = 0x1000007 if target.endswith('-x64') else 0x100000c
    assert struct.unpack_from('<I', data, 4)[0] == expected, (path, 'wrong Mach-O CPU')
    # SDK platform tags distinguish arm64 iOS device and simulator archives.
    expected_platform = 7 if target.startswith('ios-simulator-') else (2 if target.startswith('ios-') else 1)
    offset, commands, found = 32, struct.unpack_from('<I', data, 16)[0], False
    for _ in range(commands):
        kind, size = struct.unpack_from('<II', data, offset)
        assert size >= 8 and offset + size <= len(data), (path, 'invalid Mach-O load command')
        if kind == 0x32: # LC_BUILD_VERSION
            assert struct.unpack_from('<I', data, offset + 8)[0] == expected_platform, (path, 'wrong Apple SDK platform')
            found = True
        elif kind in (0x24, 0x25): # Legacy MACOSX / IPHONEOS deployment command.
            assert (kind == 0x24) == (expected_platform == 1), (path, 'wrong Apple deployment platform')
            found = True
        offset += size
    if require_platform:
        assert found, (path, 'missing Apple deployment target')
    return found

def verify_binary(path, target):
    data = path.read_bytes()
    if target.startswith('win-'):
        assert data[:2] == b'MZ', path
        pe_offset = struct.unpack_from('<I', data, 0x3c)[0]
        assert data[pe_offset:pe_offset+4] == b'PE\0\0', path
        machine = struct.unpack_from('<H', data, pe_offset+4)[0]
        assert machine == (0x8664 if target == 'win-x64' else 0x14c), (path,machine)
    elif target.startswith(('linux-', 'android-')):
        assert data[:4] == b'\x7fELF' and data[5] == 1, path
        expected = {'linux-x64':(2,62),'android-arm64':(2,183),'android-armv7':(1,40)}[target]
        assert (data[4],struct.unpack_from('<H',data,18)[0]) == expected, path
        if target.startswith('android-'):
            if data[4] == 2:
                phoff = struct.unpack_from('<Q',data,32)[0]
                phsize, phnum = struct.unpack_from('<HH',data,54)
                align_offset, align_format = 48, '<Q'
            else:
                phoff = struct.unpack_from('<I',data,28)[0]
                phsize, phnum = struct.unpack_from('<HH',data,42)
                align_offset, align_format = 28, '<I'
            for index in range(phnum):
                header = phoff + index * phsize
                if struct.unpack_from('<I',data,header)[0] == 1: # PT_LOAD
                    assert struct.unpack_from(align_format,data,header+align_offset)[0] >= 16384, path
    elif target.startswith(('macos-', 'ios-')):
        if data.startswith(b'!<arch>\n'):
            offset, objects, platform_objects = 8, 0, 0
            while offset + 60 <= len(data):
                header = data[offset:offset + 60]
                assert header[58:60] == b'`\n', (path, 'invalid ar member')
                length = int(header[48:58].strip())
                content = data[offset + 60:offset + 60 + length]
                if header[:3] == b'#1/':
                    content = content[int(header[3:16].strip()):]
                if content[:4] == b'\xcf\xfa\xed\xfe':
                    # NASM Mach-O objects have no platform command; their CPU
                    # must still match and the C/C++ archive members supply SDK tags.
                    platform_objects += bool(verify_macho(content, target, path, require_platform=False))
                    objects += 1
                offset += 60 + length + (length & 1)
            assert objects > 0, (path, 'archive contains no Mach-O objects')
            assert platform_objects > 0, (path, 'archive contains no tagged Apple SDK objects')
        else:
            verify_macho(data, target, path)

def verify_hardware_backends(directory, manifest):
    """Inspect exported decoder configurations without requiring a GPU or creating devices."""
    if not manifest.get('verifiedHardwareBackends'):
        return
    target = manifest['target']
    def filename(name):
        major = manifest['source']['abi'][name]
        return f'{name}-{major}.dll' if target.startswith('win-') else f'lib{name}.so.{major}'
    avutil = ctypes.CDLL(str(directory / filename('avutil')))
    avcodec = ctypes.CDLL(str(directory / filename('avcodec')))

    class Configuration(ctypes.Structure):
        _fields_ = [('pixel_format', ctypes.c_int), ('methods', ctypes.c_int), ('device_type', ctypes.c_int)]

    avutil.av_hwdevice_find_type_by_name.argtypes = [ctypes.c_char_p]
    avutil.av_hwdevice_find_type_by_name.restype = ctypes.c_int
    avcodec.avcodec_find_decoder_by_name.argtypes = [ctypes.c_char_p]
    avcodec.avcodec_find_decoder_by_name.restype = ctypes.c_void_p
    avcodec.avcodec_get_hw_config.argtypes = [ctypes.c_void_p, ctypes.c_int]
    avcodec.avcodec_get_hw_config.restype = ctypes.POINTER(Configuration)
    for backend in ['vulkan'] + (['d3d12va'] if target.startswith('win-') else []):
        device_type = avutil.av_hwdevice_find_type_by_name(backend.encode())
        assert device_type > 0, (target, backend, 'hardware device type missing')
        for codec in ['h264', 'hevc', 'av1', 'vp9']:
            decoder = avcodec.avcodec_find_decoder_by_name(codec.encode())
            assert decoder, (target, codec, 'decoder missing')
            index, found = 0, False
            while True:
                configuration = avcodec.avcodec_get_hw_config(decoder, index)
                if not configuration:
                    break
                if configuration.contents.device_type == device_type and configuration.contents.methods & 1:
                    found = True
                index += 1
            assert found, (target, codec, backend, 'hardware decoder configuration missing')
    print(f'PASS {target}: H264/HEVC/AV1/VP9 native Vulkan' + (' and D3D12VA' if target.startswith('win-') else '') + ' decoder configurations exported')


def verify_software_decoders(directory, manifest):
    decoders = manifest.get('softwareDecoders', [])
    if not decoders:
        return
    target, major = manifest['target'], manifest['source']['abi']['avcodec']
    name = f'avcodec-{major}.dll' if target.startswith('win-') else (
        f'libavcodec.{major}.dylib' if target.startswith('macos-') else f'libavcodec.so.{major}')
    library = ctypes.CDLL(str(directory / name))
    library.avcodec_find_decoder_by_name.argtypes = [ctypes.c_char_p]
    library.avcodec_find_decoder_by_name.restype = ctypes.c_void_p
    for decoder in decoders:
        assert library.avcodec_find_decoder_by_name(decoder.encode()), (target, decoder, 'software decoder missing')
    print(f'PASS {target}: software decoders exported: ' + ', '.join(decoders))


def main():
    count = 0
    manifests = list(NATIVE.rglob('build-manifest.json'))
    simulator_root = Path(os.environ.get('FFMPEG_BUILD_ROOT', str(ROOT / 'Tools/FFmpeg/.build'))) / 'artifacts'
    if simulator_root.exists():
        manifests += list(simulator_root.rglob('build-manifest.json'))
    for manifest_file in sorted(manifests):
        manifest = json.loads(manifest_file.read_text(encoding='utf-8'))
        target = manifest['target']
        directory = manifest_file.parent
        host = target == HOST_TARGET
        handle = os.add_dll_directory(str(directory)) if host and os.name == 'nt' else None
        try:
            for item in manifest.get('buildDependencyLicenses', []):
                license_file = directory / item['file']
                assert hashlib.sha256(license_file.read_bytes()).hexdigest() == item['sha256'], license_file
            for item in manifest['files']:
                file = directory / item['file']
                assert hashlib.sha256(file.read_bytes()).hexdigest() == item['sha256'], file
                assert file.stat().st_size == item['bytes'], file
                if 'simulator' not in target:
                    assert Path(str(file)+'.meta').is_file(), file
                verify_binary(file,target)
                if host:
                    library = ctypes.CDLL(str(file))
                    name = next(name for name in manifest['source']['abi'] if name in file.name)
                    version_function = getattr(library,name+'_version')
                    version_function.restype = ctypes.c_uint
                    assert version_function() >> 16 == manifest['source']['abi'][name], file
                count += 1
            if host:
                verify_hardware_backends(directory, manifest)
                verify_software_decoders(directory, manifest)
        finally:
            if handle:
                handle.close()
        print(f'PASS {target}: {len(manifest["files"])} SHA256/machine/importer checks' + ('; native ABI loaded' if host else ''))
    assert count, 'No staged FFmpeg libraries found'
    for manifest_file in sorted(NATIVE.rglob('dependency-manifest.json')):
        manifest = json.loads(manifest_file.read_text(encoding='utf-8'))
        for item in manifest['files']:
            file = manifest_file.parent / item['file']
            assert hashlib.sha256(file.read_bytes()).hexdigest() == item['sha256'], file
            assert file.stat().st_size == item['bytes'], file
            assert Path(str(file) + '.meta').is_file(), file
            verify_binary(file, manifest['target'])
            license_file = manifest_file.parent / item['license']
            assert hashlib.sha256(license_file.read_bytes()).hexdigest() == item['licenseSha256'], license_file
        print(f'PASS {manifest["target"]}: {len(manifest["files"])} runtime dependency/license checks')
    bridges = list(NATIVE.rglob('bridge-manifest.json'))
    if simulator_root.exists():
        bridges += list(simulator_root.rglob('bridge-manifest.json'))
    for manifest_file in sorted(bridges):
        manifest = json.loads(manifest_file.read_text(encoding='utf-8'))
        target = manifest.get('target')
        if not target and manifest.get('platform') == 'Windows':
            target = 'win-x64' if manifest['architecture'] == 'x86_64' else 'win-x86'
        assert target, (manifest_file, 'missing bridge target')
        expected_abi = 2 if target.startswith(('macos-', 'ios-')) else 4
        if 'bridgeAbi' in manifest:
            assert manifest['bridgeAbi'] == expected_abi, (manifest_file, 'unexpected bridge ABI')
        items = manifest.get('files') or [{'file': manifest['artifact'], 'sha256': manifest['sha256']}]
        directory = manifest_file.parent
        handle = os.add_dll_directory(str(directory)) if target == HOST_TARGET and os.name == 'nt' else None
        try:
            for item in items:
                file = directory / item['file']
                assert hashlib.sha256(file.read_bytes()).hexdigest() == item['sha256'], file
                if 'bytes' in item:
                    assert file.stat().st_size == item['bytes'], file
                verify_binary(file, target)
                if target == HOST_TARGET:
                    library = ctypes.CDLL(str(file))
                    library.ffu_abi_version.restype = ctypes.c_int
                    assert library.ffu_abi_version() == expected_abi, (file, 'loaded bridge ABI mismatch')
        finally:
            if handle:
                handle.close()
        print(f'PASS {target}: bridge SHA256/machine' + (f'; native ABI {expected_abi} loaded' if target == HOST_TARGET else ''))
    print(f'PASS {count} FFmpeg libraries; PE/ELF/Mach-O machine and Apple SDK platform verified; Android PT_LOAD alignment >=16 KiB verified.')

if __name__ == '__main__':
    main()

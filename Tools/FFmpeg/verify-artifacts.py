#!/usr/bin/env python3
"""Verify staged FFmpeg SHA256, target machine, Android load alignment and host ABI."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import struct

ROOT = Path(__file__).resolve().parents[2]
NATIVE = ROOT / 'Assets/Plugins/MajdataPlay/FFmpeg/Native'
HOST_TARGET = {'Windows':'win-x64', 'Linux':'linux-x64'}.get(platform.system()) if struct.calcsize('P') == 8 else None
PATCH_MARKERS = {
    'patches/amf-rate-control.patch': 'MajdataPlay-AMF-RC-v1-',
    'patches/x265-parameter-check.patch': 'MajdataPlay-X265-Params-v1-',
}
SOFTWARE_ENCODERS = {'x264': 'libx264', 'x265': 'libx265', 'libvpx': 'libvpx-vp9', 'libaom': 'libaom-av1'}
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
        # Compiler DLLs can be accidentally satisfied by a developer's PATH.
        # The build recipe statically links the C++/pthread compiler runtime.
        imports = pe_import_names(data, path)
        compiler_dlls = [name for name in imports if re.fullmatch(r'lib(?:stdc\+\+|gcc_s_[a-z0-9_]+|winpthread|c\+\+(?:abi)?)(?:-[0-9]+)?\.dll', name, re.IGNORECASE)]
        assert not compiler_dlls, (path, 'unexpected dynamic compiler runtime dependencies', compiler_dlls)
    elif target.startswith(('linux-', 'android-')):
        assert data[:4] == b'\x7fELF' and data[5] == 1, path
        expected = {'linux-x64':(2,62),'android-arm64':(2,183),'android-armv7':(1,40)}[target]
        assert (data[4],struct.unpack_from('<H',data,18)[0]) == expected, path
        dependencies = elf_needed_names(data, path)
        compiler_runtimes = [name for name in dependencies if re.fullmatch(r'lib(?:stdc\+\+|c\+\+(?:_shared|abi)?|unwind)\.so(?:\.[0-9.]+)?', name)]
        assert not compiler_runtimes, (path, 'unexpected dynamic C++ compiler runtime dependencies', compiler_runtimes)
        if target.startswith('android-'):
            # Android delivers static C++/unwind archives. Every remaining
            # dependency must be an NDK system library or a shipped FFmpeg SO.
            system_libraries = {'libc.so', 'libdl.so', 'libm.so', 'liblog.so', 'libandroid.so', 'libmediandk.so',
                                'libvulkan.so', 'libEGL.so', 'libGLESv2.so', 'libGLESv3.so', 'libz.so', 'libOpenSLES.so'}
            unexpected = [name for name in dependencies if name not in system_libraries
                          and not re.fullmatch(r'lib(?:avcodec|avdevice|avfilter|avformat|avutil|swresample|swscale)\.so', name)]
            assert not unexpected, (path, 'unexpected Android non-system runtime dependencies', unexpected)
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


def pe_import_names(data, path):
    """Read imported DLL names directly without resolving dependencies from the host PATH."""
    pe = struct.unpack_from('<I', data, 0x3c)[0]
    section_count = struct.unpack_from('<H', data, pe + 6)[0]
    optional_size = struct.unpack_from('<H', data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from('<H', data, optional)[0]
    assert magic in (0x10b, 0x20b), (path, 'invalid PE optional header')
    directories = optional + (112 if magic == 0x20b else 96)
    import_rva = struct.unpack_from('<I', data, directories + 8)[0]
    if import_rva == 0:
        return []
    def file_offset(rva):
        for index in range(section_count):
            header = optional + optional_size + index * 40
            virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from('<IIII', data, header + 8)
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                offset = raw_pointer + rva - virtual_address
                assert offset < len(data), (path, 'PE import RVA outside the file')
                return offset
        raise AssertionError((path, 'PE import RVA outside sections', rva))
    names = []
    descriptor = file_offset(import_rva)
    while True:
        assert descriptor + 20 <= len(data), (path, 'truncated PE import descriptor')
        if not any(data[descriptor:descriptor + 20]):
            return names
        name_offset = file_offset(struct.unpack_from('<I', data, descriptor + 12)[0])
        terminator = data.find(b'\0', name_offset)
        assert terminator >= 0, (path, 'unterminated PE import name')
        names.append(data[name_offset:terminator].decode('ascii'))
        descriptor += 20


def elf_needed_names(data, path):
    """Read DT_NEEDED from either ELF class without executing foreign architecture code."""
    is_64 = data[4] == 2
    phoff = struct.unpack_from('<Q' if is_64 else '<I', data, 32 if is_64 else 28)[0]
    phsize, phnum = struct.unpack_from('<HH', data, 54 if is_64 else 42)
    loads, dynamic = [], None
    for index in range(phnum):
        header = phoff + index * phsize
        kind = struct.unpack_from('<I', data, header)[0]
        offset = struct.unpack_from('<Q' if is_64 else '<I', data, header + (8 if is_64 else 4))[0]
        address = struct.unpack_from('<Q' if is_64 else '<I', data, header + (16 if is_64 else 8))[0]
        size = struct.unpack_from('<Q' if is_64 else '<I', data, header + (32 if is_64 else 16))[0]
        assert offset + size <= len(data), (path, 'ELF segment outside the file')
        if kind == 1:
            loads.append((address, size, offset))
        elif kind == 2:
            dynamic = (offset, size)
    if dynamic is None:
        return []
    string_table, names = None, []
    entry_size = 16 if is_64 else 8
    for offset in range(dynamic[0], dynamic[0] + dynamic[1], entry_size):
        tag, value = struct.unpack_from('<QQ' if is_64 else '<II', data, offset)
        if tag == 0:
            break
        if tag == 1:
            names.append(value)
        elif tag == 5:
            string_table = value
    if not names:
        return []
    assert string_table is not None, (path, 'ELF dependencies without a string table')
    string_offset = next((offset + string_table - address for address, size, offset in loads
                          if address <= string_table < address + size), None)
    assert string_offset is not None, (path, 'ELF dependency string table outside load segments')
    dependencies = []
    for name in names:
        start = string_offset + name
        terminator = data.find(b'\0', start)
        assert terminator >= 0, (path, 'unterminated ELF dependency name')
        dependencies.append(data[start:terminator].decode('ascii'))
    return dependencies


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


def verify_source_patches(directory, manifest):
    """Verify exact reviewed patch bytes and the combined configure capability marker."""
    target = manifest['target']
    patches = manifest.get('sourcePatches', [])
    identities = [patch['file'] for patch in patches]
    assert len(set(identities)) == len(identities), (target, 'duplicate source patch')
    for patch in patches:
        assert patch['file'] in PATCH_MARKERS, (target, 'unknown source patch')
        source_patch = ROOT / 'Tools/FFmpeg' / patch['file']
        assert patch['stagedFile'] == source_patch.name, (target, 'invalid staged source patch path')
        staged_patch = directory / patch['stagedFile']
        assert hashlib.sha256(source_patch.read_bytes()).hexdigest() == patch['sha256'], source_patch
        assert hashlib.sha256(staged_patch.read_bytes()).hexdigest() == patch['sha256'], staged_patch
        assert patch['versionMarker'] == PATCH_MARKERS[patch['file']] + patch['sha256'][:12], (target, 'source patch marker mismatch')
        if 'simulator' not in target:
            assert Path(str(staged_patch) + '.meta').is_file(), staged_patch
    if patches:
        extra_versions = [argument for argument in manifest['configure'] if argument.startswith('--extra-version=')]
        expected = '--extra-version=' + '_'.join(patch['versionMarker'] for patch in patches)
        assert extra_versions == [expected], (target, 'combined source patch build marker mismatch')
        codec_items = [item for item in manifest['files'] if 'avcodec' in item['file']]
        assert len(codec_items) == 1, (target, 'missing unique avcodec artifact for source patch verification')
        codec_bytes = (directory / codec_items[0]['file']).read_bytes()
        for patch in patches:
            assert patch['versionMarker'].encode('utf-8') in codec_bytes, (target, patch['file'], 'native artifact source patch marker missing')
    encoders = manifest.get('recordingProfile', {}).get('encoders', [])
    if any(encoder.endswith('_amf') for encoder in encoders):
        assert 'patches/amf-rate-control.patch' in identities, (target, 'checked AMF source patch provenance missing')
    if 'libx265' in encoders:
        assert 'patches/x265-parameter-check.patch' in identities, (target, 'checked x265 source patch provenance missing')


def verify_software_encoder_provenance(directory, manifest):
    """Verify the full software profile and redistributable pinned dependency notices."""
    builds = manifest.get('softwareEncoderBuilds')
    if builds is None:
        return
    target = manifest['target']
    ffmpeg_lock = json.loads((ROOT / 'Tools/FFmpeg/ffmpeg.lock.json').read_text(encoding='utf-8'))
    for field in ['repository', 'tag', 'commit']:
        assert manifest['source'][field] == ffmpeg_lock[field], (target, 'FFmpeg source identity does not match license pins')
    lock = json.loads((ROOT / 'Tools/FFmpeg/dependencies.lock.json').read_text(encoding='utf-8'))['softwareEncoders']
    assert set(builds) == set(SOFTWARE_ENCODERS), (target, 'incomplete software encoder build provenance')
    assert manifest.get('license') == 'GPL-3.0-or-later', (target, 'full encoder profile requires GPL-3.0-or-later')
    assert manifest.get('buildDependencies', {}).get('softwareEncoders') == lock, (target, 'software dependency lock mismatch')
    encoders = manifest.get('recordingProfile', {}).get('encoders', [])
    assert {'mpeg4', *SOFTWARE_ENCODERS.values()} <= set(encoders), (target, 'incomplete software recording profile')
    for option in ['--enable-gpl', '--enable-libx264', '--enable-libx265', '--enable-libvpx', '--enable-libaom']:
        assert option in manifest['configure'], (target, option, 'software encoder configure flag missing')
    licenses = {item['file']: item for item in manifest.get('buildDependencyLicenses', [])}
    for name, build in builds.items():
        spec = lock[name]
        assert build['source'] == spec, (target, name, 'software encoder source identity mismatch')
        assert build['static'] is True and build['pic'] is True, (target, name, 'software encoder must be static PIC')
        archive_name = (name if name.startswith('lib') else 'lib' + name) + '.a'
        assert build['archive'] == archive_name, (target, name, 'invalid software encoder archive name')
        assert build['bytes'] > 0 and len(build['sha256']) == 64 and all(character in '0123456789abcdef' for character in build['sha256']), (target, name, 'invalid static archive provenance')
        assert isinstance(build['configure'], list) and build['configure'], (target, name, 'missing encoder configure recipe')
        item = licenses.get(name + '.LICENSE.txt')
        assert item and item['license'] == spec['license'] and item['sourceCommit'] == spec['commit'], (target, name, 'software encoder license provenance mismatch')
        file = directory / item['file']
        contents = file.read_text(encoding='utf-8')
        assert contents.startswith(name + ' ' + spec['version'] + '\n' + spec['repository'] + '\nCommit: ' + spec['commit'] + '\n'), (target, file, 'software encoder license source identity mismatch')
        for source_license in spec['licenseFiles']:
            assert '\n--- ' + source_license + ' ---\n' in contents, (target, name, source_license, 'redistribution notice omitted')
        if 'simulator' not in target:
            assert Path(str(file) + '.meta').is_file(), file
    assert builds['x265'].get('cxxRuntimeLink'), (target, 'x265 C++ runtime link recipe missing')
    if target.startswith(('win-', 'linux-', 'android-')):
        runtime = licenses.get('compiler-runtime.LICENSE.txt')
        assert runtime and runtime.get('runtimeArchives') and runtime.get('sourceNotices'), (target, 'static compiler runtime attribution missing')
        runtime_file = directory / runtime['file']
        contents = runtime_file.read_text(encoding='utf-8')
        assert hashlib.sha256(runtime_file.read_bytes()).hexdigest() == runtime['sha256'], runtime_file
        archive_names = {archive['file'] for archive in runtime['runtimeArchives']}
        assert archive_names & {'libstdc++.a', 'libc++.a', 'libc++_static.a'}, (target, 'static C++ archive attribution missing')
        for archive in runtime['runtimeArchives']:
            assert archive['bytes'] > 0 and re.fullmatch(r'[0-9a-f]{64}', archive['sha256']), (target, 'invalid compiler archive provenance')
            assert archive['file'] + ' SHA256 ' + archive['sha256'] in contents, (target, 'compiler notice/archive hash mismatch')
        for notice in runtime['sourceNotices']:
            assert re.fullmatch(r'[0-9a-f]{64}', notice['sha256']), (target, 'invalid compiler source notice provenance')
            assert '\n--- ' + notice['file'] + ' ---\n' in contents, (target, 'selected compiler notice omitted')
        assert Path(str(runtime_file) + '.meta').is_file(), runtime_file
    for name in ['COPYING.GPLv2', 'COPYING.GPLv3', 'LICENSE.md']:
        file = directory / name
        assert file.is_file() and file.stat().st_size > 0, (target, name, 'GPL redistribution license missing')
        if name in ffmpeg_lock['licenseSha256']:
            assert hashlib.sha256(file.read_bytes()).hexdigest() == ffmpeg_lock['licenseSha256'][name], (target, name, 'GPL full text does not match pinned FFmpeg source')
        if 'simulator' not in target:
            assert Path(str(file) + '.meta').is_file(), file
    assert 'Version 3, 29 June 2007' in (directory / 'COPYING.GPLv3').read_text(encoding='utf-8'), (target, 'GPLv3 license contents missing')
    print(f'PASS {target}: four pinned static PIC software encoders and GPLv3/dependency redistribution notices verified')


def verify_recording_profile(directory, manifest):
    """Check encoder/muxer registration without claiming device or recording support."""
    profile = manifest.get('recordingProfile')
    if not profile:
        return
    target = manifest['target']
    def filename(name):
        major = manifest['source']['abi'][name]
        return f'{name}-{major}.dll' if target.startswith('win-') else (
            f'lib{name}.{major}.dylib' if target.startswith('macos-') else f'lib{name}.so.{major}')
    codec = ctypes.CDLL(str(directory / filename('avcodec')))
    if manifest.get('softwareEncoderBuilds') is not None:
        codec.avcodec_license.restype = ctypes.c_char_p
        assert codec.avcodec_license().decode('utf-8') == 'GPL version 3 or later', (target, 'loaded software encoder library license mismatch')
    if manifest.get('sourcePatches'):
        codec.avcodec_configuration.restype = ctypes.c_char_p
        configuration = codec.avcodec_configuration().decode('utf-8')
        for patch in manifest['sourcePatches']:
            assert patch['versionMarker'] in configuration, (target, patch['file'], 'native source patch build marker mismatch')
    codec.avcodec_find_encoder_by_name.argtypes = [ctypes.c_char_p]
    codec.avcodec_find_encoder_by_name.restype = ctypes.c_void_p
    format_library = ctypes.CDLL(str(directory / filename('avformat')))
    format_library.av_guess_format.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_char_p]
    format_library.av_guess_format.restype = ctypes.c_void_p
    for encoder in profile['encoders']:
        assert codec.avcodec_find_encoder_by_name(encoder.encode()), (target, encoder, 'recording encoder missing')
    for muxer in profile['muxers']:
        assert format_library.av_guess_format(muxer.encode(), None, None), (target, muxer, 'recording muxer missing')
    print(f'PASS {target}: recording encoders and muxers exported (device encoding unverified)')


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
            verify_source_patches(directory, manifest)
            verify_software_encoder_provenance(directory, manifest)
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
                verify_recording_profile(directory, manifest)
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

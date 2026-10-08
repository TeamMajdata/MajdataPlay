#!/usr/bin/env python3
"""Independently inspect staged Windows PE machines, public exports and DLL closure."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[3]
LOCK = json.loads((ROOT / 'Tools/FFmpeg/ffmpeg.lock.json').read_text())
TARGETS = {'win-x86': ('x86', 0x014c), 'win-x64': ('x86_64', 0x8664), 'win-arm64': ('arm64', 0xaa64)}
SYSTEM_DLLS = {'advapi32.dll', 'avicap32.dll', 'bcrypt.dll', 'd3d11.dll', 'd3d12.dll', 'dxgi.dll',
               'gdi32.dll', 'kernel32.dll', 'msvcrt.dll', 'ntdll.dll', 'ole32.dll', 'oleaut32.dll',
               'opengl32.dll', 'psapi.dll', 'secur32.dll', 'shell32.dll', 'shlwapi.dll', 'ucrtbase.dll',
               'user32.dll', 'vfw32.dll', 'winmm.dll', 'ws2_32.dll', 'mf.dll', 'mfplat.dll', 'mfuuid.dll',
               'strmiids.dll', 'cfgmgr32.dll', 'imm32.dll', 'version.dll', 'crypt32.dll', 'ncrypt.dll'}
REQUIRED_EXPORTS = {
    'avcodec': ['avcodec_find_decoder_by_name', 'avcodec_find_encoder_by_name', 'avcodec_configuration',
                'avcodec_send_packet', 'avcodec_receive_frame', 'avcodec_send_frame', 'avcodec_receive_packet'],
    'avdevice': ['avdevice_register_all'],
    'avfilter': ['avfilter_graph_alloc'],
    'avformat': ['avformat_open_input', 'avformat_alloc_output_context2', 'av_guess_format'],
    'avutil': ['av_version_info', 'av_buffer_unref', 'av_frame_alloc', 'av_hwdevice_ctx_create'],
    'swresample': ['swr_alloc', 'swr_init', 'swr_convert'],
    'swscale': ['sws_getContext', 'sws_scale']
}


def inspect(directory, target, readobj):
    """Return an auditable list of machines, exported symbols and import closures."""
    architecture, machine = TARGETS[target]
    binaries = {f'{name}-{major}.dll': name for name, major in LOCK['abi'].items()}
    binaries['FFmpegUnityBridge.dll'] = 'bridge'
    available = {name.lower() for name in binaries}
    records = []
    for filename, component in binaries.items():
        file = directory / filename
        data = file.read_bytes()
        assert data[:2] == b'MZ', (target, filename, 'not PE')
        offset = struct.unpack_from('<I', data, 0x3c)[0]
        assert data[offset:offset + 4] == b'PE\0\0', (target, filename, 'bad PE signature')
        actual = struct.unpack_from('<H', data, offset + 4)[0]
        assert actual == machine, (target, filename, hex(actual), hex(machine))
        output = subprocess.check_output([readobj, '--coff-exports', '--coff-imports', str(file)], text=True)
        exports = set(re.findall(r'Export \{\s+Ordinal: [^\n]+\s+Name: ([^\n]+)', output))
        imports = re.findall(r'Import \{\s+Name: ([^\n]+)', output)
        required = ['UnityPluginLoad', 'UnityPluginUnload', 'ffu_abi_version', 'ffu_d3d11_prepare',
                    'ffu_d3d12va_acquire_device', 'ffu_vulkan_video_acquire_device'] if component == 'bridge' else \
                    [component + '_version'] + REQUIRED_EXPORTS[component]
        missing = set(required) - exports
        assert not missing, (target, filename, 'missing exports', sorted(missing))
        dependencies = []
        for name in imports:
            lower = name.lower()
            if lower in available:
                dependency = directory / next(file for file in binaries if file.lower() == lower)
                raw = dependency.read_bytes()
                dependency_offset = struct.unpack_from('<I', raw, 0x3c)[0]
                assert struct.unpack_from('<H', raw, dependency_offset + 4)[0] == machine, dependency
                dependencies.append(name)
            else:
                assert lower in SYSTEM_DLLS or re.fullmatch(r'(?:api-ms|ext-ms)-.*\.dll', lower), \
                       (target, filename, 'undeployed/non-system dependency', name)
        assert not any(re.search(r'lib(?:stdc\+\+|gcc|winpthread|c\+\+)', name, re.IGNORECASE) for name in imports), \
               (target, filename, 'dynamic compiler runtime')
        records.append({'file': filename, 'machine': hex(machine), 'exportCount': len(exports),
                        'requiredExports': required, 'imports': imports, 'colocatedDependencies': dependencies})
        print(f'PASS {target} {filename}: PE machine {hex(machine)}, {len(exports)} exports, dependency closure')
    return {'target': target, 'directory': str(directory.resolve()), 'files': records, 'runtimeTested': False}


def main():
    """Inspect selected staged targets without loading or executing an ARM64 library."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--targets', default='win-x86,win-x64,win-arm64')
    parser.add_argument('--llvm-readobj', default=shutil.which('llvm-readobj') or str(
        ROOT / 'Tools/FFmpeg/.build/toolchains/llvm-mingw-20260922-ucrt-x86_64/bin/llvm-readobj.exe'))
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    reports = []
    for target in args.targets.split(','):
        if target not in TARGETS:
            parser.error('Unknown Windows target: ' + target)
        reports.append(inspect(ROOT / 'Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows' / TARGETS[target][0],
                               target, args.llvm_readobj))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(reports, indent=2) + '\n', encoding='utf-8')
    print('PASS static PE/export/dependency inspection; execution/GPU behavior is not inferred.')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Offline Windows ARM64 recipe regressions; never download or compile dependencies."""
from __future__ import annotations

from contextlib import ExitStack, redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shlex
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location('ffmpeg_windows_build', ROOT / 'Tools/FFmpeg/build.py')
BUILD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BUILD)


class WindowsArm64BuildTests(unittest.TestCase):
    """Verify target ABI selection, static codecs, shell paths and Unity staging."""

    def setUp(self):
        """Create a private fixture tree with inert compiler files."""
        self.temporary = tempfile.TemporaryDirectory(prefix='ffmpeg-windows-recipe-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.bin = self.root / 'llvm/bin'
        self.bin.mkdir(parents=True)
        for triple in ('i686', 'x86_64', 'aarch64'):
            for tool in ('clang', 'clang++', 'windres'):
                (self.bin / (triple + '-w64-mingw32-' + tool)).touch()
        self.options = types.SimpleNamespace(jobs=2, unity_plugin_api=None)
        self.stack = ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(BUILD, 'CACHE', self.root / 'cache'))
        self.stack.enter_context(patch.object(BUILD, 'OUTPUT', self.root / 'assets'))
        self.stack.enter_context(patch.object(BUILD, 'HOST', 'Linux'))
        self.stack.enter_context(patch.dict(os.environ, {'LLVM_MINGW': str(self.bin.parent)}, clear=True))
        self.stack.enter_context(patch.object(BUILD.DEPENDENCIES, 'llvm_root', return_value=None))
        self.stack.enter_context(patch.object(BUILD.DEPENDENCIES, 'verify_vulkan_headers'))
        self.stack.enter_context(patch.object(BUILD.DEPENDENCIES, 'verify_d3d12_overlay', return_value=None))
        self.amf = self.stack.enter_context(patch.object(BUILD.DEPENDENCIES, 'verify_amf_headers'))
        self.nvcodec = self.stack.enter_context(patch.object(BUILD.DEPENDENCIES, 'verify_nvcodec_headers'))

    def selected(self, target='win-arm64'):
        """Return a recipe using the inert LLVM-MinGW target drivers."""
        selected, reason = BUILD.toolchain(target)
        self.assertIsNone(reason)
        return selected

    def tools(self):
        """Create the resolved software-encoder tool record without subprocesses."""
        specific, _ = self.selected()
        return {'environment': {'PATH': str(self.bin)},
                'flags': dict(arg[2:].split('=', 1) for arg in specific if '=' in arg),
                'cc': [str(self.bin / 'aarch64-w64-mingw32-clang')],
                'cxx': [str(self.bin / 'aarch64-w64-mingw32-clang++')],
                'ar': str(self.bin / 'llvm-ar'), 'ranlib': str(self.bin / 'llvm-ranlib'),
                'strip': str(self.bin / 'llvm-strip'),
                'cflags': ['-IC:/TOOLCH~1/include'], 'ldflags': ['-static-libgcc'],
                'family': 'arm64', 'nasm': None, 'cmake': 'cmake',
                'cmakeVersion': 'cmake version 3.22.1', 'ninja': 'ninja'}

    def test_target_registration_and_destination(self):
        """ARM64 is a distinct staged target, not an x86 fallback."""
        self.assertIn('win-arm64', BUILD.TARGETS)
        self.assertEqual(BUILD.destination_for('win-arm64'), BUILD.OUTPUT / 'Windows/arm64')

    def test_arm64_compilers_and_direct3d_backends(self):
        """All FFmpeg Windows backends use the AArch64 target compiler."""
        args, paths = self.selected()
        for flag in ('--arch=aarch64', '--cc=aarch64-w64-mingw32-clang',
                     '--cxx=aarch64-w64-mingw32-clang++', '--windres=aarch64-w64-mingw32-windres',
                     '--enable-w32threads', '--enable-d3d11va', '--enable-d3d12va',
                     '--enable-dxva2', '--enable-vulkan', '--enable-schannel'):
            self.assertIn(flag, args)
        self.assertEqual(paths, [self.bin])

    def test_windows_llvm_cross_prefix_selects_dlltool(self):
        """Every LLVM Windows target derives its import-library tool from the triple."""
        for target, triple in [('win-x86', 'i686'), ('win-x64', 'x86_64'), ('win-arm64', 'aarch64')]:
            with self.subTest(target=target):
                prefix = triple + '-w64-mingw32'
                args, paths = self.selected(target)
                self.assertIn('--cross-prefix=' + prefix + '-', args)
                for flag in ('--cc=' + prefix + '-clang', '--cxx=' + prefix + '-clang++',
                             '--ar=llvm-ar', '--ranlib=llvm-ranlib', '--nm=llvm-nm',
                             '--strip=llvm-strip', '--windres=' + prefix + '-windres'):
                    self.assertIn(flag, args)
                self.assertEqual(paths, [self.bin])

    def test_windows_gnu_cross_prefix_is_preserved(self):
        """GNU fallback retains its target prefix without selecting LLVM driver names."""
        for target, triple in [('win-x86', 'i686'), ('win-x64', 'x86_64'), ('win-arm64', 'aarch64')]:
            with self.subTest(target=target):
                prefix = triple + '-w64-mingw32-'
                with patch.object(BUILD, 'HERE', self.root / 'tool'), \
                     patch.object(BUILD, 'CACHE', self.root / 'gnu-cache'), \
                     patch.dict(os.environ, {'LLVM_MINGW': str(self.root / 'missing-llvm')}), \
                     patch.object(BUILD.shutil, 'which', side_effect=lambda name, **kw: prefix + 'gcc' if name == prefix + 'gcc' else None):
                    args, paths = self.selected(target)
                self.assertEqual([flag for flag in args if flag.startswith('--cross-prefix=')],
                                 ['--cross-prefix=' + prefix])
                self.assertFalse(any(flag.startswith(('--cc=', '--cxx=', '--ar=', '--nm=')) for flag in args))
                self.assertEqual(paths, [])

    def test_arm64_disables_unavailable_vendor_runtimes(self):
        """NVENC and AMF cannot be silently enabled on Windows ARM64."""
        args, _ = self.selected()
        for name in ('amf', 'ffnvcodec', 'nvenc', 'nvdec', 'cuvid'):
            self.assertIn('--disable-' + name, args)
            self.assertNotIn('--enable-' + name, args)
        self.amf.assert_not_called()
        self.nvcodec.assert_not_called()

    def test_existing_windows_targets_keep_vendor_backends(self):
        """The new ARM64 exception preserves x86/x64 hardware profiles."""
        for target, architecture in [('win-x86', 'x86'), ('win-x64', 'x86_64')]:
            with self.subTest(target=target):
                args, _ = self.selected(target)
                for flag in ('--arch=' + architecture, '--enable-amf', '--enable-ffnvcodec', '--enable-nvenc'):
                    self.assertIn(flag, args)

    def hardware_fixture(self):
        """Create generated config headers for the supported ARM64 hardware profile."""
        work = self.root / 'ffmpeg'
        work.mkdir()
        required = ['VULKAN', 'H264_VULKAN_HWACCEL', 'HEVC_VULKAN_HWACCEL', 'AV1_VULKAN_HWACCEL',
                    'VP9_VULKAN_HWACCEL', 'D3D12VA', 'H264_D3D12VA_HWACCEL', 'HEVC_D3D12VA_HWACCEL',
                    'AV1_D3D12VA_HWACCEL', 'VP9_D3D12VA_HWACCEL', 'D3D11VA', 'DXVA2', 'SCHANNEL']
        (work / 'config.h').write_text(''.join('#define CONFIG_' + name + ' 1\n' for name in required))
        (work / 'config_components.h').write_text('')
        return work, required

    def test_configured_arm64_hardware_profile_is_checked(self):
        """A successful configure cannot silently lose Direct3D, DXVA2 or Schannel."""
        work, expected = self.hardware_fixture()
        self.assertEqual(BUILD.verify_hardware_configuration('win-arm64', work), expected)
        config = work / 'config.h'
        config.write_text(config.read_text().replace('#define CONFIG_D3D11VA 1', '#define CONFIG_D3D11VA 0'))
        with self.assertRaisesRegex(RuntimeError, 'D3D11VA'):
            BUILD.verify_hardware_configuration('win-arm64', work)

    def test_configure_cannot_reenable_unsupported_arm64_vendor_sdk(self):
        """Reject config drift that enables an unavailable NVENC/AMF runtime."""
        work, _ = self.hardware_fixture()
        for name in ('AMF', 'FFNVCODEC', 'NVENC', 'NVDEC', 'CUVID'):
            with self.subTest(backend=name):
                (work / 'config_components.h').write_text('#define CONFIG_' + name + ' 1\n')
                with self.assertRaisesRegex(RuntimeError, name):
                    BUILD.verify_hardware_configuration('win-arm64', work)

    def test_recording_profile_is_software_only_on_arm64(self):
        """The four fixed codecs and MPEG4 remain mandatory without vendor encoders."""
        encoders, muxers = BUILD.recording_configuration('win-arm64')
        self.assertEqual(encoders, ['mpeg4', 'libx264', 'libx265', 'libvpx_vp9', 'libaom_av1'])
        self.assertEqual(muxers, ['mov', 'mp4', 'matroska', 'webm', 'avi'])
        self.assertIn('h264_nvenc', BUILD.recording_configuration('win-x64')[0])
        self.assertIn('h264_amf', BUILD.recording_configuration('win-x86')[0])

    def test_encoder_cmake_uses_windows_arm64_abi(self):
        """x265/libaom receive the same Windows CPU, compilers and resource compiler."""
        args = BUILD.encoder_cmake_settings('win-arm64', self.tools())
        for flag in ('-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_SYSTEM_PROCESSOR=arm64',
                     '-DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY',
                     '-DCMAKE_RC_COMPILER=aarch64-w64-mingw32-windres'):
            self.assertIn(flag, args)
        self.assertIn('-DCMAKE_C_COMPILER=' + str(self.bin / 'aarch64-w64-mingw32-clang'), args)
        self.assertIn('-DCMAKE_CXX_COMPILER=' + str(self.bin / 'aarch64-w64-mingw32-clang++'), args)

    def test_encoder_tool_resolution_retains_target_aliases(self):
        """Encoder compiler resolution never canonicalizes LLVM target-driver names."""
        specific, paths = self.selected()
        with patch.object(BUILD, 'dav1d_tools', return_value={'PATH': str(self.bin)}), \
             patch.object(BUILD.shutil, 'which', side_effect=lambda name, **kw: None if name == 'nasm' else str(self.bin / name)), \
             patch.object(BUILD, 'command', return_value='cmake version 3.22.1'):
            tools = BUILD.encoder_toolchain('win-arm64', specific, paths)
        self.assertEqual(tools['family'], 'arm64')
        self.assertEqual(Path(tools['cc'][0]).name, 'aarch64-w64-mingw32-clang')
        self.assertEqual(Path(tools['cxx'][0]).name, 'aarch64-w64-mingw32-clang++')
        self.assertEqual(Path(tools['strip']).name, 'llvm-strip')

    def test_dav1d_windows_aarch64_machine_without_nvcodec_wrapper(self):
        """dav1d's cross file and pkg-config wrapper are genuinely Windows AArch64."""
        commands = []
        def run(args, **kwargs):
            commands.append(args)
            if 'setup' in args:
                prefix = Path(args[args.index('--prefix') + 1])
                (prefix / 'lib').mkdir(parents=True)
                (prefix / 'lib/libdav1d.a').write_bytes(b'archive')
                build = Path(args[args.index('setup') + 1])
                build.mkdir()
                (build / 'config.h').write_text('#define CONFIG_8BPC 1\n#define CONFIG_16BPC 1\n')
        with patch.object(BUILD, 'dav1d_tools', return_value={'PATH': str(self.bin)}), \
             patch.object(BUILD.shutil, 'which', return_value='compiler'), \
             patch.object(BUILD.DEPENDENCIES, 'meson_command', return_value=['python', 'meson.py']), \
             patch.object(BUILD.DEPENDENCIES, 'dav1d_source', return_value=self.root / 'dav1d'), \
             patch.object(BUILD.DEPENDENCIES, 'nvcodec_headers') as headers, \
             patch.object(BUILD.subprocess, 'run', side_effect=run):
            args, paths = self.selected()
            prefix, flags = BUILD.build_dav1d('win-arm64', args, paths, 2)
        setup = commands[0]
        machine = Path(setup[setup.index('--cross-file') + 1]).read_text()
        self.assertIn("system = 'windows'", machine)
        self.assertIn("cpu_family = 'aarch64'", machine)
        self.assertIn('aarch64-w64-mingw32-clang', machine)
        self.assertIn('aarch64-w64-mingw32-windres', machine)
        self.assertIn('-Denable_asm=true', setup)
        headers.assert_not_called()
        wrapper = prefix.parent / 'pkg-config.sh'
        self.assertNotIn('ffnvcodec', wrapper.read_text())
        self.assertIn('--enable-libdav1d', flags)

    def software(self, host='Linux', strip_name='llvm-strip', target='win-arm64'):
        """Capture complete four-codec recipes, fabricating only output archives."""
        shells = []
        def produce(name, prefix, build):
            lib = prefix / 'lib'
            lib.mkdir(parents=True, exist_ok=True)
            archive = 'libaom.a' if name == 'libaom' else 'libvpx.a' if name == 'libvpx' else 'lib' + name + '.a'
            (lib / archive).write_bytes(b'fixture-' + name.encode())
            (lib / 'pkgconfig').mkdir(exist_ok=True)
            if name == 'x265':
                (lib / 'pkgconfig/x265.pc').write_text('Libs.private: -lstdc++\n')
            if name == 'libvpx':
                (lib / 'pkgconfig/vpx.pc').write_text('Libs.private: -lpthread\n')
            if name == 'libaom':
                (build / 'config').mkdir(exist_ok=True)
                (build / 'config/aom_config.h').write_text('#define HAVE_PTHREAD_H 0\n#define CONFIG_MULTITHREAD 1\n')
        def shell(bash, text, cwd, log):
            shells.append(text)
            produce(cwd.parent.name, cwd.parent / 'install', cwd)
        def run(args, **kwargs):
            prefix = next((arg.split('=', 1)[1] for arg in args if arg.startswith('-DCMAKE_INSTALL_PREFIX=')), None)
            if prefix:
                build = Path(args[args.index('-B') + 1])
                produce(build.parent.name, Path(prefix), build)
        runtime = ['-L' + (self.root / 'runtime').as_posix(), '-l:libwinpthread.a', '-lm']
        tools = self.tools()
        tools['strip'] = str(self.bin / strip_name)
        with patch.object(BUILD, 'HOST', host), \
             patch.object(BUILD, 'posix', side_effect=lambda value: '/c/TOOLCH~1/' + Path(value).name), \
             patch.object(BUILD, 'encoder_toolchain', return_value=tools), \
             patch.object(BUILD, 'find_ndk', return_value=self.root / 'ndk'), \
             patch.object(BUILD, 'x265_runtime_libraries', return_value=runtime), \
             patch.object(BUILD.DEPENDENCIES, 'verify_encoder_source'), \
             patch.object(BUILD.DEPENDENCIES, 'encoder_source', side_effect=lambda name: self.root / 'sources' / name), \
             patch.object(BUILD, 'shell', side_effect=shell), \
             patch.object(BUILD.subprocess, 'run', side_effect=run), redirect_stdout(io.StringIO()):
            result = BUILD.build_software_encoders(target, [], [], 2, 'bash', ['--pkg-config=pkg-config'])
        return result, shells

    def test_all_four_codecs_are_static_windows_arm64(self):
        """x264 uses the Windows triple; x265/vpx/aom use portable ARM64 code."""
        result, _ = self.software()
        reports = result['provenance']
        self.assertIn('--host=aarch64-w64-mingw32', reports['x264']['configure'])
        self.assertIn('--target=generic-gnu', reports['libvpx']['configure'])
        self.assertIn('-DENABLE_ASSEMBLY=OFF', reports['x265']['configure'])
        self.assertIn('-DAOM_TARGET_CPU=generic', reports['libaom']['configure'])
        for name in ('x265', 'libaom'):
            self.assertIn('-DCMAKE_SYSTEM_NAME=Windows', reports[name]['configure'])
            self.assertIn('-DCMAKE_SYSTEM_PROCESSOR=arm64', reports[name]['configure'])
        for report in reports.values():
            self.assertTrue(report['static'])
            self.assertTrue(report['pic'])
        self.assertEqual(len(result['archives']), 4)

    def test_pe_and_elf_encoder_symbol_visibility_flags(self):
        """PE keeps explicit .def exports while ELF hides private archive symbols."""
        pe = '--extra-ldflags=-Wl,--exclude-all-symbols'
        elf = '--extra-ldflags=-Wl,--exclude-libs,ALL'
        for target in ('win-x86', 'win-x64', 'win-arm64', 'linux-x64', 'android-arm64'):
            with self.subTest(target=target):
                result, _ = self.software(target=target)
                self.assertIn(pe if target.startswith('win-') else elf, result['configure'])
                self.assertNotIn(elf if target.startswith('win-') else pe, result['configure'])

    def test_native_shell_compiler_and_flags_have_no_literal_quotes(self):
        """Autoconf word splitting must not inherit shlex quotes around NTFS '~' paths."""
        _, shells = self.software(host='Windows')
        for text in shells:
            tokens = shlex.split(text)
            for token in tokens:
                if token.startswith(('CC=', 'CXX=', 'AS=', 'STRIP=', 'CFLAGS=', 'CXXFLAGS=', 'ASFLAGS=')):
                    self.assertNotIn("'", token, token)
                    self.assertNotIn('"', token, token)

    def test_x264_and_vpx_use_the_selected_target_strip(self):
        """Configure, build and install never inherit the host's bare strip command."""
        for host in ('Linux', 'Windows'):
            with self.subTest(host=host):
                _, shells = self.software(host=host)
                self.assertEqual(len(shells), 6)
                for command in shells:
                    selected = [token.rstrip(';') for token in shlex.split(command) if token.startswith('STRIP=')]
                    self.assertEqual(selected, ['STRIP=/c/TOOLCH~1/llvm-strip'])

    def test_encoder_cache_tracks_the_selected_strip(self):
        """Changing the target strip tool cannot reuse a differently processed archive."""
        first, _ = self.software()
        second, _ = self.software(strip_name='llvm-strip-other')
        self.assertNotEqual(first['prefixes']['x264'].parents[1], second['prefixes']['x264'].parents[1])

    def test_static_runtime_selection_is_target_specific(self):
        """C++/ABI/unwind/pthread archives resolve through the AArch64 C++ driver."""
        runtime = self.root / 'runtime'
        runtime.mkdir()
        for name in ('libc++.a', 'libc++abi.a', 'libunwind.a', 'libwinpthread.a'):
            (runtime / name).touch()
        calls = []
        def output(args, **kwargs):
            calls.append(args)
            return str(runtime / args[-1].split('=', 1)[1])
        with patch.object(BUILD.subprocess, 'check_output', side_effect=output):
            libraries = BUILD.x265_runtime_libraries('win-arm64', self.tools())
        for name in ('libc++.a', 'libc++abi.a', 'libunwind.a', 'libwinpthread.a'):
            self.assertIn('-l:' + name, libraries)
        self.assertTrue(all(Path(args[0]).name == 'aarch64-w64-mingw32-clang++' for args in calls))

    def test_bridge_keeps_target_driver_when_resolve_would_lose_it(self):
        """A POSIX-hosted Windows cross bridge cannot resolve target symlinks to host clang."""
        selected = self.selected()
        original = Path.resolve
        def resolve(path, *args, **kwargs):
            if 'aarch64-w64-mingw32-clang' in path.name:
                return self.bin / 'clang'
            return original(path, *args, **kwargs)
        with patch.object(BUILD.shutil, 'which', side_effect=lambda name, **kw: str(self.bin / name)), \
             patch.object(Path, 'resolve', resolve):
            plan = BUILD.bridge_plan('win-arm64', self.root / 'prefix', self.options, selected)
        args = plan['args']
        self.assertIn('-DCMAKE_SYSTEM_PROCESSOR=aarch64', args)
        self.assertIn('-DCMAKE_C_COMPILER=' + (self.bin / 'aarch64-w64-mingw32-clang').as_posix(), args)
        self.assertIn('-DCMAKE_CXX_COMPILER=' + (self.bin / 'aarch64-w64-mingw32-clang++').as_posix(), args)

    def test_windows_short_path_keeps_semantic_executable_basename(self):
        """NTFS shortening must never rename a target clang/ranlib executable."""
        exe = self.bin / 'aarch64-w64-mingw32-clang++.exe'
        exe.touch()
        calls = []
        def shorten(value, buffer, length):
            calls.append(value)
            buffer.value = 'C:\\TOOLS~1\\bin'
            return len(buffer.value)
        fake = types.SimpleNamespace(kernel32=types.SimpleNamespace(GetShortPathNameW=shorten))
        with patch.object(BUILD, 'HOST', 'Windows'), patch.object(BUILD.ctypes, 'windll', fake, create=True):
            result = BUILD.short_path(exe)
        self.assertTrue(result.endswith('/aarch64-w64-mingw32-clang++.exe'))
        self.assertEqual(calls, [str(exe.parent)])

    def test_importer_arm64_cpu_and_disabled_editor(self):
        """Windows ARM64 is a Win64/ARM64 Player-only plugin; the bridge is preloaded."""
        directory = BUILD.OUTPUT / 'Windows/arm64'
        directory.mkdir(parents=True)
        file = directory / 'FFmpegUnityBridge.dll'
        file.touch()
        BUILD.write_meta(file, 'win-arm64')
        text = Path(str(file) + '.meta').read_text()
        self.assertIn('Standalone: Win64', text)
        self.assertIn('CPU: ARM64', text)
        self.assertIn('Editor: Editor\n    second:\n      enabled: 0', text)
        self.assertIn('DefaultValueInitialized: true', text)
        self.assertIn('isPreloaded: 1', text)

    def test_restage_preserves_existing_guid(self):
        """Replacing a DLL importer must not recreate an existing Unity asset identity."""
        file = BUILD.OUTPUT / 'Windows/arm64/avutil-61.dll'
        file.parent.mkdir(parents=True)
        file.touch()
        meta = Path(str(file) + '.meta')
        guid = '1234567890abcdef1234567890abcdef'
        meta.write_text('fileFormatVersion: 2\nguid: ' + guid + '\n')
        BUILD.write_meta(file, 'win-arm64')
        self.assertIn('guid: ' + guid, meta.read_text())

    def test_new_target_has_complete_paired_metadata(self):
        """Every ARM64 DLL, build record and target folder has one deterministic asset identity."""
        directory = BUILD.destination_for('win-arm64')
        directory.mkdir(parents=True)
        for library, major in BUILD.LOCK['abi'].items():
            file = directory / (library + '-' + str(major) + '.dll')
            file.touch()
            BUILD.write_meta(file, 'win-arm64')
        bridge = directory / 'FFmpegUnityBridge.dll'
        bridge.touch()
        BUILD.write_meta(bridge, 'win-arm64')
        for name in ('build-manifest.json', 'bridge-manifest.json', 'configure.txt'):
            (directory / name).write_text('{}\n')
        BUILD.write_stage_metadata(directory)
        assets = [file for file in directory.iterdir() if file.suffix != '.meta']
        guids = set()
        for file in assets + [directory, directory.parent]:
            meta = Path(str(file) + '.meta')
            self.assertTrue(meta.is_file(), str(file))
            guid = next(line for line in meta.read_text().splitlines() if line.startswith('guid:'))
            self.assertNotIn(guid, guids)
            guids.add(guid)
        for name in ('build-manifest.json', 'bridge-manifest.json'):
            self.assertIn('DefaultImporter:', (directory / (name + '.meta')).read_text())
        self.assertIn('TextScriptImporter:', (directory / 'configure.txt.meta').read_text())
        self.assertIn('folderAsset: yes', Path(str(directory) + '.meta').read_text())
        original = {path: path.read_bytes() for path in directory.parent.rglob('*.meta')}
        BUILD.write_stage_metadata(directory)
        self.assertEqual(original, {path: path.read_bytes() for path in original})

    def test_existing_record_and_folder_metadata_bytes_are_preserved(self):
        """Stage metadata never rewrites an existing importer, even its whitespace."""
        directory = BUILD.destination_for('win-arm64')
        directory.mkdir(parents=True)
        (directory / 'build-manifest.json').write_text('{}\n')
        contents = b'fileFormatVersion: 2\r\nguid: 1234567890abcdef1234567890abcdef\r\n\r\n'
        file = directory / 'build-manifest.json.meta'
        folder = Path(str(directory) + '.meta')
        file.write_bytes(contents)
        folder.write_bytes(contents + b'folderAsset: yes\r\n')
        BUILD.write_stage_metadata(directory)
        self.assertEqual(file.read_bytes(), contents)
        self.assertEqual(folder.read_bytes(), contents + b'folderAsset: yes\r\n')

    def test_bridge_manifest_contains_def_and_normalized_hashes(self):
        """Raw hashes remain compatible while LF hashes cover every .def link input."""
        here = self.root / 'tool'
        native = here / 'Native'
        native.mkdir(parents=True)
        for name in ('Bridge.cpp', 'ExportsWin32MinGW.def', 'CMakeLists.txt'):
            (native / name).write_bytes(b'first\r\nsecond\r\n')
        build = self.root / 'bridge'
        build.mkdir()
        (build / 'FFmpegUnityBridge.dll').write_bytes(b'fixture DLL')
        destination = BUILD.destination_for('win-arm64')
        destination.mkdir(parents=True)
        plan = {'args': ['cmake'], 'build': build, 'cmake': 'cmake', 'environment': {}}
        with patch.object(BUILD, 'HERE', here), patch.object(BUILD, 'bridge_plan', return_value=plan), \
             patch.object(BUILD.platform, 'platform', return_value='offline test host'), \
             patch.object(BUILD.subprocess, 'run'):
            BUILD.build_bridge('win-arm64', self.root / 'prefix', self.options)
        report = json.loads((destination / 'bridge-manifest.json').read_text())
        for name in ('Bridge.cpp', 'ExportsWin32MinGW.def', 'CMakeLists.txt'):
            self.assertEqual(report['sourceSha256'][name], hashlib.sha256(b'first\r\nsecond\r\n').hexdigest())
            self.assertEqual(report['sourceSha256Lf'][name], hashlib.sha256(b'first\nsecond\n').hexdigest())


if __name__ == '__main__':
    unittest.main(verbosity=2)

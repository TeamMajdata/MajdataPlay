#!/usr/bin/env python3
"""Offline QSV encoder recipe and provenance regressions; no GPU or downloads."""
from __future__ import annotations

from contextlib import redirect_stdout
import copy
import hashlib
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[4]
SPEC = importlib.util.spec_from_file_location('ffmpeg_qsv_build', ROOT / 'Tools/FFmpeg/build.py')
BUILD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BUILD)
SPEC = importlib.util.spec_from_file_location('ffmpeg_qsv_audit', ROOT / 'Tools/FFmpeg/verify-artifacts.py')
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)
QSV_ENCODERS = {'h264_qsv', 'hevc_qsv', 'av1_qsv', 'vp9_qsv'}


class QsvBuildTests(unittest.TestCase):
    """Require QSV encoding without replacing the native Intel decoding paths."""

    def test_qsv_encoder_targets(self):
        """Only the supported desktop Intel architectures compile QSV wrappers."""
        for target in BUILD.TARGETS:
            with self.subTest(target=target):
                encoders, _ = BUILD.recording_configuration(target)
                expected = QSV_ENCODERS if target in {'win-x86', 'win-x64', 'linux-x64'} else set()
                self.assertEqual({name for name in encoders if name.endswith('_qsv')}, expected)

    def test_linux_retains_other_encoder_backends(self):
        """Adding QSV preserves the existing software, NVENC and VAAPI encoders."""
        encoders, _ = BUILD.recording_configuration('linux-x64')
        self.assertTrue({'mpeg4', 'libx264', 'libx265', 'libvpx_vp9', 'libaom_av1',
                         'h264_nvenc', 'hevc_nvenc', 'av1_nvenc',
                         'h264_vaapi', 'hevc_vaapi', 'vp9_vaapi', 'av1_vaapi'} <= set(encoders))

    def configuration(self, work, disabled=None, decoder=None):
        """Write the hardware components of a synthetic Linux configure result."""
        names = ['VULKAN', 'LIBVPL', 'QSV', 'VAAPI', 'LIBDRM',
                 'H264_VULKAN_HWACCEL', 'HEVC_VULKAN_HWACCEL', 'AV1_VULKAN_HWACCEL', 'VP9_VULKAN_HWACCEL']
        contents = ''.join('#define CONFIG_' + name + ' ' + ('0' if name == disabled else '1') + '\n'
                           for name in names)
        if decoder:
            contents += '#define CONFIG_' + decoder.upper() + '_DECODER 1\n'
        (work / 'config.h').write_text(contents, encoding='utf-8')
        (work / 'config_components.h').write_text('', encoding='utf-8')

    def test_linux_requires_qsv_and_its_vaapi_child_device(self):
        """A successful configure must not silently omit QSV or either VAAPI dependency."""
        with tempfile.TemporaryDirectory() as temporary:
            work = Path(temporary)
            self.configuration(work)
            BUILD.verify_hardware_configuration('linux-x64', work)
            for name in ['LIBVPL', 'QSV', 'VAAPI', 'LIBDRM']:
                with self.subTest(component=name):
                    self.configuration(work, disabled=name)
                    with self.assertRaisesRegex(RuntimeError, name):
                        BUILD.verify_hardware_configuration('linux-x64', work)

    def test_qsv_decoders_remain_disabled(self):
        """Intel playback continues to use the existing D3D11VA/VAAPI decoder paths."""
        with tempfile.TemporaryDirectory() as temporary:
            work = Path(temporary)
            for decoder in BUILD.QSV_DECODERS:
                with self.subTest(decoder=decoder):
                    self.configuration(work, decoder=decoder)
                    with self.assertRaisesRegex(RuntimeError, 'QSV decoders were enabled'):
                        BUILD.verify_hardware_configuration('linux-x64', work)

    def test_linux_dispatcher_links_cpp_runtime_statically(self):
        """The installed pkg-config interface must carry the static C++/dl/thread link closure."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            tools = {'flags': {}, 'cc': ['/target/cc'], 'cxx': ['/target/c++'], 'ar': '/target/ar',
                     'ranlib': '/target/ranlib', 'family': 'x86_64', 'ninja': '/host/ninja',
                     'cmake': '/host/cmake', 'cmakeVersion': 'cmake version 3.28.3',
                     'cflags': [], 'ldflags': [], 'environment': {}}
            runtime = ['-L/static', '-l:libstdc++.a', '-lm', '-ldl', '-pthread']
            def install(args, **kwargs):
                if '--install' in args:
                    prefix = Path(args[2]).parent / 'install'
                    (prefix / 'lib/pkgconfig').mkdir(parents=True, exist_ok=True)
                    (prefix / 'lib/libvpl.a').write_bytes(b'fixture static dispatcher')
                    (prefix / 'lib/pkgconfig/vpl.pc').write_text('Libs: -lvpl\nLibs.private: -lstdc++\n')
            with patch.object(BUILD, 'CACHE', root), \
                 patch.object(BUILD, 'encoder_toolchain', return_value=tools), \
                 patch.object(BUILD, 'x265_runtime_libraries', return_value=runtime), \
                 patch.object(BUILD.DEPENDENCIES, 'verify_libvpl_source'), \
                 patch.object(BUILD.DEPENDENCIES, 'libvpl_source', return_value=root / 'source'), \
                 patch.object(BUILD.subprocess, 'run', side_effect=install), redirect_stdout(io.StringIO()):
                result = BUILD.build_libvpl('linux-x64', [], [], 2, ['--pkg-config=fixture-pkg-config'])
            configuration = result['provenance']['configure']
            self.assertIn('-DBUILD_SHARED_LIBS=OFF', configuration)
            self.assertIn('-DCMAKE_POSITION_INDEPENDENT_CODE=ON', configuration)
            self.assertIn('-DMFX_MODULES_DIR=/usr/lib/x86_64-linux-gnu', configuration)
            pc = (result['archive'].parent / 'pkgconfig/vpl.pc').read_text()
            self.assertIn('-l:libstdc++.a -lm -ldl -pthread', pc)
            self.assertNotIn('-lstdc++', pc)
            self.assertIn('--disable-decoder=' + ','.join(BUILD.QSV_DECODERS), result['configure'])
            self.assertTrue(result['provenance']['static'])
            self.assertTrue(result['provenance']['pic'])


class QsvArtifactTests(unittest.TestCase):
    """Require the authenticated dispatcher and Linux child-device configuration."""

    def manifest(self, directory, target='linux-x64'):
        """Create minimal QSV build evidence and its matching license notice."""
        source = copy.deepcopy(BUILD.DEPENDENCIES.LOCK['libvpl'])
        notice = directory / 'libvpl.LICENSE.txt'
        notice.write_text('Intel VPL dispatcher ' + source['commit'] + '\n', encoding='utf-8')
        return {'target': target, 'recordingProfile': {'encoders': sorted(QSV_ENCODERS)},
                'configure': ['--enable-libvpl', '--disable-decoder=' + ','.join(BUILD.QSV_DECODERS)]
                             + (['--enable-vaapi', '--enable-libdrm'] if target == 'linux-x64' else []),
                'qsvDispatcherBuild': {'source': source, 'archive': 'libvpl.a', 'static': True, 'pic': True},
                'buildDependencyLicenses': [{'file': notice.name, 'license': source['license'],
                                            'sha256': hashlib.sha256(notice.read_bytes()).hexdigest()}]}

    def test_supported_desktop_dispatcher_provenance(self):
        """The same pinned static dispatcher evidence is accepted on Linux and Windows."""
        with tempfile.TemporaryDirectory() as temporary, redirect_stdout(io.StringIO()):
            directory = Path(temporary)
            for target in ['linux-x64', 'win-x64', 'win-x86']:
                with self.subTest(target=target):
                    AUDIT.verify_qsv_dispatcher(directory, self.manifest(directory, target))

    def test_linux_dispatcher_requires_vaapi_and_drm(self):
        """A QSV manifest must retain the backend used to bind the selected DRM render node."""
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for option in ['--enable-vaapi', '--enable-libdrm']:
                with self.subTest(option=option):
                    manifest = self.manifest(directory)
                    manifest['configure'].remove(option)
                    with self.assertRaisesRegex(AssertionError, 'QSV.*VAAPI'):
                        AUDIT.verify_qsv_dispatcher(directory, manifest)

    def test_missing_dispatcher_is_rejected(self):
        """Encoder declarations alone cannot replace dispatcher build evidence."""
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            manifest = self.manifest(directory)
            manifest.pop('qsvDispatcherBuild')
            with self.assertRaisesRegex(AssertionError, 'source identity'):
                AUDIT.verify_qsv_dispatcher(directory, manifest)

    def test_dynamic_or_non_pic_dispatcher_is_rejected(self):
        """No separately installed VPL dispatcher or non-PIC archive may be required."""
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            for field in ['static', 'pic']:
                with self.subTest(field=field):
                    manifest = self.manifest(directory)
                    manifest['qsvDispatcherBuild'][field] = False
                    with self.assertRaisesRegex(AssertionError, 'static PIC'):
                        AUDIT.verify_qsv_dispatcher(directory, manifest)

    def test_modified_dispatcher_notice_is_rejected(self):
        """License hashes remain tied to the staged dispatcher provenance."""
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            manifest = self.manifest(directory)
            (directory / 'libvpl.LICENSE.txt').write_text('changed', encoding='utf-8')
            with self.assertRaisesRegex(AssertionError, 'license hash'):
                AUDIT.verify_qsv_dispatcher(directory, manifest)


if __name__ == '__main__':
    unittest.main()

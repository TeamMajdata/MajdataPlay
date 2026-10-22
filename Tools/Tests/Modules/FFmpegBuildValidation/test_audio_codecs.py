#!/usr/bin/env python3
"""Offline audio profile/source-integrity regressions; no native tools or downloads."""
from __future__ import annotations
import importlib.util
import io
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[4]
SPEC = importlib.util.spec_from_file_location('ffmpeg_audio_build', ROOT / 'Tools/FFmpeg/build.py')
BUILD = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BUILD)
AUDIT_SPEC = importlib.util.spec_from_file_location('ffmpeg_audio_audit', ROOT / 'Tools/FFmpeg/verify-artifacts.py')
AUDIT = importlib.util.module_from_spec(AUDIT_SPEC)
AUDIT_SPEC.loader.exec_module(AUDIT)


class AudioCodecTests(unittest.TestCase):
    """Protect required capabilities and authenticated dependency handling."""

    def configuration(self, directory, target):
        """Write a complete synthetic component configuration for negative tests."""
        encoders, muxers = BUILD.recording_configuration(target)
        macros = [name.upper() + '_ENCODER' for name in encoders]
        macros += [name.upper() + '_MUXER' for name in muxers]
        macros += [name.upper() + '_DECODER' for name in BUILD.audio_decoders()]
        contents = ''.join('#define CONFIG_' + name + ' 1\n' for name in macros)
        (directory / 'config_components.h').write_text(contents)
        return contents

    def test_every_target_requires_audio_pairs_and_containers(self):
        """Foreign targets must carry the same audio feature set as the host."""
        for target in BUILD.TARGETS:
            with self.subTest(target=target), tempfile.TemporaryDirectory() as temporary:
                work = Path(temporary)
                self.configuration(work, target)
                result = BUILD.verify_recording_configuration(target, work)
                self.assertTrue({'aac', 'libopus', 'libmp3lame', 'pcm_s16le', 'pcm_f64be', 'pcm_s24le_planar'} <= set(result['encoders']))
                self.assertTrue({'aac', 'opus', 'libopus', 'mp3', 'mp3float'} <= set(result['audioDecoders']))
                self.assertTrue({'adts', 'latm', 'opus', 'ogg', 'mp3', 'wav'} <= set(result['muxers']))
                self.assertEqual(len(BUILD.pcm_encoders()), 31)

    def test_missing_audio_components_fail_closed(self):
        """Configure success must not hide an omitted encoder, decoder or muxer."""
        for macro in ['AAC_ENCODER', 'LIBOPUS_ENCODER', 'LIBMP3LAME_ENCODER', 'PCM_S24LE_ENCODER',
                      'OPUS_DECODER', 'LIBOPUS_DECODER', 'MP3_DECODER', 'PCM_F32BE_DECODER', 'WAV_MUXER']:
            with self.subTest(macro=macro), tempfile.TemporaryDirectory() as temporary:
                work = Path(temporary)
                contents = self.configuration(work, 'win-arm64')
                (work / 'config_components.h').write_text(contents.replace('#define CONFIG_' + macro + ' 1', '#define CONFIG_' + macro + ' 0'))
                with self.assertRaisesRegex(RuntimeError, macro):
                    BUILD.verify_recording_configuration('win-arm64', work)

    def test_audio_source_tampering_and_additions_rejected(self):
        """Out-of-tree builds may never consume changed or injected release files."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / 'release.tar.gz'
            with tarfile.open(archive, 'w:gz') as package:
                for filename in ['COPYING', 'codec.c']:
                    data = b'authenticated release file\n'
                    entry = tarfile.TarInfo('opus-1.6.1/' + filename)
                    entry.size = len(data)
                    package.addfile(entry, io.BytesIO(data))
            with patch.object(BUILD.DEPENDENCIES, 'SHARED', root / 'sources'), \
                 patch.object(BUILD.DEPENDENCIES, 'download_verified', return_value=archive):
                BUILD.DEPENDENCIES.verify_audio_source('opus', extract=True)
                source = BUILD.DEPENDENCIES.audio_source('opus')
                (source / 'injected.h').write_text('injected')
                with self.assertRaisesRegex(RuntimeError, 'additional'):
                    BUILD.DEPENDENCIES.verify_audio_source('opus')
                (source / 'injected.h').unlink()
                (source / 'codec.c').write_text('modified')
                with self.assertRaisesRegex(RuntimeError, 'modified'):
                    BUILD.DEPENDENCIES.verify_audio_source('opus')

    def test_audio_archive_cannot_escape_its_source_root(self):
        """Authentication does not waive independent extraction path validation."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            archive = root / 'release.tar.gz'
            with tarfile.open(archive, 'w:gz') as package:
                entry = tarfile.TarInfo('../outside.c')
                entry.size = 1
                package.addfile(entry, io.BytesIO(b'x'))
            with patch.object(BUILD.DEPENDENCIES, 'SHARED', root / 'sources'), \
                 patch.object(BUILD.DEPENDENCIES, 'download_verified', return_value=archive):
                with self.assertRaisesRegex(RuntimeError, 'Unexpected'):
                    BUILD.DEPENDENCIES.verify_audio_source('opus', extract=True)
            self.assertFalse((root / 'outside.c').exists())

    def test_artifact_audit_rejects_old_audio_profile(self):
        """Old libraries cannot pass the audit simply by retaining video provenance."""
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaisesRegex(AssertionError, 'incomplete audio codec build provenance'):
                AUDIT.verify_audio_codec_provenance(Path(temporary), {'target': 'win-arm64'})

    def test_android_runtime_search_paths_cannot_select_static_bionic(self):
        """Link C++ archives without exposing the NDK directory containing libc.a."""
        for target in ['android-arm64', 'android-armv7']:
            with self.subTest(target=target), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                ndk = root / 'ndk/sysroot/usr/lib/target'
                ndk.mkdir(parents=True)
                selected = ['libc++_static.a', 'libc++abi.a', 'libunwind.a']
                for filename in selected + ['libc.a', 'libm.a']:
                    (ndk / filename).write_bytes(filename.encode())
                def output(args, **kwargs):
                    return str(ndk / args[-1].split('=', 1)[1])
                with patch.object(BUILD, 'CACHE', root / 'cache'), \
                     patch.object(BUILD, 'HOST', 'Linux'), \
                     patch.object(BUILD.subprocess, 'check_output', side_effect=output):
                    flags = BUILD.x265_runtime_libraries(target, {'cxx': ['clang++'], 'environment': {}})
                directories = [Path(flag[2:]) for flag in flags if flag.startswith('-L')]
                self.assertNotIn(ndk, directories)
                self.assertEqual(len(directories), 3)
                for directory, filename in zip(directories, selected):
                    self.assertEqual((directory / filename).read_bytes(), (ndk / filename).read_bytes())
                    self.assertFalse((directory / 'libc.a').exists())
                    self.assertFalse((directory / 'libm.a').exists())


if __name__ == '__main__':
    unittest.main(verbosity=2)

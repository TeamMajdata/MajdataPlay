"""Regression tests for the native artifact audit without loading foreign binaries."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from contextlib import ExitStack, redirect_stdout
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location('verify_artifacts', ROOT / 'Tools/FFmpeg/verify-artifacts.py')
VERIFY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFY)
LOCK = json.loads((ROOT / 'Tools/FFmpeg/ffmpeg.lock.json').read_text(encoding='utf-8'))
DEPENDENCY_SPEC = importlib.util.spec_from_file_location('build_dependencies', ROOT / 'Tools/FFmpeg/build-dependencies.py')
DEPENDENCIES = importlib.util.module_from_spec(DEPENDENCY_SPEC)
DEPENDENCY_SPEC.loader.exec_module(DEPENDENCIES)


def importer_text(platform_key, cpu, editor_os=None, extra=''):
    """Create only the importer fields read by the verifier."""
    return f"""fileFormatVersion: 2
 guid: 0123456789abcdef0123456789abcdef
 PluginImporter:
   platformData:
   - first:
       Any:
     second:
       enabled: 0
       settings: {{}}
   - first:
       Editor: Editor
     second:
       enabled: {1 if editor_os else 0}
       settings:
         DefaultValueInitialized: true
         CPU: {cpu if editor_os else 'AnyCPU'}
         OS: {editor_os or 'AnyOS'}
   - first:
       {platform_key}
     second:
       enabled: 1
       settings:
         CPU: {cpu}
 {extra}  userData:
""".replace('\n ', '\n')


class SourceLockTests(unittest.TestCase):
    """Keep the seven-file inventory and its revision tied to the repository lock."""

    def manifest(self):
        """Return a valid minimal ARM64 Windows manifest."""
        return {'target': 'win-arm64', 'source': copy.deepcopy(LOCK),
                'files': [{'file': f'{name}-{major}.dll'} for name, major in LOCK['abi'].items()]}

    def test_locked_arm64_inventory(self):
        VERIFY.verify_source_lock(self.manifest())

    def test_self_consistent_wrong_abi_is_rejected(self):
        manifest = self.manifest()
        manifest['source']['abi']['avcodec'] = 62
        manifest['files'][0]['file'] = 'avcodec-62.dll'
        with self.assertRaisesRegex(AssertionError, 'source lock mismatch'):
            VERIFY.verify_source_lock(manifest)

    def test_wrong_commit_is_rejected(self):
        manifest = self.manifest()
        manifest['source']['commit'] = '0' * 40
        with self.assertRaisesRegex(AssertionError, 'source lock mismatch'):
            VERIFY.verify_source_lock(manifest)

    def test_missing_library_is_rejected(self):
        manifest = self.manifest()
        manifest['files'].pop()
        with self.assertRaisesRegex(AssertionError, 'inventory'):
            VERIFY.verify_source_lock(manifest)

    def test_duplicate_library_is_rejected(self):
        manifest = self.manifest()
        manifest['files'][-1] = manifest['files'][0]
        with self.assertRaisesRegex(AssertionError, 'inventory'):
            VERIFY.verify_source_lock(manifest)


class HostTargetTests(unittest.TestCase):
    """Never dlopen an OS-native ARM64 library from an emulated x64 interpreter."""

    def test_windows_arm64_interpreter(self):
        self.assertEqual(VERIFY.host_target('Windows', 'ARM64', 8, 'win-arm64'), 'win-arm64')

    def test_windows_x64_interpreter_on_arm64_os(self):
        self.assertEqual(VERIFY.host_target('Windows', 'ARM64', 8, 'win-amd64'), 'win-x64')

    def test_windows_x86_interpreter(self):
        self.assertEqual(VERIFY.host_target('Windows', 'AMD64', 4, 'win32'), 'win-x86')

    def test_linux_arm64_has_no_shipped_host_target(self):
        self.assertIsNone(VERIFY.host_target('Linux', 'aarch64', 8, 'linux-aarch64'))

    def test_apple_hosts(self):
        self.assertEqual(VERIFY.host_target('Darwin', 'arm64', 8, 'macosx'), 'macos-arm64')
        self.assertEqual(VERIFY.host_target('Darwin', 'x86_64', 8, 'macosx'), 'macos-x64')


class BinaryMachineTests(unittest.TestCase):
    """Inspect minimal PE headers rather than attempting to execute foreign code."""

    def binary(self, machine, magic):
        """Create enough PE structure for machine, pointer-width and import checks."""
        data = bytearray(512)
        data[:2] = b'MZ'
        struct.pack_into('<I', data, 0x3c, 0x80)
        data[0x80:0x84] = b'PE\0\0'
        struct.pack_into('<H', data, 0x84, machine)
        struct.pack_into('<H', data, 0x94, 240)
        struct.pack_into('<H', data, 0x98, magic)
        return bytes(data)

    def verify(self, machine, magic, target):
        """Write one temporary test image and run its static validation."""
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / 'probe.dll'
            file.write_bytes(self.binary(machine, magic))
            VERIFY.verify_binary(file, target)

    def test_each_windows_architecture(self):
        for target, machine, magic in [('win-x86', 0x14c, 0x10b), ('win-x64', 0x8664, 0x20b), ('win-arm64', 0xaa64, 0x20b)]:
            with self.subTest(target=target):
                self.verify(machine, magic, target)

    def test_x64_image_mislabeled_as_arm64(self):
        with self.assertRaisesRegex(AssertionError, 'wrong PE machine'):
            self.verify(0x8664, 0x20b, 'win-arm64')

    def test_arm64_image_mislabeled_as_x64(self):
        with self.assertRaisesRegex(AssertionError, 'wrong PE machine'):
            self.verify(0xaa64, 0x20b, 'win-x64')

    def test_wrong_arm64_pointer_width(self):
        with self.assertRaisesRegex(AssertionError, 'wrong PE pointer width'):
            self.verify(0xaa64, 0x10b, 'win-arm64')


class AndroidAlignmentTests(unittest.TestCase):
    """Keep Android PT_LOAD offsets compatible with both 4 KiB and 16 KiB loaders."""

    def verify(self, alignment=16384, address=0, elf_class=2):
        """Create a minimal ELF image containing one loadable segment."""
        data = bytearray(512)
        data[:6] = b'\x7fELF' + bytes((elf_class, 1))
        machine = 183 if elf_class == 2 else 40
        struct.pack_into('<H', data, 18, machine)
        if elf_class == 2:
            struct.pack_into('<Q', data, 32, 64)
            struct.pack_into('<HH', data, 54, 56, 1)
            struct.pack_into('<I', data, 64, 1)
            struct.pack_into('<QQ', data, 72, 0, address)
            struct.pack_into('<Q', data, 96, len(data))
            struct.pack_into('<Q', data, 112, alignment)
        else:
            struct.pack_into('<I', data, 28, 52)
            struct.pack_into('<HH', data, 42, 32, 1)
            struct.pack_into('<III', data, 52, 1, 0, address)
            struct.pack_into('<I', data, 68, len(data))
            struct.pack_into('<I', data, 80, alignment)
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / 'probe.so'
            file.write_bytes(data)
            VERIFY.verify_binary(file, 'android-arm64' if elf_class == 2 else 'android-armv7')

    def test_both_android_architectures_have_16k_segments(self):
        self.verify(elf_class=1)
        self.verify(elf_class=2)

    def test_4k_only_segment_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, 'invalid Android PT_LOAD alignment'):
            self.verify(alignment=4096)

    def test_alignment_must_be_power_of_two(self):
        with self.assertRaisesRegex(AssertionError, 'invalid Android PT_LOAD alignment'):
            self.verify(alignment=24576)

    def test_offset_address_congruence_is_required(self):
        with self.assertRaisesRegex(AssertionError, 'offset/address mismatch'):
            self.verify(address=4096)


class ImporterTests(unittest.TestCase):
    """A correct binary cannot compensate for a wrong Unity importer architecture."""

    def verify(self, text, target='win-arm64', filename='probe.dll'):
        """Run the importer check against a temporary paired asset."""
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / filename
            Path(str(file) + '.meta').write_text(text, encoding='utf-8')
            VERIFY.verify_importer(file, target)

    def test_all_production_importers(self):
        for target, expected in VERIFY.TARGET_IMPORTERS.items():
            if expected is not None:
                with self.subTest(target=target):
                    self.verify(importer_text(*expected), target)

    def test_bridge_requires_preload(self):
        with self.assertRaisesRegex(AssertionError, 'bridge must preload'):
            self.verify(importer_text('Standalone: Win64', 'ARM64'), filename='FFmpegUnityBridge.dll')

    def test_bridge_preload_enabled(self):
        text = importer_text('Standalone: Win64', 'ARM64').replace('  platformData:', '  isPreloaded: 1\n  platformData:')
        self.verify(text, filename='FFmpegUnityBridge.dll')

    def test_arm64_wrong_cpu(self):
        with self.assertRaisesRegex(AssertionError, 'wrong Player platform/CPU'):
            self.verify(importer_text('Standalone: Win64', 'x86_64'))

    def test_arm64_must_not_load_in_x64_editor(self):
        with self.assertRaisesRegex(AssertionError, 'wrong Editor enablement'):
            self.verify(importer_text('Standalone: Win64', 'ARM64', 'Windows'))

    def test_arm64_editor_defaults_must_be_initialized(self):
        for replacement in ('', '        DefaultValueInitialized: false\n'):
            with self.subTest(replacement=replacement):
                text = importer_text('Standalone: Win64', 'ARM64').replace('        DefaultValueInitialized: true\n', replacement)
                with self.assertRaisesRegex(AssertionError, 'ARM64 Editor defaults must be initialized'):
                    self.verify(text)

    def test_other_targets_allow_legacy_editor_settings(self):
        for target, expected in VERIFY.TARGET_IMPORTERS.items():
            if target != 'win-arm64' and expected is not None:
                with self.subTest(target=target):
                    text = importer_text(*expected).replace('        DefaultValueInitialized: true\n', '')
                    self.verify(text, target)

    def test_any_platform_enabled(self):
        text = importer_text('Standalone: Win64', 'ARM64').replace('enabled: 0', 'enabled: 1', 1)
        with self.assertRaisesRegex(AssertionError, 'Any platform must be disabled'):
            self.verify(text)

    def test_enabled_unrelated_platform(self):
        extra = '  - first:\n      Android: Android\n    second:\n      enabled: 1\n      settings:\n        CPU: ARM64\n'
        # Inject after rendering to preserve the real Unity indentation.
        text = importer_text('Standalone: Win64', 'ARM64').replace('  userData:', extra + '  userData:')
        with self.assertRaisesRegex(AssertionError, 'enabled unrelated platform'):
            self.verify(text)

    def test_invalid_guid(self):
        text = importer_text('Standalone: Win64', 'ARM64').replace('0123456789abcdef0123456789abcdef', 'invalid')
        with self.assertRaisesRegex(AssertionError, 'invalid Unity GUID'):
            self.verify(text)


class BridgeSourceTests(unittest.TestCase):
    """Detect stale compiled inputs without false positives for line endings or tests."""

    def verify(self, current, recorded, normalized=None):
        """Verify one mocked production input against a recorded build snapshot."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / 'Tools/FFmpeg/Native'
            source.mkdir(parents=True)
            (source / 'Bridge.cpp').write_bytes(current)
            manifest = {'sourceSha256': {'Bridge.cpp': hashlib.sha256(recorded).hexdigest()}}
            if normalized is not None:
                manifest['sourceSha256Lf'] = {'Bridge.cpp': normalized}
            with patch.object(VERIFY, 'ROOT', root), patch.object(VERIFY, 'bridge_source_inputs', return_value={'Bridge.cpp'}):
                VERIFY.verify_bridge_sources(manifest, 'win-arm64')

    def test_lf_manifest_matches_crlf_checkout(self):
        self.verify(b'new source\r\n', b'new source\n')

    def test_crlf_manifest_matches_lf_checkout(self):
        self.verify(b'new source\n', b'new source\r\n')

    def test_normalized_provenance(self):
        self.verify(b'new source\r\n', b'new source\n', hashlib.sha256(b'new source\n').hexdigest())

    def test_real_source_change_requires_rebuild(self):
        with self.assertRaisesRegex(AssertionError, 'rebuild bridge'):
            self.verify(b'new source\n', b'old source\n')

    def test_normalized_provenance_cannot_mask_raw_source_change(self):
        with self.assertRaisesRegex(AssertionError, 'rebuild bridge'):
            self.verify(b'new source\n', b'old source\n', hashlib.sha256(b'new source\n').hexdigest())

    def test_bad_normalized_hash_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, 'normalized bridge source mismatch'):
            self.verify(b'new source\n', b'new source\n', '0' * 64)

    def test_platform_inputs_exclude_foreign_code_and_tests(self):
        apple = VERIFY.bridge_source_inputs('macos-arm64')
        android = VERIFY.bridge_source_inputs('android-arm64')
        windows = VERIFY.bridge_source_inputs('win-x86')
        self.assertNotIn('D3D11.cpp', apple)
        self.assertNotIn('AndroidMediaCodec.cpp', windows)
        self.assertNotIn('Metal.mm', android)
        self.assertIn('ExportsWin32MinGW.def', windows)
        self.assertIn('VulkanPortable.cpp', android)
        for target in VERIFY.TARGET_IMPORTERS:
            self.assertFalse(any(name.startswith('tests/') or name == 'README.md' for name in VERIFY.bridge_source_inputs(target)))


class DependencySelectionTests(unittest.TestCase):
    """ARM64-only builds must not download unsupported vendor encoding SDKs."""

    def prepare(self, targets):
        """Observe preparation choices without invoking Git or the network."""
        names = ('ensure_dav1d', 'ensure_meson', 'ensure_encoder_source', 'ensure_vulkan_headers',
                 'ensure_nvcodec_headers', 'ensure_amf_headers', 'ensure_llvm')
        with ExitStack() as stack:
            mocks = {name: stack.enter_context(patch.object(DEPENDENCIES, name)) for name in names}
            DEPENDENCIES.prepare(targets, Path('unused-cache'))
            return {name: mock.call_count for name, mock in mocks.items()}

    def test_arm64_only_skips_vendor_sdk_downloads(self):
        calls = self.prepare(['win-arm64'])
        self.assertEqual(calls['ensure_nvcodec_headers'], 0)
        self.assertEqual(calls['ensure_amf_headers'], 0)
        self.assertEqual(calls['ensure_llvm'], 1)
        self.assertEqual(calls['ensure_vulkan_headers'], 1)
        self.assertEqual(calls['ensure_dav1d'], 1)
        self.assertEqual(calls['ensure_encoder_source'], 4)

    def test_mixed_windows_targets_keep_vendor_headers(self):
        for target in ('win-x86', 'win-x64'):
            with self.subTest(target=target):
                calls = self.prepare(['win-arm64', target])
                self.assertEqual(calls['ensure_nvcodec_headers'], 1)
                self.assertEqual(calls['ensure_amf_headers'], 1)
                self.assertEqual(calls['ensure_llvm'], 1)

    def test_linux_keeps_nvcodec_without_windows_sdk(self):
        calls = self.prepare(['linux-x64'])
        self.assertEqual(calls['ensure_nvcodec_headers'], 1)
        self.assertEqual(calls['ensure_amf_headers'], 0)
        self.assertEqual(calls['ensure_llvm'], 0)

    def test_android_does_not_prepare_windows_dependencies(self):
        calls = self.prepare(['android-arm64', 'android-armv7'])
        self.assertEqual(calls['ensure_vulkan_headers'], 1)
        self.assertEqual(calls['ensure_nvcodec_headers'], 0)
        self.assertEqual(calls['ensure_amf_headers'], 0)
        self.assertEqual(calls['ensure_llvm'], 0)


class AssetPairTests(unittest.TestCase):
    """Check that build records and directories, not only DLLs, have stable metadata."""

    def verify(self, metadata=True, duplicate=False, orphan=False):
        """Create a small staged target with manifest and directory metadata."""
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / 'arm64'
            target.mkdir()
            Path(str(target) + '.meta').write_text('guid: ' + '1' * 32 + '\nfolderAsset: yes\n', encoding='utf-8')
            (target / 'build-manifest.json').write_text('{}', encoding='utf-8')
            if metadata:
                (target / 'build-manifest.json.meta').write_text('guid: ' + ('1' if duplicate else '2') * 32 + '\n', encoding='utf-8')
            if orphan:
                (target / 'orphan.dll.meta').write_text('guid: ' + '3' * 32 + '\n', encoding='utf-8')
            VERIFY.verify_asset_pairs(target, 'win-arm64')

    def test_paired_metadata(self):
        self.verify()

    def test_manifest_without_metadata_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, 'missing paired Unity metadata'):
            self.verify(metadata=False)

    def test_duplicate_guid_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, 'duplicate Unity GUID'):
            self.verify(duplicate=True)

    def test_orphan_metadata_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, 'orphan Unity metadata'):
            self.verify(orphan=True)


class AuditCompletenessTests(unittest.TestCase):
    """Prevent partial inventories or missing bridge manifests from reporting success."""

    def run_audit(self, requested, bridge=True):
        """Run the CLI orchestration with deterministic static-only test artifacts."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            native = root / 'Native'
            native.mkdir()
            payload = b'synthetic artifact'
            digest = hashlib.sha256(payload).hexdigest()
            files = []
            for name, major in LOCK['abi'].items():
                filename = f'{name}-{major}.dll'
                (native / filename).write_bytes(payload)
                files.append({'file': filename, 'sha256': digest, 'bytes': len(payload)})
            manifest = {'target': 'win-arm64', 'source': copy.deepcopy(LOCK), 'files': files}
            (native / 'build-manifest.json').write_text(json.dumps(manifest), encoding='utf-8')
            if bridge:
                (native / 'FFmpegUnityBridge.dll').write_bytes(payload)
                bridge_manifest = {'target': 'win-arm64', 'bridgeAbi': 4,
                                   'files': [{'file': 'FFmpegUnityBridge.dll', 'sha256': digest, 'bytes': len(payload)}]}
                (native / 'bridge-manifest.json').write_text(json.dumps(bridge_manifest), encoding='utf-8')
            with ExitStack() as stack:
                stack.enter_context(patch.object(VERIFY, 'NATIVE', native))
                stack.enter_context(patch.dict(VERIFY.os.environ, {'FFMPEG_BUILD_ROOT': str(root / 'Build')}))
                stack.enter_context(patch.object(sys, 'argv', ['verify-artifacts.py', '--targets', requested, '--skip-host-load']))
                for name in ('verify_source_patches', 'verify_software_encoder_provenance', 'verify_binary', 'verify_asset_pairs',
                             'verify_importer', 'verify_bridge_sources'):
                    stack.enter_context(patch.object(VERIFY, name))
                output = stack.enter_context(redirect_stdout(io.StringIO()))
                VERIFY.main()
                return output.getvalue()

    def test_complete_requested_target(self):
        self.assertIn('PASS 7 FFmpeg libraries', self.run_audit('win-arm64'))

    def test_missing_requested_target(self):
        with self.assertRaisesRegex(AssertionError, 'missing requested FFmpeg target'):
            self.run_audit('win-arm64,win-x64')

    def test_all_requires_all_production_architectures(self):
        with self.assertRaisesRegex(AssertionError, 'missing requested FFmpeg target'):
            self.run_audit('all')

    def test_missing_bridge_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, 'missing matching Unity bridge'):
            self.run_audit('win-arm64', bridge=False)


if __name__ == '__main__':
    unittest.main()

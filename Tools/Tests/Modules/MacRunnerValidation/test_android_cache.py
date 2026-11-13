"""Regression checks for Android native caches copied to a different workspace."""

from contextlib import redirect_stdout
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[4]
SPEC = importlib.util.spec_from_file_location(
    "prepare_android_cache", ROOT / "Tools/CI/prepare_android_cache.py"
)
ANDROID_CACHE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ANDROID_CACHE)


class AndroidCacheTests(unittest.TestCase):
    def make_cache(self, export, architecture, output_root):
        cache = export / f"unityLibrary/build/il2cpp_{architecture}_ReleasePlus/il2cpp_cache"
        graph = cache / "buildstate/bee.dag.json"
        graph.parent.mkdir(parents=True)
        output = output_root / f"src/main/jniLibs/{architecture}/libil2cpp.so"
        graph.write_text(json.dumps({"Nodes": [{"Outputs": [output.as_posix()]}]}))
        (cache / "native-object.o").write_bytes(b"compiled object")
        return cache

    def prepare(self, export):
        with redirect_stdout(io.StringIO()):
            return ANDROID_CACHE.prepare_cache(export)

    def test_relocated_graphs_are_invalidated_even_when_the_old_workspace_exists(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            export = root / "new volume/export with spaces"
            old_library = root / "old volume/unityLibrary"
            old_library.mkdir(parents=True)
            (old_library / "old-output.so").write_bytes(b"old binary")
            stale = [self.make_cache(export, abi, old_library)
                     for abi in ("armeabi-v7a", "arm64-v8a")]
            current = self.make_cache(export, "current", export / "unityLibrary")
            original_time = (current / "native-object.o").stat().st_mtime_ns
            retained = [export / "unityLibrary/build/intermediates/object.o",
                        export / "unityLibrary/symbols/arm64-v8a/libil2cpp.so",
                        export / ".gradle/cache"]
            for file in retained:
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_bytes(b"keep")
            self.assertEqual(self.prepare(export), 2)
            self.assertTrue(all(not cache.exists() for cache in stale))
            self.assertEqual(self.prepare(export), 0)
            self.assertEqual((current / "native-object.o").stat().st_mtime_ns, original_time)
            self.assertEqual((old_library / "old-output.so").read_bytes(), b"old binary")
            self.assertTrue(all(file.read_bytes() == b"keep" for file in retained))

    def test_first_export_needs_no_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            export = Path(temporary) / "not-exported"
            self.assertEqual(self.prepare(export), 0)
            self.assertFalse(export.exists())

    def test_unknown_graph_layout_fails_without_deleting_the_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            export = Path(temporary).resolve()
            cache = self.make_cache(export, "arm64-v8a", export / "unityLibrary")
            (cache / "buildstate/bee.dag.json").write_text('{"Nodes": []}')
            with self.assertRaisesRegex(RuntimeError, "No IL2CPP library output"):
                self.prepare(export)
            self.assertTrue((cache / "native-object.o").exists())

    @unittest.skipIf(os.name == "nt", "Run symlink checks on the macOS runner.")
    def test_aliases_of_the_same_workspace_keep_the_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            export = root / "export"
            export.mkdir()
            alias = root / "alias"
            alias.symlink_to(export, target_is_directory=True)
            cache = self.make_cache(export, "arm64-v8a", alias / "unityLibrary")
            self.assertEqual(self.prepare(export), 0)
            self.assertTrue(cache.exists())

    def test_relative_graph_outputs_fail_without_deleting_the_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            export = Path(temporary).resolve()
            cache = self.make_cache(export, "arm64-v8a", Path("relative/unityLibrary"))
            with self.assertRaisesRegex(RuntimeError, "Expected absolute"):
                self.prepare(export)
            self.assertTrue((cache / "native-object.o").exists())

    @unittest.skipIf(os.name == "nt", "Run symlink checks on the macOS runner.")
    def test_cache_symlinks_cannot_delete_another_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            outside = self.make_cache(root / "other", "arm64-v8a", root / "old")
            export = root / "export"
            parent = export / "unityLibrary/build/il2cpp_arm64-v8a_ReleasePlus"
            parent.mkdir(parents=True)
            (parent / "il2cpp_cache").symlink_to(outside, target_is_directory=True)
            with self.assertRaisesRegex(RuntimeError, "Refusing"):
                self.prepare(export)
            self.assertTrue((outside / "native-object.o").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)

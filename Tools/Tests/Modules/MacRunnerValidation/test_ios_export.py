"""Regression checks for first and incremental Unity iOS exports."""

from contextlib import redirect_stdout
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[4]
SCRIPT = ROOT / "Tools/CI/prepare_ios_export.py"
SPEC = importlib.util.spec_from_file_location("prepare_ios_export", SCRIPT)
IOS_EXPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(IOS_EXPORT)


class IosExportTests(unittest.TestCase):
    def test_first_export_omits_append_without_creating_output_directories(self):
        with tempfile.TemporaryDirectory() as temporary:
            export = Path(temporary) / "Release/iOS/MajdataPlay"
            self.assertEqual(IOS_EXPORT.custom_parameters(export), "")
            self.assertFalse(export.exists())

    def test_empty_export_and_project_shell_omit_append(self):
        with tempfile.TemporaryDirectory() as temporary:
            export = Path(temporary) / "MajdataPlay"
            export.mkdir()
            self.assertEqual(IOS_EXPORT.custom_parameters(export), "")
            project = export / "Unity-iPhone.xcodeproj"
            project.mkdir()
            self.assertEqual(IOS_EXPORT.custom_parameters(export), "")
            (project / "project.pbxproj").touch()
            self.assertEqual(IOS_EXPORT.custom_parameters(export), "")

    def test_existing_project_with_spaces_appends_and_preserves_build_caches(self):
        with tempfile.TemporaryDirectory() as temporary:
            export = Path(temporary) / "export with spaces/MajdataPlay"
            project = export / "Unity-iPhone.xcodeproj/project.pbxproj"
            project.parent.mkdir(parents=True)
            project.write_text("// !$*UTF8*$!\n{ objects = {}; }\n", encoding="utf-8")
            cache = export.parent / "Il2CppBuildCache/Release/artifacts/object.o"
            cache.parent.mkdir(parents=True)
            cache.write_bytes(b"native object")
            original_time = project.stat().st_mtime_ns
            for _ in range(2):
                self.assertEqual(IOS_EXPORT.custom_parameters(export), IOS_EXPORT.APPEND_PARAMETER)
            self.assertEqual(project.stat().st_mtime_ns, original_time)
            self.assertEqual(cache.read_bytes(), b"native object")

    def test_cli_writes_step_output_for_first_and_incremental_exports(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            export = directory / "export with spaces"
            output_file = directory / "github output"
            environment = dict(os.environ, GITHUB_OUTPUT=str(output_file))
            first = subprocess.run([sys.executable, "-B", str(SCRIPT), str(export)],
                                   env=environment, check=True, capture_output=True, text=True)
            self.assertEqual(first.stdout, "")
            self.assertEqual(output_file.read_text(encoding="utf-8"), "custom_parameters=\n")
            project = export / "Unity-iPhone.xcodeproj/project.pbxproj"
            project.parent.mkdir(parents=True)
            project.write_text("// !$*UTF8*$!\n{ objects = {}; }\n", encoding="utf-8")
            subprocess.run([sys.executable, "-B", str(SCRIPT), str(export)],
                           env=environment, check=True, capture_output=True, text=True)
            self.assertEqual(output_file.read_text(encoding="utf-8"),
                             "custom_parameters=\ncustom_parameters=" + IOS_EXPORT.APPEND_PARAMETER + "\n")

    def test_missing_github_output_prints_the_selected_parameters(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = io.StringIO()
            with redirect_stdout(output):
                IOS_EXPORT.prepare_export(Path(temporary) / "missing")
            self.assertEqual(output.getvalue(), "custom_parameters=\n")


if __name__ == "__main__":
    unittest.main(verbosity=2)

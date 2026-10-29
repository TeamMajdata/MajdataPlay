"""Regression checks for persistent macOS runner licensing and checkout cleanup."""

import base64
from contextlib import redirect_stdout
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[4]
SPEC = importlib.util.spec_from_file_location(
    "prepare_unity_license", ROOT / "Tools/CI/prepare_unity_license.py"
)
LICENSING = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(LICENSING)
SERIAL = "F4-AAAA-BBBB-CCCC-DDDD-EEEE"
ENVIRONMENT = {
    "UNITY_SERIAL": SERIAL,
    "UNITY_EMAIL": "private@example.invalid",
    "UNITY_PASSWORD": "private-password",
}
ULF = '<root><License id="Terms"/></root>'


class LicensingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.license_directory = self.directory / "licenses"
        self.license_directory.mkdir()
        self.license_file = self.license_directory / "Unity_lic.ulf"

    def prepare(self, environment, run):
        output = io.StringIO()
        with redirect_stdout(output):
            LICENSING.prepare_license(Path("Unity"), self.license_directory,
                                      self.directory, environment, run)
        return output.getvalue()

    def test_decodes_gameci_legacy_serial(self):
        encoded = base64.b64encode(b"\x01\x00\x00\x00" + SERIAL.encode()).decode()
        xml = f'<root><License><DeveloperData Value="{encoded}"/></License></root>'
        self.assertEqual(LICENSING.serial_from_environment({"UNITY_LICENSE": xml}), SERIAL)

    def test_rejects_malformed_license_without_echoing_secrets(self):
        for environment in ({"UNITY_LICENSE": "private-invalid-xml"},
                            {"UNITY_SERIAL": "private-invalid-serial"}):
            with self.subTest(environment=environment), self.assertRaises(RuntimeError) as error:
                LICENSING.serial_from_environment(environment)
            self.assertNotIn("private-", str(error.exception))

    def test_existing_license_needs_no_credentials_or_activation(self):
        self.license_file.write_text(ULF)
        def unexpected_run(*args, **kwargs):
            self.fail("A persistent license must not activate again.")
        self.assertIn("Reusing", self.prepare({}, unexpected_run))

    def test_missing_system_directory_requires_explicit_admin_setup(self):
        self.license_directory.rmdir()
        with self.assertRaisesRegex(RuntimeError, "sudo install -d"):
            self.prepare(ENVIRONMENT, None)
        self.assertFalse(self.license_directory.exists())

    def test_first_activation_keeps_license_and_hides_output(self):
        def activate(command, **kwargs):
            self.assertIn(SERIAL, command)
            self.assertIn("-createProject", command)
            self.assertNotIn("-projectPath", command)
            self.assertNotIn("-returnlicense", command)
            self.assertEqual(kwargs["stdout"], subprocess.DEVNULL)
            self.assertEqual(kwargs["stderr"], subprocess.DEVNULL)
            self.license_file.write_text(ULF)
            return subprocess.CompletedProcess(command, 0)
        output = self.prepare(ENVIRONMENT, activate)
        self.assertTrue(self.license_file.exists())
        for secret in ENVIRONMENT.values():
            self.assertNotIn(secret, output)
        if os.name != "nt":
            self.assertEqual(self.license_file.stat().st_mode & 0o777, 0o600)
            log = self.directory / "license-activation.log"
            self.assertEqual(log.stat().st_mode & 0o777, 0o600)

    def test_nonzero_exit_is_not_treated_as_success(self):
        def failed_activation(command, **kwargs):
            self.license_file.write_text(ULF)
            return subprocess.CompletedProcess(command, 1)
        with self.assertRaisesRegex(RuntimeError, "exit 1"):
            self.prepare(ENVIRONMENT, failed_activation)

    def test_zero_exit_without_license_is_not_treated_as_success(self):
        with self.assertRaisesRegex(RuntimeError, "activation failed"):
            self.prepare(ENVIRONMENT, lambda command, **kwargs: subprocess.CompletedProcess(command, 0))


class WorkspaceTests(unittest.TestCase):
    def test_ios_export_preserves_the_native_incremental_project(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        apple_job = workflow.split("  build-apple:", 1)[1].split("  publish-nightly:", 1)[0]
        self.assertIn("'-AcceptExternalModificationsToPlayer'", apple_job)
        self.assertIn("clean: false,", apple_job)
        self.assertIn('"Release", "iOS", "DerivedData"', apple_job)

    @unittest.skipIf(os.name == "nt", "Run Bash checkout cleanup tests on the macOS runner.")
    def test_workflow_cleanup_preserves_git_bee_and_native_builds(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        marker = "      - name: Clean previous job files while retaining incremental build data\n"
        blocks = workflow.split(marker)[1:]
        self.assertEqual(len(blocks), 2)
        for block in blocks:
            script = block.split("        run: |\n", 1)[1].split("\n\n", 1)[0]
            script = "\n".join(line[10:] for line in script.splitlines())
            with self.subTest(script=script), tempfile.TemporaryDirectory() as temporary:
                project = Path(temporary) / "project"
                project.mkdir()
                subprocess.run(["git", "init", str(project)], check=True, capture_output=True)
                preserved = ["Library/Bee/artifact", "Release/iOS/DerivedData/object.o",
                             "Release/Android/MajdataPlay/.gradle/cache"]
                removed = ["out/old.apk", "android-publish.keystore", "Assets/Editor/UnityBuilderAction/stale.cs"]
                for relative in preserved + removed:
                    file = project / relative
                    file.parent.mkdir(parents=True, exist_ok=True)
                    file.write_text("test")
                environment = dict(os.environ, PROJECT_ROOT=str(project))
                # Run the actual workflow script, including its first-checkout guard.
                for _ in range(2):
                    subprocess.run(["bash", "-c", script], env=environment, check=True, capture_output=True)
                self.assertTrue((project / ".git/HEAD").exists())
                for relative in preserved:
                    self.assertTrue((project / relative).exists(), relative)
                for relative in removed:
                    self.assertFalse((project / relative).exists(), relative)
                environment["PROJECT_ROOT"] = str(project / "not-yet-cloned")
                subprocess.run(["bash", "-c", script], env=environment, check=True, capture_output=True)


if __name__ == "__main__":
    unittest.main(verbosity=2)

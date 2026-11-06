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
IOS_SPEC = importlib.util.spec_from_file_location(
    "prepare_ios_cache", ROOT / "Tools/CI/prepare_ios_cache.py"
)
IOS_CACHE = importlib.util.module_from_spec(IOS_SPEC)
IOS_SPEC.loader.exec_module(IOS_CACHE)
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


class IosCacheTests(unittest.TestCase):
    def test_archive_cache_is_migrated_and_patch_is_idempotent(self):
        import json
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / "MajdataPlay/Unity-iPhone.xcodeproj/project.pbxproj"
            project.parent.mkdir(parents=True)
            script = ('mkdir -p "$CONFIGURATION_TEMP_DIR/artifacts/arm64/buildstate"\n'
                      'ln -sF "$CONFIGURATION_TEMP_DIR/artifacts" "$PROJECT_DIR/cache"\n'
                      'il2cpp --compile-cpp --cachedirectory=cache\n')
            untouched = 'shellScript = "echo unrelated";'
            project.write_text("shellScript = " + json.dumps(script) + ";\n" + untouched)
            original_cache = root / (
                "DerivedData/Build/Intermediates.noindex/ArchiveIntermediates/Unity-iPhone/"
                "IntermediateBuildFilesPath/Unity-iPhone.build/Release-iphoneos/artifacts"
            )
            original_cache.mkdir(parents=True)
            (original_cache / "object.o").write_bytes(b"native object")
            original_time = (original_cache / "object.o").stat().st_mtime_ns
            with redirect_stdout(io.StringIO()):
                IOS_CACHE.prepare_cache(project, "Release")
                once = project.read_text()
                IOS_CACHE.prepare_cache(project, "Release")
            self.assertEqual(project.read_text(), once)
            self.assertIn(untouched, once)
            self.assertNotIn(IOS_CACHE.ARCHIVE_CACHE, once)
            self.assertIn(IOS_CACHE.PERSISTENT_CACHE, once)
            persistent = root / "Il2CppBuildCache/Release/artifacts/object.o"
            self.assertEqual(persistent.read_bytes(), b"native object")
            self.assertEqual(persistent.stat().st_mtime_ns, original_time)
            self.assertFalse(original_cache.exists())

    def test_append_export_removes_duplicate_phase_references_per_target(self):
        import json
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "MajdataPlay/Unity-iPhone.xcodeproj/project.pbxproj"
            project.parent.mkdir(parents=True)
            phase = "C62A2A42F32E085EF849CF0B"
            sources = "7F4E059C2717216D00A2CBE4"
            phases = ("buildPhases = (\n"
                      f"    {sources} /* Sources */,\n"
                      f"    {phase} /* Run Script */,\n"
                      f"    {phase} /* Run Script */,\n"
                      f"    {phase} /* Run Script */,\n"
                      ");\n"
                      "buildPhases = (\n"
                      f"    {phase} /* Shared with another target */,\n"
                      ");\n")
            script = 'il2cpp --compile-cpp --cachedirectory=cache "' + IOS_CACHE.ARCHIVE_CACHE + '"'
            project.write_text(phases + "shellScript = " + json.dumps(script) + ";\n")
            with redirect_stdout(io.StringIO()):
                IOS_CACHE.prepare_cache(project, "Release")
                once = project.read_text()
                IOS_CACHE.prepare_cache(project, "Release")
            self.assertEqual(project.read_text(), once)
            self.assertEqual(once.count(phase), 2)
            self.assertIn(f"{sources} /* Sources */,\n    {phase} /* Run Script */,", once)
            self.assertIn(f"{phase} /* Shared with another target */,", once)

    def test_unexpected_unity_layout_is_not_silently_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "project.pbxproj"
            original = 'shellScript = "echo unknown layout";'
            project.write_text(original)
            with self.assertRaises(RuntimeError):
                IOS_CACHE.prepare_cache(project, "Release")
            self.assertEqual(project.read_text(), original)


class WorkspaceTests(unittest.TestCase):
    def test_mobile_and_macos_builds_use_different_runner_labels(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        android_job = workflow.split("  build-android:", 1)[1].split("  build-apple:", 1)[0]
        apple_job = workflow.split("  build-apple:", 1)[1].split("  publish-nightly:", 1)[0]
        self.assertIn("runs-on: [self-hosted, macOS, ARM64, majplay-android]", android_job)
        self.assertIn('runs-on: [self-hosted, macOS, ARM64, "${{ matrix.runnerLabel }}"]', apple_job)
        self.assertIn("platform: StandaloneOSX\n            runnerLabel: majplay-apple", apple_job)
        self.assertIn("platform: iOS\n            runnerLabel: majplay-ios", apple_job)
        self.assertNotIn("max-parallel: 1", apple_job)

    def test_self_hosted_jobs_use_only_local_caches_and_skip_aab_artifacts(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        android_job = workflow.split("  build-android:", 1)[1].split("  build-apple:", 1)[0]
        apple_job = workflow.split("  build-apple:", 1)[1].split("  publish-nightly:", 1)[0]
        self.assertIn("cache-disabled: true", android_job)
        self.assertNotIn("cache: 'gradle'", android_job)
        self.assertIn('flags: "--build-cache"', android_job)
        for job in (android_job, apple_job):
            self.assertNotIn("uses: actions/cache@", job)
        self.assertNotIn("Upload Play AAB outputs as workflow artifact", android_job)
        self.assertNotIn("/out/*.aab", android_job)
        self.assertIn("uses: r0adkll/upload-google-play@v1", android_job)

    def test_signing_keychain_is_unique_and_cleaned_after_failures(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        self.assertIn('CI_KEYCHAIN="$RUNNER_TEMP/majplay-${{ github.run_id }}-${{ github.run_attempt }}"',
                      workflow)
        self.assertIn("keychain: ${{ env.CI_KEYCHAIN }}", workflow)
        cleanup = workflow.split("      - name: Remove temporary signing keychain\n", 1)[1]
        self.assertIn("always()", cleanup.split("        run:", 1)[0])
        self.assertIn('security delete-keychain "$CI_KEYCHAIN.keychain"', cleanup)

    @unittest.skipIf(os.name == "nt", "Run Bash signing cleanup tests on the macOS runner.")
    def test_signing_cleanup_uses_the_absolute_keychain_path(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        preparation = workflow.split("      - name: Prepare temporary signing keychain\n", 1)[1]
        preparation = preparation.split("        run: |\n", 1)[1].split("\n\n", 1)[0]
        preparation = "\n".join(line[10:] for line in preparation.splitlines())
        preparation = preparation.replace("${{ github.run_id }}", "123")
        preparation = preparation.replace("${{ github.run_attempt }}", "2")
        cleanup = workflow.split("      - name: Remove temporary signing keychain\n", 1)[1]
        script = cleanup.split("        run: |\n", 1)[1].split("\n\n", 1)[0]
        script = "\n".join(line[10:] for line in script.splitlines())
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            keychain = directory / "runner temp.with spaces/majplay-123-2"
            keychain.parent.mkdir()
            database = Path(str(keychain) + ".keychain-db")
            database.write_text("test")
            tools = directory / "bin"
            tools.mkdir()
            security = tools / "security"
            security.write_text('#!/bin/bash\nprintf "%s\\n" "$@" > "$SECURITY_CALLS"\n')
            security.chmod(0o700)
            calls = directory / "security-calls"
            github_environment = directory / "github-env"
            environment = dict(os.environ, CI_KEYCHAIN=str(keychain),
                               HOME=str(directory / "different-home"), SECURITY_CALLS=str(calls),
                               RUNNER_TEMP=str(keychain.parent), GITHUB_ENV=str(github_environment),
                               GITHUB_OUTPUT=str(directory / "github-output"),
                               PATH=str(tools) + os.pathsep + os.environ["PATH"])
            subprocess.run(["bash", "-c", preparation], env=environment,
                           check=True, capture_output=True)
            self.assertEqual(github_environment.read_text().splitlines(),
                             ["CI_KEYCHAIN=" + str(keychain)])
            subprocess.run(["bash", "-c", script], env=environment,
                           check=True, capture_output=True)
            self.assertTrue(calls.exists(), "Cleanup must find the keychain outside HOME.")
            self.assertEqual(calls.read_text().splitlines(),
                             ["delete-keychain", str(keychain) + ".keychain"])
            database.unlink()
            calls.unlink()
            subprocess.run(["bash", "-c", script], env=environment,
                           check=True, capture_output=True)
            self.assertFalse(calls.exists(), "A failed import may not have created a keychain.")

    @unittest.skipIf(os.name == "nt", "Run Bash/Perl Android path tests on the macOS runner.")
    def test_android_sdk_paths_are_literal_gradle_strings(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        block = workflow.split("      - name: Fix Android SDK and NDK paths\n", 1)[1]
        script = block.split("        run: |\n", 1)[1].split("\n      - name:", 1)[0]
        script = "\n".join(line[10:] for line in script.splitlines())
        script = script.replace("${{ env.UNITY_PROJECT_EXPORT_PATH }}", ".")
        script = script.replace("${{ env.UNITY_VERSION }}", "6000.3.17f1")
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            sdk = project / "sdk-with.dots"
            unity_android = "/Applications/Unity/Hub/Editor/6000.3.17f1/PlaybackEngines/AndroidPlayer"
            gradle = project / "build.gradle"
            gradle.write_text(f'ndkPath "{unity_android}/NDK"\nsdkPath "{unity_android}/SDK"\n')
            environment = dict(os.environ, ANDROID_HOME=str(sdk))
            subprocess.run(["bash", "-c", script], cwd=project, env=environment,
                           check=True, capture_output=True)
            self.assertEqual(gradle.read_text(),
                             f'ndkPath "{sdk}/ndk/23.1.7779620"\nsdkPath "{sdk}"\n')
            self.assertIn(f"sdk.dir={sdk}\n", (project / "local.properties").read_text())

    def test_ios_export_preserves_the_native_incremental_project(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        apple_job = workflow.split("  build-apple:", 1)[1].split("  publish-nightly:", 1)[0]
        self.assertIn("'-AcceptExternalModificationsToPlayer'", apple_job)
        self.assertIn("clean: false,", apple_job)
        self.assertIn('"Release", "iOS", "DerivedData"', apple_job)

    @unittest.skipIf(os.name == "nt", "Run Bash checkout migration tests on the macOS runner.")
    def test_checkout_removes_a_submodule_deleted_by_the_new_revision(self):
        workflow = (ROOT / ".github/workflows/main.yml").read_text(encoding="utf-8")
        jobs = [workflow.split("  build-android:", 1)[1].split("  build-apple:", 1)[0],
                workflow.split("  build-apple:", 1)[1].split("  publish-nightly:", 1)[0]]
        marker = "      - name: Clean previous job files while retaining incremental build data\n"
        for job in jobs:
            block = job.split(marker, 1)[1]
            script = block.split("        run: |\n", 1)[1].split("\n\n", 1)[0]
            script = "\n".join(line[10:] for line in script.splitlines())
            with self.subTest(job=job.splitlines()[1]), tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary)
                project = directory / "project"
                source = directory / "dependency"

                def git(repository, *arguments):
                    return subprocess.run(["git", "-C", str(repository), *arguments],
                                          check=True, capture_output=True, text=True).stdout.strip()

                for repository in (project, source):
                    repository.mkdir()
                    git(repository, "init")
                    git(repository, "config", "user.name", "CI validation")
                    git(repository, "config", "user.email", "ci@example.invalid")
                (source / "old.asmdef").write_text("{}")
                git(source, "add", ".")
                git(source, "commit", "-m", "Old dependency")
                dependency = "Assets/Plugins/MajSimai"
                git(project, "-c", "protocol.file.allow=always", "submodule", "add",
                    str(source), dependency)
                git(project, "commit", "-m", "Use the submodule")
                previous = git(project, "rev-parse", "HEAD")
                git(project, "rm", "--cached", dependency)
                (project / ".gitmodules").write_text("")
                git(project, "add", ".gitmodules")
                git(project, "commit", "-m", "Replace the submodule with a package")
                current = git(project, "rev-parse", "HEAD")
                git(project, "checkout", "--force", previous)
                environment = dict(os.environ, PROJECT_ROOT=str(project))
                cleanup_first = job.index(marker) < job.index("      - name: Checkout the repository\n")
                if cleanup_first:
                    subprocess.run(["bash", "-c", script], env=environment,
                                   check=True, capture_output=True)
                git(project, "checkout", "--force", current)
                self.assertTrue((project / dependency / "old.asmdef").exists())
                if not cleanup_first:
                    subprocess.run(["bash", "-c", script], env=environment,
                                   check=True, capture_output=True)
                self.assertFalse((project / dependency).exists(),
                                 "Removed submodules must be cleaned after checkout.")
                self.assertTrue((project / ".git/modules" / dependency / "HEAD").exists())

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

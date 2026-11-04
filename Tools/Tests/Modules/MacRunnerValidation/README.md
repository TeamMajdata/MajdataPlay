# macOS runner validation

Run on macOS or Linux with Python 3, Bash and Git. The Linux runner also needs
Java 17 for the JVM temporary-directory regression check:

```bash
python3 -B Tools/Tests/Modules/MacRunnerValidation/test_mac_runner.py
```

The tests use temporary repositories and fake Unity activation results. They
check legacy GameCI serial decoding, persistent license reuse, private activation
logs, actionable administrator setup errors, activation failure handling, and
the actual workflow cleanup scripts. Cleanup must retain Git metadata,
`Library/Bee`, Gradle caches and Xcode DerivedData while removing stale artifacts,
signing files and injected build scripts. Repeating cleanup and an initial run
without a checkout are covered. Bash fixtures ignore SSH startup files so runner
profiles cannot overwrite their fake editor and SDK paths. Android SDK/NDK
replacement is exercised with
paths containing hyphens and dots to catch accidental Perl replacement escaping.
Android native and Java temporary files must use the disk-backed runner temp
directory rather than the host's quota-limited `/tmp`. The conditional iOS export
entry point and persistent Xcode build settings are checked as well. Unity tests
in `BuildHelperTests` cover missing and incomplete iOS exports with the real
`BuildPipeline.BuildCanBeAppended` API.
Cache migration preserves native object contents and timestamps, repeated patching
is idempotent, and an unexpected generated Xcode phase is rejected. Temporary
signing keychains use unique names and are cleaned up even after failed jobs.

This suite does not validate Unity entitlements, network activation, project
import, IL2CPP output, Xcode signing, or Android/iOS/macOS Player behavior. Run a
manual `Build Project` workflow twice to validate those build layers and compare
incremental build timings; manual dispatch does not publish releases or stores.

# macOS runner validation

Run on macOS with Python 3, Bash and Git:

```bash
python3 -B Tools/Tests/Modules/MacRunnerValidation/test_mac_runner.py
```

The tests use temporary repositories and fake Unity activation results. They
check legacy GameCI serial decoding, persistent license reuse, private activation
logs, actionable administrator setup errors, activation failure handling, and
the actual workflow cleanup scripts. Cleanup must retain Git metadata,
`Library/Bee`, Gradle caches and Xcode DerivedData while removing stale artifacts,
signing files and injected build scripts. Repeating cleanup and an initial run
without a checkout are covered. A real submodule-to-package migration checks
that both self-hosted jobs remove obsolete submodule worktrees after checkout
while retaining their Git object caches. Android SDK/NDK replacement is exercised with
paths containing hyphens and dots to catch accidental Perl replacement escaping.
The iOS append option and persistent Xcode build settings are checked as well.
Cache migration preserves native object contents and timestamps, repeated patching
is idempotent, and an unexpected generated Xcode phase is rejected. Temporary
signing keychains use absolute paths and unique names and are cleaned up even
after failed jobs. Cleanup also covers paths containing spaces, a different home
directory, and an import that did not create a keychain.

This suite does not validate Unity entitlements, network activation, project
import, IL2CPP output, Xcode signing, or Android/iOS/macOS Player behavior. Run a
manual `Build Project` workflow twice to validate those build layers and compare
incremental build timings; manual dispatch does not publish releases or stores.

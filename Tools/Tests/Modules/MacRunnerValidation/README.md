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
without a checkout are covered.

This suite does not validate Unity entitlements, network activation, project
import, IL2CPP output, Xcode signing, or Android/iOS/macOS Player behavior. Run a
manual `Build Project` workflow twice to validate those build layers and compare
incremental build timings; manual dispatch does not publish releases or stores.

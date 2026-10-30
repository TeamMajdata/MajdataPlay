# macOS self-hosted build runners

The Android and Apple jobs use Unity **6000.3.17f1** installed on the Mac, and run
as `majplay-ci`. Both services may use the existing HTTP proxy
`http://127.0.0.1:10808`; configure it in their service environment, including
lowercase `http_proxy` and `https_proxy` for tools that require them.

## Three concurrent runner instances

| Runner directory | Custom label | Build | Work directory |
| --- | --- | --- | --- |
| `~/actions-runner-android` | `majplay-android` | Android | `~/actions-runner-android/_work` |
| `~/actions-runner-apple` | `majplay-apple` | macOS | `~/actions-runner-apple/_work-macos` |
| `~/actions-runner-ios` | `majplay-ios` | iOS | `~/actions-runner-apple/_work` |

All three runners register with `self-hosted`, `macOS`, and `ARM64` labels. The
Apple matrix maps its two targets to different custom labels, so Android, macOS
and iOS can run simultaneously on the Mac. Work directories must be distinct:
concurrent jobs must not share the runner's `_temp` or `_actions` directories.
The iOS runner deliberately inherits the former Apple work directory to preserve
absolute paths in its existing IL2CPP cache. The macOS checkout is moved once to
`_work-macos`; its Git objects and Library are retained.

## One-time license directory setup

From an administrator account on the Mac:

```bash
sudo install -d -o majplay-ci -g staff -m 755 '/Library/Application Support/Unity'
```

The original activation failure occurred because Unity received a license
response but could not save `/Library/Application Support/Unity/Unity_lic.ulf`.
GameCI's macOS entrypoint attempts to create this directory with sudo; an
unprivileged runner cannot do so. Do not grant the runner passwordless sudo or
make the license directory world-writable.

`prepare_unity_license.py` serializes initial activation across both runner
services using a user-scoped file lock. It reuses a readable installed ULF, or
activates with `UNITY_SERIAL` (or the legacy serial decoded from `UNITY_LICENSE`),
`UNITY_EMAIL`, and `UNITY_PASSWORD` from repository secrets. Unity validates the
license entitlement on each build. Account credentials and activation logs are
never printed. The private activation log is kept at
`~/Library/Application Support/MajdataPlayCI/license-activation.log`; inspect it
locally if activation fails and do not upload it as a workflow artifact.

The build action uses `skipActivation: true` after preparation. Jobs retain the
license rather than returning it while another runner is building. The machine's
license must be managed as a persistent activation, including returning it before
decommissioning the Mac. A license in another user's Unity Hub session alone is
not proof that the runner account can build. To renew an invalid/expired license,
stop builds, return or repair the old license through Unity, then run the workflow
again; the preparation step only checks ULF structure, not remote entitlement.

## Persistent checkouts and incremental builds

The runner workspaces contain separate checkouts at `projects/Android`,
`projects/iOS`, and `projects/StandaloneOSX`. Each path remains stable between
runs, so Git reuses repository objects and initialized submodules. The Android
checkout retains full history because its version code uses `git rev-list`.
The first run at each new path still downloads a checkout.

Before checkout, `git clean` removes stale job files except root `Library/` and
`Release/`. Checkout uses `clean: false`, while its normal tracked-file reset
still discards previous CI modifications to tracked files. This is exclusively
for dedicated CI checkouts, not developers' working directories. Git metadata,
Unity's `Library/Bee`, Android's exported Gradle project and its caches, and
iOS's exported Xcode project and fixed DerivedData directory persist. Old `out/`
artifacts, signing files at the project root, and injected GameCI scripts are
removed. Changing platforms does not replace another target's Library. No remote
`actions/cache` restore/save is needed for these persistent self-hosted jobs.
The Android job also disables setup-java/setup-gradle remote caching and reuses
`~/.gradle` locally, including dependencies and the Gradle build cache enabled by
Fastlane. The Windows/Linux jobs run on ephemeral GitHub-hosted machines and
retain their separate Unity Library cache.

Unity's incremental build pipeline is enabled by default; retaining `Library/Bee`
and stable paths allows IL2CPP to reuse unchanged work. The iOS Unity export uses
`AcceptExternalModificationsToPlayer` (append mode), and Fastlane uses `clean: false`
and a fixed `Release/iOS/DerivedData`. Changes to Unity,
source, platform settings, generated Xcode projects or native toolchains may
invalidate portions of the cache; this does not guarantee a no-op rebuild.

Xcode's archive action recreates its intermediate directory even without an
explicit clean. `prepare_ios_cache.py` changes only the generated Unity IL2CPP
shell phase to put its artifacts in `Release/iOS/Il2CppBuildCache/<configuration>`
instead. The existing archive cache is moved there once, preserving compiled
objects and Bee state. The helper checks for the expected generated phase and
fails explicitly if a later Unity version changes its layout. This patch is
applied after each Unity export; no tracked Unity assets or installed Editor
files are patched.
The archive also uses a fixed `Release/iOS/archive/MajdataPlay.xcarchive` path so
successive CI runs replace their previous archive instead of accumulating dated
archives in the runner account's Xcode Organizer directory.

Each iOS job imports signing certificates into a keychain named for its run ID
and attempt, with a masked random password, and removes that keychain even if a
later step fails. This avoids the fixed `signing_temp` name colliding on the next
job of a persistent runner. The user's login keychain is not deleted.

Run `python3 -B Tools/Tests/Modules/MacRunnerValidation/test_mac_runner.py` on the
Mac for isolated licensing/cleanup regression tests. Validate actual activation
and builds with `workflow_dispatch`; dispatches retain artifacts without
publishing nightly releases, Google Play uploads or TestFlight submissions.
Android AABs stay on the runner and are not uploaded as workflow artifacts; APK
and symbol uploads continue. Push builds can still submit the AAB to Google Play.

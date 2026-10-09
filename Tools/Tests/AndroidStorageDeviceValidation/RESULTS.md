# Android storage / generated JNI / IL2CPP validation results

## Executed 2026-10-09

- Unity: **6000.3.17f1**, isolated project `Temp/AndroidStorageDeviceValidation`.
- Physical device: **Xiaomi Mi MIX 2S**, LineageOS, Android API **35**.
- Provider exercised: **`com.android.externalstorage.documents`** (local storage).
- Player: development **IL2CPP**, Release native compiler configuration,
  **High managed stripping**, production `MajdataPlayActivity` and SAF bridge.
- Dedicated package: `net.majdata.storagevalidation`; the main game was not rebuilt,
  replaced, or used for the tests.
- Universal APK contains both `lib/arm64-v8a/libil2cpp.so` and
  `lib/armeabi-v7a/libil2cpp.so`. Its SHA-256 is
  `8E8611132097E55C82F070647A1A52E5C0373CC159CCFFCC3582C935C9ED7B7A`.

| Installed ABI | Actual pointer size | Full process | Restart process | Grant-release process | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| arm64-v8a | 8 bytes | 12856 | 13262 | 13465 | full / replay / release passed |
| armeabi-v7a | 4 bytes | 14070 | 15001 | 15185 | full / replay / release passed |

Each durable result confirmed `EnableIl2Cpp: true` and SDK 35. Package-manager
ABI selection and Java process-bitness checks agreed with the managed pointer
size; compiling both libraries alone was not treated as two runtime passes.
The different full/replay process IDs establish a force-stop and real process
restart, not a second operation in the same JVM.

## Runtime coverage passed in each ABI

- Generated Intent and KeyEvent constructors/methods, typed and nullable results,
  independently owned returned references, borrowing/adoption and deterministic
  wrapper disposal. Disposing a returned wrapper did not invalidate its input.
- Signed Java byte arrays preserved bit patterns 0, 127, 128, and 255, null arrays
  and independent array copies. UTF-16 preserved Chinese text, embedded NUL and
  supplementary characters; nullable string/object results were preserved.
- An intentional Java exception became `JavaInvocationException`, retained its
  Java stack, cleared pending JVM exceptions, and allowed a following JNI call.
- The same JNI cases passed on a scoped-attached background worker. Generated
  Activity dispatch and the Unity Runnable adapter completed a managed UI callback.
- The actual system SAF picker returned a persisted read/write tree grant through
  the production activity-result callback, including IL2CPP/High stripping.
- The production facade/backend created a unique `MajdataPlay-validation-<guid>`
  child, created a file, and round-tripped **197,121 bytes** containing all 256 byte
  values through multiple bounded signed-byte JNI transfers and async helpers.
- The owned child was recursively removed after streams closed. The selected
  parent was retained. No preexisting document was deleted.
- After force-stop and restart, the persisted grant reopened without another
  picker and passed the same storage round-trip and cleanup.
- The newly acquired test grant was released explicitly after replay in each ABI.
  Final private bookkeeping is finished and its owned-child location is empty.

The device was unlocked by its owner, who completed the real picker selections.
The first launch in each installed ABI did not remain running while the device
was locked / under its background manager; relaunching after unlock completed.
No lock-screen or background-manager configuration was modified by the tools.

## Evidence and reproduction

The ignored `Temp/AndroidStorageDeviceValidation/DeviceResults/` contains
`arm64-full.json`, `arm64-replay.json`, `arm64-release.json`,
`armv7-full.json`, `armv7-replay.json`, `armv7-release.json`, and process logs.
Results contain mode, backend, process, bitness and pass/cleanup markers, not
selected document URIs. The URI-containing private state was not copied into
repository evidence or committed. The APK and build log are also ignored.

Use the build/runner commands in `README.md`. The runner validates the durable
terminal result against the current PID, selected mode, requested ABI pointer
size, and IL2CPP flag rather than accepting a historical log marker alone.
The test APK remains installed for reproduction and has been force-stopped.
Both test-acquired URI grants were released.

## Other verification

- Isolated Editor: unique categorized Activity/Intent/KeyEvent mappings, removed
  legacy root types, generated API guards and **all 338 original KeyCode names
  and values** passed, together with existing picker concurrency regressions.
- Player script compilation: Windows64, Linux64, macOS, iOS and Android passed.
- Android Player reference discovery regression: real IL2CPP Player CoreModule
  references located bundled SDK/JDK and `{UnityData}` without environment overrides.
- Source-generator managed suite: **37/37 cases** passed, including the new
  environment-free Android Player reference-discovery regression.
- Portable managed suite: **28 cases, 810 assertions**, warnings-as-errors passed.

## Not established by these results

These are isolated-app results on **one physical device and one local provider**.
They do not validate the full game's lifecycle/packaging, Mono Player behavior,
other Android releases/devices, cloud/remote or virtual-document providers,
stream pipe behavior, grant revocation, picker dismissal/late cancellation on
hardware, rename/collision semantics, seek/error branches, full cross-provider
copy/append/truncate coverage, R8 minification, or a production non-development
APK. Managed/Editor tests cover some of these contracts separately, but those
must not be presented as device passes. No all-platform runtime conclusion is
made from the successful script compilations.

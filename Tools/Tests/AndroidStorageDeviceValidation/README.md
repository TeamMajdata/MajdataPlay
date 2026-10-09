# Android SAF + generated JNI / IL2CPP device validation

This is a **real-device runtime harness**, not a provider double or an Editor
substitute. It runs production `StorageFile`, `StorageDirectory`,
`AndroidStorageAccess`, `AndroidDocumentStream`, the production activity-result
bridge, and generated Android wrappers. Only the small JNI input probe is
validation-specific. No UniTask or third-party package is required.

The harness has been built and executed on a physical device in both ARM64 and
ARMv7 IL2CPP processes. See `RESULTS.md` for the dated evidence and exact scope.
A successful script compilation alone is not a runtime pass. Keep separate
evidence for each actually executed ABI.

## Integration contract

Use Unity **6000.3.17f1**. The isolated builder stages a separate project at
`Temp/AndroidStorageDeviceValidation`, with package ID
`net.majdata.storagevalidation`, a development IL2CPP player, High managed
stripping, and the production `net.majdata.majdataplay.MajdataPlayActivity`
launcher. The target device is the connected physical Mi MIX 2S, API 35.

| Source | Isolated project destination / responsibility |
| --- | --- |
| `AndroidStorageDeviceValidation.cs` | `Assets/Validation/`, in a runtime assembly referencing `MajdataPlay.IO` and `MajdataPlay.Platform.Android` |
| `DeviceJniProbe.cs` | `Assets/Android/Runtime/Validation/DeviceJniProbe.cs`, **inside the same Android assembly compilation as the production annotated wrappers** |
| `DeviceJniProbe.java` | **`Assets/Plugins/Android/src/java/net/majdata/validation/DeviceJniProbe.java`**, matching the exact `Sources` attribute |
| Production `Runtime/` | Copy recursively; includes the annotated `Runtime/View/KeyEvent.cs` |
| Production Java and activity | Copy and package the real storage bridge, Unity dependencies, and launcher/activity-result implementation |

The probe's Java package is `net.majdata.validation`. Source-generator `Sources`
only supplies analysis input; the Java source must also be packaged into the
APK. Do not package SDK `android.jar`. Do not create a second annotated mapping
for `android.view.KeyEvent`: the probe declaration binds only the probe class.
Its Intent/KeyEvent results are strongly typed only when those declarations are
in the **same generator compilation**. Do not work around staging mistakes with
handwritten `AndroidJavaClass.Call*` dispatch.

The isolated project's modules include `androidjni`, `imgui`, and
`jsonserialize`. The harness's public static
`AndroidStorageDeviceValidation.Bootstrap()` has
`RuntimeInitializeOnLoadMethod(AfterSceneLoad)`, creates a persistent component,
and needs **no manually attached scene component**. Its namespace is
`MajdataPlay.Platform.Android.Runtime.Validation`.

The builder, deploy runner, and narrow unrelated-init stubs are parent-owned;
this runtime implementation does not modify existing production scripts,
assemblies, or generated Unity projects.

## Launch protocol and durable files

Launch extra: **`validationMode`**, a string:

- Missing, empty, `full`, or compatibility alias `pick`: run automatic JNI
  checks, open the real SAF directory picker, and run the binary storage check.
- **`replay`**: never open the picker. Read private saved state, require a
  successful `full` run and a different process ID, verify persisted read/write
  access, reopen the grant, and run a fresh binary check in a new owned child.
- **`release`**: optionally finish grant ownership bookkeeping. Release only a
  grant that did **not** exist in the pre-picker snapshot. A preexisting grant
  is retained, including any additional write access the picker may have added.

All modes run the automatic JNI preflight. A new `full` run refuses to overwrite
an active grant state: first use `replay` / `release` as appropriate. Do not use
`pm clear`, uninstall, or revoke the grant between `full` and `replay`; that
would destroy the persistence experiment. Replace-installing an APK preserves
state, but must preserve package identity/signing. Wait for the terminal full
marker before force-stopping.

Files live at **`Application.persistentDataPath`**:

| Basename | Contents |
| --- | --- |
| **`android-storage-device-result.json`** | URI-free status; overwritten and flushed at each phase |
| **`android-storage-device-result.txt`** | Latest marker and non-sensitive explanation, also flushed |
| **`android-storage-device-state.json`** | Private grant URI, complete pre-picker URI snapshot, grant ownership, full-process ID, completion state, and any currently owned child URI |

Result JSON uses PascalCase field names: `Marker`, `Terminal`, `Mode`, `RunId`,
`EnableIl2Cpp`, `PointerSize`, `SdkInt`, `ProcessId`, `CleanupSucceeded`,
`GrantWasPreexisting`, `GrantReleased`, `ErrorType`, `Detail`, and `UpdatedUtc`.
`Terminal: false` means progress, **not** an overall pass. Only a mode's terminal
marker with `Terminal: true` is a completed outcome. Do not mistake
`JNI_PASSED`, `BINARY_PASSED`, or `CLEANUP_PASSED` for `FULL_PASSED`.

Each local file write uses `FileStream.Flush(true)`. These are ordinary local
files, not provider streams. The files are not a transaction: process death
*during* an overwrite can leave an incomplete file. Treat missing, malformed,
stale, or nonterminal results as incomplete, never as a pass. Compare the
invocation's `RunId`, `Mode`, and `ProcessId` with the observed launch. A runner
should poll logcat and then capture the terminal JSON/text as durable evidence.

On Android, persistentDataPath is normally the app-specific external files
path, for example `/sdcard/Android/data/net.majdata.storagevalidation/files`.
`run-as net.majdata.storagevalidation cat files/...` refers to the **internal**
files directory and is not generally the correct location. Verify the actual
persistentDataPath if this device/configuration differs. The dedicated package
is debuggable, so `run-as` can read the app's own result files. Keep the **state
file private**: it intentionally contains the selected grant URI and is not
suitable for public logs, issue attachments, or commits.

## Concrete physical-device run

The provided scripts can build the isolated universal APK and select one
ABI during installation. In PowerShell, from the repository root:

```powershell
./Tools/Tests/Build-AndroidStorageDeviceValidation.ps1 -Architecture Both
./Tools/Tests/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Install -Abi arm64-v8a -Mode full
```

Keep the device unlocked and watch for:

```text
FILE_SYSTEM_DEVICE_PICKER_READY
```

In **real DocumentsUI**, navigate to a dedicated disposable scratch directory
and choose "Use this folder" / confirm read/write access. Do not inject a URI,
fabricate an activity result, or replace the provider with a mock. Prefer the
local Documents provider for this minimum test. The harness never deletes,
renames, or writes a file directly in the selected parent: all content goes in
one `MajdataPlay-validation-<guid>` child that it created, which is removed in
`finally`.

Wait for `FILE_SYSTEM_DEVICE_FULL_PASSED`. Then:

```powershell
./Tools/Tests/Run-AndroidStorageDeviceValidation.ps1 -Serial '<device-serial>' -Abi arm64-v8a -Mode replay
```

The runner force-stops **only the dedicated validation package** before launch.
Replay must reach `FILE_SYSTEM_DEVICE_REPLAY_PASSED` without any picker. Run the
optional `release` mode after recording replay evidence. If a runner version
has not exposed release yet, use the explicit ADB launch below rather than
clearing all app data or modifying the private state.

Equivalent explicit ADB protocol (commands shown for the parent/operator;
this agent does not execute them):

```powershell
$adb = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$serial = '<device-serial>'
$package = 'net.majdata.storagevalidation'
$component = "$package/net.majdata.majdataplay.MajdataPlayActivity"
$apk = 'Temp/AndroidStorageDeviceValidation/Artifacts/storage-validation-Both.apk'
& $adb -s $serial install --abi arm64-v8a -r $apk
& $adb -s $serial shell am force-stop $package
& $adb -s $serial shell am start -n $component --es validationMode full
# Operate DocumentsUI only after FILE_SYSTEM_DEVICE_PICKER_READY; await FULL_PASSED.
$deviceFiles = '/sdcard/Android/data/net.majdata.storagevalidation/files'
& $adb -s $serial shell run-as $package cat "$deviceFiles/android-storage-device-result.json"
& $adb -s $serial shell run-as $package cat "$deviceFiles/android-storage-device-result.txt"
& $adb -s $serial shell am force-stop $package
& $adb -s $serial shell am start -n $component --es validationMode replay
# Await REPLAY_PASSED and retain its result before the next launch overwrites it.
& $adb -s $serial shell run-as $package cat "$deviceFiles/android-storage-device-result.json"
& $adb -s $serial shell am force-stop $package
& $adb -s $serial shell am start -n $component --es validationMode release
# Await RELEASE_PASSED; GrantReleased=false means an original grant was retained.
```

For ARMv7, install with `--abi armeabi-v7a` (or the runner's `-Abi armeabi-v7a`)
and repeat `full` + `replay` after the previous ownership state is finished.
A universal APK alone does not prove both ABIs ran. Check real runtime
`PointerSize` **8** for ARM64 and **4** for ARMv7. The probe also checks
`android.os.Process.is64Bit()` against managed pointer size. These probe APIs
are available on API 35; the generator's compile-time API 36 does not force an
API-36-only runtime call.

The runner can separate launching from observation with `-StartOnly` /
`-ObserveOnly`, where supported. Logcat should be filtered to the newly started
validation process; other Unity applications or stale log lines are not valid
evidence.

## Minimum implemented checks

Before opening the picker:

1. A result field and `FILE_SYSTEM_DEVICE_IL2CPP_PASSED` verify the **actual**
   `ENABLE_IL2CPP` compilation flag. Defining an unrelated validation symbol is
   not sufficient.
2. Generated `UnityPlayer.CurrentActivity` and `Activity.GetIntent()` read the
   launch mode. There is no assumed `Activity.Current` API.
3. Generated Intent construction, fluent `AddFlags`, typed probe result,
   disposal of independently owned returned references, and null-reference
   propagation.
4. Generated `View.KeyEvent(ActionDown, KeycodeA)`, `GetAction`, `GetKeyCode`,
   typed returns, borrowed-reference disposal that leaves the owner live,
   adoption of an untyped owned Java result, idempotent wrapper disposal, and
   disposed-wrapper rejection. `KeycodeA` follows the real generator's naming.
5. An independently owned `sbyte[]` result preserves bit patterns
   **0, 127, 128, 255**, without aliasing the input; null arrays are preserved.
6. String UTF-16 round-trip includes embedded NUL, a CJK character, and a
   supplementary character; null strings are preserved.
7. An intentional Java `IllegalStateException` becomes
   `JavaInvocationException`, retains the sentinel/Java stack, leaves no JNI
   exception pending, and permits a subsequent generated call.
8. Repeat JNI checks on a genuine background thread with
   `AndroidJNI.InvokeAttached`. Managed exceptions are caught **inside** the
   attachment callback using `ExceptionDispatchInfo` and rethrown **outside**.
   Owned wrappers are disposed before leaving the attachment scope.
9. Schedule a managed UI callback through production `Activity.RunOnUiThread`
   and its generated Runnable + Unity proxy adapter; await it with a ten-second
   timeout. The callback captures failures rather than throwing through native
   reverse JNI.

Full and replay storage checks:

- Snapshot persisted grant locations **before** the real picker.
- Request writable + persistable access with the owner token; confirm the
  exact selected original URI appears in `GetPersistedPermissions` with read
  and write flags.
- Reopen the selected directory through production `FileSystem.OpenDirectory`
  during replay, with no new picker grant and a different process ID.
- Create a unique owned child and an empty binary document. Write/read
  **197,121 bytes**, containing all 256 byte values, using production async
  helpers and several transfers exceeding the 64-KiB JNI boundary.
- Check every byte and length; production helpers offload provider work and
  deterministically dispose their streams.
- Delete only the owned created child in `finally`, **without using the
  cancelled lifetime token for cleanup**. Successful cleanup is required for
  the terminal pass. Preserve the selected parent and the grant for replay.
- Optional release never intentionally releases a preexisting grant.

## Markers, failure handling, and safety limits

The screen and logcat use `FILE_SYSTEM_DEVICE_*_(READY|PASSED|FAILED)` markers.
Important intermediate markers are `BOOT_READY`, `IL2CPP_PASSED`, `JNI_PASSED`,
`BACKGROUND_JNI_PASSED`, `UI_CALLBACK_PASSED`, `PICKER_READY`,
`PERSISTED_GRANT_PASSED`, `BINARY_PASSED`, and `CLEANUP_PASSED`, all prefixed with
`FILE_SYSTEM_DEVICE_`. Terminal success is `FULL_PASSED`, `REPLAY_PASSED`, or
`RELEASE_PASSED`; failure uses the selected mode's `*_FAILED`. If result writing
itself fails, `FILE_SYSTEM_DEVICE_RESULT_FAILED` is logged as an observable
infrastructure failure.

A tracked ordinary `Task` has a top-level failure boundary. `OnDestroy` cancels
its owner token; cancellation/provider/cleanup failures produce a terminal
failure rather than a false pass. Exception **types** and the last phase are
recorded, but provider exception messages/stacks are suppressed because they
can contain private document URIs. The deliberate Java sentinel is checked,
not printed. A dismissed picker is a failure to complete this controlled test.

An Android force-stop, crash, or device power loss cannot execute managed
`finally`. Do not stop during an active storage phase. An interrupted run can
leave its test-owned child; `ActiveChildLocation` in **private** state diagnoses
that case and replay refuses to proceed. Inspect/remove only that specifically
created child while access remains available; do not release its grant first,
edit state blindly, recursively clean the selected parent, or treat this case
as successful cleanup. The minimum harness does not automatically recover
orphans after an abrupt process kill.

## Explicitly deferred coverage

The urgent first finalized runtime version deliberately does **not** implement
append/truncate, text encodings/BOMs, early cursor disposal regression loops,
file/directory rename and provider URI-change assertions, invalid-name and
missing-file error cases, nonseekable stream property rejection checks,
pre-cancel/in-flight cancellation assertions, local-to-SAF/SAF-to-local copy,
nonrecursive nonempty-directory deletion, or provider descriptor-count leak
measurement. Stream/cursor closure is exercised only by the production helper
paths and child cleanup, not proved by a native resource counter.

Cloud providers, picker dismissal/cancellation races, read-only/revoked grants,
unknown metadata, concurrent provider mutations, hardware/device differences,
release-mode runtime behavior, and other platforms remain separate validation
work. Do not claim these cases passed from this minimum suite. Both ARM64 and
ARMv7 IL2CPP execution, real DocumentsUI selection, and a force-stop replay
require parent-run physical-device evidence.

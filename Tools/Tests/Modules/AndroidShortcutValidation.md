# Android shortcut lifecycle validation

Build the Android Player with the repository-pinned Unity 6000.3.17f1 and test on
a device with a launcher that supports app shortcuts. Record the APK revision,
Android version, launcher, and ABI with the results. Java compilation and manifest
inspection do not establish Android task or Unity Player lifecycle correctness.

## Regression and routing contract

Android adds `FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK` to the first
intent of a static shortcut. Targeting `MajdataPlayActivity` directly therefore
clears the existing Unity task, including when its launch mode is `singleTop`.
Unity's activity destruction tears down the player. See the official
[shortcut management documentation](https://developer.android.com/develop/ui/compose/system/shortcuts/managing-shortcuts#start-one-activity-from-another).

Both `import_archive` and `import_folder` must target `MajdataShortcutActivity`.
This plain Android activity has an empty task affinity and an invisible theme,
forwards the requested import action to `MajdataPlayActivity`, and finishes during
`onCreate`. The forwarded intent must contain `NEW_TASK | CLEAR_TOP | SINGLE_TOP`
and must not inherit `CLEAR_TASK` from the launcher. The existing Unity instance
receives the action through `onNewIntent`; cold starts retain the action in the
launch intent for importer initialization.

Inspect the built APK's merged manifest and shortcut resource, rather than only
the source XML: verify the forwarding activity's empty affinity, exported entry
point, and invisible theme; both shortcut targets; and the Unity activity's
`singleTop` launch mode. The forwarding activity must leave no extra app card in
Recents.

## Device smoke cases

Use disposable import inputs or cancel the picker; no chart import is required
to check the lifecycle. Finish or cancel each picker before the next iteration
unless a case explicitly keeps it open.

| Case | Steps | Required result |
| --- | --- | --- |
| Cold archive and folder | Close the test app, then activate each shortcut separately. | The player initializes and opens exactly one picker of the requested type. Canceling returns to a usable player. |
| Same shortcut after Home | Open the app, record its PID and Unity activity record, press Home, and activate the archive shortcut. Cancel and repeat at least three times; repeat for folder. | The PID and Unity activity record remain unchanged; each activation opens one picker, with no restart, crash, or additional Recents card. |
| Different shortcut after Home | Activate archive, cancel, press Home, and activate folder; reverse the order. | The existing Unity activity survives both directions and opens the requested picker type. |
| Picker already open | Leave the folder picker open, press Home, and activate a shortcut again. Cancel the active picker, then cancel any pending request and repeat folder. | Only one picker is active at a time; the existing Unity activity remains alive and cancellation does not leave import permanently blocked. |
| Ordinary app icon | Cancel a shortcut picker, press Home, and tap the ordinary app icon; repeat after both shortcut types. | The existing player returns without another import picker or restart. |
| Existing pinned shortcuts after upgrade | Pin both shortcuts before installing the fixed APK as an upgrade, then repeat cold and Home cases using those existing icons. | Both pinned shortcuts use the corrected routing and preserve the warm Unity activity. Do not uninstall or clear app data between versions. |
| Task removal and process death | Separately remove the app task from Recents or stop the test app, then activate either shortcut. | A fresh player can start and handle the requested picker. An activity or process that Android already destroyed cannot be retained. |

Home/backgrounding and swiping the app card away in Recents are different cases.
Record which gesture reproduced a failure. A PID change alone can also indicate
an independent OS process death; capture Android logs when diagnosing one.

## Optional ADB evidence

Select the intended test device explicitly. Replace the package if testing a
variant; the Java activity's fully qualified class name remains unchanged.

```powershell
$shortcutSerial = '<device-serial>'
$shortcutPackage = 'net.majdata.majdataplay'
adb -s $shortcutSerial shell pidof $shortcutPackage
adb -s $shortcutSerial shell dumpsys activity activities |
    Select-String 'ActivityRecord.*net\.majdata\.majdataplay/.*MajdataPlayActivity'
```

Compare the Unity activity record token and task ID before and after each warm
activation, in addition to its PID. A replacement activity can share the same
PID. The record can appear more than once in `dumpsys`; compare the matching
activity identity rather than the number of matching lines.

To exercise the framework flags without a launcher gesture, background the app
and start the forwarding activity with the flags Android uses for static
shortcuts (`0x10008000` = `NEW_TASK | CLEAR_TASK`):

```powershell
adb -s $shortcutSerial shell input keyevent KEYCODE_HOME
adb -s $shortcutSerial shell am start -W -f 0x10008000 `
    -n "$shortcutPackage/net.majdata.majdataplay.MajdataShortcutActivity" `
    -a net.majdata.majdataplay.action.IMPORT_ARCHIVE
```

Repeat with `IMPORT_FOLDER` and compare the evidence again. This command checks
the forwarding route; it does not replace testing actual launcher and pinned
shortcuts. Report unexecuted device, launcher, and ABI cases as **not verified**.

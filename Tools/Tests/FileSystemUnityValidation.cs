#nullable enable
using MajdataPlay.IO.Storage;
using AndroidActivity = MajdataPlay.Platform.Android.Runtime.App.Activity;
using AndroidIntent = MajdataPlay.Platform.Android.Runtime.Content.Intent;
using AndroidKeyCode = MajdataPlay.Platform.Android.IO.KeyCode;
using AndroidKeyEvent = MajdataPlay.Platform.Android.Runtime.View.KeyEvent;
using JavaRunnable = MajdataPlay.Platform.Android.Runtime.Java.Lang.Runnable;
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
using MajdataPlay.Platform.Android.Runtime.Storage;
using MajdataPlay.Platform.Android.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace MajdataPlay.Tests
{
    /// <summary>Runs isolated storage and runtime-wrapper Editor checks and compiles actual assemblies for requested players.</summary>
    public static class FileSystemUnityValidation
    {
        /// <summary>Freezes every pre-migration KeyCode name; its array index is the original numeric value (0 through 337).</summary>
        private static readonly string[] s_legacyKeyCodeNames =
        {
            "Unknown", "SoftLeft", "SoftRight", "Home",
            "Back", "Call", "Endcall", "Num0",
            "Num1", "Num2", "Num3", "Num4",
            "Num5", "Num6", "Num7", "Num8",
            "Num9", "Star", "Pound", "DpadUp",
            "DpadDown", "DpadLeft", "DpadRight", "DpadCenter",
            "VolumeUp", "VolumeDown", "Power", "Camera",
            "Clear", "A", "B", "C",
            "D", "E", "F", "G",
            "H", "I", "J", "K",
            "L", "M", "N", "O",
            "P", "Q", "R", "S",
            "T", "U", "V", "W",
            "X", "Y", "Z", "Comma",
            "Period", "AltLeft", "AltRight", "ShiftLeft",
            "ShiftRight", "Tab", "Space", "Sym",
            "Explorer", "Envelope", "Enter", "Del",
            "Grave", "Minus", "Equals", "LeftBracket",
            "RightBracket", "Backslash", "Semicolon", "Apostrophe",
            "Slash", "At", "Num", "Headsethook",
            "Focus", "Plus", "Menu", "Notification",
            "Search", "MediaPlayPause", "MediaStop", "MediaNext",
            "MediaPrevious", "MediaRewind", "MediaFastForward", "Mute",
            "PageUp", "PageDown", "Pictsymbols", "SwitchCharset",
            "ButtonA", "ButtonB", "ButtonC", "ButtonX",
            "ButtonY", "ButtonZ", "ButtonL1", "ButtonR1",
            "ButtonL2", "ButtonR2", "ButtonThumbl", "ButtonThumbr",
            "ButtonStart", "ButtonSelect", "ButtonMode", "Escape",
            "ForwardDel", "CtrlLeft", "CtrlRight", "CapsLock",
            "ScrollLock", "MetaLeft", "MetaRight", "Function",
            "Sysrq", "Break", "MoveHome", "MoveEnd",
            "Insert", "Forward", "MediaPlay", "MediaPause",
            "MediaClose", "MediaEject", "MediaRecord", "F1",
            "F2", "F3", "F4", "F5",
            "F6", "F7", "F8", "F9",
            "F10", "F11", "F12", "NumLock",
            "Numpad0", "Numpad1", "Numpad2", "Numpad3",
            "Numpad4", "Numpad5", "Numpad6", "Numpad7",
            "Numpad8", "Numpad9", "NumpadDivide", "NumpadMultiply",
            "NumpadSubtract", "NumpadAdd", "NumpadDot", "NumpadComma",
            "NumpadEnter", "NumpadEquals", "NumpadLeftParen", "NumpadRightParen",
            "VolumeMute", "Info", "ChannelUp", "ChannelDown",
            "ZoomIn", "ZoomOut", "Tv", "Window",
            "Guide", "Dvr", "Bookmark", "Captions",
            "Settings", "TvPower", "TvInput", "StbPower",
            "StbInput", "AvrPower", "AvrInput", "ProgRed",
            "ProgGreen", "ProgYellow", "ProgBlue", "AppSwitch",
            "Button1", "Button2", "Button3", "Button4",
            "Button5", "Button6", "Button7", "Button8",
            "Button9", "Button10", "Button11", "Button12",
            "Button13", "Button14", "Button15", "Button16",
            "LanguageSwitch", "MannerMode", "Key3DMode", "Contacts",
            "Calendar", "Music", "Calculator", "ZenkakuHankaku",
            "Eisu", "Muhenkan", "Henkan", "KatakanaHiragana",
            "Yen", "Ro", "Kana", "Assist",
            "BrightnessDown", "BrightnessUp", "MediaAudioTrack", "Sleep",
            "Wakeup", "Pairing", "MediaTopMenu", "Num11",
            "Num12", "LastChannel", "TvDataService", "VoiceAssist",
            "TvRadioService", "TvTeletext", "TvNumberEntry", "TvTerrestrialAnalog",
            "TvTerrestrialDigital", "TvSatellite", "TvSatelliteBs", "TvSatelliteCs",
            "TvSatelliteService", "TvNetwork", "TvAntennaCable", "TvInputHdmi1",
            "TvInputHdmi2", "TvInputHdmi3", "TvInputHdmi4", "TvInputComposite1",
            "TvInputComposite2", "TvInputComponent1", "TvInputComponent2", "TvInputVga1",
            "TvAudioDescription", "TvAudioDescriptionMixUp", "TvAudioDescriptionMixDown", "TvZoomMode",
            "TvContentsMenu", "TvMediaContextMenu", "TvTimerProgramming", "Help",
            "NavigatePrevious", "NavigateNext", "NavigateIn", "NavigateOut",
            "StemPrimary", "Stem1", "Stem2", "Stem3",
            "DpadUpLeft", "DpadDownLeft", "DpadUpRight", "DpadDownRight",
            "MediaSkipForward", "MediaSkipBackward", "MediaStepForward", "MediaStepBackward",
            "SoftSleep", "Cut", "Copy", "Paste",
            "SystemNavigationUp", "SystemNavigationDown", "SystemNavigationLeft", "SystemNavigationRight",
            "AllApps", "Refresh", "ThumbsUp", "ThumbsDown",
            "ProfileSwitch", "VideoApp1", "VideoApp2", "VideoApp3",
            "VideoApp4", "VideoApp5", "VideoApp6", "VideoApp7",
            "VideoApp8", "FeaturedApp1", "FeaturedApp2", "FeaturedApp3",
            "FeaturedApp4", "DemoApp1", "DemoApp2", "DemoApp3",
            "DemoApp4", "KeyboardBacklightDown", "KeyboardBacklightUp", "KeyboardBacklightToggle",
            "StylusButtonPrimary", "StylusButtonSecondary", "StylusButtonTertiary", "StylusButtonTail",
            "RecentApps", "Macro1", "Macro2", "Macro3",
            "Macro4", "EmojiPicker", "Screenshot", "Dictate",
            "New", "Close", "DoNotDisturb", "Print",
            "Lock", "Fullscreen", "F13", "F14",
            "F15", "F16", "F17", "F18",
            "F19", "F20", "F21", "F22",
            "F23", "F24",
        };

        /// <summary>Validates the storage layer without scenes, game services, or Android JNI execution.</summary>
        public static void Run()
        {
            try
            {
                CheckEditorOperations();
                CheckGeneratedStorageWrappers();
                CheckGeneratedPickerWrappers();
                CheckLegacyRuntimeMigration();
                CheckKeyCodeSnapshot();
                CheckGeneratedBridgeContract();
                CheckPickerCompletion();
                CheckPickerConcurrency();
                var targets = GetRequestedTargets();
                foreach (var targetName in targets)
                {
                    var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName);
                    var group = BuildPipeline.GetBuildTargetGroup(target);
                    if (!BuildPipeline.IsBuildTargetSupported(group, target))
                    {
                        throw new InvalidOperationException("Install the Unity build module for requested target " + targetName + ".");
                    }
                    var output = Path.GetFullPath(Path.Combine("Temp", "StoragePlayerScripts", targetName,
                        Guid.NewGuid().ToString("N")));
                    System.IO.Directory.CreateDirectory(output);
                    var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
                    {
                        target = target,
                        group = group,
                        options = ScriptCompilationOptions.None,
                        extraScriptingDefines = Array.Empty<string>()
                    }, output);
                    Require(result.assemblies is not null && result.assemblies.Count > 0,
                        "Player script compilation produced no assemblies for " + targetName + ".");
                    Require(ContainsAssembly(result.assemblies!, "MajdataPlay.IO.dll"), "The IO assembly was not compiled.");
                    if (target == BuildTarget.Android)
                    {
                        Require(ContainsAssembly(result.assemblies!, "MajdataPlay.Platform.Android.dll"),
                            "The Android player branch was not compiled.");
                    }
                    Debug.Log("FILE_SYSTEM_PLAYER_COMPILE_PASSED: " + targetName + " (compile only, not runtime).");
                }
                Debug.Log("FILE_SYSTEM_UNITY_PASSED: Editor local smoke, production generated wrappers, Editor JNI guards, picker regressions, requested player script compilation.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("FILE_SYSTEM_UNITY_FAILED: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Checks basic local operations and that Android-only APIs remain safe in the Editor.</summary>
        /// <exception cref="InvalidOperationException">An expectation fails.</exception>
        private static void CheckEditorOperations()
        {
            var path = Path.GetFullPath(Path.Combine("Temp", "StorageEditorSmoke", Guid.NewGuid().ToString("N")));
            var directory = FileSystem.CreateDirectory(path);
            try
            {
                Require(!directory.FileSystem.GetType().IsVisible, "The local backend must not be visible outside the IO assembly.");
                Require(typeof(FileSystem).GetMember("Local").Length == 0, "The facade must not expose its local backend.");
                var file = directory.CreateFile("storage-中文.txt", "text/plain");
                file.WriteAllText("portable storage 中文");
                Require(file.ReadAllText() == "portable storage 中文", "UTF-8 round-trip failed.");
                var copy = file.CopyTo(directory, "copy.txt");
                Require(copy.ReadAllText() == file.ReadAllText(), "Local copy failed.");
                var renamed = copy.Rename("renamed.txt");
                Require(renamed.Exists && !copy.Exists, "Rename did not return the authoritative new location.");
                Require(directory.FindFile("renamed.txt") is not null, "Child lookup failed.");
                RequireThrowsPlatform(() => new AndroidDocumentFileSystem().GetEntry("content://test/tree/root"));
                RequireThrowsPlatform(() => AndroidStorageAccess.GetPersistedPermissions());
                RequireThrowsPlatform(() => AndroidStorageAccess.PickDirectoryAsync().GetAwaiter().GetResult());
                Debug.Log("FILE_SYSTEM_EDITOR_SMOKE_PASSED");
            }
            finally
            {
                directory.Delete(true);
            }
        }

        /// <summary>Checks the production SAF wrappers, signed bytes, typed envelopes, and no-JNI reference lifecycle.</summary>
        /// <exception cref="InvalidOperationException">A generated member, mapping, ownership, or Editor guard is incorrect.</exception>
        private static void CheckGeneratedStorageWrappers()
        {
            RequireGeneratedWrapper(typeof(StorageAccess), "net.majdata.majdataplay.StorageAccess");
            RequireGeneratedWrapper(typeof(StorageResult), "net.majdata.majdataplay.StorageAccess$Result");
            RequireGeneratedWrapper(typeof(DocumentEntry), "net.majdata.majdataplay.StorageAccess$Entry");
            RequireGeneratedWrapper(typeof(DocumentCursor), "net.majdata.majdataplay.StorageAccess$CursorHandle");
            RequireGeneratedWrapper(typeof(DocumentStream), "net.majdata.majdataplay.StorageAccess$StreamHandle");
            foreach (var wrapperType in new[] { typeof(StorageAccess), typeof(StorageResult), typeof(DocumentEntry), typeof(DocumentCursor), typeof(DocumentStream) })
            {
                Require(!wrapperType.GetCustomAttribute<JavaClassAttribute>()!.IncludeInheritedMembers,
                    wrapperType.FullName + " must expose only the declared storage protocol.");
                Require(wrapperType.GetConstructors().Length == 1,
                    wrapperType.FullName + " must not expose the Java bridge's private constructors.");
                CheckGeneratedReferenceLifecycle(wrapperType);
            }

            RequireGeneratedConstant(typeof(StorageAccess), "Success", 0);
            RequireGeneratedConstant(typeof(StorageAccess), "InvalidArgument", 1);
            RequireGeneratedConstant(typeof(StorageAccess), "NotFound", 2);
            RequireGeneratedConstant(typeof(StorageAccess), "DirectoryNotFound", 3);
            RequireGeneratedConstant(typeof(StorageAccess), "AccessDenied", 4);
            RequireGeneratedConstant(typeof(StorageAccess), "Unsupported", 5);
            RequireGeneratedConstant(typeof(StorageAccess), "IoError", 6);
            RequireGeneratedConstant(typeof(StorageAccess), "MaxTransferSize", 32768);
            RequireGeneratedProperty(typeof(StorageResult), "ErrorCode", typeof(int));
            RequireGeneratedProperty(typeof(StorageResult), "ErrorMessage", typeof(string));
            RequireGeneratedProperty(typeof(StorageResult), "Entry", typeof(DocumentEntry));
            RequireGeneratedProperty(typeof(StorageResult), "Cursor", typeof(DocumentCursor));
            RequireGeneratedProperty(typeof(StorageResult), "Stream", typeof(DocumentStream));
            RequireGeneratedProperty(typeof(StorageResult), "Data", typeof(sbyte[]));
            RequireGeneratedProperty(typeof(StorageResult), "Count", typeof(int));
            RequireGeneratedProperty(typeof(DocumentEntry), "Uri", typeof(string));
            RequireGeneratedProperty(typeof(DocumentEntry), "ResourceId", typeof(string));
            RequireGeneratedProperty(typeof(DocumentEntry), "Name", typeof(string));
            RequireGeneratedProperty(typeof(DocumentEntry), "Directory", typeof(bool));
            RequireGeneratedProperty(typeof(DocumentEntry), "HasSize", typeof(bool));
            RequireGeneratedProperty(typeof(DocumentEntry), "Size", typeof(long));
            RequireGeneratedProperty(typeof(DocumentEntry), "HasModified", typeof(bool));
            RequireGeneratedProperty(typeof(DocumentEntry), "Modified", typeof(long));
            RequireGeneratedProperty(typeof(DocumentEntry), "Flags", typeof(int));

            RequireGeneratedMethod(typeof(StorageAccess), "GetEntry", typeof(StorageResult), true, typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "GetChildEntry", typeof(StorageResult), true, typeof(string), typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "EnumerateEntries", typeof(StorageResult), true, typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "OpenRead", typeof(StorageResult), true, typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "OpenWrite", typeof(StorageResult), true, typeof(string), typeof(bool));
            RequireGeneratedMethod(typeof(StorageAccess), "CreateFile", typeof(StorageResult), true, typeof(string), typeof(string), typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "CreateDirectory", typeof(StorageResult), true, typeof(string), typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "Rename", typeof(StorageResult), true, typeof(string), typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "DeleteFile", typeof(StorageResult), true, typeof(string));
            RequireGeneratedMethod(typeof(StorageAccess), "DeleteDirectory", typeof(StorageResult), true, typeof(string), typeof(bool));
            RequireGeneratedMethod(typeof(DocumentCursor), "Next", typeof(StorageResult), false);
            RequireGeneratedMethod(typeof(DocumentCursor), "Close", typeof(StorageResult), false);
            RequireGeneratedMethod(typeof(DocumentStream), "Read", typeof(StorageResult), false, typeof(int));
            RequireGeneratedMethod(typeof(DocumentStream), "Write", typeof(StorageResult), false, typeof(sbyte[]), typeof(int));
            RequireGeneratedMethod(typeof(DocumentStream), "Flush", typeof(StorageResult), false);
            RequireGeneratedMethod(typeof(DocumentStream), "Close", typeof(StorageResult), false);

            const string Location = "content://test/document/guarded";
            RequireThrowsPlatform(() => StorageAccess.GetEntry(Location));
            RequireThrowsPlatform(() => StorageAccess.GetChildEntry(Location, "child"));
            RequireThrowsPlatform(() => StorageAccess.EnumerateEntries(Location));
            RequireThrowsPlatform(() => StorageAccess.OpenRead(Location));
            RequireThrowsPlatform(() => StorageAccess.OpenWrite(Location, false));
            RequireThrowsPlatform(() => StorageAccess.CreateFile(Location, "file.txt", "text/plain"));
            RequireThrowsPlatform(() => StorageAccess.CreateDirectory(Location, "directory"));
            RequireThrowsPlatform(() => StorageAccess.Rename(Location, "renamed"));
            RequireThrowsPlatform(() => StorageAccess.DeleteFile(Location));
            RequireThrowsPlatform(() => StorageAccess.DeleteDirectory(Location, true));
            Debug.Log("FILE_SYSTEM_GENERATED_STORAGE_PASSED: production members, typed results, sbyte arrays, borrowing/disposal, Editor guards (no JNI).");
        }

        /// <summary>Checks SDK and Unity wrapper mappings without depending on category namespaces or C# class names.</summary>
        /// <exception cref="InvalidOperationException">A picker dependency or its generated signature is missing.</exception>
        private static void CheckGeneratedPickerWrappers()
        {
            var activity = FindGeneratedWrapper("android.app.Activity");
            var intent = FindGeneratedWrapper("android.content.Intent");
            var resolver = FindGeneratedWrapper("android.content.ContentResolver");
            var permission = FindGeneratedWrapper("android.content.UriPermission");
            var uri = FindGeneratedWrapper("android.net.Uri");
            var documents = FindGeneratedWrapper("android.provider.DocumentsContract");
            var unityPlayer = FindGeneratedWrapper("com.unity3d.player.UnityPlayer");
            var list = FindGeneratedWrapper("java.util.List");
            var runnable = FindGeneratedWrapper("java.lang.Runnable");
            RequireGeneratedMethod(activity, "GetContentResolver", resolver, false);
            RequireGeneratedMethod(activity, "StartActivityForResult", typeof(void), false, intent, typeof(int));
            RequireGeneratedMethod(activity, "RunOnUiThread", typeof(void), false, runnable);
            RequireGeneratedMethod(intent, "AddFlags", intent, false, typeof(int));
            RequireGeneratedMethod(intent, "SetData", intent, false, uri);
            RequireGeneratedMethod(intent, "GetData", uri, false);
            RequireGeneratedMethod(resolver, "TakePersistableUriPermission", typeof(void), false, uri, typeof(int));
            RequireGeneratedMethod(resolver, "ReleasePersistableUriPermission", typeof(void), false, uri, typeof(int));
            RequireGeneratedMethod(resolver, "GetPersistedUriPermissions", list, false);
            RequireGeneratedMethod(permission, "GetUri", uri, false);
            RequireGeneratedMethod(permission, "IsReadPermission", typeof(bool), false);
            RequireGeneratedMethod(permission, "IsWritePermission", typeof(bool), false);
            RequireGeneratedMethod(uri, "Parse", uri, true, typeof(string));
            RequireGeneratedMethod(documents, "IsTreeUri", typeof(bool), true, uri);
            RequireGeneratedProperty(unityPlayer, "CurrentActivity", activity, true);
            RequireGeneratedMethod(list, "Size", typeof(int), false);
            RequireGeneratedMethod(list, "Get", typeof(AndroidJavaObject), false, typeof(int));
            RequireGeneratedMethod(runnable, "Run", typeof(void), false);
            var intentConstructor = intent.GetConstructor(new[] { typeof(string) });
            Require(intentConstructor is not null, "The generated Intent action constructor is missing.");
            RequireThrowsPlatform(() => intentConstructor!.Invoke(new object[] { "android.intent.action.OPEN_DOCUMENT" }));
            RequireThrowsPlatform(() => uri.GetMethod("Parse", new[] { typeof(string) })!.Invoke(null,
                new object[] { "content://test/tree/guarded" }));
            RequireThrowsPlatform(() => unityPlayer.GetProperty("CurrentActivity")!.GetValue(null));
            Debug.Log("FILE_SYSTEM_GENERATED_PICKER_PASSED: SDK/Unity typed mappings and Editor guards (no JNI).");
        }

        /// <summary>Checks unique categorized Activity, Intent, and KeyEvent bindings and their Editor guards.</summary>
        /// <exception cref="InvalidOperationException">A legacy type remains or a generated signature, constant, adapter, or guard is incorrect.</exception>
        /// <exception cref="ReflectionTypeLoadException">A production assembly type cannot be loaded for inspection.</exception>
        private static void CheckLegacyRuntimeMigration()
        {
            var activityType = FindGeneratedWrapper("android.app.Activity");
            var intentType = FindGeneratedWrapper("android.content.Intent");
            var keyEventType = FindGeneratedWrapper("android.view.KeyEvent");
            Require(activityType == typeof(AndroidActivity) && intentType == typeof(AndroidIntent) &&
                keyEventType == typeof(AndroidKeyEvent), "Java mappings must use the categorized runtime declarations.");
            Require(activityType.GetCustomAttribute<JavaClassAttribute>()!.IncludeInheritedMembers &&
                !intentType.GetCustomAttribute<JavaClassAttribute>()!.IncludeInheritedMembers &&
                !keyEventType.GetCustomAttribute<JavaClassAttribute>()!.IncludeInheritedMembers,
                "Activity must retain inherited picker APIs; Intent and KeyEvent must generate declared members only.");
            foreach (var name in new[] { "Activity", "Intent", "KeyEvent" })
            {
                Require(typeof(JavaObject).Assembly.GetType("MajdataPlay.Platform.Android.Runtime." + name) is null,
                    "The duplicate legacy Runtime." + name + " type must be removed.");
            }
            RequireGeneratedConstant(activityType, "ResultCanceled", 0);
            RequireGeneratedConstant(activityType, "ResultFirstUser", 1);
            RequireGeneratedConstant(activityType, "ResultOk", -1);
            RequireGeneratedConstant(intentType, "ActionOpenDocument", "android.intent.action.OPEN_DOCUMENT");
            RequireGeneratedConstant(intentType, "FlagGrantReadUriPermission", 1);
            RequireGeneratedConstant(intentType, "FlagGrantWriteUriPermission", 2);
            RequireGeneratedConstant(keyEventType, "ActionDown", 0);
            RequireGeneratedConstant(keyEventType, "ActionUp", 1);
            RequireGeneratedConstant(keyEventType, "Keycode0", 7);
            RequireGeneratedConstant(keyEventType, "Keycode3dMode", 206);
            RequireGeneratedConstant(keyEventType, "KeycodeF24", 337);
            RequireGeneratedMethod(activityType, "RunOnUiThread", typeof(void), false, typeof(AndroidJavaRunnable));
            RequireGeneratedMethod(keyEventType, "GetAction", typeof(int), false);
            RequireGeneratedMethod(keyEventType, "GetKeyCode", typeof(int), false);
            RequireGeneratedMethod(keyEventType, "GetRepeatCount", typeof(int), false);
            RequireGeneratedMethod(keyEventType, "IsLongPress", typeof(bool), false);
            RequireGeneratedMethod(keyEventType, "KeyCodeToString", typeof(string), true, typeof(int));
            RequireGeneratedMethod(keyEventType, "KeyCodeFromString", typeof(int), true, typeof(string));
            RequireGeneratedMethod(keyEventType, "ChangeAction", keyEventType, true, keyEventType, typeof(int));
            RequireGeneratedProperty(keyEventType, "Creator", typeof(AndroidJavaObject), true);

            RequireThrowsPlatform(() => new AndroidActivity());
            RequireThrowsPlatform(() => new AndroidIntent());
            RequireThrowsPlatform(() => new AndroidIntent(AndroidIntent.ActionOpenDocument));
            RequireThrowsPlatform(() => new AndroidKeyEvent(AndroidKeyEvent.ActionDown, AndroidKeyEvent.KeycodeA));
            RequireThrowsPlatform(() => new AndroidKeyEvent(0L, 0L, AndroidKeyEvent.ActionDown, AndroidKeyEvent.KeycodeA, 0));
            RequireThrowsPlatform(() => AndroidKeyEvent.KeyCodeToString(AndroidKeyEvent.KeycodeA));
            RequireThrowsPlatform(() => AndroidKeyEvent.KeyCodeFromString("KEYCODE_A"));
            RequireThrowsPlatform(() => AndroidKeyEvent.GetMaxKeyCode());
            RequireThrowsPlatform(() =>
            {
                _ = AndroidKeyEvent.Creator;
            });
            using var reference = ManagedJavaReference.Create();
            using var activity = new AndroidActivity(reference, ownsReference: false);
            using var intent = new AndroidIntent(reference, ownsReference: false);
            using var keyEvent = new AndroidKeyEvent(reference, ownsReference: false);
            using var runnable = new JavaRunnable(reference, ownsReference: false);
            RequireThrowsPlatform(() => new AndroidIntent(intent));
            RequireThrowsPlatform(() => new AndroidKeyEvent(keyEvent));
            RequireThrowsPlatform(() => activity.GetContentResolver());
            RequireThrowsPlatform(() => activity.StartActivityForResult(intent, 1));
            RequireThrowsPlatform(() => activity.RunOnUiThread(runnable));
            var callback = new AndroidJavaRunnable(() =>
            {
            });
            RequireThrowsPlatform(() => activity.RunOnUiThread(callback));
            RequireThrows<ArgumentNullException>(() => activity.RunOnUiThread((AndroidJavaRunnable)null!),
                "The retained Runnable adapter must reject a null managed callback before JNI.");
            RequireThrowsPlatform(() => intent.GetAction());
            RequireThrowsPlatform(() => intent.AddFlags(AndroidIntent.FlagGrantReadUriPermission));
            RequireThrowsPlatform(() => keyEvent.GetAction());
            RequireThrowsPlatform(() => keyEvent.GetKeyCode());
            RequireThrowsPlatform(() => keyEvent.GetRepeatCount());
            RequireThrowsPlatform(() => keyEvent.IsLongPress());
            RequireThrowsPlatform(() => AndroidKeyEvent.ChangeAction(keyEvent, AndroidKeyEvent.ActionUp));
            Require(reference.DisposeCalls == 0, "Runtime Editor checks must not dispose externally owned Java references.");
            Debug.Log("FILE_SYSTEM_GENERATED_RUNTIME_PASSED: unique categorized wrappers, no legacy types, constructors/methods and Runnable adapter guarded (no JNI).");
        }

        /// <summary>Compares all public KeyCode names and integer values with the independent pre-migration snapshot.</summary>
        /// <exception cref="InvalidOperationException">An enum member is added, removed, renamed, or renumbered.</exception>
        private static void CheckKeyCodeSnapshot()
        {
            var enumType = typeof(AndroidKeyCode);
            Require(Enum.GetUnderlyingType(enumType) == typeof(int), "Android KeyCode must retain its Int32 underlying type.");
            Require(Enum.GetNames(enumType).Length == s_legacyKeyCodeNames.Length,
                "Android KeyCode must retain every pre-migration member without additions or removals.");
            for (var value = 0; value < s_legacyKeyCodeNames.Length; value++)
            {
                var name = s_legacyKeyCodeNames[value];
                var field = enumType.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                Require(field is not null && field.IsLiteral && Equals(field.GetRawConstantValue(), value),
                    "Android KeyCode." + name + " must retain its pre-migration value " + value + ".");
            }
            Debug.Log("FILE_SYSTEM_KEYCODE_SNAPSHOT_PASSED: all 338 original names and numeric values (0 through 337).");
        }

        /// <summary>Checks typed bridge integration and null-envelope rejection without reading Java fields.</summary>
        /// <exception cref="InvalidOperationException">The bridge accepts a missing envelope or no longer consumes typed wrappers.</exception>
        private static void CheckGeneratedBridgeContract()
        {
            var bridge = typeof(AndroidStorageAccess).Assembly.GetType("MajdataPlay.Platform.Android.Storage.AndroidDocumentBridge");
            Require(bridge is not null, "The production AndroidDocumentBridge is missing.");
            var requireResult = bridge!.GetMethod("RequireResult", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(StorageResult) }, null);
            Require(requireResult is not null && requireResult.ReturnType == typeof(StorageResult),
                "RequireResult must accept and return the generated StorageResult wrapper.");
            RequireThrows<IOException>(() => requireResult!.Invoke(null, new object?[] { null }),
                "A null SAF result envelope must throw IOException before accessing JNI.");
            using var reference = ManagedJavaReference.Create();
            using var envelope = new StorageResult(reference, ownsReference: false);
            Require(ReferenceEquals(requireResult!.Invoke(null, new object[] { envelope }), envelope),
                "RequireResult must preserve the envelope and its caller-owned reference.");
            var checkResult = bridge!.GetMethod("CheckResult", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(StorageResult), typeof(string), typeof(bool) }, null);
            Require(checkResult is not null, "Provider-error translation must consume the generated StorageResult wrapper.");
            var streamConstructor = typeof(AndroidDocumentStream).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(DocumentStream), typeof(bool) }, null);
            Require(streamConstructor is not null, "AndroidDocumentStream must adopt the generated DocumentStream wrapper.");
            using var stream = new DocumentStream(reference, ownsReference: false);
            RequireThrowsPlatform(() => streamConstructor!.Invoke(new object[] { stream, true }));
            Require(reference.DisposeCalls == 0, "The bridge contract checks disposed a borrowed reference.");
            Debug.Log("FILE_SYSTEM_GENERATED_BRIDGE_PASSED: typed stream/envelope integration and null-envelope IOException (no JNI).");
        }

        /// <summary>Requires a real generated JavaObject wrapper and its adopting reference constructor.</summary>
        /// <param name="wrapperType">The production C# declaration augmented by the installed analyzer.</param>
        /// <param name="binaryName">The exact Java binary name, including dollar-delimited nested classes.</param>
        /// <exception cref="InvalidOperationException">Generated code or its API-36 mapping is missing.</exception>
        private static void RequireGeneratedWrapper(Type wrapperType, string binaryName)
        {
            var attribute = wrapperType.GetCustomAttribute<JavaClassAttribute>();
            Require(attribute is not null && attribute.ClassName == binaryName && attribute.ApiLevel == 36,
                wrapperType.FullName + " must request the expected API-36 Java binary name.");
            Require(wrapperType.IsPublic && wrapperType.BaseType == typeof(JavaObject),
                wrapperType.FullName + " must be a public generated JavaObject wrapper.");
            RequireGeneratedConstant(wrapperType, "JavaClassName", binaryName);
            var constructor = wrapperType.GetConstructor(new[] { typeof(AndroidJavaObject), typeof(bool) });
            Require(constructor is not null, wrapperType.FullName + " is missing the generated reference constructor.");
            var ownership = constructor!.GetParameters()[1];
            Require(ownership.IsOptional && Equals(ownership.DefaultValue, true),
                wrapperType.FullName + " must adopt existing references by default.");
        }

        /// <summary>Finds a unique production wrapper by Java annotation rather than a movable C# namespace.</summary>
        /// <param name="binaryName">The Java class or interface's exact binary name.</param>
        /// <returns>The production wrapper with that annotation and generated API.</returns>
        /// <exception cref="InvalidOperationException">The mapping is absent, duplicated, or not generated.</exception>
        /// <exception cref="ReflectionTypeLoadException">A production assembly type cannot be loaded for inspection.</exception>
        private static Type FindGeneratedWrapper(string binaryName)
        {
            Type? match = null;
            foreach (var candidate in typeof(JavaObject).Assembly.GetTypes())
            {
                if (candidate.GetCustomAttribute<JavaClassAttribute>()?.ClassName == binaryName)
                {
                    Require(match is null, "Duplicate generated wrapper for " + binaryName + ".");
                    match = candidate;
                }
            }
            Require(match is not null, "Missing production generated wrapper for " + binaryName + ".");
            RequireGeneratedWrapper(match!, binaryName);
            return match!;
        }

        /// <summary>Requires exact generated parameter/return mappings and static versus instance dispatch.</summary>
        /// <param name="wrapperType">The generated wrapper to inspect.</param>
        /// <param name="name">The PascalCase generated method name.</param>
        /// <param name="returnType">The expected managed result type.</param>
        /// <param name="isStatic">Whether the Java method uses static dispatch.</param>
        /// <param name="parameters">The exact managed parameter types in declaration order.</param>
        /// <exception cref="InvalidOperationException">The generated signature does not match.</exception>
        private static void RequireGeneratedMethod(Type wrapperType, string name, Type returnType, bool isStatic, params Type[] parameters)
        {
            var method = wrapperType.GetMethod(name, BindingFlags.Public | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, parameters, null);
            Require(method is not null && method.ReturnType == returnType && method.IsStatic == isStatic,
                wrapperType.FullName + "." + name + " has an incorrect generated signature or typed return.");
        }

        /// <summary>Requires a getter-only generated Java field with its exact managed type.</summary>
        /// <param name="wrapperType">The generated wrapper declaring the field.</param>
        /// <param name="name">The generated PascalCase property name.</param>
        /// <param name="propertyType">The expected managed field type.</param>
        /// <param name="isStatic">Whether the Java field is static.</param>
        /// <exception cref="InvalidOperationException">The generated field mapping or accessors are incorrect.</exception>
        private static void RequireGeneratedProperty(Type wrapperType, string name, Type propertyType, bool isStatic = false)
        {
            var property = wrapperType.GetProperty(name, BindingFlags.Public | BindingFlags.DeclaredOnly |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance));
            Require(property is not null && property.PropertyType == propertyType && property.GetMethod is not null &&
                property.GetMethod.IsStatic == isStatic && property.SetMethod is null,
                wrapperType.FullName + "." + name + " must be a getter-only property with the exact generated type.");
        }

        /// <summary>Requires a compile-time Java constant, readable without JNI.</summary>
        /// <param name="wrapperType">The generated wrapper declaring the constant.</param>
        /// <param name="name">The generated constant name.</param>
        /// <param name="expected">The exact expected value and managed type.</param>
        /// <exception cref="InvalidOperationException">The constant is absent or has an incorrect type/value.</exception>
        private static void RequireGeneratedConstant(Type wrapperType, string name, object expected)
        {
            var field = wrapperType.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Require(field is not null && field.IsLiteral && field.FieldType == expected.GetType() &&
                Equals(field.GetRawConstantValue(), expected), wrapperType.FullName + "." + name + " must be the expected compile-time constant.");
        }

        /// <summary>Exercises wrapping, borrowing, adopting, disposal, and instance guards with a managed-only reference.</summary>
        /// <param name="wrapperType">One of the five production storage wrappers.</param>
        /// <exception cref="InvalidOperationException">Ownership, idempotence, null rejection, or an Editor guard fails.</exception>
        private static void CheckGeneratedReferenceLifecycle(Type wrapperType)
        {
            var constructor = wrapperType.GetConstructor(new[] { typeof(AndroidJavaObject), typeof(bool) })!;
            RequireThrows<ArgumentNullException>(() => constructor.Invoke(new object?[] { null, false }),
                wrapperType.FullName + " must reject a null borrowed reference.");
            RequireThrows<ArgumentNullException>(() => constructor.Invoke(new object?[] { null, true }),
                wrapperType.FullName + " must reject a null adopted reference.");
            using var reference = ManagedJavaReference.Create();
            using var borrowed = (JavaObject)constructor.Invoke(new object[] { reference, false });
            using var otherBorrower = (JavaObject)constructor.Invoke(new object[] { reference, false });
            Require(ReferenceEquals(borrowed.JavaReference, reference), "Wrapping must retain the exact supplied reference.");
            foreach (var property in wrapperType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                RequireThrowsPlatform(() => property.GetValue(borrowed));
            }
            if (borrowed is DocumentCursor cursor)
            {
                RequireThrowsPlatform(() => cursor.Next());
                RequireThrowsPlatform(() => cursor.Close());
            }
            if (borrowed is DocumentStream stream)
            {
                RequireThrowsPlatform(() => stream.Read(3));
                RequireThrowsPlatform(() => stream.Write(new sbyte[] { -128, 0, 127 }, 3));
                RequireThrowsPlatform(() => stream.Flush());
                RequireThrowsPlatform(() => stream.Close());
            }
            borrowed.Dispose();
            borrowed.Dispose();
            Require(reference.DisposeCalls == 0 && ReferenceEquals(otherBorrower.JavaReference, reference),
                "Disposing a borrowed wrapper must leave its externally owned reference and other borrowers intact.");
            RequireThrows<ObjectDisposedException>(() =>
            {
                _ = borrowed.JavaReference;
            }, "Disposed borrowed wrappers must not expose the reference.");
            using var adoptedReference = ManagedJavaReference.Create();
            using var adopted = (JavaObject)constructor.Invoke(new object[] { adoptedReference, Type.Missing });
            adopted.Dispose();
            adopted.Dispose();
            Require(adoptedReference.DisposeCalls == 1, "Default ownership must dispose the adopted reference exactly once.");
            RequireThrows<ObjectDisposedException>(() =>
            {
                _ = adopted.JavaReference;
            }, "Disposed adopted wrappers must not expose the reference.");
        }

        /// <summary>Observes managed disposal only; it never constructs a Java object or owns native JNI handles.</summary>
        private sealed class ManagedJavaReference : AndroidJavaObject
        {
            /// <summary>Gets the number of explicit managed disposal callbacks.</summary>
            public int DisposeCalls { get; private set; }

            /// <summary>Prevents normal construction; the factory bypasses Unity's Java-object constructors.</summary>
            /// <exception cref="Exception">Unity rejects the zero reference if this constructor is invoked.</exception>
            private ManagedJavaReference()
                : base(IntPtr.Zero)
            {
            }

            /// <summary>Records explicit disposal without accessing native Unity or Java code.</summary>
            /// <param name="disposing">Whether this is explicit disposal rather than finalization.</param>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCalls++;
                }
            }

            /// <summary>Allocates an inert managed probe without running constructors or creating JNI handles.</summary>
            /// <returns>A probe whose disposal cannot enter native Unity or Java code.</returns>
            /// <exception cref="SerializationException">The runtime cannot allocate the managed probe without construction.</exception>
            public static ManagedJavaReference Create()
            {
                var reference = (ManagedJavaReference)FormatterServices.GetUninitializedObject(typeof(ManagedJavaReference));
                GC.SuppressFinalize(reference);
                return reference;
            }
        }

        /// <summary>Checks picker cancellation, unrelated results, and late-result cleanup without invoking JNI.</summary>
        /// <exception cref="InvalidOperationException">The picker completion or reservation is incorrect.</exception>
        private static void CheckPickerCompletion()
        {
            var pickerType = typeof(AndroidStorageAccess);
            var requestType = pickerType.GetNestedType("PickerRequest", BindingFlags.NonPublic)!;
            var pendingField = pickerType.GetField("s_pending", BindingFlags.NonPublic | BindingFlags.Static)!;
            var onResult = pickerType.GetMethod("OnActivityResult", BindingFlags.NonPublic | BindingFlags.Static)!;
            var completionProperty = requestType.GetProperty("Completion")!;
            var register = requestType.GetMethod("RegisterCancellation")!;
            using var cancellation = new CancellationTokenSource();
            var request = Activator.CreateInstance(requestType, 0x4D10, true, true, true, cancellation.Token)!;
            var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
            pendingField.SetValue(null, request);
            try
            {
                register.Invoke(request, null);
                cancellation.Cancel();
                Require(completion.Task.IsCanceled, "Owner cancellation did not complete the picker task.");
                Require(ReferenceEquals(pendingField.GetValue(null), request), "Cancellation freed a still-open picker.");
                onResult.Invoke(null, new object?[] { null, 0x4D11, 0, null });
                Require(ReferenceEquals(pendingField.GetValue(null), request), "An unrelated result consumed the request.");
                onResult.Invoke(null, new object?[] { null, 0x4D10, -1, null });
                Require(pendingField.GetValue(null) is null, "The cancelled picker's late result did not release its reservation.");
                Require(completion.Task.IsCanceled, "The late result replaced cancellation.");
            }
            finally
            {
                pendingField.SetValue(null, null);
                ((IDisposable)request).Dispose();
            }
            var dismissed = Activator.CreateInstance(requestType, 0x4D12, false, false, false, CancellationToken.None)!;
            var dismissal = (TaskCompletionSource<string?>)completionProperty.GetValue(dismissed)!;
            pendingField.SetValue(null, dismissed);
            try
            {
                register.Invoke(dismissed, null);
                onResult.Invoke(null, new object?[] { null, 0x4D12, 0, null });
                Require(dismissal.Task.IsCompletedSuccessfully && dismissal.Task.Result is null,
                    "User dismissal did not return null.");
                Require(pendingField.GetValue(null) is null, "User dismissal retained the reservation.");
            }
            finally
            {
                pendingField.SetValue(null, null);
                ((IDisposable)dismissed).Dispose();
            }
            Debug.Log("FILE_SYSTEM_PICKER_COMPLETION_PASSED: cancellation, unrelated/late results, dismissal (no JNI).");
        }

        /// <summary>Exercises actual commit/launch gates with deterministic no-JNI thread interleavings.</summary>
        /// <exception cref="InvalidOperationException">A cancellation, shutdown, or ownership assertion fails.</exception>
        /// <exception cref="TargetInvocationException">An invoked picker helper unexpectedly fails.</exception>
        private static void CheckPickerConcurrency()
        {
            var pickerType = typeof(AndroidStorageAccess);
            var requestType = pickerType.GetNestedType("PickerRequest", BindingFlags.NonPublic)!;
            var pendingField = pickerType.GetField("s_pending", BindingFlags.NonPublic | BindingFlags.Static)!;
            var completionProperty = requestType.GetProperty("Completion")!;
            var register = requestType.GetMethod("RegisterCancellation")!;
            var commit = pickerType.GetMethod("CommitResult", BindingFlags.NonPublic | BindingFlags.Static)!;
            var launch = pickerType.GetMethod("TryLaunch", BindingFlags.NonPublic | BindingFlags.Static)!;
            var shutdown = pickerType.GetMethod("Shutdown", BindingFlags.NonPublic | BindingFlags.Static)!;
            const string ResultLocation = "content://test/document/committed";

            using (var cancelled = new CancellationTokenSource())
            {
                var request = Activator.CreateInstance(requestType, 0x4D20, true, true, true, cancelled.Token)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                cancelled.Cancel();
                var sideEffects = 0;
                var launched = (bool)launch.Invoke(null, new object[] { request, new Action(() =>
                {
                    sideEffects++;
                }) })!;
                commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    sideEffects++;
                    return ResultLocation;
                }) });
                Require(!launched && sideEffects == 0 && completion.Task.IsCanceled,
                    "Prior cancellation allowed native launch or permission persistence.");
                Require(pendingField.GetValue(null) is null, "Cancelled commitment retained the reservation.");
            }

            using (var reentrant = new CancellationTokenSource())
            {
                var request = Activator.CreateInstance(requestType, 0x4D21, true, true, true, reentrant.Token)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    reentrant.Cancel();
                    return ResultLocation;
                }) });
                Require(reentrant.IsCancellationRequested && completion.Task.IsCompletedSuccessfully &&
                    completion.Task.Result == ResultLocation,
                    "Reentrant cancellation hid a result after commitment began.");
                Require(pendingField.GetValue(null) is null, "Committed result retained the reservation.");
            }

            using (var concurrent = new CancellationTokenSource())
            using (var commitStarted = new ManualResetEventSlim())
            using (var releaseCommit = new ManualResetEventSlim())
            {
                var request = Activator.CreateInstance(requestType, 0x4D22, true, true, true, concurrent.Token)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                var commitWork = Task.Run(() => commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    commitStarted.Set();
                    Require(releaseCommit.Wait(5000), "The test did not release result commitment.");
                    return ResultLocation;
                }) }));
                Task? cancelWork = null;
                try
                {
                    Require(commitStarted.Wait(5000), "Result commitment did not start.");
                    cancelWork = Task.Run(() => concurrent.Cancel());
                    Require(SpinWait.SpinUntil(() => concurrent.IsCancellationRequested, 5000), "Cancellation did not signal.");
                    Require(!completion.Task.IsCompleted, "Cancellation replaced an in-flight committed result.");
                    Require(!cancelWork.IsCompleted, "Cancellation bypassed the result commitment gate.");
                }
                finally
                {
                    releaseCommit.Set();
                    var work = cancelWork is null ? new Task[] { commitWork } : new Task[] { commitWork, cancelWork };
                    Require(Task.WaitAll(work, 5000), "Result commitment/cancellation deadlocked during registration disposal.");
                }
                Require(completion.Task.IsCompletedSuccessfully && completion.Task.Result == ResultLocation,
                    "Concurrent cancellation hid the committed URI.");
                Require(pendingField.GetValue(null) is null, "Concurrent commitment retained the reservation.");
            }

            using (var launchStarted = new ManualResetEventSlim())
            using (var releaseLaunch = new ManualResetEventSlim())
            using (var shutdownStarted = new ManualResetEventSlim())
            {
                var request = Activator.CreateInstance(requestType, 0x4D23, true, true, true, CancellationToken.None)!;
                var completion = (TaskCompletionSource<string?>)completionProperty.GetValue(request)!;
                pendingField.SetValue(null, request);
                register.Invoke(request, null);
                var launches = 0;
                var launchWork = Task.Run(() => launch.Invoke(null, new object[] { request, new Action(() =>
                {
                    launches++;
                    launchStarted.Set();
                    Require(releaseLaunch.Wait(5000), "The test did not release the native launch boundary.");
                }) }));
                Task? shutdownWork = null;
                try
                {
                    Require(launchStarted.Wait(5000), "The authorized launch did not start.");
                    shutdownWork = Task.Run(() =>
                    {
                        shutdownStarted.Set();
                        shutdown.Invoke(null, null);
                    });
                    Require(shutdownStarted.Wait(5000), "Shutdown did not start.");
                    Require(!completion.Task.IsCompleted && ReferenceEquals(pendingField.GetValue(null), request),
                        "Shutdown invalidated a request inside its native launch boundary.");
                }
                finally
                {
                    releaseLaunch.Set();
                    var work = shutdownWork is null ? new Task[] { launchWork } : new Task[] { launchWork, shutdownWork };
                    Require(Task.WaitAll(work, 5000), "Launch/shutdown arbitration deadlocked.");
                }
                Require((bool)launchWork.Result! && launches == 1 && completion.Task.IsCanceled,
                    "Launch-before-shutdown did not preserve ordering or cancel outstanding completion.");
                Require(pendingField.GetValue(null) is null, "Shutdown retained the request.");
                var launchedAfterShutdown = (bool)launch.Invoke(null, new object[] { request, new Action(() =>
                {
                    launches++;
                }) })!;
                var resultReads = 0;
                commit.Invoke(null, new object[] { request, new Func<string?>(() =>
                {
                    resultReads++;
                    return ResultLocation;
                }) });
                Require(!launchedAfterShutdown && launches == 1 && resultReads == 0,
                    "A stale request launched or persisted a result after shutdown.");
            }
            Debug.Log("FILE_SYSTEM_PICKER_CONCURRENCY_PASSED: commit vs cancellation, reentrancy, launch vs shutdown (no JNI).");
        }

        /// <summary>Finds a compiled assembly by filename without relying on output path formatting.</summary>
        /// <param name="assemblies">The paths returned by Unity's player compiler.</param>
        /// <param name="name">The expected assembly filename.</param>
        /// <returns>Whether Unity reported the assembly.</returns>
        private static bool ContainsAssembly(IEnumerable<string> assemblies, string name)
        {
            foreach (var assembly in assemblies)
            {
                if (string.Equals(Path.GetFileName(assembly), name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Reads the optional comma-delimited player targets passed by the staging script.</summary>
        /// <returns>The requested target names, or Windows64 and Android by default.</returns>
        private static string[] GetRequestedTargets()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (arguments[i] == "-storageValidationTargets")
                {
                    return arguments[i + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            return new[] { "StandaloneWindows64", "Android" };
        }

        /// <summary>Requires an Android API to throw before executing JNI in the Editor.</summary>
        /// <param name="operation">The Android-only operation.</param>
        /// <exception cref="InvalidOperationException">The operation does not throw the platform guard.</exception>
        private static void RequireThrowsPlatform(Action operation)
        {
            RequireThrows<PlatformNotSupportedException>(operation, "Android JNI was not guarded in the Editor.");
        }

        /// <summary>Requires a direct or reflection-wrapped exception from a no-JNI validation operation.</summary>
        /// <typeparam name="TException">The expected operation exception type.</typeparam>
        /// <param name="operation">The operation whose behavior is being checked.</param>
        /// <param name="message">The explanation if no matching exception is raised.</param>
        /// <exception cref="InvalidOperationException">The operation does not throw the expected exception.</exception>
        private static void RequireThrows<TException>(Action operation, string message) where TException : Exception
        {
            try
            {
                operation();
            }
            catch (TException)
            {
                return;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is TException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        /// <summary>Fails an unexpected validation condition.</summary>
        /// <param name="condition">Whether the expectation holds.</param>
        /// <param name="message">The failure explanation.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}

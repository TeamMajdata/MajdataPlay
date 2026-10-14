# Android Java wrappers

The project uses the standalone `Tools/CSharp/AndroidJavaWrapperGenerator` CLI.
It analyzes SDK, project, and Unity Java APIs in C# and does not require the legacy
AndroidJavaGenerator analyzer or a Java compiler.

## Generate before the first Unity import

The runtime consumers compile against the generated wrapper DLL. From the
repository root, run the independent tool before opening a fresh checkout:

```powershell
Push-Location Tools/CSharp/AndroidJavaWrapperGenerator
dotnet run --project AndroidJavaWrapperGenerator.csproj -- --project ../../.. --unity-data "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data" --no-docs
Pop-Location
```

Use the repository-pinned Unity Editor with Android Build Support and .NET 9.
Pass another `--unity-data` directory for another installation, and `--sdk` if
using a separately configured Android SDK. Project mode discovers the same Java
inputs as the Editor refresh, generates outside Assets, and publishes only a
successful, ownership-checked result. `--no-docs` skips official documentation
for the initial bootstrap; normal generation fetches it, and `--offline-docs`
uses the shared cache without network access.

## Namespaces and directories

The source root is `Tools/AndroidJavaWrapper/src`, with
namespace `MajdataPlay.Platform.Android.Runtime`:

```text
Tools/AndroidJavaWrapper/
    AndroidJavaWrapper.csproj
    src/
        AndroidJni.cs
        IJavaObject.cs
        JavaInvocationException.cs
        JavaObjectReference.cs
        JavaWrapperConverter.cs
        AndroidJavaObjectExtensions.cs
        JavaObjectExtensions.cs
        Bindings/
            Android/App/Activity.e.cs
            Android/Net/Uri.g.cs
            Com/Unity3d/Player/UnityPlayer.g.cs
            Java/Lang/JavaObject.g.cs
            Net/Majdata/Majdataplay/StorageAccess.g.cs
```

Generated Java namespaces are below `.Runtime.Bindings`; each relative namespace
component has a matching directory. The JNI helpers belong directly to the
root namespace and directory. Unnamed Java packages use `Bindings/` itself.

`AndroidJavaWrapper.csproj` targets netstandard2.1/C# 9, includes every `src/**/*.cs`,
and references the pinned Unity managed modules. The compiled
`MajdataPlay.Platform.Android.Runtime.dll` is installed directly in this Android
plugin directory; Unity imports the DLL for Editor and Android.

Generated sources, the ownership manifest, and local reference configuration
stay local and are ignored by Git. The installed DLL/XML are tracked Git LFS
assets and are updated by the standalone build. Handwritten partial extensions
such as `src/Bindings/Android/App/Activity.e.cs` and the numeric conversions are
tracked beside `.g.cs` files. The DLL importer `.meta` remains tracked and retains
its GUID across rebuilds. The legacy generator's runtime fixtures
live under `Tools/Tests/AndroidJavaGeneratorValidation/Fixtures/LegacyRuntime`.

## Editor refresh and migration

Use **Tools > MajdataPlay > Android > Regenerate Java wrappers** for full SDK/BCL,
project Java inputs under `Assets/Plugins/Android`, and Unity Android sources and
runtime metadata. The current Editor's SDK configuration takes precedence over
the bundled SDK. The highest installed stable API is selected; generation does
not change the Player minimum SDK.

The manual menu fetches official English documentation. The cached-documentation
menu runs offline. Java source/class/JAR/AAR asset changes queue a refresh, reuse
cached pages, and fetch only missing documentation. Requests are coalesced;
changes during a run queue one follow-up. Cancel from the tools menu or progress
indicator. A domain reload cancels the process and restores queued work.

Successful compilation publishes the DLL and then retires unchanged manifest-owned
sources and paired metadata from the former `Runtime` and `Generated` directories.
Modified generated sources, conflicting metadata, and ambiguous ownership are
rejected before publication. Unowned files remain intact. A failed build retains
the installed DLL, and identical rebuilt bytes retain its timestamp.

Create `.e.cs` partial extensions in `Tools/AndroidJavaWrapper/src`. Rebuild and
install them with `--build-wrapper PROJECT --unity-data DIRECTORY`, without
regenerating Java inputs. Existing custom project files are preserved. See
`Tools/AndroidJavaWrapper/README.md` for commands. The DLL is compiled with
`UNITY_ANDROID`; extensions must use `AndroidJni.EnsureAndroid()` for runtime
platform checks because DLL conditional compilation does not change in the Editor.

Unity GameActivity metadata is read from `GRADLE_USER_HOME` or the user's Gradle
cache. Resolve Android Gradle dependencies once, or supply the official runtime
AAR/JAR dependencies under `Assets/Plugins/Android`. Explicit plugin archives take
precedence; fallback cache versions are reported. Complete process logs remain
in the tool's ignored `.work/editor-logs` directory.

## Runtime ownership and storage

Returned references are owned wrappers. Dispose them deterministically on the
Unity main thread or a JVM-attached thread. `ownsReference: false` borrows an
existing callback reference through `reference.ToWrapper<T>(ownsReference: false)`.
Reference constructors are internal; this extension and
`JavaWrapperConverter.FromHandle<T>(handle)` check Java assignability before
wrapping. Null references/zero handles return null. Java interfaces use `I`-prefixed contracts and
reference wrapper classes; a C# interface implementation does not create a Java
callback proxy. The tracked Activity extension adapts `AndroidJavaRunnable`.

The JavaObject extension `T As<T>() where T : JavaObject` in `JavaObjectExtensions`
checks the Java type and returns an independently owned wrapper reference.
Dispose the result after use; disposing either wrapper leaves the other valid.
The AndroidJavaObject extension remains `ToWrapper<T>(ownsReference)`.

Each generated `.Class` access returns a fresh owned `Class<TWrapper>` wrapper
representing a Java `java.lang.Class` object; throwable wrappers use the
non-generic `Class` companion. Use it for reflection or `Class` parameters and
dispose it after use. `JavaReference` exposes the Unity object for JNI APIs.
Call generated static methods for static Java dispatch.

Java throwable wrappers preserve Java exception inheritance and derive from
`JavaInvocationException`, so callers can catch the corresponding generated type.
Dispose caught throwable wrappers after use. They expose `IJavaObject` and
`JavaStackTrace`; CLR single inheritance prevents an exception wrapper from also
inheriting `JavaObject`. Borrow a throwable as `JavaObject` with
`exception.JavaReference.ToWrapper<JavaObject>(ownsReference: false)` when required
by a Java object parameter. Java generics retain their class/interface bounds, with
non-generic companions for raw/wildcard Java signatures not expressible as CLR
type arguments.

The tracked `MajdataPlayActivity.e.cs` extension supplies `AndroidJavaProxy`
overloads for `RegisterOnNewIntentCallback`, `RegisterOnActivityResultCallback`,
and `RegisterDispatchKeyEventCallback`. Each overload creates a Java proxy,
borrows it through the generated callback-interface wrapper, and invokes the
typed registration method with its exact JNI descriptor. Temporary wrappers,
global references, and local references are released after registration. The
production runtime and keyboard keep the managed proxies rooted for the Java
activity's lifetime; Java retains the registered callbacks.

`AndroidRuntime.IsInitialized` becomes true only after SDK/application metadata
and all three callback registrations, including the real `AndroidKeyboard`,
complete successfully. Failed initialization leaves it false and logs the
exception. The current activity and metadata queries return owned references,
which initialization disposes after use.

The SAF backend uses typed bindings in `Bindings/Android` and
`Bindings/Net/Majdata/Majdataplay`. Java nested types remain inside their parent as
`StorageAccess.Result`, `StorageAccess.Entry`, `StorageAccess.CursorHandle`, and
`StorageAccess.StreamHandle`. The backend closes cursors/streams before disposing
references, retains opaque grant-preserving URIs, and uses the bridge's typed
error protocol and bounded signed-byte transfers. Managed protocol constants are
checked against the production `StorageAccess.java` source. All 338 public
`KeyCode` names and numeric values remain unchanged.

## Validation

Run `Tools/Tests/AndroidJavaWrapperGeneratorValidation` with `--editor-inputs`
for the complete real Java input set and production consumer compilation in
Editor, Android Editor, and Android Player conditions. Run
`Tools/Tests/ValidateAndroidJavaWrapperRefreshUnity.ps1` for real isolated Unity
refresh, import, compilation, cancellation, and repeated GUID/timestamp checks.
`ValidateFileSystemUnity.ps1` checks the generated API, key codes, ownership,
platform guards, and selected Player compilation in an isolated project.

The Android storage and FFmpeg device harnesses generate fresh CLI bindings and
compile an isolated wrapper DLL including their own test Java APIs and extensions.
Build and run them separately for each required ABI. Match each dated result
to its binding implementation, APK, and executed scope; managed compilation
and Editor checks do not establish JNI, provider, native FFmpeg, IL2CPP,
ARMv7/ARM64, or physical-device runtime correctness.

The storage device harness now stages the production keyboard and requires
`AndroidRuntime.IsInitialized`, matching package metadata, new-intent callbacks,
non-null and null activity results, and key-down/key-up callbacks before emitting
`RUNTIME_INIT_PASSED`. This is an intermediate regression marker; require the
selected mode's terminal result and matching run/ABI evidence for a completed
device run. See `Tools/Tests/AndroidStorageDeviceValidation/README.md` and its
dated `RESULTS.md` for commands and executed coverage.

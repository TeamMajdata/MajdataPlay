# Android Java wrappers

The project uses the standalone `Tools/CSharp/AndroidJavaWrapperGenerator` CLI.
It analyzes SDK, project, and Unity Java APIs in C# and does not require the legacy
AndroidJavaGenerator analyzer or a Java compiler.

## Generate before the first Unity import

The runtime consumers compile against local generated sources. From the
repository root, run the independent tool before opening a fresh checkout:

```powershell
dotnet run --project Tools/CSharp/AndroidJavaWrapperGenerator/AndroidJavaWrapperGenerator.csproj -- --project . --unity-data "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data" --no-docs
```

Use the repository-pinned Unity Editor with Android Build Support and .NET 9.
Pass another `--unity-data` directory for another installation, and `--sdk` if
using a separately configured Android SDK. Project mode discovers the same Java
inputs as the Editor refresh, generates outside Assets, and publishes only a
successful, ownership-checked result. `--no-docs` skips official documentation
for the initial bootstrap; normal generation fetches it, and `--offline-docs`
uses the shared cache without network access.

## Namespaces and directories

The output root is `Assets/Plugins/MajdataPlay/Platform/Android/Runtime`, with
namespace `MajdataPlay.Platform.Android.Runtime`:

```text
Runtime/
    AndroidJni.cs
    IJavaObject.cs
    JavaInvocationException.cs
    JavaObjectReference.cs
    Bindings/
        Android/Net/Uri.g.cs
        Com/Unity3d/Player/UnityPlayer.g.cs
        Java/Lang/JavaObject.g.cs
        Net/Majdata/Majdataplay/StorageAccess.g.cs
```

Generated Java namespaces are below `.Runtime.Bindings`; each relative namespace
component has a matching directory. The four JNI helpers belong directly to the
root namespace and directory. Unnamed Java packages use `Bindings/` itself.

Generated sources, their metadata, and the ownership manifest stay local and are
ignored by Git. Handwritten partial extensions such as `Bindings/Android/App/Activity.cs`
and the numeric conversion extensions are tracked alongside the `.g.cs` files.
Their original script and retained directory GUIDs are preserved. The old
annotated declarations are removed; the legacy generator's runtime fixtures now
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

Successful publication migrates the former `Generated` directory and former
package-relative paths into `Runtime/Bindings`. Source and manifest `.meta`
files move with their GUIDs; unchanged source timestamps are preserved. Modified
generated sources, handwritten destinations, conflicting metadata, and ambiguous
ownership are rejected before publication. Unowned files remain intact.

Unity GameActivity metadata is read from `GRADLE_USER_HOME` or the user's Gradle
cache. Resolve Android Gradle dependencies once, or supply the official runtime
AAR/JAR dependencies under `Assets/Plugins/Android`. Explicit plugin archives take
precedence; fallback cache versions are reported. Complete process logs remain
in the tool's ignored `.work/editor-logs` directory.

## Runtime ownership and storage

Returned references are owned wrappers. Dispose them deterministically on the
Unity main thread or a JVM-attached thread. `ownsReference: false` borrows an
existing callback reference. Java interfaces use `I`-prefixed contracts and
reference wrapper classes; a C# interface implementation does not create a Java
callback proxy. The tracked Activity extension adapts `AndroidJavaRunnable`.

The SAF backend uses typed bindings in `Bindings/Android` and
`Bindings/Net/Majdata/Majdataplay`. Java nested types are flattened as
`StorageAccess_Result`, `StorageAccess_Entry`, `StorageAccess_CursorHandle`, and
`StorageAccess_StreamHandle`. The backend closes cursors/streams before disposing
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

The Android storage and FFmpeg device harnesses generate fresh CLI bindings.
Build and run them separately for each required ABI. Historical device results
predate this wrapper migration; managed compilation and Editor checks do not
establish JNI, provider, native FFmpeg, IL2CPP, ARMv7/ARM64, or physical-device
runtime correctness.

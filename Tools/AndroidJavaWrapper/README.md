# Android Java wrapper library

This standalone .NET 9 project compiles generated Java bindings and handwritten
partial extensions into `MajdataPlay.Platform.Android.Runtime.dll`. Unity imports
that DLL from `Assets/Plugins/MajdataPlay/Platform/Android`.

## Generate and install

From the repository root, select the generator's SDK pin before running it:

```powershell
Push-Location Tools/CSharp/AndroidJavaWrapperGenerator
dotnet run --project AndroidJavaWrapperGenerator.csproj -- --project ../../.. --unity-data "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data" --no-docs
Pop-Location
```

The pinned Unity **6000.3.17f1** Editor and its Android Build Support supply the
Java metadata and managed references. The command creates missing project files,
generates sources under `src`, compiles the library, and installs it only after
successful compilation. Existing project customizations are preserved. The Unity
menu **Tools > MajdataPlay > Android > Regenerate Java wrappers** runs the same
pipeline.

## Extend a wrapper

Add `XXX.e.cs` beside the generated `XXX.g.cs`. Use the same namespace and a
`partial class`; all `src/**/*.cs` files are included in the assembly. For example,
`src/Bindings/Java/Lang/Integer.e.cs` extends
`MajdataPlay.Platform.Android.Runtime.Bindings.Java.Lang.Integer`.

The helpers `AndroidJni`, `IJavaObject`, `JavaInvocationException`, and
`JavaObjectReference` belong directly to `MajdataPlay.Platform.Android.Runtime`.
Generated bindings use its `Bindings` child namespace, with matching directories.

After editing an extension, rebuild and install without scanning Java metadata:

```powershell
Push-Location Tools/CSharp/AndroidJavaWrapperGenerator
dotnet run --project AndroidJavaWrapperGenerator.csproj -- --build-wrapper ../../.. --unity-data "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data"
Pop-Location
```

The library targets **netstandard2.1**, uses **C# 9**, and compiles with
`UNITY_ANDROID`. Unity does not recompile conditional directives in imported
DLLs. Extensions must call `AndroidJni.EnsureAndroid()` before Android-only work
instead of selecting Editor behavior with `UNITY_EDITOR`; the runtime check
rejects JNI calls outside an Android Player.

For an IDE or local compilation, open `AndroidJavaWrapper.csproj`. Generation
writes ignored `AndroidJavaWrapper.local.props` with the local Unity installation.
Alternatively pass `-p:UnityData=...` or set `UNITY_EDITOR_DATA`. A direct
`dotnet build AndroidJavaWrapper.csproj -c Release` from this directory builds
the library locally; use `--build-wrapper` above to install it into Unity.

## Ownership and validation

Generated helpers, `.g.cs` files, the ownership manifest, local configuration,
build output, and the installed DLL/XML are ignored by Git. Keep the project,
handwritten `.e.cs` extensions, and the DLL importer `.meta` tracked. The installer
preserves the DLL GUID and skips identical output. A failed build retains the
previous installed DLL. Former manifest-owned Unity wrapper sources and paired
metadata are retired after successful publication; modified or unowned files are
preserved or rejected by ownership checks.

See `../Tests/AndroidJavaWrapperGeneratorValidation/README.md` for managed
regressions and the isolated Unity refresh smoke test. These checks do not establish
Android JNI, IL2CPP, or device correctness.

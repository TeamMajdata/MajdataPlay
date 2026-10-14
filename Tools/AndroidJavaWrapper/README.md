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

The helpers `AndroidJni`, `IJavaObject`, `JavaInvocationException`,
`JavaObjectReference`, `JavaWrapperConverter`, `AndroidJavaObjectExtensions`, and `JavaObjectExtensions`
belong directly to `MajdataPlay.Platform.Android.Runtime`.
Generated bindings use its `Bindings` child namespace, with matching directories.
Java nested types remain inside their parent partial wrapper; extend one by
reopening the same enclosing partial classes in an `.e.cs` file. Generic wrappers
preserve Java bounds, alongside non-generic companions for raw/wildcard APIs
that cannot be represented by CLR generic arguments.

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
and intermediate build output are ignored by Git. The installed DLL/XML are
tracked Git LFS assets updated by the standalone build. Keep the project,
handwritten `.e.cs` extensions, and the DLL importer `.meta` tracked. The installer
preserves the DLL GUID and skips identical output. A failed build retains the
previous installed DLL. Former manifest-owned Unity wrapper sources and paired
metadata are retired after successful publication; modified or unowned files are
preserved or rejected by ownership checks.

Generated `.Class` properties return fresh owned `Class<TWrapper>` wrappers for
Java `java.lang.Class` objects. Throwable wrappers use the non-generic `Class`
companion. Dispose each result after reflection or a `Class` parameter. Access
`JavaReference` when a Unity JNI API requires the underlying reference; use
generated static methods for static Java dispatch.

The `(AndroidJavaObject, bool ownsReference = true)` constructors are internal.
Use `reference.ToWrapper<TWrapper>(ownsReference: false)` to borrow an existing
reference with a Java assignability check, or omit `ownsReference` to adopt it.
`JavaWrapperConverter.FromHandle<TWrapper>(handle)` creates an independently owned
wrapper from a real JNI handle after checking its Java type. Null references and
zero handles return null; incompatible types throw `InvalidCastException`.
The `JavaObjectExtensions` helper supplies `T As<T>() where T : JavaObject` for checked
conversion to another wrapper. `As<T>()` returns a new, independently owned
reference; dispose the result after use. Disposing either wrapper does not
invalidate the other. The `AndroidJavaObject.ToWrapper<T>(ownsReference)`
extension retains its explicit adoption/borrowing semantics.

Java failures are catchable through the corresponding generated throwable class.
Dispose caught throwable wrappers after use. They preserve Java inheritance and
derive from `JavaInvocationException`, which implements `IJavaObject` and
`IDisposable`. CLR single inheritance prevents them from also deriving from
`JavaObject`; use `IJavaObject` when an API must accept both object and throwable
wrappers. For a `JavaObject` parameter, use
`exception.JavaReference.ToWrapper<JavaObject>(ownsReference: false)` and dispose
the temporary borrowed wrapper after the call. The original Java stack trace is
available as `JavaStackTrace`.

`src/Bindings/Net/Majdata/Majdataplay/MajdataPlayActivity.e.cs` adapts
`AndroidJavaProxy` arguments for the generated `RegisterOnNewIntentCallback`,
`RegisterOnActivityResultCallback`, and `RegisterDispatchKeyEventCallback` methods.
The overloads create the Java proxy, borrow its reference through the generated
callback-interface wrapper, and invoke the typed registration method. They
dispose the temporary wrappers/global references and delete the local reference
after registration. Callers must keep the managed proxy alive while Java uses
it; the production runtime and keyboard hold static references for this purpose.

`AndroidRuntime.IsInitialized` reports completion of SDK/application metadata
and all production registrations, including the real keyboard callback. The
storage device harness checks this state, metadata, new-intent and nullable
activity-result dispatch, and key-down/key-up dispatch before emitting the
intermediate `RUNTIME_INIT_PASSED` marker. A completed device regression also
requires the selected mode's terminal result with fresh run/ABI evidence; consult
`../Tests/AndroidStorageDeviceValidation/README.md` and its dated `RESULTS.md`.

See `../Tests/AndroidJavaWrapperGeneratorValidation/README.md` for managed
regressions and the isolated Unity refresh smoke test. These checks do not establish
Android JNI, IL2CPP, or device correctness.

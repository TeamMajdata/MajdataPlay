# Android Java wrappers

## Editor refresh with the standalone CLI

Use **Tools > MajdataPlay > Android > Regenerate Java wrappers** to build and run
`Tools/CSharp/AndroidJavaWrapperGenerator` in the background. This command exports
the complete public Android SDK and Android Java BCL, all supported Java inputs
under `Assets/Plugins/Android`, and the current Editor's Android player Java
sources and runtime class archive. The SDK comes from Unity's Android External
Tools setting, with the bundled SDK as the fallback. The highest installed stable
API is selected; generation does not change the Player minimum SDK.

Generated sources go to `Assets/Plugins/MajdataPlay/Platform/Android/Generated`,
with the root namespace `MajdataPlay.Platform.Android.Runtime.Bindings`. For example,
`android.net.Uri` becomes `MajdataPlay.Platform.Android.Runtime.Bindings.Android.Net.Uri`.
This complete binding set can coexist with the existing annotated wrappers below.
The CLI and its generated JNI helpers do not reference the legacy source generator.
The local output and documentation cache are ignored by Git.

The manual command downloads and caches official English SDK/BCL documentation.
**Regenerate Java wrappers (cached documentation)** uses that cache without HTTP.
Imports, deletions, and moves of Java sources, class files, JARs, or AARs under
`Assets/Plugins/Android` queue the documented refresh automatically. Cached
pages are reused; only missing documentation is fetched.
Multiple changes are coalesced, and changes made during a running command queue
one follow-up. External Unity/SDK changes require the manual command. Use
**Cancel Java wrapper generation**, or the Editor progress indicator, to stop it.

The Editor builds only the independent .NET 9 tool, then runs its DLL directly.
It waits for a successful generation outside Assets before publishing and
refreshing Unity. Changed generated files retain their existing `.meta` GUIDs;
unchanged files are not rewritten, and stale owned sources are removed together
with their metadata. Handwritten files and modified generated files are protected
by the generator ownership manifest. Put custom partial extensions outside the
local generated directory so that they remain tracked. A script domain reload
stops the current command and restores its queued request in the next domain.

.NET 9 and Android Build Support are required. No Java compiler or bytecode tool
is invoked by this refresh command. Unity GameActivity dependency metadata is
read from `GRADLE_USER_HOME` (or the user `.gradle` module cache); resolve the
Android Gradle dependencies once or supply the official AAR/JAR dependencies
under `Assets/Plugins/Android`. AAR runtime JARs are analyzed directly in C#.
Explicit plugin inputs take precedence, and selected fallback cache versions
are reported in the Console. Tool-local `.work/editor-logs` retain full CLI
diagnostics; Console display is budgeted per Editor update. A failed build or
CLI generation does not replace the imported bindings. See the independent CLI
README for source-analysis limits, optional inputs, and JNI/runtime limits.

Run `Tools/Tests/ValidateAndroidJavaWrapperRefreshUnity.ps1` for isolated
validation of the new refresh workflow in the pinned Unity Editor. The smoke
checks real process cancellation, full generation/import/compilation, repeat
refresh GUID/timestamp preservation, and deeply nested package paths. It does not
execute JNI or build the main game.

## Existing annotated wrappers

The `JavaClassAttribute` and `JavaApiConfigurationAttribute` request generated
`JavaObject`-derived wrappers for Java classes and interfaces. The generator
is authored outside Assets and installed as an analyzer, not as a runtime plugin.

`JavaObject` is a concrete base class rather than an abstract one: `new JavaObject()`
creates a Java `java.lang.Object` (Java's `new Object()`), and the descriptor and
reference constructors construct or adopt arbitrary Java objects directly. Concrete
Java classes map each declared public constructor from the selected SDK to a C#
constructor with the exact JNI descriptor. Non-public constructors are ignored.
Classes without public constructors, Java interfaces, and abstract classes expose
only the reference-wrapping constructor; the generator does not add a parameterless
constructor that creates a plain `java.lang.Object` for these wrappers.

`JavaObject.Equals`, `GetHashCode`, and `ToString` invoke the wrapped Java object's
virtual `equals`, `hashCode`, and `toString` methods. The `==` and `!=` operators
use the same equality semantics. Managed reference identity and null/non-wrapper
comparisons do not invoke JNI, including after disposal. Comparing distinct
wrappers, hashing, and formatting require live references on a JVM-attached thread;
accessing a disposed wrapper's reference throws `ObjectDisposedException`.

When the target Java class or a superclass overrides these Object methods, the
generator also emits the corresponding managed overrides. An `equals(Object)`
override adds `IEquatable<TheWrapper>` and nullable typed `Equals`, plus an explicit
`GetHashCode` override. Generated Object overrides invoke JNI directly with the
exact descriptors for `equals(Object):boolean`, `hashCode():int`, and
`toString():String`. These three instance signatures are excluded from ordinary
method mapping, so no ordinary Object method alias is generated. Names are
reserved by signature rather than by name, so a Java `toString(int)` or
`toString(String)` overload keeps its natural C# name and becomes a `ToString`
overload next to the generated `ToString()` override.
Detection is independent of inherited member flattening; Java interface
declarations and same-name overloads do not qualify. Interface wrappers inherit
the Object methods from `JavaObject`.

A wrapper declaration may explicitly derive from another annotated wrapper to keep
the Java inheritance hierarchy in C#, for example `java.lang.Number` and the boxed
types derived from it. Java override members already generated by the base wrapper
are not repeated, `JavaClassName` and same-named fields or static methods are
shadowed with `new`, and a derived Java constructor creates the instance through
`AndroidJni.Construct` before the base wrapper adopts the reference. The generator
does not verify that the declared C# base matches the Java ancestry; the
declaration owns that correspondence. See the generator README for the full
contract and its limits.

See `Tools/CSharp/AndroidJavaGenerator/README.md` at the repository root for installation,
SDK/JDK configuration, Java inputs, member mapping, documentation provenance,
ownership, threading, diagnostics, and managed/Unity/device validation limits.

```powershell
./Tools/CSharp/AndroidJavaGenerator/build.ps1 -Install
```

Only JNI invocation is Android-player-specific; the attributes and generated
wrapper declarations remain available in the Editor. Each Java class has one
categorized partial declaration: `Runtime/App/Activity.cs`,
`Runtime/Content/Intent.cs`, and `Runtime/View/KeyEvent.cs`. The old root-level
constant classes have been removed; use the generated PascalCase constants
(for example, `Activity.ResultOk`, `Intent.ActionOpenDocument`, and
`KeyEvent.KeycodeA`). `IO/KeyCode.cs` keeps all 338 existing enum names and
numeric values while referencing generated constants. The Activity declaration
retains its managed `AndroidJavaRunnable` adapter alongside the generated
`Java/Lang/Runnable` overload; interface wrappers are not callback proxies.

### Asset identity during consolidation

A pre-migration search of Assets, Packages, ProjectSettings, and Tools found no
references to either the original script GUIDs or the newly declared Activity /
Intent GUIDs outside their own metadata. The categorized declarations retain
the **original tracked** metadata unchanged, rather than regenerating script
GUIDs or keeping duplicate compatibility types:

| Original script | Categorized declaration | Preserved GUID |
| --- | --- | --- |
| `Runtime/Activity.cs` | `Runtime/App/Activity.cs` | `39aab5cf0d5f33a4191672095f28c364` |
| `Runtime/Intent.cs` | `Runtime/Content/Intent.cs` | `05788a69f2044d34a9c8c943d4efdfa2` |
| `Runtime/KeyEvent.cs` | `Runtime/View/KeyEvent.cs` | `f0f486f2d88203040b05d82998a231b3` |

The shared `JavaObject` base class was relocated from the assembly root into
`Runtime/Java/Lang/JavaObject.cs` with its original metadata GUID
`1b30d938cc2149f39e859cfcaf866fa7` preserved. The generator emits that fully
qualified base name, so reinstall the analyzer after changing it.

The unused, newly created Activity / Intent metadata GUIDs were retired; only
the new View directory receives a new GUID. Repeated isolated validation removes
only the six obsolete root-level script / metadata files before copying Runtime,
so directory merging cannot resurrect the legacy types.

## Storage runtime bindings

The SAF backend consumes generated wrappers declared in categorized subdirectories
of `Runtime/`:

| Category | Generated wrappers |
| --- | --- |
| `App` | `Activity` |
| `Content` | `Intent`, `ContentResolver`, `UriPermission` |
| `Net` | `Uri` |
| `Provider` | `DocumentsContract` |
| `Java/Lang` | `Runnable` |
| `Java/Util` | `List` |
| `Unity` | `UnityPlayer` |
| `View` | `KeyEvent` |
| `Storage` | `StorageAccess`, `StorageResult`, `DocumentEntry`, `DocumentCursor`, `DocumentStream` |

The declarations pin Android API 36; they do not change the Player minimum SDK.
The project bridge and its nested result/entry/cursor/stream classes are in `Runtime/Storage/`. Their `Sources` refer to the production
`Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java`, not
to duplicated Java stubs. Unity's Java dependency is located with the
`{UnityData}` configuration token and the bundled `classes.jar`, so no
developer-specific absolute editor path is committed.

Install the analyzer before importing a fresh checkout. Only the annotated
partial declarations and their Unity metadata are checked in; generated members
remain in the compiler. After changing the generator itself, reinstall it with
the command above. Changes to Java inputs require a script recompilation as
described in the generator README.

Java reference results are owned wrappers and must be disposed deterministically
on a JVM-attached thread. Wrapping an existing callback reference with
`ownsReference: false` borrows rather than disposes it. Disposing a cursor or
stream wrapper only releases the JNI reference: the Storage backend explicitly
calls the generated `Close()` first, even on exceptional paths. SAF results
retain their typed error protocol, signed-byte arrays, and bounded transfers;
locations remain grant-preserving opaque URIs rather than local paths.

Run `Tools/Tests/ValidateFileSystemUnity.ps1` to install and stage the production
wrappers and the real KeyCode enum in an isolated Unity project, verify generated
API shapes, unique categorized mappings, all 338 pre-migration enum values, and
Editor JNI guards, and compile selected Player targets. Compilation does not validate
JNI dispatch, URI grants, providers, or IL2CPP behavior on an Android device.

## Real Android runtime validation

`Tools/Tests/Build-AndroidStorageDeviceValidation.ps1` stages a separate project
and builds a development IL2CPP APK with High stripping. The corresponding
`Run-AndroidStorageDeviceValidation.ps1` runner selects an actual installed ABI,
opens the real SAF picker, restarts the dedicated application to check persisted
grants, and validates process/backend/bitness against its durable result. It
never clears the main game's data or deletes the selected parent directory.

See `Tools/Tests/AndroidStorageDeviceValidation/README.md` for the full / replay /
release protocol, and `RESULTS.md` in that directory for the 2026-10-09 physical
Mi MIX 2S API-35 ARM64 and ARMv7 passes. Coverage is limited to the isolated app
and the local external-storage document provider; other providers, devices,
Mono, and the full game's Player remain separate validation work.

# Android Java generator managed regression harness

This is an **independent .NET 9 executable**, not a build of the Unity project's
IDE-generated solution or project files. It links the production generator's
directly maintained source files and production Android runtime sources. It uses
**real Unity 6000.3.17f1** CoreModule and AndroidJNIModule assemblies; it does not
substitute Unity or JNI stubs.

## Prerequisites

- Initialize the repository's existing submodules as described in its root README.
- Install the .NET 9 SDK/runtime.
- Install Unity **6000.3.17f1** with Android support, including its SDK and OpenJDK.
- Install SDK platform **android-36** (or select another installed API explicitly).
- Have the NuGet package **Microsoft.CodeAnalysis.CSharp 4.3.1** and its transitive
  dependencies cached. Restore uses only this directory as a package source;
  there is no implicit network restore or package upgrade.
- Have the production files from the generator/runtime implementation available:
  - Tools/AndroidJavaGenerator/Source/**/*.cs
  - Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java
  - Assets/Plugins/MajdataPlay/Platform/Android/JavaClassAttribute.cs
  - Assets/Plugins/MajdataPlay/Platform/Android/JavaApiConfigurationAttribute.cs
  - Assets/Plugins/MajdataPlay/Platform/Android/JavaObject.cs
  - Assets/Plugins/MajdataPlay/Platform/Android/AndroidJni.cs
  - Assets/Plugins/MajdataPlay/Platform/Android/JavaInvocationException.cs

No generated analyzer DLL installation is necessary. The installed analyzer DLL,
its importer metadata, and the Unity build/install scripts are outside this
harness's scope.

## Run

From the repository root, run only this standalone project:

~~~powershell
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj
~~~

The default UnityEditorData property is:

~~~text
C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data
~~~

Select a different installation at **build time**, and give the executable the
same runtime path if overriding discovery:

~~~powershell
$unityData = 'D:/Unity/6000.3.17f1/Editor/Data'
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj "-p:UnityEditorData=$unityData" -- --unity-editor-data $unityData
~~~

The executable defaults these generator environment settings to the selected
Unity installation and the repository helper. Existing environment values are
respected; explicit application arguments take precedence:

~~~powershell
$env:UNITY_ANDROID_SDK = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK'
$env:UNITY_JAVA_HOME = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK'
$env:MAJDATA_JAVA_EXTRACTOR = (Resolve-Path 'Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java').Path
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj -- --api-level 36 --timeout-seconds 180
~~~

Useful bounded subsets and help:

~~~powershell
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj -- --list
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj -- --filter source-api
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj -- --filter runtime
dotnet run --project Tools/Tests/AndroidJavaGeneratorValidation/AndroidJavaGeneratorValidation.csproj -- --help
~~~

Supported application arguments:

| Argument | Purpose |
| --- | --- |
| --unity-editor-data path | Runtime Unity DLL/reference-discovery directory; also set the MSBuild property when building. |
| --android-sdk path | SDK root overriding UNITY_ANDROID_SDK or the bundled SDK. |
| --java-home path | JDK root overriding UNITY_JAVA_HOME or bundled OpenJDK. |
| --extractor path | Real repository Java extractor helper overriding MAJDATA_JAVA_EXTRACTOR. |
| --api-level number | Installed Android platform to use for fixture bootclasspath and generator requests; default 36. |
| --filter text | Run case names containing this text, case-insensitively. |
| --timeout-seconds number | Entire worker deadline; default 180, maximum 3600 seconds. |
| --tool-timeout-seconds number | javac/jar preparation deadline; default 90, maximum 3600 seconds. |
| --repo-root path | Relocate the production sources and Temp workspace. |
| --list / --help | Inspect the finite suite or its arguments without running prerequisites. |

Internal --worker / --workspace arguments are used by the supervisor, not
required for normal execution. A filter matching no cases is an error, not a pass.

## Isolation and outputs

- MSBuild obj/bin files go exclusively to the ignored
  Temp/AndroidJavaGeneratorValidation/{obj,bin} directories.
- Each invocation creates a unique directory below
  Temp/AndroidJavaGeneratorValidation/runs. No pre-existing run is deleted.
- Only the small tracked Fixtures/**/*.java files are copied into this workspace.
  Fixture count is limited to 64 and each file to one MiB.
- Fixture compilation uses the selected android.jar as **bootclasspath**:
  javac -encoding UTF-8 -source 8 -target 8 -parameters -bootclasspath android.jar -d classes ...
  The archive is built with jar cf fixtures.jar -C classes .
- Source-file, individual class-file, and jar requests bind the **same five root
  APIs**: fixtures.Widget, fixtures.Contract, fixtures.Mode,
  fixtures.Widget$Nested, and fixtures.Widget$Inner. No classpath/jar fallback is silently added to
  source/class-file mode. Separate ClassPath and assembly-configuration cases
  cover those supported inputs explicitly.
- Every case runs in its own child process with an entire-process wall-clock
  limit. On timeout the supervisor terminates that worker's process tree,
  reports failure, and continues the finite remaining cases. javac/jar have
  their own deadlines. Captured output is capped at one MiB per stream.
- Workers instantiate the real AndroidJavaGenerator using CSharpGeneratorDriver.
  CSharpCompilation includes all five production runtime sources, the actual
  Unity DLLs, and host trusted BCL assemblies. The harness assembly itself is
  excluded from dynamic compilation references to avoid masking missing types.
- Positive output is semantically compiled and emitted to an in-memory DLL.
  Generated source, input declarations, diagnostics, worker logs, and Summary.txt
  remain under the run directory for failure diagnosis. No fixture, generated
  script, DLL, or test scene is written into the real Assets tree.

Exit status is 0 for a passing suite, 1 for regression failures, and 2 for
invalid arguments/prerequisites or preparation failures. Failing assertions are
not converted into skips, and generator crashes/internal-failure diagnostics
are not accepted as successful negative-test results.

## Coverage

- All Java primitives, including signed byte -> sbyte and char -> char, exact
  method/constructor/field JNI descriptors forwarded to AndroidJni.Call/GetField,
  and getter-only fields even when Java fields are mutable.
- Nullable annotated-wrapper references, nullable unknown AndroidJavaObject
  references, interface/concrete overloads, generic and bounded-generic erasure.
- Public inherited methods, inherited static methods, covariant override
  selection, field hiding, and IncludeInheritedMembers=false.
- Static final primitive/string **compile-time const** values: finite values,
  NaN, infinities, Unicode, escaped punctuation, newlines, tabs, embedded NUL, and
  unpaired UTF-16 surrogates. String constants must round-trip exactly through
  the helper's base64-utf16be XML representation.
  The NUL fixture intentionally remains strict to catch broken XML transports.
- Interface default/static methods and constants, no interface instantiation
  constructors, enum values/valueOf, and public static/non-static nested Java
  binary names with $. Non-static Widget$Inner constructors must prepend the
  exact Lfixtures/Widget; JVM parameter and expose typed enclosingInstance in
  C#. The parity case compares these source/class/jar constructor contracts.
- Primitive, string, typed-wrapper, and unknown-object arrays; varargs; jagged
  arrays; nullable array dimensions and nullable object leaves.
- Throwing static initializers on Widget and Mode. Successful extraction proves
  metadata discovery did not initialize these classes.
- Valid XML Java type/method/parameter/return documentation and escaped <, >, &,
  plus multiple DocumentationPaths and SDK/JDK per-class configuration precedence.
- Byte-for-byte determinism across reused/fresh drivers and refresh after a Java
  constant changes at the same path with identical C# declaration text and a
  reused driver on the next compilation. Cached unchanged-driver, fresh-driver,
  and rebuilt-compilation behaviors are tested explicitly; see the caching
  boundary below.
- Nonpartial/static/nested/generic declarations, duplicate mappings, missing Java
  types/dependencies/inputs/SDK/platform/JDK/helper, negative API levels, erased
  constructor/method overload collisions, and handwritten member conflicts.
  Assertions use actual AJG diagnostic IDs, not unstable full message text.
- Pure managed MapArray/Wrap null behavior, null elements/rows, signed bytes,
  empty arrays, converter errors, and non-Android PlatformNotSupportedException
  guards. No fake AndroidJavaObject handles are created.
- UNITY_ANDROID runtime **managed compilation** using the actual Unity modules;
  this case does not execute JNI or simulate a Player.

## External-file refresh and Roslyn caching

Java sources are external filesystem inputs, not immutable C# inputs tracked by
Roslyn's driver. The source-refresh regression keeps Java paths and C# declaration
text fixed, changes REVISION from 1 to 2, and tests **three distinct behaviors**:

1. Replaying the **same driver and identical compilation** returns cached
   revision 1. The generator is not called. This limitation is asserted and
   documented, not silently treated as an external-file refresh success.
2. A **fresh driver on the unchanged compilation** reads revision 2. This proves
   the generator has no stale cross-compilation static metadata cache.
3. The **reused driver on a rebuilt compilation** with new syntax-tree objects
   but identical C# text reads revision 2 and agrees exactly with fresh output.
   This models a real new script-compiler invocation after input invalidation.

The parent's Unity Editor refresh integration requests
CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)
to invalidate Unity's compilation cache. Imported Java assets and the explicit
regeneration menu need that clean compilation; this managed suite does not
execute the Unity AssetPostprocessor or prove the Editor refresh lifecycle.
An untouched cached driver is not a filesystem watcher, and passing this suite
does not claim otherwise.

## Java source-level boundary

The extractor compiles direct .java inputs at source/target level 8 against
the selected Android bootclasspath. Java 9+ source syntax is not supported in
that direct-source mode. APIs already compiled with newer Java syntax can be
inspected via .class/.jar inputs when their class-file version is supported by
the configured JDK; source level 8 does not imply a Java 8 class-file-only
extractor. This suite's main fixtures deliberately use Java 8 syntax so all three
input representations are comparable. Extraction support is separate from
Android dex/packaging/runtime compatibility.

## Validation limits

Passing this suite means only managed .NET/Roslyn validation and successful
Java compiler-symbol extraction have passed. It does **not** validate Unity
Editor analyzer loading, Unity's own script compiler, scene lifecycle, Player
integration, Java packaging, JNI marshaling on Android, reference ownership on a
real JVM, IL2CPP/AOT, ARMv7/ARM64 ABI, or hardware behavior. Run the parent's
isolated Unity/Player validation and real Android device tests separately.

# Android Java wrapper generator validation

This standalone .NET 9 harness validates the independent CLI in
../../CSharp/AndroidJavaWrapperGenerator/. It never builds a Unity-generated
project or modifies a production Unity asset.

## Prerequisites

- .NET 9 SDK and the NETStandard.Library.Ref 2.1.0 reference pack.
- Unity 6000.3.17f1, including its real UnityEngine.CoreModule and
  UnityEngine.AndroidJNIModule assemblies.
- A stable Android SDK platform; the pinned Unity SDK defaults to installed API 36.
- For source/class/JAR parity only, Unity's OpenJDK with javac.

The generator itself does not need or invoke a JDK. The harness compiles Java
fixtures with javac as a separate correctness oracle for the pure C# source
analyzer. Its --sdk-only mode does not require a JDK or launch Java tools.
Roslyn assemblies come from the executing .NET SDK, without NuGet packages.

## Commands

From the repository root:

~~~powershell
dotnet run --project Tools/Tests/AndroidJavaWrapperGeneratorValidation/AndroidJavaWrapperGeneratorValidation.csproj
~~~

Compile the complete SDK API with no Java tool requirement:

~~~powershell
dotnet run --project Tools/Tests/AndroidJavaWrapperGeneratorValidation/AndroidJavaWrapperGeneratorValidation.csproj -- --sdk-only
~~~

Validate the actual complete SDK together with all project and Unity Java inputs,
and the Editor command and publication helpers, without launching Java tools:

~~~powershell
dotnet run --project Tools/Tests/AndroidJavaWrapperGeneratorValidation/AndroidJavaWrapperGeneratorValidation.csproj -- --editor-inputs
~~~

This mode also requires the Unity GameActivity dependency metadata in the local
Gradle module cache, or supplied as an Android plugin dependency. It preserves
both Unity Activity source declarations rather than substituting a stub.

Run all source regressions and complete SDK compilation together:

~~~powershell
dotnet run --project Tools/Tests/AndroidJavaWrapperGeneratorValidation/AndroidJavaWrapperGeneratorValidation.csproj -- --full-sdk
~~~

The Windows defaults use C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data.
For another host, pass --unity-data DIRECTORY, --sdk DIRECTORY,
--jdk DIRECTORY (parity tests only), and/or --netstandard-refs DIRECTORY.
The reference-pack path is the directory containing netstandard.dll and the
other netstandard2.1 reference assemblies, not the .NET runtime DLL directory.

All scratch sources, classes, archives, and generated wrappers stay in this
project's ignored .work/ directory. Each run creates an exclusively owned
subdirectory and removes it after validation, including on ordinary failures.

Official HTML parsing, cache/offline behavior, parameter restoration, and
bounded request concurrency use independently written local HTML fixtures;
ordinary validation never needs network access. To also validate downloaded
real official pages, place android-uri.html and oracle-integer.html in an ignored
directory and pass --official-samples DIRECTORY. That validation compares the
HTML with actual SDK metadata and compiles the documented output in both branches.

## Isolated Unity Editor validation

Run the real background CLI refresh with the repository-pinned Unity Editor:

~~~powershell
./Tools/Tests/ValidateAndroidJavaWrapperRefreshUnity.ps1
~~~

The PowerShell 7 script creates a new ignored `.work/native-editor-*` project,
stages the production Editor sources and asmdef boundaries, and copies the
project Java inputs and independent CLI sources. Use `-UnityPath` for another
installation of the exact required version. It never opens or builds the main
game project. The Gradle dependency prerequisites above still apply.

The smoke cancels a directly running CLI, reruns full generation with cached
documentation, waits for actual Unity asset import and binding assembly loading,
then repeats the refresh and checks that a generated source keeps its GUID and
timestamp. Deep staging/package paths exercise the Windows Unity Mono extended
path support. The script requires the success marker and rejects C# compiler
errors even if the Editor process exits with zero. Project and logs stay in
`.work` for inspection. This validates the Editor workflow, without JNI execution
or an Android Player build.

## Coverage

- Actual SDK metadata and package structure, Java BCL, boxed Integer, exact JVM
  method descriptors, and independent SDK discovery.
- Pure C# .java analysis compared with the corresponding .class and .jar public
  nonsynthetic API. Scanning does not initialize throwing static initializers.
- Public and inaccessible enclosing types, static and nonstatic nested classes,
  exact enclosing-instance constructor descriptors, and arbitrary class filenames.
- Nonpublic intermediate bases, public inherited members, interface inheritance,
  covariant interface returns, defaults/constants/static methods, and enums.
- Generic erasure and bounds, imports and nested binary names, varargs, field
  array declarators, annotations, boxed values, strings, nullable/jagged arrays,
  and names colliding with CLR/generated members.
- Serial/parallel SDK metadata and byte-for-byte output identity (one/four CPU
  workers), original exception ordering, and bounded work batches.
- Android/Oracle HTML API matching, same-arity overloads, varargs, fields, English
  XML summaries/params/returns/Java throws, provenance, cache, offline mode, and
  actual bounded simultaneous HTTP-fixture requests.
- Deterministic generation, partial class / nonpartial interface declarations,
  getter-only Java fields, custom namespaces, and SDK-free archive input.
- Compilation as C# 9 against the complete .NET Standard 2.1 reference pack and
  the actual pinned Unity modules, with and without UNITY_ANDROID.
- Pure managed MapArray/Wrap contracts and non-Android guards, with no invented
  JNI pointers or fake Java references.
- Editor request path discovery and argument quoting, real Unity Editor API
  compilation, import coalescing, online/cache request selection, cancellation,
  and persisted pending requests using narrow managed lifecycle substitutes.
- Editor output publication from the CLI manifest, unchanged timestamps, Unity
  metadata retention, stale asset/metadata cleanup, modified-file conflicts,
  invalid manifests, and corrupt staged outputs before destination writes.
- CLI errors, cancellation, malformed class files, output traversal rejection,
  regeneration ownership, stale generated cleanup, and handwritten extensions.

Successful managed compilation is separate from JNI execution. These checks do
**not** validate Unity Editor import/lifecycle, Player behavior, JVM calls,
reference ownership on a real device, IL2CPP/AOT, ARMv7/ARM64, callbacks, or
physical Android hardware. Test those integration layers separately.

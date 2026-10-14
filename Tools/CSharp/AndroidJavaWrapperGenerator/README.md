# Android Java wrapper CLI

This independent .NET 9 command-line tool exports public Java APIs as Unity JNI
bindings. It has no project, assembly, or source reference to the existing
AndroidJavaGenerator Roslyn analyzer. Ordinary output mode writes only its
selected destination; project mode compiles and installs a wrapper library without
installing an analyzer.

## Requirements and build

- .NET 9 SDK (the tool directory pins the 9.0 SDK family).
- An Android SDK with an installed stable platform, normally the SDK bundled with
  the repository's Unity **6000.3.17f1** Android Build Support.
- Unity CoreModule and AndroidJNIModule when compiling/using the output.

**The CLI does not require a JDK, javac, javap, or any other external executable.**
It reads JVM class files in C# and analyzes Java source declarations in C#.
The separate regression harness uses javac only as an independent reference for
checking source/class/JAR API parity.

From the repository root, select the tool's pinned SDK directory:

~~~powershell
Push-Location Tools/CSharp/AndroidJavaWrapperGenerator
dotnet build AndroidJavaWrapperGenerator.csproj
dotnet run --project AndroidJavaWrapperGenerator.csproj -- --help
Pop-Location
~~~

Only this explicit tool project is built; do not build Unity-generated projects.
For a packaged CLI:

~~~powershell
dotnet publish Tools/CSharp/AndroidJavaWrapperGenerator/AndroidJavaWrapperGenerator.csproj -c Release -o Temp/AndroidJavaWrapperGenerator
~~~

## Bootstrap this Unity project

Before importing a fresh checkout, generate the bindings its runtime consumers
need. From the repository root:

~~~powershell
Push-Location Tools/CSharp/AndroidJavaWrapperGenerator
dotnet run --project AndroidJavaWrapperGenerator.csproj -- --project ../../.. --unity-data "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data" --no-docs
Pop-Location
~~~

Project mode reuses the Editor's Java-input discovery and ownership publisher,
without opening the project in Unity. It writes the runtime helpers into
`Tools/AndroidJavaWrapper/src` and Java bindings into its `Bindings` directory.
It creates missing `AndroidJavaWrapper.csproj` and `global.json`, compiles the
netstandard2.1 library together with handwritten `.e.cs` extensions, and installs
`MajdataPlay.Platform.Android.Runtime.dll` and XML documentation into
`Assets/Plugins/MajdataPlay/Platform/Android`. It accepts `--sdk`, `--api-level`, worker counts, and
documentation options; the destination and root namespace are project-owned.
Use `--offline-docs` for cached documentation, or omit documentation flags to
retrieve official pages. Existing project customizations and DLL importer GUIDs
are preserved. A failed compilation retains the installed DLL. After successful
publication, unchanged manifest-owned sources in the former `Runtime` and
`Generated` Unity directories and their paired metadata are retired. Unowned files
are preserved; modified owned files stop publication.

Same-layout source publication records its verified previous source hashes and
intended manifest in an ignored `.java-wrappers.publication.json` before the
first write. If publication stops after replacing only some sources, the next
run accepts only bytes matching that record's previous or intended hashes, then
completes the requested generation and removes the record after committing the
final manifest. New inputs may supersede the interrupted generation; its newly
owned stale sources are still retired. Real edits, malformed records, mismatched
manifest bindings and unsafe paths remain publication errors. Output-root and
namespace-layout migrations retain their existing strict preflight checks.
Windows atomic replacement briefly retries sharing, locking and unable-to-remove
failures; persistent failures report the affected path and native error code.

After editing extensions, use `--build-wrapper PROJECT --unity-data DIRECTORY`
to compile and install the existing source project without SDK input discovery
or Java generation. See `../../AndroidJavaWrapper/README.md` for the extension
workflow and local IDE builds.

## Generate the full Android and Java API

~~~powershell
dotnet run --project Tools/CSharp/AndroidJavaWrapperGenerator/AndroidJavaWrapperGenerator.csproj -- --sdk "C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK" --namespace Example --output Temp/AndroidJavaWrappers
~~~

The CLI default root namespace is MajdataPlay.Platform.Android.
The project Editor integration explicitly uses MajdataPlay.Platform.Android.Runtime.
Runtime support types are directly in that root namespace; generated Java
bindings are below its Bindings child namespace.
The default exports every publicly accessible, nonsynthetic type in the chosen
platform's android.jar, including Android's Java BCL and boxed primitive types.
It does not merge in desktop-only JDK APIs. Without --sdk, SDK discovery searches
the environment and the fixed Unity installation. Without --api-level, the
highest installed stable numeric platform is selected. Use --api-level 36 to pin
an installed platform.

~~~powershell
dotnet run --project Tools/CSharp/AndroidJavaWrapperGenerator/AndroidJavaWrapperGenerator.csproj -- --sdk "C:/Android/SDK" --api-level 36 --namespace Example --output Temp/AndroidJavaWrappers --input "C:/Libraries/Example.java" --input "C:/Libraries/Other.class" --input "C:/Libraries/api.jar"
~~~

Repeat --input for additional .java, .class, and .jar files. Input directories
are scanned recursively; .zip archives are accepted as well. A class file's binary
name comes from its constant pool, so its filename and enclosing directory do
not have to reproduce its Java package. Archives and SDK inputs are inspected
without loading classes or executing static initializers. Multi-release archives
use their base declarations; META-INF/versions entries are skipped with a warning.

Use --no-sdk together with --input to export standalone Java libraries. Unknown
reference types that are absent from the provided metadata use the generated
JavaObject reference wrapper. Source names must still resolve unambiguously to
an exact Java binary name, including fully qualified source names. Supply
missing dependency archives for source type resolution.

An optional --include-package java.nio limits the initially selected package
and its subpackages. Public dependencies needed by the selected API are also
exported; this can include packages outside the filter. Omit this option for
full generation.

## Official English API documentation

Official-documentation enrichment is enabled by default. Android SDK and BCL
pages are read from the English Android Developers reference. Missing Java BCL
members are supplemented from Oracle JDK 17 Javadoc. Both modern and legacy
Javadoc HTML layouts are supported by the pure C# parser.

Class/interface descriptions, field descriptions, method and constructor
summaries, parameter descriptions, return values, and Java exception notes are
translated into English C# XML documentation. Every imported description
includes its official page/member URL in a seealso element. Java exceptions are
recorded as Java behavior in remarks; the generated CLR JNI exception contract
remains explicit. HTML entities and XML-sensitive text are escaped.

Parameter names come from the documented Java signature, matched to the exact
JVM overload. Primitive and reference types, arrays, varargs, nested types,
generic erasure, bridge methods, and enclosing-instance constructor prefixes
are considered. Ambiguous overloads are left unchanged rather than assigned
another overload's names. The restored names are sanitized only when required
for legal, unique C# identifiers, and param tags use those final names.

Parsed descriptions are cached in OUTPUT/.documentation-cache; complete HTML
navigation trees are not stored. Cache keys include the official URL, parser
version, and exact public JVM member contract. Positive entries normally expire
after 30 days; missing pages are cached for one day. Offline mode can reuse older
entries whose contract still matches. Cache files are local runtime data.

~~~powershell
dotnet run --project Tools/CSharp/AndroidJavaWrapperGenerator/AndroidJavaWrapperGenerator.csproj -- --output Temp/AndroidJavaWrappers --jobs 8 --docs-jobs 4 --docs-cache Temp/AndroidJavaDocumentation
~~~

- --docs-jobs NUMBER bounds simultaneous official-site requests (default 4,
  maximum 32), separately from CPU worker count.
- --docs-cache DIRECTORY selects a shared parsed-documentation cache.
- --offline-docs reads that cache without HTTP requests.
- --refresh-docs refreshes online entries; it cannot be combined with offline mode.
- --no-docs disables enrichment while retaining source/class-file names and
  binding-purpose XML descriptions.

If a page is missing, unreachable, or has no recognized API layout, its available
metadata names and binding descriptions remain. The CLI reports coverage and a
bounded list of missing types; it does not invent a name or pair descriptions
with a different overload. Repeated host failures stop further requests to that
host during the run. Generated documentation reflects the selected SDK's member
set even if the official online page describes a newer platform.

## Parallel analysis and generation

--jobs NUMBER controls CPU worker parallelism; its default is the available CPU
count. --jobs 1 executes indexed analysis and emission serially on the calling
thread. SDK/JAR bytes are decompressed in bounded batches, and independent class
parsing/hashing runs in parallel. ZIP stream access stays serialized. Each batch
contains at most 128 entries or 32 MiB of raw bytes, except that a permitted
larger class occupies its own batch. Existing per-class and total scan bounds
still apply.

Java source files are read and parsed as independent compilation units in
parallel. Source type resolution and global inheritance/name planning commit in
stable input order. Wrapper file emission runs in parallel over frozen plans
and read-only caches, then diagnostics and files merge deterministically. JNI
descriptors, generated source bytes, and duplicate-definition precedence are
independent of worker count. Cancellation and original parsing exceptions remain
observable without AggregateException wrappers.

## Generated API

- Packages map to PascalCase namespace components below {root}.Bindings and
  matching directories relative to the configured root: android.net.Uri becomes
  Example.Bindings.Android.Net.Uri in Bindings/Android/Net/Uri.g.cs when the root is Example.
- java.lang.Object is Example.Bindings.Java.Lang.JavaObject.
- Public nested types remain inside their enclosing partial wrapper classes.
  Exact $ binary names remain in JNI descriptors. Inaccessible enclosing types
  prevent export.
- Every generated wrapper is a public partial class. Java abstract classes can
  wrap existing instances but do not expose Java construction methods.
- Java interfaces generate nonpartial I-prefixed C# interfaces, plus partial
  reference wrapper classes for interface-valued JNI results. Java interface
  inheritance and class/interface implementation relationships are preserved.
- Classes derive from their closest exported Java superclass. Public members of
  inaccessible intermediate bases remain available.
- Public Java fields, including static/final fields, become getter-only
  properties. Field reads use JNI and the original field descriptor.
- Public constructors and methods use exact JVM descriptors. Java overloads are
  preserved; collisions caused by C# naming or erased signatures receive stable
  disambiguation. Colliding constructor signatures use stable CreateJavaInstance
  factory methods for the additional overloads. Interface contracts with
  incompatible C# return types use explicit implementations.
- Generic Java classes also expose generic C# wrappers with their Java class and
  interface bounds. Type parameters derive from JavaObject, either directly or
  through their class bound. Non-generic companions preserve raw and wildcard
  Java APIs where CLR type arguments cannot express the Java signature. Nested
  generic wrappers preserve their enclosing type parameters. Boxed primitives
  remain typed reference wrappers. Java byte maps to sbyte.
- Java strings map to nullable C# strings in member signatures. Reference values
  and every array dimension preserve nullability. Jagged/reference arrays are
  marshaled using the declared JVM component descriptor.
- Each wrapper exposes a getter-only static Class property for its Java class
  object (ClassJavaProperty when the wrapper itself is named Class). That result
  is an independently owned `Class<TWrapper>` wrapper. Throwable classes use the
  non-generic Class companion because CLR exceptions cannot also inherit JavaObject.
- Java equals, hashCode, and toString overrides become CLR Equals, GetHashCode,
  and ToString overrides. An equals override also implements `IEquatable<TWrapper>`.
  Java Comparable, Comparator, and Iterable expose the corresponding CLR
  `IComparable`/`IComparable<T>`, `IComparer<T>`, and `IEnumerable`/`IEnumerable<T>` contracts.
- JNI exceptions become the matching exported Java throwable wrapper, preserving
  the Java throwable inheritance chain and Java stack trace. Unknown subclasses
  use their nearest exported throwable ancestor. Throwable derives from the
  CLR JavaInvocationException base and implements IJavaObject/IDisposable;
  CLR single inheritance prevents it from also deriving from JavaObject.
  For a JavaObject parameter, borrow the throwable's reference with
  `exception.JavaReference.ToWrapper<JavaObject>(ownsReference: false)`.
- Reference constructors taking AndroidJavaObject are internal. Use
  `javaReference.ToWrapper<TWrapper>(ownsReference: true)` or
  `JavaWrapperConverter.FromHandle<TWrapper>(handle)` for checked public conversion.
  Conversion validates Java assignability before constructing a wrapper;
  incompatible types throw InvalidCastException. A null reference or zero handle
  returns null. Dispose an owned wrapper, including a caught throwable wrapper.
- JavaObjectExtensions provides `T As<T>(this JavaObject) where T : JavaObject`
  for checked wrapper conversion. The result owns an independent Java reference and is non-null.
  Dispose it after use; source and result lifetimes are independent.

The output root contains AndroidJni.cs, IJavaObject.cs,
JavaObjectReference.cs, JavaInvocationException.cs, JavaWrapperConverter.cs,
AndroidJavaObjectExtensions.cs, JavaObjectExtensions.cs, and generated throwable registration. These are
self-contained, declared directly in the configured root namespace, and have no
MajdataPlay runtime or old-generator dependency. For --namespace Example, their
qualified names are Example.AndroidJni, Example.IJavaObject,
Example.JavaObjectReference, Example.JavaInvocationException, Example.JavaWrapperConverter,
Example.AndroidJavaObjectExtensions, and Example.JavaObjectExtensions. Types in the
unnamed Java package are generated directly in Example.Bindings, with their
sources under Bindings/. Every source's directory matches its namespace relative
to the configured root. The Editor publisher migrates previous manifest-owned
paths together with their Unity metadata, preserving existing GUIDs.
All generated sources use nullable analysis, block namespaces, CRLF, and C# 9
features compatible with **netstandard2.1**. Compile the complete output with
UnityEngine.CoreModule and UnityEngine.AndroidJNIModule references.

Handwritten extensions belong in separate partial-class source files. Generated
files are tracked by java-wrappers.manifest.json using content hashes. Re-running
the CLI updates unchanged generated files and removes only unchanged stale files
owned by its previous manifest. It refuses to overwrite handwritten or modified
files; choose a fresh destination if you intend to discard such changes.

Android library archives (`.aar`) are also supported as inputs. Pure C# ZIP
analysis reads `classes.jar` and direct `libs/*.jar` entries, with shared entry
and byte limits. Resources and native libraries are not treated as Java classes.

## Unity Editor integration

The project menu **Tools > MajdataPlay > Android > Regenerate Java wrappers**
builds this CLI with .NET 9 and launches its compiled DLL in the background. It
uses the current Editor's SDK configuration, project Java inputs from
`Assets/Plugins/Android`, Unity Android player sources, and the selected Unity
runtime `classes.jar`. Unity GameActivity and available transitive dependencies
are located in the Gradle module cache (`GRADLE_USER_HOME`, or the user `.gradle`
directory). Literal project/Editor Gradle template versions take precedence; when
no matching cached version is available, the newest stable cached version is used
with a Console notice. Explicit plugin archives take precedence. A missing
GameActivity dependency keeps every Unity source in the request and gives an
actionable diagnostic; resolve the Android Gradle dependencies once, or supply the
official dependency AAR/JARs under `Assets/Plugins/Android`. The CLI continues to
be independent of the old analyzer.

The menu generates the full SDK/BCL with online official English documentation.
Java asset changes automatically queue a refresh with official documentation;
existing cached pages are reused and only missing pages are fetched.
The cached-documentation menu also permits an explicit offline run. Generation
runs outside Assets in `Tools/AndroidJavaWrapper/src`, with namespace root
`MajdataPlay.Platform.Android.Runtime`; wrappers are under its `Bindings` child.
The independent project compiles these sources and handwritten extensions before
publishing its DLL into `Assets/Plugins/MajdataPlay/Platform/Android`. Auto refresh
is suspended until DLL publication and legacy-source retirement finish, so Unity
imports the complete result. Output ownership is checked before publication,
preserving custom source/project files and the DLL importer GUID.
Generated `.g.cs` files, runtime helpers, DLL/XML output, and tool `.work` caches
and logs are local and ignored by Git; handwritten `.e.cs` extensions stay tracked.
Console display is budgeted per Editor update; complete process
diagnostics are retained in `.work/editor-logs`.

Cancel from the Android tools menu or the Editor progress indicator. Queued work
survives script domain reloads. Runtime consumers use the full bindings and no
longer require the old annotated wrapper declarations or analyzer.

## Source analysis boundary

Source input is API analysis rather than a Java compiler. The parser resolves
package/import/nested-type names and generic erasure against all supplied
metadata, and skips method bodies and initializers. It does not type-check Java
expressions, create bytecode, package Java code, or prove that a source compiles.
Unresolved or ambiguous declarations report a generation error instead of
silently guessing a JNI descriptor. The parser supports class, interface,
annotation, enum, and record declarations, nested types, imports, bounded
generics, Unicode escapes, text blocks, and source array
declarators. It excludes method-local/anonymous types and synthetic bridges.
Module declarations are unsupported; provide module libraries as .class/.jar
inputs. Annotation values, throws declarations, and generic arguments are not
semantically type-checked. APIs that require compiler-specific synthetic behavior
should be provided as compiled .class/.jar files.

## JNI ownership and validation

Generated references own their Unity Java reference by default. Dispose owning
wrappers and Class results on Unity's main thread or a JVM-attached thread.
The reference constructor accepts ownsReference: false for explicitly borrowed
references; their original owner must outlive the wrapper. Disposed wrappers
reject subsequent reference access. Returned wrappers/arrays contain owning
references and must be disposed by callers.

JNI execution requires an Android Player. Editor/non-Android entry points throw
PlatformNotSupportedException before invoking Unity native APIs. Interface
contracts describe existing Java instances; implementing a C# interface alone
does not create a Java callback object. Java proxies/packaging remain the
consumer's responsibility.

Run the independent regressions as described in
../../Tests/AndroidJavaWrapperGeneratorValidation/README.md. These compile
emitted code against the .NET Standard 2.1 reference pack and real Unity modules,
with and without UNITY_ANDROID. They do not execute JNI, Unity lifecycle,
IL2CPP/AOT, or physical Android devices.

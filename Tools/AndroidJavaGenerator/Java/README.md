# Android Java metadata sidecar

JavaApiExtractor.java is a dependency-free, JDK 17 source-launchable metadata
extractor. It reads Java source and class-file compiler symbols through
JavaCompiler, JavacTask, Elements, Types and DocTrees. It never uses reflection,
loads target classes, evaluates their static initializers, runs annotation
processors, or generates target bytecode.

## Run

Use a **full JDK 17**. Unity 6000.3.17f1's Android OpenJDK distribution is suitable.
The Android SDK platform jar must contain java/lang/Object.class; it is the
exclusive boot classpath for target analysis, not the desktop JDK runtime.

PowerShell, from the repository root:

~~~powershell
$jdk = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK'
$android = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platforms/android-35/android.jar'
& "$jdk/bin/java.exe" Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java `
    --android-jar $android `
    --classpath 'C:/dependencies/example.jar' `
    --source 'C:/sources/example/src' `
    --type 'com.example.Example$Inner' `
    --documentation 'C:/android-sdk/sources/android-35' `
    --include-inherited true
~~~

Use single quotes for binary nested names in PowerShell so '$' is not interpreted.
On other platforms, use the corresponding JDK bin/java and quote shell arguments.
Pass actual argument-vector items, not shell-concatenated strings, from callers.

| Option | Meaning |
| --- | --- |
| --android-jar path | Required Android SDK platform jar; exactly once. |
| --source path | Repeatable .java file or root. Roots recursively supply all .java files; empty roots fail. |
| --classpath path | Repeatable .jar or class-directory root. Caller order determines classpath precedence. |
| --type binary.name | At least one; repeatable. Nested types use '$'. Duplicates are collapsed. |
| --documentation path | Optional repeatable source root or source zip/jar. Ordered exact matches with prose take precedence over stripped stubs. |
| --include-inherited true or false | Global flag; exactly once, default false. |

Individual .class paths must be normalized to their package root **by the caller**.
A --classpath argument is one entry, not a platform-separated path list. There is
no implicit caller CLASSPATH, current-directory classpath, or source discovery
from classpath directories. All required source dependencies must be supplied
explicitly. Sources and requested types are canonicalized/deduplicated and sorted;
classpath order is preserved.

The primary compiler task uses:

~~~text
-proc:none -source 8 -target 8 -bootclasspath <android.jar>
-encoding UTF-8 -implicit:none -Xlint:none
~~~

Only parse() and analyze() run. When there are no --source inputs, a minimal
in-memory dummy compilation unit enables compiler symbol lookup of classpath and
Android runtime APIs. The extractor never calls generate() or task.call().
Optional documentation uses a separate parse-only task and is never analyzed.

For a caller-managed cache, the same single source may be compiled once:

~~~powershell
& "$jdk/bin/javac.exe" -proc:none -encoding UTF-8 -d 'C:/sidecar-cache' `
    Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java
& "$jdk/bin/java.exe" -cp 'C:/sidecar-cache' JavaApiExtractor --android-jar $android `
    --type 'android.os.Build$VERSION_CODES'
~~~

The caller owns cache invalidation, JDK discovery, launcher/compiler availability,
process timeouts and cancellation. Target jars belong in --classpath, not the
launcher's executable classpath. This tool makes no network requests.

## XML version 1

Success means exit 0 and exactly one **UTF-8 XML 1.0 document on stdout**. There is
no stdout logging or byte-order mark. Input/compiler/dependency errors mean a
nonzero exit code, an actionable UTF-8 message on stderr, and no partial XML.
Do not parse stdout after a failed process. The document is assembled before it
is published. Compiler warnings are collected internally and do not alter stdout.

~~~xml
<?xml version="1.0" encoding="UTF-8"?>
<java-api version="1">
  <type name="com.example.Example" interface="false" abstract="false" final="false"
        deprecated="false" summary="Example API." documentationUrl="">
    <field name="COUNT" descriptor="I" static="true" final="true" deprecated="false"
           declaringType="com.example.Example" constantKind="int" constantValue="123"
           summary="The count."/>
    <method name="&lt;init&gt;" descriptor="(I)V" static="false" deprecated="false"
            declaringType="com.example.Example" summary="Creates an example." returns="">
      <parameter name="value" summary="Initial value."/>
      <exception type="java.lang.IllegalArgumentException" summary="If invalid."/>
    </method>
    <nestedType name="com.example.Example$Inner"/>
  </type>
</java-api>
~~~

- Type and declaringType names are Java **binary names**. Descriptors use JVM
  internal names, such as Lcom/example/Example$Inner;, not canonical dotted names.
- interface, abstract, final, static and deprecated are lowercase true/false.
  Type flags reflect compiler modifiers (an interface is also abstract).
- summary, returns and documentationUrl are always present, and empty if unavailable.
  returns is documentation text, **not** a Java type; the return type is in descriptor.
- Types sort by binary name. Fields sort by name, erased descriptor and declaring
  type, followed by methods in that same order and nestedType references by binary
  name. Exceptions sort by binary name. Parameters retain JVM argument order.
- With inheritance enabled, public compiler-model members are included; constructors
  are always declared-only. Java override/hiding relationships eliminate inherited
  duplicates. Synthetic/bridge methods are excluded. Deduplication uses name plus
  the **complete erased descriptor, including return type**. Unrelated covariant
  interface signatures can therefore coexist; C# signature collision handling
  remains the emitter's responsibility.
- Public interface default and static methods are supported. Interface static
  methods are not inherited by implementing classes. As exposed by javac's
  getAllMembers, interface metadata with inheritance may also include Object APIs.
- Only public constructors on non-interface, non-abstract types are emitted. An
  implicit public default constructor is included; an enum has no public constructors.
- For a non-static member class (NestingKind.MEMBER, kind.CLASS, not STATIC), an
  <init> descriptor prepends its immediate enclosing type. The first parameter is
  named enclosingInstance with an empty summary. For example,
  Example.Inner(int) becomes (Lcom/example/Example;I)V. Static nested classes have
  no such argument. This compensates for the language model hiding a real JVM/JNI
  constructor argument, both for source and class-file symbols.
- Public nested members are reported as nestedType references, not recursively
  expanded; request additional --type arguments to extract their APIs. Local and
  anonymous classes are not supported as requested public APIs.
- Fields are read-only metadata: no getters, setters or target method invocations
  occur. Generic signatures are erased **at the declaration**, not substituted as
  members of a concrete generic subclass, so descriptors remain JNI-accurate.
- Missing superclasses, interfaces, generic bounds and public API signature types
  fail extraction. Source implementations must also analyze successfully; optional
  SDK documentation implementations do not need to compile.

### Constants and lossless String encoding

Only compile-time **public static final** primitive/String fields have
constantKind and constantValue. Boxed values, null, arrays, non-constant final
fields, enum instances and instance constants do not. Allowed kinds are:

~~~text
boolean byte char short int long float double string
~~~

Booleans are true/false; char is the decimal UTF-16 code unit (0..65535). Integers
are invariant decimal. Float/double use Java's locale-independent round-trippable
spelling, including NaN, Infinity, -Infinity and signed -0.0. The C# emitter must
handle these tokens explicitly rather than parsing with the user's culture.

Ordinary String values are XML-escaped directly; tabs, CR and LF use numeric
entities so attribute normalization does not destroy exact content. When **any**
code point is illegal in XML 1.0 (for example NUL, an unpaired UTF-16 surrogate, or
U+FFFE), the whole String uses this optional attribute:

~~~xml
<field name="NUL" descriptor="Ljava/lang/String;" static="true" final="true"
       deprecated="false" declaringType="com.example.Example"
       constantKind="string" constantEncoding="base64-utf16be"
       constantValue="AGEAAABi" summary=""/>
~~~

This example is the raw Java String code units 'a', NUL, 'b'. The emitter must:

1. If constantKind is string and constantEncoding is absent, use the XML-decoded
   constantValue without modification.
2. If constantEncoding equals base64-utf16be, base64-decode constantValue, require
   an even byte length, and reconstruct **each** 16-bit code unit as
   (highByte << 8) | lowByte. Preserve lone surrogates. Do not use a replacement-
   fallback UTF-16 decoder, UTF-8 conversion or a Unicode-scalar-only API.
3. Reject an unknown encoding instead of silently emitting the encoded text.

There is no UTF-16 byte-order mark. The extractor writes raw code units manually,
so malformed surrogate sequences are never replaced. This optional extension is
only used for String constants; numeric and char constants retain their original
protocol representation.

## Documentation matching and limitations

Comments attached to analyzed source symbols take precedence. Repeated documentation roots are searched in caller order; the first exact declaration match with actual prose is used. Empty, stripped SDK stubs do not hide documented declarations in later roots. If no root provides prose, a uniquely matched declaration may still supply parameter names. Each root retains the same conservative signature checks; documentation from distinct roots is not merged. DocTrees extracts
the entire body and @param, @return, @throws/@exception descriptions. Generic
@param tags are omitted. HTML structure is stripped, entities decoded, inline
code/literal contents and link labels retained, and whitespace normalized. No
translation is attempted; supplied English comments stay English. XML-invalid
**documentation** characters are replaced with U+FFFD; constants are never
sanitized. @inheritDoc is not expanded, @value is not evaluated, and unsupported
structural tags contribute no invented prose.

For optional SDK sources:

- Find package/path/TopLevel.java (nested classes share their outer file). A source
  root is the directory above package folders. A source archive can have an exact
  package path or a unique SDK/module prefix. Multiple matching entries are treated
  as ambiguous and ignored. Source files must use UTF-8.
- Parse only the matching file. Do not resolve implementation bodies, run annotation
  processors, visit local/anonymous implementation classes, or analyze SDK code.
  Unknown imports/private SDK implementation dependencies are not compilation errors.
- Match the exact binary owner and field name/type, or method name/full erased
  parameter-and-return descriptor. Resolve syntax conservatively against the
  primary compiler's real symbols, lexical bounds, explicit imports, packages and
  unique wildcard imports. Overloads, nested types, arrays and type variables are
  considered. If a name/signature cannot be resolved uniquely, omit its docs.
- If matched source comments exist, parameter names may be recovered from the
  exact source declaration. Otherwise class files without parameter metadata keep
  javac's arg0/arg1 names. No runtime reflection is used to recover names.
- Same-named top-level types housed in a differently named .java file may lack
  optional docs; the sidecar does not scan every SDK file or invent aliases. Complex
  inherited lexical type references that cannot be matched conservatively may also
  lack optional docs. Duplicate source declarations do not get guessed matches.
- Directory symlinks cannot escape the documentation root. Archives are read in
  place and never extracted. Traversal-looking zip entry names are ignored, and each
  decompressed source is bounded to 16 MiB. Malformed matched source syntax fails
  with a diagnostic instead of publishing potentially misleading partial output.

Official Android reference pages are supplied for android.* and java.* types:

~~~text
https://developer.android.com/reference/<package/slashes>/<Outer.Inner>
~~~

Other namespaces have an empty documentationUrl. These URLs identify the official
reference page; the page's API level/version is not fetched or verified.

JDK 17 analyzes target source at Java 8 language level. Java 9+ source syntax is
unsupported in --source. Readable class-file input can contain newer declarations
only when their dependencies exist in the selected Android runtime; missing newer
platform APIs still fail. Neither successful extraction nor Java-side regression
validation proves Unity/IL2CPP integration or Android device behavior.

## Regression validation

Run with PowerShell 7 and an installed Android SDK platform:

~~~powershell
& Tools/AndroidJavaGenerator/Java/Validate-JavaApiExtractor.ps1 `
    -JdkRoot 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK' `
    -AndroidJar 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platforms/android-35/android.jar'
~~~

The script compiles the sidecar with -Xlint:all -Werror, validates both cached and
source-launch execution, and exercises source/class-directory/jar metadata,
Android-only symbol lookup, nested constructors, generic/covariant erasure,
interface behavior, constants including invalid XML Strings, source/zip docs,
parse-only SDK implementation handling, disabled poison annotation processors,
determinism, XML parsing and nonzero/no-stdout failure paths. Target static
initializers deliberately throw; they must never execute. Fresh fixtures remain
under the adjacent ignored .work/ directory. No Unity project build is involved.

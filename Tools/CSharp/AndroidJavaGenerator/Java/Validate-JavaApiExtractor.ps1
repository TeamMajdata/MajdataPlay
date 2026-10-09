# Runs isolated Java-sidecar regression tests. No Unity assets or project files are touched.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$JdkRoot,

    [Parameter(Mandatory = $true)]
    [string]$AndroidJar
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$jdk = (Resolve-Path -LiteralPath $JdkRoot).Path
$android = (Resolve-Path -LiteralPath $AndroidJar).Path
$java = Join-Path $jdk 'bin/java.exe'
$javac = Join-Path $jdk 'bin/javac.exe'
$jar = Join-Path $jdk 'bin/jar.exe'
if (-not $IsWindows) {
    $java = Join-Path $jdk 'bin/java'
    $javac = Join-Path $jdk 'bin/javac'
    $jar = Join-Path $jdk 'bin/jar'
}
foreach ($tool in @($java, $javac, $jar)) {
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        throw "Required JDK tool does not exist: $tool"
    }
}

# Use a fresh, ignored directory for each run instead of deleting existing data.
$work = Join-Path $PSScriptRoot ('.work/regression-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'sources'
$classes = Join-Path $work 'classes'
$extractorClasses = Join-Path $work 'extractor'
$extractor = Join-Path $PSScriptRoot 'JavaApiExtractor.java'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$script:assertions = 0

# Capture both streams concurrently. Tool output is data, never PowerShell code.
function Invoke-Tool {
    param([string]$Executable, [string[]]$Arguments)
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $PSScriptRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    foreach ($argument in $Arguments) {
        $start.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) {
            throw "Could not start $Executable"
        }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout.GetAwaiter().GetResult()
            Stderr = $stderr.GetAwaiter().GetResult()
        }
    } finally {
        $process.Dispose()
    }
}

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
    $script:assertions++
}

function Assert-Success {
    param($Result, [string]$Operation)
    Assert-Condition ($Result.ExitCode -eq 0) "$Operation failed: $($Result.Stderr)"
    Assert-Condition ([string]::IsNullOrEmpty($Result.Stderr)) "$Operation wrote unexpected stderr: $($Result.Stderr)"
}

function Write-Fixture {
    param([string]$RelativePath, [string]$Content)
    $destination = Join-Path $work $RelativePath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    [System.IO.File]::WriteAllText($destination, $Content, $utf8)
    return $destination
}

function Invoke-Extractor {
    param([string[]]$Arguments)
    return Invoke-Tool $java (@('-Dfile.encoding=UTF-8', '-Duser.language=en', '-cp', $extractorClasses,
            'JavaApiExtractor', '--android-jar', $android) + $Arguments)
}

function Assert-Failure {
    param([string]$Operation, [string[]]$Arguments, [string]$Diagnostic)
    $result = Invoke-Extractor $Arguments
    Assert-Condition ($result.ExitCode -ne 0) "$Operation must fail"
    Assert-Condition ([string]::IsNullOrEmpty($result.Stdout)) "$Operation emitted partial XML"
    Assert-Condition ($result.Stderr.Contains($Diagnostic)) "$Operation diagnostic: $($result.Stderr)"
}

$fixture = Write-Fixture 'sources/fixture/Sample.java' @'
package fixture;
/** Sample <b>API</b> with {@code a < b}, {@link java.lang.String label}, &amp; text. */
public class Sample extends Base<String> implements Feature {
    static { if (true) { throw new AssertionError("TARGET EXECUTED"); } }
    /** Count constant. */ public static final int COUNT = 123;
    public static final boolean BOOL = true;
    public static final byte BYTE = -8;
    public static final short SHORT = 99;
    public static final char CHAR = '\ud800';
    public static final long LONG = 9223372036854775807L;
    public static final float NAN = 0.0f / 0.0f;
    public static final float INF = 1.0f / 0.0f;
    public static final double NEG_INF = -1.0 / 0.0;
    public static final double NEG_ZERO = -0.0;
    public static final String TEXT = "x\n\r\t<&\"'\ud83d\ude00";
    public static final String NUL = "a\000b";
    public static final String SURROGATE = "\ud800x\udfff";
    public static final String DYNAMIC = new String("not a constant");
    public static final Integer BOXED = 12;
    public final int INSTANCE_CONSTANT = 7;
    public int hidden;
    public int deprecatedByDoc;
    /** Creates this API.
     * @param value the <em>value</em> &amp; input
     */
    public Sample(int value) {}
    private Sample() {}
    /** Selects a result.
     * @param value user value
     * @return selected {@code text}
     * @throws IllegalArgumentException when {@literal value < 0}
     */
    @Deprecated public String compute(int value) throws IllegalArgumentException { return ""; }
    /** Returns a value. */
    @Override public String item() { return ""; }
    /** Uses arrays. */
    public Sample[][] arrays(int[][] values, Sample.Inner[] nested) { return null; }
    /** Uses a bounded type.
     * @param values generic values
     * @return generic result
     */
    public <T extends Number & Comparable<T>> T[][] generic(T[][] values) { return values; }
    /** Doc-only unchecked exception.
     * @throws IllegalStateException on a bad state
     */
    public void unchecked() {}
    /** Nested static API. */
    public static class StaticInner { public StaticInner(int value) {} }
    /** Inner documentation. */
    public class Inner {
        /** Inner constructor.
         * @param value inner value
         */
        public Inner(int value) {}
        public class Deep { public Deep(long number) {} }
    }
    public abstract static class AbstractInner { public AbstractInner() {} }
    public enum Choice { YES, NO }
    private class PrivateInner {}
}
class Base<T> {
    public int hidden;
    public int inherited;
    public Base() {}
    public T item() { return null; }
    /** Inherited erased signature.
     * @param values base values
     */
    public T[][] baseGeneric(T[][] values) { return values; }
    public static int inheritedStatic() { return 1; }
}
interface Feature {
    int FEATURE_VALUE = 9;
    default int defaultCall() { return 1; }
    static int interfaceOnly() { return 1; }
}
'@

# Include legal '$' identifiers, default constructors and covariant interface diamonds.
$more = Write-Fixture 'sources/fixture/More.java' @'
package fixture;
class More {
    public static class DefaultConstructor {}
    public static class A$B {
        /** Literal dollar name. */ public A$B(int value) {}
    }
    public interface First { Number result(); }
    public interface Second { Integer result(); }
    public interface Diamond extends First, Second {}
}
'@

[System.IO.Directory]::CreateDirectory($classes) | Out-Null
[System.IO.Directory]::CreateDirectory($extractorClasses) | Out-Null
$compilation = Invoke-Tool $javac @('-J-Dfile.encoding=UTF-8', '-J-Duser.language=en', '-Xlint:all', '-Werror',
        '-encoding', 'UTF-8', '-d', $extractorClasses, $extractor)
Assert-Success $compilation 'Sidecar strict JDK 17 compilation'
$compileFlags = @('-J-Dfile.encoding=UTF-8', '-J-Duser.language=en', '-proc:none', '-source', '8', '-target', '8',
    '-bootclasspath', $android, '-encoding', 'UTF-8', '-g:none', '-Xlint:none')
Assert-Success (Invoke-Tool $javac ($compileFlags + @('-d', $classes, $fixture, $more))) 'Fixture compilation'

$requested = @('--type', 'fixture.Sample', '--type', 'fixture.Sample$Inner', '--type', 'fixture.Sample$Inner$Deep',
    '--type', 'fixture.Sample$StaticInner', '--type', 'fixture.Sample$AbstractInner', '--type', 'fixture.Sample$Choice',
    '--type', 'fixture.Feature', '--type', 'fixture.More$DefaultConstructor', '--type', 'fixture.More$A$B',
    '--type', 'fixture.More$Diamond', '--include-inherited', 'true')
$fromSource = Invoke-Extractor (@('--source', $source) + $requested)
Assert-Success $fromSource 'Source extraction (static initializer must never execute)'
[xml]$sourceXml = $fromSource.Stdout
$fromClass = Invoke-Extractor (@('--classpath', $classes, '--documentation', $source) + $requested)
Assert-Success $fromClass 'Class-directory extraction with source documentation'
[xml]$classXml = $fromClass.Stdout
$targetJar = Join-Path $work 'targets.jar'
Assert-Success (Invoke-Tool $jar @('--create', '--file', $targetJar, '-C', $classes, '.')) 'Target jar creation'
$fromJar = Invoke-Extractor (@('--classpath', $targetJar, '--documentation', $source) + $requested)
Assert-Success $fromJar 'Jar extraction'
Assert-Condition ($fromClass.Stdout -ceq $fromJar.Stdout) 'Jar and class-directory XML must be identical'
$repeated = Invoke-Extractor (@('--classpath', $targetJar, '--documentation', $source) + $requested)
Assert-Condition ($repeated.Stdout -ceq $fromJar.Stdout) 'Repeated extraction must be byte-stable'

# Verify actual source-launch operation, independently of the cached class invocation above.
$launched = Invoke-Tool $java @('-Dfile.encoding=UTF-8', $extractor, '--android-jar', $android,
        '--classpath', $targetJar, '--type', 'fixture.Sample$Inner')
Assert-Success $launched 'JDK 17 source launcher'
[xml]$launchedXml = $launched.Stdout
Assert-Condition ($launchedXml.'java-api'.type.method.descriptor -ceq '(Lfixture/Sample;I)V') 'Source-launched inner descriptor'

function Select-Type {
    param([xml]$Xml, [string]$Name)
    return $Xml.SelectSingleNode('/java-api/type[@name="' + $Name + '"]')
}

foreach ($document in @($sourceXml, $classXml)) {
    Assert-Condition ($document.DocumentElement.Name -ceq 'java-api') 'Protocol root'
    Assert-Condition ($document.DocumentElement.GetAttribute('version') -ceq '1') 'Protocol version'
    $api = Select-Type $document 'fixture.Sample'
    Assert-Condition ($api.GetAttribute('summary') -ceq 'Sample API with a < b, label, & text.') 'Javadoc plain text'
    $compute = $api.SelectSingleNode('method[@name="compute"]')
    Assert-Condition ($compute.GetAttribute('descriptor') -ceq '(I)Ljava/lang/String;') 'Method descriptor'
    Assert-Condition ($compute.GetAttribute('deprecated') -ceq 'true') 'Deprecation flag'
    Assert-Condition ($compute.GetAttribute('returns') -ceq 'selected text') 'Return documentation'
    Assert-Condition ($compute.parameter.GetAttribute('name') -ceq 'value') 'Source parameter name recovery'
    Assert-Condition ($compute.parameter.GetAttribute('summary') -ceq 'user value') 'Parameter documentation'
    Assert-Condition ($compute.exception.GetAttribute('summary') -ceq 'when value < 0') 'Exception documentation'
    Assert-Condition ($compute.exception.GetAttribute('type') -ceq 'java.lang.IllegalArgumentException') 'Resolved exception name'
    $constructor = $api.SelectNodes('method[@name="<init>"]')
    Assert-Condition ($constructor.Count -eq 1) 'Only declared public constructors'
    Assert-Condition ($constructor[0].GetAttribute('descriptor') -ceq '(I)V') 'Public constructor descriptor'
    Assert-Condition ($constructor[0].parameter.GetAttribute('summary') -ceq 'the value & input') 'Constructor documentation'
    Assert-Condition ($api.SelectNodes('method[@name="item"]').Count -eq 1) 'Overridden and bridge methods excluded'
    Assert-Condition ($api.SelectSingleNode('method[@name="item"]').GetAttribute('descriptor') -ceq '()Ljava/lang/String;') 'Covariant return descriptor'
    Assert-Condition ($api.SelectSingleNode('method[@name="baseGeneric"]').GetAttribute('descriptor') -ceq '([[Ljava/lang/Object;)[[Ljava/lang/Object;') 'Inherited generic declaration erasure'
    Assert-Condition ($api.SelectSingleNode('method[@name="generic"]').GetAttribute('descriptor') -ceq '([[Ljava/lang/Number;)[[Ljava/lang/Number;') 'Intersection-bound erasure'
    Assert-Condition ($api.SelectSingleNode('method[@name="arrays"]').GetAttribute('descriptor') -ceq '([[I[Lfixture/Sample$Inner;)[[Lfixture/Sample;') 'Jagged and nested array descriptors'
    Assert-Condition ($api.SelectSingleNode('method[@name="unchecked"]').exception.GetAttribute('type') -ceq 'java.lang.IllegalStateException') 'Documented unchecked exceptions'
    Assert-Condition ($api.SelectNodes('field[@name="hidden"]').Count -eq 1) 'Hidden fields excluded'
    Assert-Condition ($api.SelectSingleNode('field[@name="inherited"]').GetAttribute('declaringType') -ceq 'fixture.Base') 'Inherited declaring type'
    Assert-Condition ($api.SelectNodes('method[@name="defaultCall"]').Count -eq 1) 'Inherited interface default method'
    Assert-Condition ($api.SelectNodes('method[@name="interfaceOnly"]').Count -eq 0) 'Interface static method not inherited by class'
    Assert-Condition ($api.SelectNodes('nestedType[@name="fixture.Sample$PrivateInner"]').Count -eq 0) 'Private nested type excluded'
    Assert-Condition ($api.SelectNodes('nestedType[@name="fixture.Sample$Inner"]').Count -eq 1) 'Public nested type reported'
    foreach ($excluded in @('BOXED', 'DYNAMIC', 'INSTANCE_CONSTANT')) {
        Assert-Condition (-not $api.SelectSingleNode('field[@name="' + $excluded + '"]').HasAttribute('constantKind')) "$excluded is not a scalar static constant"
    }
    foreach ($entry in @{
        BOOL = @('boolean', 'true'); BYTE = @('byte', '-8'); SHORT = @('short', '99'); CHAR = @('char', '55296');
        COUNT = @('int', '123'); LONG = @('long', '9223372036854775807'); NAN = @('float', 'NaN');
        INF = @('float', 'Infinity'); NEG_INF = @('double', '-Infinity'); NEG_ZERO = @('double', '-0.0')
    }.GetEnumerator()) {
        $field = $api.SelectSingleNode('field[@name="' + $entry.Key + '"]')
        Assert-Condition ($field.GetAttribute('constantKind') -ceq $entry.Value[0]) "$($entry.Key) constant kind"
        Assert-Condition ($field.GetAttribute('constantValue') -ceq $entry.Value[1]) "$($entry.Key) exact constant value"
    }
    $text = $api.SelectSingleNode('field[@name="TEXT"]')
    # Construct exact control characters without shell escape interpretation.
    $expectedText = 'x' + [char]10 + [char]13 + [char]9 + '<&"' + "'" + [char]0xd83d + [char]0xde00
    Assert-Condition ($text.GetAttribute('constantValue') -ceq $expectedText) 'XML attribute whitespace and supplementary characters round trip'
    Assert-Condition (-not $text.HasAttribute('constantEncoding')) 'XML-safe strings stay unencoded'
    foreach ($encoded in @('NUL', 'SURROGATE')) {
        $field = $api.SelectSingleNode('field[@name="' + $encoded + '"]')
        Assert-Condition ($field.GetAttribute('constantEncoding') -ceq 'base64-utf16be') "$encoded encoding marker"
        $bytes = [Convert]::FromBase64String($field.GetAttribute('constantValue'))
        $expectedBytes = if ($encoded -ceq 'NUL') { [byte[]]@(0, 97, 0, 0, 0, 98) } else { [byte[]]@(0xd8, 0, 0, 120, 0xdf, 0xff) }
        Assert-Condition ([Convert]::ToHexString($bytes) -ceq [Convert]::ToHexString($expectedBytes)) "$encoded raw UTF-16 code units preserved"
    }
    $inner = Select-Type $document 'fixture.Sample$Inner'
    $innerConstructor = $inner.SelectSingleNode('method[@name="<init>"]')
    Assert-Condition ($innerConstructor.GetAttribute('descriptor') -ceq '(Lfixture/Sample;I)V') 'Nonstatic member constructor outer argument'
    Assert-Condition ($innerConstructor.parameter[0].GetAttribute('name') -ceq 'enclosingInstance') 'Generated enclosing-instance parameter'
    Assert-Condition ($innerConstructor.parameter[1].GetAttribute('summary') -ceq 'inner value') 'Outer argument does not shift documentation'
    $deep = Select-Type $document 'fixture.Sample$Inner$Deep'
    Assert-Condition ($deep.SelectSingleNode('method[@name="<init>"]').GetAttribute('descriptor') -ceq '(Lfixture/Sample$Inner;J)V') 'Deeply nested outer binary descriptor'
    $static = Select-Type $document 'fixture.Sample$StaticInner'
    Assert-Condition ($static.SelectSingleNode('method[@name="<init>"]').GetAttribute('descriptor') -ceq '(I)V') 'Static nested constructor has no outer argument'
    foreach ($name in @('fixture.Sample$AbstractInner', 'fixture.Sample$Choice', 'fixture.Feature')) {
        $type = Select-Type $document $name
        Assert-Condition ($type.SelectNodes('method[@name="<init>"]').Count -eq 0) "$name cannot be instantiated through public constructors"
    }
    $default = Select-Type $document 'fixture.More$DefaultConstructor'
    Assert-Condition ($default.SelectSingleNode('method[@name="<init>"]').GetAttribute('descriptor') -ceq '()V') 'Public implicit constructor included'
    $dollar = Select-Type $document 'fixture.More$A$B'
    Assert-Condition ($dollar.SelectSingleNode('method[@name="<init>"]').GetAttribute('summary') -ceq 'Literal dollar name.') 'Literal dollar identifiers resolve and match docs'
    $diamond = Select-Type $document 'fixture.More$Diamond'
    $diamondReturns = @($diamond.SelectNodes('method[@name="result"]') | ForEach-Object { $_.GetAttribute('descriptor') })
    Assert-Condition ($diamondReturns -contains '()Ljava/lang/Integer;') 'Covariant interface return retained'
}

$declared = Invoke-Extractor @('--source', $source, '--type', 'fixture.Sample', '--include-inherited', 'false')
Assert-Success $declared 'Declared-only extraction'
[xml]$declaredXml = $declared.Stdout
Assert-Condition ($declaredXml.SelectNodes('/java-api/type/field[@name="inherited"]').Count -eq 0) 'Inherited fields disabled'
Assert-Condition ($declaredXml.SelectNodes('/java-api/type/method[@name="defaultCall"]').Count -eq 0) 'Inherited methods disabled'
Assert-Condition ($declaredXml.SelectNodes('/java-api/type/method[@name="<init>"]').Count -eq 1) 'Declared constructor retained'


# SDK documentation must be parse-only: unresolved imports/implementation bodies are harmless.
$docsFile = Write-Fixture 'documentation/fixture/Sample.java' @'
package fixture;
import unavailable.sdk.Hidden;
/** External <p>SDK documentation.</p> */
public class Sample extends UnavailableImplementation {
    /** Exact overload.
     * @param documentedValue the external argument
     * @return external result
     * @throws IllegalArgumentException external failure
     */
    public String compute(int documentedValue) { return new Hidden().missingImplementation(); }
    /** Wrong overload, must not attach. */
    public String compute(long otherValue) { return new Hidden().anotherMissingImplementation(); }
    /** Wrong return descriptor, must not attach. */
    public long arrays(int[][] values, Sample.Inner[] nested) { return 0; }
    /** Unresolvable parameter, must not attach. */
    public void generic(Hidden[][] values) {}
    /** Inner external docs. */
    public class Inner {
        /** External inner constructor.
         * @param renamedValue external inner value
         */
        public Inner(int renamedValue) { Hidden.call(); }
    }
}
'@
$docRoot = Join-Path $work 'documentation'
$external = Invoke-Extractor @('--classpath', $targetJar, '--documentation', $docRoot,
        '--type', 'fixture.Sample', '--type', 'fixture.Sample$Inner')
Assert-Success $external 'Parse-only SDK documentation'
[xml]$externalXml = $external.Stdout
$externalApi = Select-Type $externalXml 'fixture.Sample'
Assert-Condition ($externalApi.GetAttribute('summary') -ceq 'External SDK documentation.') 'External type documentation'
$externalMethod = $externalApi.SelectSingleNode('method[@name="compute"]')
Assert-Condition ($externalMethod.GetAttribute('summary') -ceq 'Exact overload.') 'Only descriptor-matched overload receives docs'
Assert-Condition ($externalMethod.parameter.GetAttribute('name') -ceq 'documentedValue') 'External parameter names recovered'
Assert-Condition ($externalMethod.parameter.GetAttribute('summary') -ceq 'the external argument') 'External parameter docs'
Assert-Condition ($externalApi.SelectSingleNode('method[@name="arrays"]').GetAttribute('summary') -ceq '') 'Mismatched return docs omitted'
Assert-Condition ($externalApi.SelectSingleNode('method[@name="generic"]').GetAttribute('summary') -ceq '') 'Unresolved type docs omitted'
$externalInner = Select-Type $externalXml 'fixture.Sample$Inner'
Assert-Condition ($externalInner.SelectSingleNode('method[@name="<init>"]').parameter[1].GetAttribute('name') -ceq 'renamedValue') 'SDK docs respect implicit outer parameter'

# Multiple documentation roots must not fail or let stripped SDK-like sources hide real prose.
$emptyDoc = Write-Fixture 'empty-documentation/fixture/Sample.java' 'package fixture; public class Sample { public String compute(int noDocName) { return null; } }'
$emptyDocRoot = Join-Path $work 'empty-documentation'
$multipleDocs = Invoke-Extractor @('--classpath', $targetJar, '--documentation', $emptyDocRoot,
        '--documentation', $docRoot, '--type', 'fixture.Sample', '--type', 'fixture.Sample$Inner')
Assert-Success $multipleDocs 'Repeatable documentation roots'
Assert-Condition ($multipleDocs.Stdout -ceq $external.Stdout) 'Stripped first documentation input does not shadow exact prose in the second'

# Zip documents may have a single SDK/module prefix; ambiguous duplicates are ignored.
$zipFile = Write-Fixture 'zip-content/sdk-source/fixture/Sample.java' ([System.IO.File]::ReadAllText($docsFile))
$zipRoot = Join-Path $work 'zip-content'
$docZip = Join-Path $work 'documentation.zip'
Assert-Success (Invoke-Tool $jar @('--create', '--file', $docZip, '-C', $zipRoot, '.')) 'Source zip creation'
$zipDocs = Invoke-Extractor @('--classpath', $targetJar, '--documentation', $docZip,
        '--type', 'fixture.Sample', '--type', 'fixture.Sample$Inner')
Assert-Success $zipDocs 'Source zip documentation'
Assert-Condition ($zipDocs.Stdout -ceq $external.Stdout) 'Directory and uniquely prefixed zip documentation agree'
$duplicateOne = Write-Fixture 'ambiguous-zip/one/fixture/Sample.java' ([System.IO.File]::ReadAllText($docsFile))
$duplicateTwo = Write-Fixture 'ambiguous-zip/two/fixture/Sample.java' ([System.IO.File]::ReadAllText($docsFile))
$ambiguousZip = Join-Path $work 'ambiguous.zip'
Assert-Success (Invoke-Tool $jar @('--create', '--file', $ambiguousZip, '-C', (Join-Path $work 'ambiguous-zip'), '.')) 'Ambiguous source zip creation'
$ambiguous = Invoke-Extractor @('--classpath', $targetJar, '--documentation', $ambiguousZip, '--type', 'fixture.Sample')
Assert-Success $ambiguous 'Ambiguous documentation omission'
[xml]$ambiguousXml = $ambiguous.Stdout
Assert-Condition ($ambiguousXml.'java-api'.type.GetAttribute('summary') -ceq '') 'Ambiguous source files never invent documentation'

# XML-invalid documentation is sanitized; String constants use exact base64 instead.
$invalidDoc = '/** Document ' + [char]0 + ' &amp; text. */' + [Environment]::NewLine + 'public class InvalidDoc {}'
$invalidDocFile = Write-Fixture 'invalid-doc/InvalidDoc.java' $invalidDoc
$sanitized = Invoke-Extractor @('--source', $invalidDocFile, '--type', 'InvalidDoc')
Assert-Success $sanitized 'XML-invalid documentation sanitization'
[xml]$sanitizedXml = $sanitized.Stdout
Assert-Condition ($sanitizedXml.'java-api'.type.GetAttribute('summary').Contains([char]0xfffd)) 'Invalid documentation code point replaced'

# The extractor must not run annotation processors discovered from the target classpath.
$processor = Write-Fixture 'processor-sources/poison/Processor.java' @'
package poison;
public class Processor extends javax.annotation.processing.AbstractProcessor {
    static { if (true) { throw new AssertionError("PROCESSOR EXECUTED"); } }
    public boolean process(java.util.Set<? extends javax.lang.model.element.TypeElement> annotations,
            javax.annotation.processing.RoundEnvironment environment) { return false; }
}
'@
$processorClasses = Join-Path $work 'processor-classes'
[System.IO.Directory]::CreateDirectory($processorClasses) | Out-Null
Assert-Success (Invoke-Tool $javac @('-J-Dfile.encoding=UTF-8', '-proc:none', '-d', $processorClasses, $processor)) 'Poison processor compilation'
$service = Write-Fixture 'processor-classes/META-INF/services/javax.annotation.processing.Processor' ('poison.Processor' + [char]10)
$processorJar = Join-Path $work 'poison-processor.jar'
Assert-Success (Invoke-Tool $jar @('--create', '--file', $processorJar, '-C', $processorClasses, '.')) 'Poison processor jar creation'
$noProcessor = Invoke-Extractor @('--classpath', $processorJar, '--source', $source, '--type', 'fixture.Sample')
Assert-Success $noProcessor 'Annotation processors disabled'

# Dummy-source lookup must use the Android runtime, not desktop JDK APIs.
$androidLookup = Invoke-Extractor @('--type', 'android.os.Build$VERSION_CODES', '--type', 'java.lang.String', '--type', 'java.util.Map$Entry')
Assert-Success $androidLookup 'Android bootclasspath-only symbol lookup'
[xml]$androidXml = $androidLookup.Stdout
$stringApi = Select-Type $androidXml 'java.lang.String'
Assert-Condition ($stringApi.GetAttribute('documentationUrl') -ceq 'https://developer.android.com/reference/java/lang/String') 'Official java.* Android reference URL'
$versionApi = Select-Type $androidXml 'android.os.Build$VERSION_CODES'
Assert-Condition ($versionApi.GetAttribute('documentationUrl') -ceq 'https://developer.android.com/reference/android/os/Build.VERSION_CODES') 'Official nested Android reference URL'
$entryApi = Select-Type $androidXml 'java.util.Map$Entry'
Assert-Condition ($entryApi.GetAttribute('documentationUrl') -ceq 'https://developer.android.com/reference/java/util/Map.Entry') 'Official nested java.* reference URL'
Assert-Failure 'Desktop-only class rejected' @('--type', 'java.net.http.HttpClient') 'Cannot resolve'

# Source errors and missing binary dependencies must fail without any stdout XML.
$missingSource = Write-Fixture 'invalid/MissingSource.java' 'public class MissingSource { public notpresent.Dependency value; }'
Assert-Failure 'Missing source dependency' @('--source', $missingSource, '--type', 'MissingSource') 'Java source analysis failed'
$malformed = Write-Fixture 'invalid/Malformed.java' 'public class Malformed { public void broken( }'
Assert-Failure 'Invalid Java syntax' @('--source', $malformed, '--type', 'Malformed') 'Java source analysis failed'
$missingDependency = Write-Fixture 'dependency-sources/missing/Dependency.java' 'package missing; public class Dependency {}'
$missingApi = Write-Fixture 'dependency-sources/broken/MissingApi.java' 'package broken; public class MissingApi { public missing.Dependency value; }'
$missingBase = Write-Fixture 'dependency-sources/broken/MissingBase.java' 'package broken; public class MissingBase extends missing.Dependency {}'
$missingGeneric = Write-Fixture 'dependency-sources/broken/GenericBound.java' 'package broken; public class GenericBound<T extends missing.Dependency> { public T value; }'
$missingGenericArgument = Write-Fixture 'dependency-sources/broken/GenericArgument.java' 'package broken; public class GenericArgument { public java.util.List<missing.Dependency> value; }'
$missingClasses = Join-Path $work 'missing-classes'
[System.IO.Directory]::CreateDirectory($missingClasses) | Out-Null
Assert-Success (Invoke-Tool $javac ($compileFlags + @('-d', $missingClasses, $missingDependency, $missingApi, $missingBase, $missingGeneric, $missingGenericArgument))) 'Missing-dependency fixture compilation'
$missingJar = Join-Path $work 'missing-dependency.jar'
Assert-Success (Invoke-Tool $jar @('--create', '--file', $missingJar, '-C', $missingClasses, 'broken')) 'Incomplete dependency jar creation'
foreach ($type in @('broken.MissingApi', 'broken.MissingBase', 'broken.GenericBound', 'broken.GenericArgument')) {
    Assert-Failure "Missing dependency in $type" @('--classpath', $missingJar, '--type', $type) 'Unresolved Java API dependency'
}
Assert-Failure 'Unresolved requested type' @('--type', 'missing.Type') 'Cannot resolve'
Assert-Failure 'Invalid binary type name' @('--type', 'invalid/name') 'Invalid Java binary'
Assert-Failure 'Wrong nested naming convention' @('--classpath', $targetJar, '--type', 'fixture.Sample.Inner') 'Cannot resolve'
Assert-Failure 'Invalid include-inherited flag' @('--type', 'java.lang.String', '--include-inherited', 'yes') "must be 'true' or 'false'"
Assert-Failure 'Unknown option' @('--type', 'java.lang.String', '--unknown', 'value') 'Unknown option'
Assert-Failure 'Missing option value' @('--type') 'Missing value'
Assert-Failure 'Duplicate singleton option' @('--type', 'java.lang.String', '--include-inherited', 'true', '--include-inherited', 'false') 'only once'
Assert-Failure 'Absent input path' @('--type', 'java.lang.String', '--source', (Join-Path $work 'absent.java')) 'does not exist'
Assert-Failure 'Class file normalization belongs to caller' @('--type', 'fixture.Sample', '--classpath', (Join-Path $classes 'fixture/Sample.class')) 'Normalize a .class'
$badArchive = Write-Fixture 'invalid.jar' 'This is not a zip.'
Assert-Failure 'Invalid classpath jar' @('--type', 'java.lang.String', '--classpath', $badArchive) 'Cannot read --classpath archive'
$missingRuntime = Invoke-Tool $java @('-cp', $extractorClasses, 'JavaApiExtractor', '--android-jar', $targetJar, '--type', 'fixture.Sample')
Assert-Condition ($missingRuntime.ExitCode -ne 0 -and $missingRuntime.Stdout.Length -eq 0) 'Invalid bootclasspath rejected with no XML'
Assert-Condition ($missingRuntime.Stderr.Contains('java/lang/Object.class')) 'Invalid bootclasspath diagnostic'
$noArguments = Invoke-Tool $java @('-cp', $extractorClasses, 'JavaApiExtractor')
Assert-Condition ($noArguments.ExitCode -ne 0 -and $noArguments.Stdout.Length -eq 0) 'Required options enforced'

Write-Output "PASS: $script:assertions assertions. JDK source launch, Android symbols, source/class/jar metadata, docs, constants and failure paths validated."
Write-Output "Validation fixtures remain ignored under $work"

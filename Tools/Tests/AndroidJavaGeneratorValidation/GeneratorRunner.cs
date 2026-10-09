#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MajdataPlay.SourceGenerators.AndroidJava;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Compiles the actual production runtime and drives the actual linked generator in memory.
    /// </summary>
    internal sealed class GeneratorRunner
    {
        /// <summary>
        /// Lists the exact production runtime sources owned by the parent implementation.
        /// </summary>
        private static readonly string[] s_runtimeFileNames =
        {
            "JavaClassAttribute.cs",
            "JavaApiConfigurationAttribute.cs",
            "JavaObject.cs",
            "AndroidJni.cs",
            "JavaInvocationException.cs"
        };

        /// <summary>
        /// Stores the isolated fixture and diagnostic workspace.
        /// </summary>
        private readonly FixtureWorkspace _workspace;

        /// <summary>
        /// Stores the metadata references shared by each small Roslyn compilation.
        /// </summary>
        private readonly ImmutableArray<MetadataReference> _references;

        /// <summary>
        /// Stores the exact runtime text, so a test never substitutes mocked JNI types.
        /// </summary>
        private readonly KeyValuePair<string, string>[] _runtimeSources;

        /// <summary>
        /// Gets the parse options compatible with the requested Roslyn 4.3.1 package.
        /// </summary>
        public CSharpParseOptions ParseOptions { get; }

        /// <summary>
        /// Creates a runner using real Unity DLLs and the host's trusted BCL assemblies.
        /// </summary>
        /// <param name="workspace">The already prepared isolated fixture workspace.</param>
        public GeneratorRunner(FixtureWorkspace workspace)
        {
            _workspace = workspace;
            ParseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose);
            var runtimeDirectory = Path.Combine(workspace.Options.RepositoryRoot, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            _runtimeSources = s_runtimeFileNames.Select(name =>
            {
                var path = Path.Combine(runtimeDirectory, name);
                return new KeyValuePair<string, string>(path, File.ReadAllText(path));
            }).ToArray();
            var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? throw new InvalidOperationException("The host did not expose trusted BCL assemblies.");
            var paths = new HashSet<string>(trustedAssemblies.Split(Path.PathSeparator), StringComparer.OrdinalIgnoreCase);
            // The harness assembly also contains the linked production runtime. Referencing it
            // would hide unresolved runtime dependencies and create duplicate type definitions.
            paths.Remove(typeof(GeneratorRunner).Assembly.Location);
            paths.RemoveWhere(path => Path.GetFileName(path).StartsWith("UnityEngine.", StringComparison.OrdinalIgnoreCase));
            paths.Add(Path.Combine(workspace.Options.UnityEditorData, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll"));
            paths.Add(Path.Combine(workspace.Options.UnityEditorData, "Managed", "UnityEngine", "UnityEngine.AndroidJNIModule.dll"));
            _references = paths.OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();
        }

        /// <summary>
        /// Creates a compilation containing the actual runtime sources and a wrapper request.
        /// </summary>
        /// <param name="source">The wrapper and optional assembly-configuration source.</param>
        /// <param name="caseName">The simple test identifier used in artifacts and diagnostics.</param>
        /// <param name="androidPlayer">Whether to additionally compile the UNITY_ANDROID runtime branch.</param>
        /// <returns>The small in-memory input compilation.</returns>
        public CSharpCompilation CreateCompilation(string source, string caseName, bool androidPlayer = false)
        {
            var parseOptions = androidPlayer ? ParseOptions.WithPreprocessorSymbols("UNITY_ANDROID") : ParseOptions;
            var trees = _runtimeSources.Select(item => CSharpSyntaxTree.ParseText(item.Value, parseOptions, item.Key)).ToList();
            var inputPath = Path.Combine(_workspace.CaseDirectory(caseName), "Input.cs");
            File.WriteAllText(inputPath, source, new UTF8Encoding(false));
            trees.Add(CSharpSyntaxTree.ParseText(source, parseOptions, inputPath));
            return CSharpCompilation.Create("AndroidJavaValidation_" + caseName.Replace('-', '_'), trees, _references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable,
                    deterministic: true,
                    concurrentBuild: false));
        }

        /// <summary>
        /// Executes the real generator and saves its generated sources and compiler diagnostics.
        /// </summary>
        /// <param name="compilation">The input containing the real runtime sources.</param>
        /// <param name="caseName">The simple artifact directory name.</param>
        /// <param name="driver">A previous driver for deterministic reruns, or null for a fresh driver.</param>
        /// <returns>The updated driver, compilation, source snapshots, and generator diagnostics.</returns>
        public GeneratorRun Run(CSharpCompilation compilation, string caseName, GeneratorDriver? driver = null)
        {
            if (driver is null)
            {
                driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new AndroidJavaGenerator() },
                    parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.Last().Options);
            }
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            var result = new GeneratorRun(compilation, (CSharpCompilation)output, driver, diagnostics);
            var directory = _workspace.CaseDirectory(caseName);
            File.WriteAllText(Path.Combine(directory, "Diagnostics.txt"), result.DescribeDiagnostics(), new UTF8Encoding(false));
            var index = 0;
            foreach (var generated in result.GeneratedSources)
            {
                // Hint names are data supplied by the generator, not safe filesystem paths.
                var name = "Generated-" + (++index).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ".cs";
                File.WriteAllText(Path.Combine(directory, name), generated.Value, new UTF8Encoding(false));
            }
            return result;
        }

        /// <summary>
        /// Builds a standard root API request for source, class-file, or jar fixture inputs.
        /// </summary>
        /// <param name="sources">The fixture inputs supplied via JavaClass.Sources.</param>
        /// <param name="documentationPaths">The source documentation files or roots.</param>
        /// <param name="includeInherited">Whether the widget includes inherited members.</param>
        /// <param name="assemblyConfiguration">Whether to place shared inputs on the assembly attribute.</param>
        /// <param name="explicitToolchain">Whether to pin SDK, JDK, and API on the attributes.</param>
        /// <returns>The wrapper declarations for the same five root Java APIs.</returns>
        public string RootDeclarations(string[] sources, string[] documentationPaths, bool includeInherited = true,
            bool assemblyConfiguration = false, bool explicitToolchain = false)
        {
            var shared = "Sources = " + StringArray(sources)
                + ", ClassPath = " + StringArray(new[] { _workspace.Options.AndroidJar })
                + ", DocumentationPaths = " + StringArray(documentationPaths);
            if (explicitToolchain)
            {
                shared += ", AndroidSdkPath = " + Literal(_workspace.Options.AndroidSdk)
                    + ", JavaHome = " + Literal(_workspace.Options.JavaHome)
                    + ", ApiLevel = " + _workspace.Options.ApiLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                shared += ", ApiLevel = " + _workspace.Options.ApiLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            var builder = new StringBuilder("#nullable enable\r\nusing MajdataPlay.Platform.Android;\r\n");
            if (assemblyConfiguration)
            {
                builder.Append("[assembly: JavaApiConfiguration(").Append(shared).Append(")]\r\n");
            }
            builder.Append("namespace Fixtures.Wrappers\r\n{\r\n");
            var names = new[]
            {
                new KeyValuePair<string, string>("fixtures.Widget", "WidgetWrapper"),
                new KeyValuePair<string, string>("fixtures.Contract", "ContractWrapper"),
                new KeyValuePair<string, string>("fixtures.Mode", "ModeWrapper"),
                new KeyValuePair<string, string>("fixtures.Widget$Nested", "NestedWrapper"),
                new KeyValuePair<string, string>("fixtures.Widget$Inner", "InnerWrapper")
            };
            foreach (var name in names)
            {
                builder.Append("    [JavaClass(").Append(Literal(name.Key));
                if (!assemblyConfiguration)
                {
                    builder.Append(", ").Append(shared);
                }
                builder.Append(", IncludeInheritedMembers = ").Append(includeInherited ? "true" : "false").Append(")]\r\n");
                builder.Append("    public partial class ").Append(name.Value).Append("\r\n    {\r\n    }\r\n");
            }
            builder.Append("}\r\n");
            return builder.ToString();
        }

        /// <summary>
        /// Escapes a path or Java binary name using Roslyn's C# string-literal formatter.
        /// </summary>
        /// <param name="value">The text to embed in attribute source.</param>
        /// <returns>A quoted C# string literal preserving the exact text.</returns>
        public static string Literal(string value)
        {
            return Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);
        }

        /// <summary>
        /// Creates an attribute-compatible C# string-array initializer.
        /// </summary>
        /// <param name="values">The exact path entries to embed.</param>
        /// <returns>A non-implicit string-array expression, including for empty arrays.</returns>
        public static string StringArray(IEnumerable<string> values)
        {
            return "new string[] { " + string.Join(", ", values.Select(Literal)) + " }";
        }
    }

    /// <summary>
    /// Holds the generated API and diagnostics for one in-memory compilation.
    /// </summary>
    internal sealed class GeneratorRun
    {
        /// <summary>
        /// Gets the original compilation without generated code.
        /// </summary>
        public CSharpCompilation Input { get; }

        /// <summary>
        /// Gets the compilation updated with the generated wrappers.
        /// </summary>
        public CSharpCompilation Output { get; }

        /// <summary>
        /// Gets the updated driver that can be reused for a deterministic rerun.
        /// </summary>
        public GeneratorDriver Driver { get; }

        /// <summary>
        /// Gets diagnostics emitted directly by the production generator.
        /// </summary>
        public ImmutableArray<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets generated hint names and source text sorted in ordinal order.
        /// </summary>
        public SortedDictionary<string, string> GeneratedSources { get; }

        /// <summary>
        /// Creates the result and checks that the driver did not swallow a generator crash.
        /// </summary>
        /// <param name="input">The original wrapper/runtime compilation.</param>
        /// <param name="output">The compilation containing generated source.</param>
        /// <param name="driver">The updated real generator driver.</param>
        /// <param name="diagnostics">The generator's diagnostics.</param>
        /// <exception cref="InvalidOperationException">A generator crashed or duplicated a hint name.</exception>
        public GeneratorRun(CSharpCompilation input, CSharpCompilation output, GeneratorDriver driver, ImmutableArray<Diagnostic> diagnostics)
        {
            Input = input;
            Output = output;
            Driver = driver;
            Diagnostics = diagnostics;
            GeneratedSources = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var result in driver.GetRunResult().Results)
            {
                if (result.Exception is not null)
                {
                    throw new InvalidOperationException("The generator threw instead of producing diagnostics.", result.Exception);
                }
                foreach (var source in result.GeneratedSources)
                {
                    GeneratedSources.Add(source.HintName, source.SourceText.ToString());
                }
            }
        }

        /// <summary>
        /// Formats all generator and compiler diagnostics for actionable failure artifacts.
        /// </summary>
        /// <returns>The complete generator/compiler diagnostic text.</returns>
        public string DescribeDiagnostics()
        {
            return "Generator diagnostics:\r\n" + string.Join("\r\n", Diagnostics)
                + "\r\nCompiler diagnostics:\r\n" + string.Join("\r\n", Output.GetDiagnostics());
        }

        /// <summary>
        /// Requires generated wrappers and successful semantic compilation with no errors.
        /// </summary>
        /// <exception cref="InvalidOperationException">Generation or output compilation fails.</exception>
        public void AssertCompiles()
        {
            Check.True(GeneratedSources.Count > 0, "The generator emitted no source.\r\n" + DescribeDiagnostics());
            var errors = Diagnostics.Concat(Output.GetDiagnostics()).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Check.True(errors.Length == 0, "Generated output has errors:\r\n" + string.Join("\r\n", errors.Select(error => error.ToString())));
        }

        /// <summary>
        /// Requires a specific actionable generator diagnostic instead of silent omission.
        /// </summary>
        /// <param name="acceptedIds">The actual generator diagnostic IDs valid for this case.</param>
        /// <exception cref="InvalidOperationException">No accepted generator diagnostic was reported.</exception>
        public void AssertDiagnostic(params string[] acceptedIds)
        {
            Check.True(Diagnostics.Any(diagnostic => acceptedIds.Contains(diagnostic.Id, StringComparer.Ordinal)
                    && diagnostic.Severity != DiagnosticSeverity.Info && diagnostic.Severity != DiagnosticSeverity.Hidden),
                "Expected generator diagnostic " + string.Join(" or ", acceptedIds) + ".\r\n" + DescribeDiagnostics());
            Check.True(!Diagnostics.Any(diagnostic => diagnostic.Id == "AJG009" || diagnostic.Id == "CS8785"),
                "Unexpected internal failure is not an acceptable negative-test diagnostic.\r\n" + DescribeDiagnostics());
        }
    }

    /// <summary>
    /// Implements small dependency-free regression assertions with useful failure messages.
    /// </summary>
    internal static class Check
    {
        /// <summary>
        /// Gets the number of assertions executed by this isolated worker.
        /// </summary>
        public static int Count { get; private set; }

        /// <summary>
        /// Requires a Boolean invariant.
        /// </summary>
        /// <param name="condition">Whether the invariant holds.</param>
        /// <param name="message">The failure's contextual explanation.</param>
        /// <exception cref="InvalidOperationException">The invariant does not hold.</exception>
        public static void True(bool condition, string message)
        {
            Count++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Requires two values to be equal using their default comparer.
        /// </summary>
        /// <typeparam name="T">The compared value type.</typeparam>
        /// <param name="expected">The expected result.</param>
        /// <param name="actual">The observed result.</param>
        /// <param name="context">The API or behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The values are not equal.</exception>
        public static void Equal<T>(T expected, T actual, string context)
        {
            True(EqualityComparer<T>.Default.Equals(expected, actual), context + ": expected <" + expected + ">, actual <" + actual + ">.");
        }

        /// <summary>
        /// Requires a reference to exist and returns its non-null value.
        /// </summary>
        /// <typeparam name="T">The reference type.</typeparam>
        /// <param name="value">The possibly missing value.</param>
        /// <param name="context">The expected symbol or artifact.</param>
        /// <returns>The non-null reference.</returns>
        /// <exception cref="InvalidOperationException">The reference is null.</exception>
        public static T NotNull<T>(T? value, string context) where T : class
        {
            True(value is not null, "Missing " + context + ".");
            return value!;
        }

        /// <summary>
        /// Requires an operation to throw the specified exception before calling native APIs.
        /// </summary>
        /// <typeparam name="TException">The expected managed exception type.</typeparam>
        /// <param name="operation">The operation to execute once.</param>
        /// <param name="context">The behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The operation returns or throws a different exception.</exception>
        public static void Throws<TException>(Action operation, string context) where TException : Exception
        {
            Count++;
            try
            {
                operation();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(context + " threw " + exception.GetType().Name + " instead of " + typeof(TException).Name + ".", exception);
            }
            throw new InvalidOperationException(context + " did not throw " + typeof(TException).Name + ".");
        }
    }
}

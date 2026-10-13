using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks generated API declarations, runtime behavior, and output ownership.</summary>
    internal static partial class Program
    {
        /// <summary>Compiles generated code against only the intended standard and real Unity modules.</summary>
        /// <param name="settings">Reference-pack and Unity locations.</param>
        /// <param name="sources">The generated C# files.</param>
        /// <param name="android">Whether the Android Player compilation branch is enabled.</param>
        /// <param name="emit">Whether to emit a managed assembly for pure managed checks.</param>
        /// <returns>The emitted assembly bytes, or null for compilation-only checks.</returns>
        /// <exception cref="InvalidOperationException">Generated sources do not compile.</exception>
        private static byte[]? CompileGenerated(ValidationSettings settings, IReadOnlyDictionary<string, string> sources, bool android, bool emit)
        {
            var symbols = android ? new[] { "UNITY_ANDROID" } : Array.Empty<string>();
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9, Microsoft.CodeAnalysis.DocumentationMode.Diagnose, preprocessorSymbols: symbols);
            var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Value, parseOptions, source.Key));
            var references = Directory.GetFiles(settings.NetStandardReferences, "*.dll").Select(file => MetadataReference.CreateFromFile(file)).ToList();
            references.Add(MetadataReference.CreateFromFile(Path.Combine(settings.UnityData, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll")));
            references.Add(MetadataReference.CreateFromFile(Path.Combine(settings.UnityData, "Managed", "UnityEngine", "UnityEngine.AndroidJNIModule.dll")));
            var compilation = CSharpCompilation.Create("GeneratedBindings" + Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
            var diagnostics = compilation.GetDiagnostics();
            var errors = diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
            {
                throw new InvalidOperationException("Generated " + (android ? "Android" : "Editor") + " code has " + errors.Length + " errors:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Take(60).Select(item => item.ToString())));
            }

            Require(!diagnostics.Any(item => item.Id == "CS1587" || item.Id == "CS1570" || item.Id == "CS1584" || item.Id == "CS1658"), "Generated XML documentation is valid");
            s_checks++;
            if (!emit)
            {
                return null;
            }

            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            if (!result.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
            }

            return stream.ToArray();
        }

        /// <summary>Checks structural wrapper rules independently from emitter implementation details.</summary>
        /// <param name="sources">The complete generated source set.</param>
        private static void VerifyDeclarations(IReadOnlyDictionary<string, string> sources)
        {
            var trees = sources.Values.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
            var classes = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()).Where(type => type.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword))).ToArray();
            Require(classes.All(type => type.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword))), "Every public generated class is partial");
            var interfaces = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>()).ToArray();
            Require(interfaces.Length > 0 && interfaces.All(type => !type.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword))), "Java interfaces generate nonpartial C# contracts");
            Require(classes.Any(type => type.Identifier.ValueText == "JavaObject"), "java.lang.Object is named JavaObject");
            Require(classes.Any(type => type.Identifier.ValueText == "Integer"), "Boxed Integer has a typed wrapper");
            var widget = classes.Single(type => type.Identifier.ValueText == "Widget");
            Require(widget.BaseList != null && widget.BaseList.Types.Any(item => item.ToString().EndsWith(".Base", StringComparison.Ordinal)), "Nonpublic intermediate superclass falls back to the public Java ancestor");
            Require(widget.Members.OfType<PropertyDeclarationSyntax>().All(item => item.AccessorList == null || item.AccessorList.Accessors.All(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))), "Public Java fields generate getter-only properties");
            Require(!classes.Any(type => type.Identifier.ValueText.Contains("PublicInside", StringComparison.Ordinal)), "Inaccessible nested Java classes are not exported");
            Require(widget.Members.OfType<MethodDeclarationSyntax>().Any(item => item.Identifier.ValueText.Contains("Inherited", StringComparison.Ordinal)), "Public members of package-private Java bases are exposed");
            var abstractWidget = classes.Single(type => type.Identifier.ValueText == "AbstractWidget");
            Require(abstractWidget.Members.OfType<ConstructorDeclarationSyntax>().All(item => item.ParameterList.Parameters.Count > 0), "Abstract Java types receive reference-only constructors");
            Require(sources.Values.All(source => source.Contains("#nullable enable", StringComparison.Ordinal)), "Nullable analysis is enabled in all generated sources");
        }

        /// <summary>Executes safe managed helpers without constructing any Java handle.</summary>
        /// <param name="settings">The actual Unity module location.</param>
        /// <param name="assemblyBytes">A non-Android emitted wrapper assembly.</param>
        private static void VerifyManagedRuntime(ValidationSettings settings, byte[] assemblyBytes)
        {
            foreach (var module in new[] { "UnityEngine.CoreModule.dll", "UnityEngine.AndroidJNIModule.dll" })
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(settings.UnityData, "Managed", "UnityEngine", module));
            }

            var assembly = Assembly.Load(assemblyBytes);
            var runtime = assembly.GetType("Validation.Bindings.Runtime.AndroidJni", true)!;
            var map = runtime.GetMethods().Single(item => item.Name == "MapArray").MakeGenericMethod(typeof(int), typeof(int));
            var converter = new Func<int, int>(value => value + 1);
            Require(Invoke(map, new object?[] { null, converter }) == null, "MapArray preserves null");
            Require(((int[])Invoke(map, new object?[] { Array.Empty<int>(), converter })!).Length == 0, "MapArray preserves empty arrays");
            Require(((int[])Invoke(map, new object?[] { new[] { 1, 2 }, converter })!).SequenceEqual(new[] { 2, 3 }), "MapArray converts values in order");
            var failingConverter = new Func<int, int>(value => throw new InvalidOperationException("converter failed"));
            Expect<InvalidOperationException>(() => Invoke(map, new object?[] { new[] { 1 }, failingConverter }), "MapArray propagates converter errors");
            var getClass = runtime.GetMethod("GetClass")!;
            Expect<PlatformNotSupportedException>(() => Invoke(getClass, new object?[] { "java.lang.Object" }), "Non-Android JNI is rejected before any Unity native call");
            var wrap = runtime.GetMethods().Single(item => item.Name == "Wrap").MakeGenericMethod(assembly.GetType("Validation.Bindings.Java.Lang.JavaObject", true)!);
            Expect<ArgumentNullException>(() => Invoke(wrap, new object?[] { null, null }), "Wrap rejects a missing reference factory");
        }

        /// <summary>Invokes a managed helper and restores the original exception type for assertions.</summary>
        /// <param name="method">The static method under test.</param>
        /// <param name="arguments">The managed arguments.</param>
        /// <returns>The managed method result.</returns>
        private static object? Invoke(MethodInfo method, object?[] arguments)
        {
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>Checks regeneration, stale cleanup, and preservation of handwritten extensions.</summary>
        /// <param name="workspace">The owned validation directory.</param>
        /// <param name="options">The fixture generator settings.</param>
        /// <param name="sources">The generated fixture source set.</param>
        private static void VerifyOutputOwnership(string workspace, GeneratorOptions options, IReadOnlyDictionary<string, string> sources)
        {
            GeneratedOutput.Write(options, null, sources, CancellationToken.None);
            var extensionPath = Path.Combine(options.OutputDirectory, "Extensions.cs");
            File.WriteAllText(extensionPath, "// handwritten extension");
            GeneratedOutput.Write(options, null, sources, CancellationToken.None);
            Require(File.ReadAllText(extensionPath) == "// handwritten extension", "Regeneration preserves handwritten partial extensions");
            var first = sources.Keys.First();
            var generatedPath = Path.Combine(options.OutputDirectory, first);
            File.AppendAllText(generatedPath, "// manual modification");
            Expect<IOException>(() => GeneratedOutput.Write(options, null, sources, CancellationToken.None), "Regeneration refuses to replace modified generated files");
            var cleanupOptions = new GeneratorOptions { OutputDirectory = Path.Combine(workspace, "ownership"), RootNamespace = "Validation.Ownership" };
            var initial = new Dictionary<string, string> { { "Old.cs", "// generated" }, { "Keep.cs", "// keep" } };
            GeneratedOutput.Write(cleanupOptions, null, initial, CancellationToken.None);
            GeneratedOutput.Write(cleanupOptions, null, new Dictionary<string, string> { { "Keep.cs", "// keep" } }, CancellationToken.None);
            Require(!File.Exists(Path.Combine(cleanupOptions.OutputDirectory, "Old.cs")), "Only manifest-owned stale sources are removed");
            GeneratedOutput.Write(cleanupOptions, null, new Dictionary<string, string> { { "KEEP.cs", "// changed casing" } }, CancellationToken.None);
            Require(File.ReadAllText(Path.Combine(cleanupOptions.OutputDirectory, "KEEP.cs")) == "// changed casing", "Generated path casing changes preserve ownership and the new source");
            Expect<IOException>(() => GeneratedOutput.Write(cleanupOptions, null, new Dictionary<string, string> { { "../escape.cs", "// invalid" } }, CancellationToken.None), "Output traversal is rejected");
        }

        /// <summary>Checks argument validation without depending on a JVM.</summary>
        private static void VerifyCli()
        {
            Require(JavaNames.PascalCase("CONTENTS_FILE_DESCRIPTOR") == "ContentsFileDescriptor", "Java constants use readable PascalCase properties");
            Require(JavaNames.PascalCase("getURL") == "GetURL", "Existing Java camelCase keeps embedded acronyms");
            Require(CliOptions.Parse(new[] { "--help" }) == null, "Help does not require SDK discovery");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--output", ".", "--namespace", "Invalid.class" }), "C# namespace keywords are rejected");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--no-sdk", "--output", "." }), "SDK-free generation requires inputs");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--output" }), "Missing CLI values are diagnosed");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--output", ".", "--api-level", "0" }), "Invalid API levels are rejected");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--output", ".", "--jobs", "0" }), "Worker counts must be positive");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--output", ".", "--docs-jobs", "1000" }), "Documentation request concurrency is bounded");
            Expect<ArgumentException>(() => CliOptions.Parse(new[] { "--output", ".", "--offline-docs", "--refresh-docs" }), "Offline documentation cannot request network refresh");
            var threading = CliOptions.Parse(new[] { "--output", ".", "--jobs", "4", "--docs-jobs", "2", "--offline-docs", "--docs-cache", "." })!;
            Require(threading.Jobs == 4 && threading.DocumentationJobs == 2 && threading.Documentation == MajdataPlay.Tools.AndroidJavaWrapperGenerator.DocumentationMode.Offline, "CLI preserves independent CPU and documentation worker counts");
            var parsed = CliOptions.Parse(new[] { "--no-sdk", "--output", ".", "--input", "one.jar", "--input", "two.class", "--namespace", "Custom.Bindings" })!;
            Require(parsed.Inputs.Count == 2 && parsed.RootNamespace == "Custom.Bindings", "Repeated inputs and custom namespaces are preserved");
        }
    }
}

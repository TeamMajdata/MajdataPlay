using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text.Json;
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
            var trees = sources.Where(source => source.Key.EndsWith(".g.cs", StringComparison.Ordinal))
                .Select(source => CSharpSyntaxTree.ParseText(source.Value)).ToArray();
            var classes = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()).Where(type => type.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword))).ToArray();
            Require(classes.All(type => type.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword))), "Every public generated class is partial");
            var interfaces = trees.SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>()).ToArray();
            Require(interfaces.Length > 0 && interfaces.All(type => !type.Modifiers.Any(token => token.IsKind(SyntaxKind.PartialKeyword))), "Java interfaces generate nonpartial C# contracts");
            Require(classes.Any(type => type.Identifier.ValueText == "JavaObject"), "java.lang.Object is named JavaObject");
            Require(classes.Any(type => type.Identifier.ValueText == "Integer"), "Boxed Integer has a typed wrapper");
            var widget = classes.Single(type => type.Identifier.ValueText == "Widget"
                && type.Members.OfType<ConstructorDeclarationSyntax>().Any());
            Require(widget.BaseList != null && widget.BaseList.Types.Any(item => item.ToString().EndsWith(".Base", StringComparison.Ordinal)), "Nonpublic intermediate superclass falls back to the public Java ancestor");
            Require(widget.Members.OfType<PropertyDeclarationSyntax>().All(item => item.AccessorList == null || item.AccessorList.Accessors.All(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration))), "Public Java fields generate getter-only properties");
            Require(!classes.Any(type => type.Identifier.ValueText.Contains("PublicInside", StringComparison.Ordinal)), "Inaccessible nested Java classes are not exported");
            Require(widget.Members.OfType<MethodDeclarationSyntax>().Any(item => item.Identifier.ValueText.Contains("Inherited", StringComparison.Ordinal)), "Public members of package-private Java bases are exposed");
            var abstractWidget = classes.Single(type => type.Identifier.ValueText == "AbstractWidget");
            Require(abstractWidget.Members.OfType<ConstructorDeclarationSyntax>()
                .Where(item => !item.Modifiers.Any(token => token.IsKind(SyntaxKind.StaticKeyword)))
                .All(item => item.ParameterList.Parameters.Count > 0), "Abstract Java types receive reference-only constructors");
            Require(sources.Values.All(source => source.Contains("#nullable enable", StringComparison.Ordinal)), "Nullable analysis is enabled in all generated sources");
            var result = classes.Single(type => type.Identifier.ValueText == "Result"
                && type.Parent is ClassDeclarationSyntax parent && parent.Identifier.ValueText == "ManagedContracts");
            Require(result.Members.OfType<PropertyDeclarationSyntax>().Any(property => property.Identifier.ValueText == "Data"
                && property.Type.ToString() == "sbyte[]?")
                && !result.Members.OfType<PropertyDeclarationSyntax>().Any(property => property.Identifier.ValueText == "DataJavaField"),
                "Ordinary nested result fields retain Data without inheriting CLR exception member reservations");
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
            VerifyEnhancedRuntimeMetadata(assembly);
            var runtime = assembly.GetType("Validation.CustomRoot.AndroidJni", true)!;
            var map = runtime.GetMethods().Single(item => item.Name == "MapArray").MakeGenericMethod(typeof(int), typeof(int));
            var converter = new Func<int, int>(value => value + 1);
            Require(Invoke(map, new object?[] { null, converter }) == null, "MapArray preserves null");
            Require(((int[])Invoke(map, new object?[] { Array.Empty<int>(), converter })!).Length == 0, "MapArray preserves empty arrays");
            Require(((int[])Invoke(map, new object?[] { new[] { 1, 2 }, converter })!).SequenceEqual(new[] { 2, 3 }), "MapArray converts values in order");
            var failingConverter = new Func<int, int>(value => throw new InvalidOperationException("converter failed"));
            Expect<InvalidOperationException>(() => Invoke(map, new object?[] { new[] { 1 }, failingConverter }), "MapArray propagates converter errors");
            var getClass = runtime.GetMethod("GetClass")!;
            Expect<PlatformNotSupportedException>(() => Invoke(getClass, new object?[] { "java.lang.Object" }), "Non-Android JNI is rejected before any Unity native call");
            var wrap = runtime.GetMethods().Single(item => item.Name == "Wrap").MakeGenericMethod(assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.JavaObject", true)!);
            Expect<ArgumentNullException>(() => Invoke(wrap, new object?[] { null, null }), "Wrap rejects a missing reference factory");
            var javaObject = assembly.GetType("Validation.CustomRoot.Bindings.Java.Lang.JavaObject", true)!;
            var converterType = assembly.GetType("Validation.CustomRoot.JavaWrapperConverter", true)!;
            var converterMethod = converterType.GetMethods().Single(method => method.Name == "ToWrapper" && method.IsGenericMethodDefinition)
                .MakeGenericMethod(javaObject);
            Require(Invoke(converterMethod, new object?[] { null, true }) is null,
                "Checked public wrapper conversion preserves a null Java reference");
            var extensionType = assembly.GetType("Validation.CustomRoot.AndroidJavaObjectExtensions", true)!;
            var extensionMethod = extensionType.GetMethod("ToWrapper")!.MakeGenericMethod(javaObject);
            Require(Invoke(extensionMethod, new object?[] { null, false }) is null,
                "AndroidJavaObject wrapper conversion extension preserves a null borrowed reference");
            var handleMethod = converterType.GetMethods().Single(method => method.Name == "FromHandle" && method.IsGenericMethodDefinition)
                .MakeGenericMethod(javaObject);
            Require(Invoke(handleMethod, new object?[] { IntPtr.Zero }) is null,
                "Checked handle conversion preserves the null JNI handle without native calls");
        }

        /// <summary>Checks that generated namespace boundaries are reflected in relative output paths.</summary>
        /// <param name="sources">The complete generated source set.</param>
        /// <param name="rootNamespace">The configured namespace that directly contains runtime support.</param>
        /// <exception cref="InvalidOperationException">A generated declaration uses an unexpected namespace or directory.</exception>
        private static void VerifyNamespaceLayout(IReadOnlyDictionary<string, string> sources, string rootNamespace)
        {
            var runtimePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var runtime in sources.Where(source => !source.Key.Contains('/') && source.Key.EndsWith(".cs", StringComparison.Ordinal)))
            {
                var path = runtime.Key;
                var name = Path.GetFileNameWithoutExtension(path).Split('.')[0];
                runtimePaths.Add(path);
                var runtimeSource = sources[path];
                var declaration = CSharpSyntaxTree.ParseText(runtimeSource).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
                Require(declaration.Name.ToString() == rootNamespace
                    && declaration.Members.OfType<BaseTypeDeclarationSyntax>().Any(type => type.Identifier.ValueText == name), "Runtime support is declared directly in the configured root and output directory: " + name);
            }

            var bindings = sources.Where(source => !runtimePaths.Contains(source.Key)).ToArray();
            var bindingsNamespace = rootNamespace + ".Bindings";
            Require(bindings.Length > 0 && bindings.All(source =>
            {
                var declaration = CSharpSyntaxTree.ParseText(source.Value).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
                var name = declaration.Name.ToString();
                return name == bindingsNamespace || name.StartsWith(bindingsNamespace + ".", StringComparison.Ordinal);
            }), "Every Java wrapper is declared beneath the configured root's Bindings namespace");
            Require(bindings.All(source =>
            {
                var declaration = CSharpSyntaxTree.ParseText(source.Value).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
                var relativeNamespace = declaration.Name.ToString().Substring(rootNamespace.Length + 1);
                return source.Key == relativeNamespace.Replace('.', '/') + "/" + Path.GetFileName(source.Key);
            }), "Every Java wrapper output directory matches its namespace relative to the configured root");
        }

        /// <summary>Runs the actual CLI entry point to migrate a previous directory layout in place.</summary>
        /// <param name="workspace">The exclusively owned directory used for inputs and both output layouts.</param>
        /// <exception cref="IOException">Writing fixtures or reading migrated sources fails.</exception>
        /// <exception cref="InvalidOperationException">Migration fails to preserve ownership or deterministic source bytes.</exception>
        private static void VerifyCliLayoutMigration(string workspace)
        {
            var directory = Path.Combine(workspace, "cli-layout-migration");
            Directory.CreateDirectory(directory);
            var input = Path.Combine(directory, "Widget.java");
            File.WriteAllText(input, "package directory.layout; public class Widget { public int value; }");
            var options = new GeneratorOptions
            {
                NoSdk = true,
                RootNamespace = "CliLayout.CustomRoot",
                OutputDirectory = Path.Combine(directory, "generated")
            };
            var previous = new Dictionary<string, string>
            {
                { "Runtime/AndroidJni.cs", "// previously generated JNI helper\r\n" },
                { "Runtime/IJavaObject.cs", "// previously generated reference contract\r\n" },
                { "Runtime/JavaInvocationException.cs", "// previously generated exception\r\n" },
                { "Runtime/JavaObjectReference.cs", "// previously generated reference owner\r\n" },
                { "Directory/Layout/Widget.g.cs", "// previously generated Java binding\r\n" },
                { "Java/Lang/JavaObject.g.cs", "// previously generated Java base binding\r\n" }
            };
            GeneratedOutput.Write(options, null, previous, CancellationToken.None);
            var handwritten = new Dictionary<string, string>
            {
                { "Runtime/Extensions.cs", "// handwritten runtime extension\r\n" },
                { "Directory/Layout/Widget.Partial.cs", "// handwritten Java partial extension\r\n" }
            };
            foreach (var file in handwritten)
            {
                File.WriteAllText(Path.Combine(options.OutputDirectory, file.Key), file.Value);
            }

            var arguments = new[] { "--no-sdk", "--no-docs", "--input", input, "--namespace", options.RootNamespace, "--output", options.OutputDirectory };
            Require(MajdataPlay.Tools.AndroidJavaWrapperGenerator.Program.Main(arguments).GetAwaiter().GetResult() == 0, "CLI regenerates a previous directory layout in place");
            Require(previous.Keys.All(path => !File.Exists(Path.Combine(options.OutputDirectory, path))), "CLI removes manifest-owned sources from the previous runtime and package directories");
            Require(handwritten.All(file => File.ReadAllText(Path.Combine(options.OutputDirectory, file.Key)) == file.Value), "CLI preserves handwritten files inside the previous generated directories");
            var manifestPath = Path.Combine(options.OutputDirectory, GeneratedOutput.ManifestName);
            var manifest = JsonSerializer.Deserialize<GeneratedOutput.OutputManifest>(File.ReadAllText(manifestPath))!;
            Require(manifest.RootNamespace == options.RootNamespace
                && manifest.Files.ContainsKey("AndroidJni.cs")
                && manifest.Files.ContainsKey("Bindings/Directory/Layout/Widget.g.cs")
                && manifest.Files.ContainsKey("Bindings/Java/Lang/JavaObject.g.cs")
                && previous.Keys.All(path => !manifest.Files.ContainsKey(path)), "CLI manifest records the configured root and only the new namespace-based directory layout");
            var migrated = manifest.Files.Keys.ToDictionary(path => path, path => File.ReadAllText(Path.Combine(options.OutputDirectory, path)), StringComparer.Ordinal);
            VerifyNamespaceLayout(migrated, options.RootNamespace);
            var first = Directory.GetFiles(options.OutputDirectory, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
            Require(MajdataPlay.Tools.AndroidJavaWrapperGenerator.Program.Main(arguments).GetAwaiter().GetResult() == 0, "CLI can repeat generation after directory migration");
            var repeated = Directory.GetFiles(options.OutputDirectory, "*", SearchOption.AllDirectories);
            Require(first.Count == repeated.Length && repeated.All(path => first.TryGetValue(path, out var bytes) && bytes.SequenceEqual(File.ReadAllBytes(path))), "Repeated CLI generation leaves migrated sources, handwritten files, and the manifest byte-identical");
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
            Require(CliOptions.Parse(new[] { "--output", "." })!.RootNamespace == "MajdataPlay.Platform.Android", "The CLI default selects the runtime root before the Bindings namespace");
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

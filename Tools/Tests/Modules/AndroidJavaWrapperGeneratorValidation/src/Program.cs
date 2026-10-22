using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Validates real JVM inputs and generated sources against the .NET Standard 2.1 reference contract.</summary>
    internal static partial class Program
    {
        /// <summary>Counts completed meaningful regression checks.</summary>
        private static int s_checks;

        /// <summary>Runs extraction, generation, compilation, and pure managed runtime validation.</summary>
        /// <param name="arguments">Optional SDK, JDK, Unity data, reference-pack, and full-SDK settings.</param>
        /// <returns>Zero when all cases pass; one when validation fails.</returns>
        private static int Main(string[] arguments)
        {
            try
            {
                var settings = ValidationSettings.Parse(arguments);
                var workRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.work"));
                Directory.CreateDirectory(workRoot);
                var workspace = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workspace);
                try
                {
                    VerifyUnityArchives(settings, workspace);
                    VerifyEditorOutput(workspace);
                    VerifyEditorRefresh(settings, workspace);
                    VerifyStandaloneWrapperBuild(settings, workspace);
                    VerifySourceNamespaces(settings, workspace);
                    VerifyCliLayoutMigration(workspace);
                    if (settings.EditorInputsOnly)
                    {
                        VerifyProjectJavaInputs(settings, workspace);
                    }
                    else if (settings.SdkOnly)
                    {
                        VerifyFullSdk(settings, workspace);
                    }
                    else
                    {
                        Run(settings, workspace);
                    }
                    Console.WriteLine("PASS: " + s_checks + " checks. JNI execution, Unity lifecycle, IL2CPP, and Android devices were not tested.");
                    return 0;
                }
                finally
                {
                    var resolved = Path.GetFullPath(workspace);
                    if (!resolved.StartsWith(workRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || resolved == workRoot)
                    {
                        throw new IOException("Unsafe validation cleanup path.");
                    }

                    Directory.Delete(resolved, true);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        /// <summary>Exercises all regression groups in an isolated ignored workspace.</summary>
        /// <param name="settings">The toolchain locations.</param>
        /// <param name="workspace">An exclusively owned scratch directory.</param>
        private static void Run(ValidationSettings settings, string workspace)
        {
            var javaSources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.java").OrderBy(item => item, StringComparer.Ordinal).ToArray();
            var options = CreateOptions(settings, Path.Combine(workspace, "generated"));
            options.Inputs.AddRange(javaSources);
            options.IncludePackages.Add("fixtures");
            Console.WriteLine("Scanning SDK and Java source fixtures...");
            var sourceApi = JavaApiScanner.Scan(options, CancellationToken.None);
            Require(sourceApi.Types.ContainsKey("java.lang.Object"), "SDK includes Java BCL metadata");
            Require(!sourceApi.Types.ContainsKey("fixtures.Fake"), "Public-looking text inside Java strings is not treated as a declaration");
            Require(sourceApi.Types.ContainsKey("android.net.Uri"), "SDK includes Android metadata");
            Require(sourceApi.Types["fixtures.Widget"].SuperName == "fixtures.HiddenBase", "Exact superclass is retained");
            Require(!sourceApi.Types["fixtures.HiddenBase"].IsPublic, "Package-private superclass is retained without export");
            Require(sourceApi.Types["fixtures.Widget$Inner"].Methods.Any(item => item.Name == "<init>" && item.Descriptor == "(Lfixtures/Widget;I)V"), "Inner constructors retain the enclosing parameter");
            Require(!sourceApi.Types["fixtures.Widget$Hidden$PublicInside"].IsPublic, "Public nested type with private enclosing class stays inaccessible");
            Require(sourceApi.Types["fixtures.Widget"].Methods.Any(item => item.Name == "boxed" && item.Descriptor == "(Ljava/lang/Integer;)Ljava/lang/Integer;"), "Boxed values retain reference descriptors");
            Require(sourceApi.Types["fixtures.GenericBox"].Signature?.Contains("Ljava/lang/Comparable<TT;>;", StringComparison.Ordinal) == true,
                "Generic class signatures retain intersection and self-referential bounds");
            Require(sourceApi.Types["fixtures.GenericBox$Inner"].Signature?.Contains("<U:TT;>", StringComparison.Ordinal) == true,
                "Nonstatic nested generic signatures retain their enclosing parameter bound");
            Require(sourceApi.Types["fixtures.ThrowBox"].Fields.Single(field => field.Name == "type").Signature == "Ljava/lang/Class<TT;>;"
                && sourceApi.Types["fixtures.ThrowBox"].Methods.Single(method => method.Name == "subtype").Signature == "<E:TT;>()Ljava/lang/Class<TE;>;",
                "Throwable bounds and dependent method variables retain their exact JVM generic signatures");

            var classes = Path.Combine(workspace, "classes");
            Directory.CreateDirectory(classes);
            CompileJava(settings, sourceApi.PlatformJar!, classes, javaSources);
            var jarPath = Path.Combine(workspace, "fixtures.jar");
            ZipFile.CreateFromDirectory(classes, jarPath);
            var jarOptions = CreateOptions(settings, options.OutputDirectory);
            jarOptions.Inputs.Add(jarPath);
            jarOptions.IncludePackages.Add("fixtures");
            var jarApi = JavaApiScanner.Scan(jarOptions, CancellationToken.None);
            var classOptions = CreateOptions(settings, options.OutputDirectory);
            classOptions.Inputs.AddRange(Directory.GetFiles(classes, "*.class", SearchOption.AllDirectories));
            classOptions.IncludePackages.Add("fixtures");
            var classApi = JavaApiScanner.Scan(classOptions, CancellationToken.None);
            var serialOptions = CreateOptions(settings, options.OutputDirectory);
            serialOptions.Jobs = 1;
            serialOptions.Inputs.AddRange(javaSources);
            var serialApi = JavaApiScanner.Scan(serialOptions, CancellationToken.None);
            Require(FixtureFingerprint(sourceApi) == FixtureFingerprint(serialApi), "Serial and parallel source/SDK analysis preserve API identity");
            Require(FixtureFingerprint(sourceApi) == FixtureFingerprint(jarApi), "Java source / JAR metadata parity");
            Require(FixtureFingerprint(sourceApi) == FixtureFingerprint(classApi), "Java source / class-file metadata parity");

            var movedClass = Path.Combine(workspace, "unrelated-name.class");
            File.Copy(Path.Combine(classes, "fixtures", "Initializer.class"), movedClass);
            var movedOptions = CreateOptions(settings, options.OutputDirectory);
            movedOptions.Inputs.Add(movedClass);
            Require(JavaApiScanner.Scan(movedOptions, CancellationToken.None).Types.ContainsKey("fixtures.Initializer"), "Class identity is read from bytes rather than its path");

            Console.WriteLine("Generating fixture wrappers and compiling C# 9 / netstandard2.1...");
            var generated = Generate(sourceApi, options);
            options.Jobs = 1;
            var serial = Generate(sourceApi, options);
            options.Jobs = 4;
            var repeated = Generate(sourceApi, options);
            Require(serial.Count == repeated.Count && serial.All(item => repeated[item.Key] == item.Value), "Serial and parallel wrapper generation are byte-identical");
            Require(generated.Keys.SequenceEqual(repeated.Keys) && generated.All(item => repeated[item.Key] == item.Value), "Byte-for-byte deterministic generation");
            VerifyDeclarations(generated);
            VerifyEnhancedDeclarations(generated);
            VerifyNamespaceLayout(generated, options.RootNamespace);
            var assembly = CompileGenerated(settings, generated, false, true);
            CompileGenerated(settings, generated, true, false);
            VerifyManagedRuntime(settings, assembly!);
            VerifyOutputOwnership(workspace, options, generated);
            VerifyCli();
            VerifyDocumentation(settings, workspace);
            if (settings.OfficialSamples != null)
            {
                VerifyOfficialSamples(settings, sourceApi, workspace);
            }
            VerifySourceAnalysis(settings, workspace);

            var standalone = new GeneratorOptions { NoSdk = true, RootNamespace = "Standalone.CustomRoot", OutputDirectory = Path.Combine(workspace, "standalone") };
            standalone.Inputs.Add(jarPath);
            var standaloneApi = JavaApiScanner.Scan(standalone, CancellationToken.None);
            CompileGenerated(settings, Generate(standaloneApi, standalone), false, false);
            Require(standaloneApi.PlatformJar == null, "SDK-free input scanning does not use android.jar");

            var invalidClass = Path.Combine(workspace, "invalid.class");
            File.WriteAllText(invalidClass, "not a JVM class");
            var invalidOptions = new GeneratorOptions { NoSdk = true };
            invalidOptions.Inputs.Add(invalidClass);
            ExpectFailure(() => JavaApiScanner.Scan(invalidOptions, CancellationToken.None), "Malformed class file is rejected");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Expect<OperationCanceledException>(() => JavaApiScanner.Scan(options, cancellation.Token), "Scan observes cancellation");

            if (settings.FullSdk)
            {
                VerifyFullSdk(settings, workspace);
            }
        }

        /// <summary>Compiles the default complete API without any Java tool dependency.</summary>
        /// <param name="settings">The bytecode and C# toolchain locations.</param>
        /// <param name="workspace">The isolated validation directory.</param>
        private static void VerifyFullSdk(ValidationSettings settings, string workspace)
        {
            Console.WriteLine("Generating the entire installed SDK, including Java BCL...");
            var fullOptions = CreateOptions(settings, Path.Combine(workspace, "full"));
            fullOptions.Jobs = 1;
            var serialApi = JavaApiScanner.Scan(fullOptions, CancellationToken.None);
            var serial = Generate(serialApi, fullOptions);
            fullOptions.Jobs = 4;
            var fullApi = JavaApiScanner.Scan(fullOptions, CancellationToken.None);
            Require(ApiFingerprint(serialApi) == ApiFingerprint(fullApi), "Entire SDK metadata is deterministic across worker counts");
            var full = Generate(fullApi, fullOptions);
            Require(serial.Count == full.Count && serial.All(item => full[item.Key] == item.Value), "All SDK source files are byte-identical for one and four workers");
            Require(full.Keys.Any(item => item.StartsWith("Bindings/Android/Net/", StringComparison.Ordinal)), "Package directories mirror the Bindings.Android.Net namespace");
            VerifyNamespaceLayout(full, fullOptions.RootNamespace);
            CompileGenerated(settings, full, false, false);
            CompileGenerated(settings, full, true, false);
            Console.WriteLine("Full SDK compilation passed: " + full.Count + " generated C# files.");
        }

        /// <summary>Creates generator options for the requested stable Android platform.</summary>
        /// <param name="settings">The toolchain settings.</param>
        /// <param name="output">The isolated destination.</param>
        /// <returns>Options using a distinctive configurable namespace.</returns>
        private static GeneratorOptions CreateOptions(ValidationSettings settings, string output)
        {
            return new GeneratorOptions { SdkDirectory = settings.Sdk, RootNamespace = "Validation.CustomRoot", OutputDirectory = output };
        }

        /// <summary>Combines wrapper and standalone runtime output in deterministic path order.</summary>
        /// <param name="api">The discovered Java metadata.</param>
        /// <param name="options">The generation settings.</param>
        /// <returns>The complete C# source set.</returns>
        private static SortedDictionary<string, string> Generate(JavaApi api, GeneratorOptions options)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in WrapperEmitter.Generate(api, options).Concat(RuntimeEmitter.Generate(options.RootNamespace)))
            {
                result.Add(source.Key, source.Value);
            }

            return result;
        }

        /// <summary>Captures the complete JVM API including flags, parameter names, and hierarchy.</summary>
        /// <param name="api">The scanned metadata graph.</param>
        /// <returns>A deterministic complete API representation.</returns>
        private static string ApiFingerprint(JavaApi api)
        {
            return string.Join("|", api.Types.Values.OrderBy(type => type.Name, StringComparer.Ordinal).Select(type => type.Name + ":" + type.AccessFlags + ":" + type.IsPublic + ":" + type.SuperName + ":" + type.Signature + ":" + type.EnclosingType + ":" + string.Join(",", type.Interfaces) + ":" + string.Join(",", type.Fields.Select(field => field.Name + field.Descriptor + field.Signature + field.AccessFlags)) + ":" + string.Join(",", type.Methods.Select(method => method.Name + method.Descriptor + method.Signature + method.AccessFlags + string.Join("/", method.ParameterNames)))));
        }

        /// <summary>Creates a representation-independent digest of the fixture API.</summary>
        /// <param name="api">Metadata obtained through one input mode.</param>
        /// <returns>The sorted binary type, field, and method contract.</returns>
        private static string FixtureFingerprint(JavaApi api)
        {
            return string.Join("|", api.Types.Values.Where(item => item.Name.StartsWith("fixtures.", StringComparison.Ordinal) && item.IsPublic && !item.IsSynthetic).OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => item.Name + ":" + item.IsPublic + ":" + item.SuperName + ":" + item.Signature + ":" + string.Join(",", item.Fields.Where(field => field.IsPublic).Select(field => field.Name + field.Descriptor + field.Signature).OrderBy(value => value, StringComparer.Ordinal)) + ":" + string.Join(",", item.Methods.Where(method => method.IsPublic && !method.IsSynthetic && !method.IsBridge).Select(method => method.Name + method.Descriptor + method.Signature).OrderBy(value => value, StringComparer.Ordinal))));
        }

        /// <summary>Compiles Java fixtures independently to compare source, class, and archive extraction.</summary>
        /// <param name="settings">The JDK location.</param>
        /// <param name="platformJar">The Android bootclasspath.</param>
        /// <param name="output">The compiler output directory.</param>
        /// <param name="sources">The fixture sources.</param>
        /// <exception cref="InvalidOperationException">The Java compiler fails.</exception>
        private static void CompileJava(ValidationSettings settings, string platformJar, string output, string[] sources)
        {
            var executable = Path.Combine(settings.Jdk, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
            var info = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-encoding", "UTF-8", "-source", "8", "-target", "8", "-parameters", "-bootclasspath", platformJar, "-d", output }.Concat(sources))
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot launch javac.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("javac failed: " + stderr.GetAwaiter().GetResult() + stdout.GetAwaiter().GetResult());
            }
        }

        /// <summary>Asserts a regression contract and records successful checks.</summary>
        /// <param name="condition">Whether the expected behavior holds.</param>
        /// <param name="description">The checked contract.</param>
        /// <exception cref="InvalidOperationException">The contract fails.</exception>
        private static void Require(bool condition, string description)
        {
            if (!condition)
            {
                throw new InvalidOperationException("FAIL: " + description);
            }

            s_checks++;
        }

        /// <summary>Requires an action to fail with the specified managed exception type.</summary>
        /// <typeparam name="TException">The required failure type.</typeparam>
        /// <param name="action">The operation under test.</param>
        /// <param name="description">The checked failure contract.</param>
        private static void Expect<TException>(Action action, string description) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                s_checks++;
                return;
            }

            throw new InvalidOperationException("FAIL: " + description);
        }

        /// <summary>Requires invalid input to report a descriptive generation failure.</summary>
        /// <param name="action">The invalid-input operation.</param>
        /// <param name="description">The checked failure contract.</param>
        private static void ExpectFailure(Action action, string description)
        {
            try
            {
                action();
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException || exception is InvalidOperationException)
            {
                Require(exception.Message.Length > 0, description);
                return;
            }

            throw new InvalidOperationException("FAIL: " + description);
        }
    }
}

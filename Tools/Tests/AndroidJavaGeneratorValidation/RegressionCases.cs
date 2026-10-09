#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
using Microsoft.CodeAnalysis;

namespace MajdataPlay.Tests.AndroidJavaGeneratorValidation
{
    /// <summary>
    /// Defines the bounded managed regression cases without duplicating production implementation.
    /// </summary>
    internal static class RegressionCases
    {
        /// <summary>
        /// Lists every case in a stable order for supervision and filtering.
        /// </summary>
        public static readonly string[] Names =
        {
            "source-api", "explicit-java-object-base", "class-api", "jar-api", "inner-descriptor-parity", "assembly-configuration", "configuration-precedence",
            "unity-data-paths", "unity-data-managed-references", "unity-data-player-references", "unity-data-editor-paths", "unity-data-missing",
            "classpath-only", "documentation-path-array", "no-inherited", "determinism", "source-refresh", "android-symbol-compilation",
            "invalid-nonpartial", "invalid-static", "invalid-nested", "invalid-generic", "duplicate-mapping",
            "missing-java-type", "missing-dependency", "missing-source", "missing-classpath", "missing-documentation",
            "missing-sdk", "missing-platform", "missing-jdk", "missing-extractor", "negative-api",
            "member-collisions", "user-member-collision", "runtime-map-array", "runtime-platform-guard"
        };

        /// <summary>
        /// Executes exactly one requested case in the current isolated worker.
        /// </summary>
        /// <param name="name">The stable case identifier.</param>
        /// <param name="workspace">The prepared isolated Java fixtures.</param>
        /// <exception cref="ArgumentException">The requested case is unknown.</exception>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        public static void Run(string name, FixtureWorkspace workspace)
        {
            if (name == "runtime-map-array")
            {
                AssertMapArray();
                return;
            }
            if (name == "runtime-platform-guard")
            {
                AssertPlatformGuard();
                return;
            }
            if (name.StartsWith("unity-data-", StringComparison.Ordinal))
            {
                ConfigurationPathCases.Run(name, workspace);
                return;
            }
            var runner = new GeneratorRunner(workspace);
            switch (name)
            {
                case "source-api":
                    RunApi(runner, workspace.SourceFiles(), workspace, name);
                    break;
                case "explicit-java-object-base":
                    var explicitBaseSource = runner.RootDeclarations(workspace.SourceFiles(), workspace.DocumentationPaths);
                    explicitBaseSource = explicitBaseSource.Replace("public partial class WidgetWrapper", "public partial class WidgetWrapper : global::MajdataPlay.Platform.Android.Runtime.Java.Lang.JavaObject");
                    var explicitBaseRun = runner.Run(runner.CreateCompilation(explicitBaseSource, name), name);
                    ApiAssertions.AssertRootApi(explicitBaseRun);
                    break;
                case "class-api":
                    RunApi(runner, workspace.ClassFiles(), workspace, name);
                    break;
                case "jar-api":
                    RunApi(runner, new[] { workspace.JarPath }, workspace, name);
                    break;
                case "inner-descriptor-parity":
                    AssertInnerInputParity(runner, workspace, name);
                    break;
                case "assembly-configuration":
                    RunApi(runner, new[] { workspace.JarPath }, workspace, name, assemblyConfiguration: true);
                    break;
                case "configuration-precedence":
                    AssertConfigurationPrecedence(runner, workspace, name);
                    break;
                case "classpath-only":
                    AssertClasspathOnly(runner, workspace, name);
                    break;
                case "documentation-path-array":
                    var secondDocumentation = Path.Combine(workspace.CaseDirectory(name), "additional-docs");
                    Directory.CreateDirectory(Path.Combine(secondDocumentation, "dependency"));
                    File.Copy(Path.Combine(workspace.SourceDirectory, "dependency", "ExternalBase.java"), Path.Combine(secondDocumentation, "dependency", "ExternalBase.java"));
                    var documentationSource = runner.RootDeclarations(new[] { workspace.JarPath }, new[] { workspace.SourceDirectory, secondDocumentation });
                    var documentationRun = runner.Run(runner.CreateCompilation(documentationSource, name), name);
                    ApiAssertions.AssertRootApi(documentationRun);
                    break;
                case "no-inherited":
                    RunApi(runner, new[] { workspace.JarPath }, workspace, name, includeInherited: false);
                    break;
                case "determinism":
                    AssertDeterminism(runner, workspace, name);
                    break;
                case "source-refresh":
                    AssertSourceRefresh(runner, workspace, name);
                    break;
                case "android-symbol-compilation":
                    var androidSource = runner.RootDeclarations(new[] { workspace.JarPath }, workspace.DocumentationPaths);
                    var androidRun = runner.Run(runner.CreateCompilation(androidSource, name, androidPlayer: true), name);
                    ApiAssertions.AssertRootApi(androidRun);
                    break;
                case "invalid-nonpartial":
                    RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Widget", "public class InvalidWrapper"), "AJG001");
                    break;
                case "invalid-static":
                    RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Widget", "public static partial class InvalidWrapper"), "AJG001");
                    break;
                case "invalid-nested":
                    var nested = SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper");
                    nested = nested.Replace("namespace Fixtures.Wrappers\r\n{", "namespace Fixtures.Wrappers\r\n{\r\n    public partial class Outer\r\n    {");
                    nested += "}\r\n";
                    RunNegative(runner, name, nested, "AJG001");
                    break;
                case "invalid-generic":
                    RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper<T>"), "AJG001");
                    break;
                case "duplicate-mapping":
                    var duplicate = SingleDeclaration(workspace, "fixtures.Widget", "public partial class FirstWrapper");
                    duplicate = duplicate.Substring(0, duplicate.LastIndexOf('}'))
                        + "    [JavaClass(\"fixtures.Widget\")]\r\n    public partial class SecondWrapper\r\n    {\r\n    }\r\n}\r\n";
                    RunNegative(runner, name, duplicate, "AJG002");
                    break;
                case "missing-java-type":
                    RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Missing", "public partial class InvalidWrapper"), "AJG004", "AJG005");
                    break;
                case "missing-dependency":
                    var missing = SingleDeclaration(workspace, "fixtures.NeedsDependency", "public partial class InvalidWrapper",
                        "Sources = " + GeneratorRunner.StringArray(new[] { Path.Combine(workspace.MissingDependencyDirectory, "fixtures", "NeedsDependency.class") }));
                    RunNegative(runner, name, missing, "AJG004", "AJG005");
                    break;
                case "missing-source":
                    AssertMissingInput(runner, workspace, name, "Sources", "does-not-exist.java");
                    break;
                case "missing-classpath":
                    AssertMissingInput(runner, workspace, name, "ClassPath", "does-not-exist.jar");
                    break;
                case "missing-documentation":
                    AssertMissingInput(runner, workspace, name, "DocumentationPaths", "does-not-exist-docs");
                    break;
                case "missing-sdk":
                    var sdkSource = SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper",
                        "AndroidSdkPath = " + GeneratorRunner.Literal(Path.Combine(workspace.Root, "does-not-exist-sdk")));
                    RunNegative(runner, name, sdkSource, "AJG003");
                    break;
                case "missing-platform":
                    var emptySdk = Path.Combine(workspace.CaseDirectory(name), "sdk");
                    Directory.CreateDirectory(Path.Combine(emptySdk, "platforms"));
                    var platformSource = SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper",
                        "AndroidSdkPath = " + GeneratorRunner.Literal(emptySdk) + ", ApiLevel = " + workspace.Options.ApiLevel);
                    RunNegative(runner, name, platformSource, "AJG003");
                    break;
                case "missing-jdk":
                    var jdkSource = SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper",
                        "JavaHome = " + GeneratorRunner.Literal(Path.Combine(workspace.Root, "does-not-exist-jdk")));
                    RunNegative(runner, name, jdkSource, "AJG003");
                    break;
                case "missing-extractor":
                    using (var environment = new EnvironmentOverride(new Dictionary<string, string?>
                    {
                        { "MAJDATA_JAVA_EXTRACTOR", Path.Combine(workspace.Root, "does-not-exist-extractor.java") }
                    }))
                    {
                        RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper"), "AJG003");
                    }
                    break;
                case "negative-api":
                    RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper", "ApiLevel = -1"), "AJG003");
                    break;
                case "member-collisions":
                    var collision = runner.Run(runner.CreateCompilation(SingleDeclaration(workspace, "fixtures.Collision", "public partial class CollisionWrapper"), name), name);
                    ApiAssertions.AssertCollisionApi(collision);
                    var repeatedCollision = runner.Run(collision.Input, name + "-repeat", collision.Driver);
                    repeatedCollision.AssertCompiles();
                    Check.True(collision.GeneratedSources.SequenceEqual(repeatedCollision.GeneratedSources), "Collision aliases and constructor factories must remain deterministic.");
                    break;
                case "user-member-collision":
                    var memberSource = SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper");
                    memberSource = memberSource.Replace("public partial class InvalidWrapper\r\n    {", "public partial class InvalidWrapper\r\n    {\r\n        public int MutableCount { get; }");
                    RunNegative(runner, name, memberSource, "AJG008");
                    break;
                default:
                    throw new ArgumentException("Unknown regression case " + name + ".", nameof(name));
            }
        }

        /// <summary>
        /// Runs a complete positive API assertion with explicitly selected Java inputs.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="sources">The source, class-file, or jar inputs.</param>
        /// <param name="workspace">The prepared Java documentation and fixtures.</param>
        /// <param name="name">The isolated case name.</param>
        /// <param name="includeInherited">Whether inherited members should be generated.</param>
        /// <param name="assemblyConfiguration">Whether shared settings live on the assembly.</param>
        private static void RunApi(GeneratorRunner runner, string[] sources, FixtureWorkspace workspace, string name,
            bool includeInherited = true, bool assemblyConfiguration = false)
        {
            var source = runner.RootDeclarations(sources, workspace.DocumentationPaths, includeInherited, assemblyConfiguration);
            var run = runner.Run(runner.CreateCompilation(source, name), name);
            ApiAssertions.AssertRootApi(run, includeInherited);
        }

        /// <summary>
        /// Compares implicit enclosing-instance constructor descriptors across source, class-file, and jar inputs.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="workspace">The same tracked fixtures in all three Java input representations.</param>
        /// <param name="name">The isolated case name.</param>
        private static void AssertInnerInputParity(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var representations = new[] { workspace.SourceFiles(), workspace.ClassFiles(), new[] { workspace.JarPath } };
            string[]? baseline = null;
            for (var index = 0; index < representations.Length; index++)
            {
                var mode = name + "-" + index;
                var source = runner.RootDeclarations(representations[index], workspace.DocumentationPaths);
                var run = runner.Run(runner.CreateCompilation(source, mode), mode);
                ApiAssertions.AssertRootApi(run);
                var inner = ApiAssertions.Type(run, "InnerWrapper");
                var contracts = inner.InstanceConstructors.Where(constructor => constructor.Parameters.Length > 0 && constructor.Parameters[0].Type.Name == "WidgetWrapper")
                    .Select(constructor => string.Join(",", constructor.Parameters.Select(parameter => parameter.Type.ToDisplayString() + " " + parameter.Name))
                        + ":" + string.Join(",", constructor.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax>()
                            .SelectMany(syntax => syntax.Initializer!.ArgumentList.Arguments)
                            .Select(argument => argument.Expression)
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax>()
                            .Select(literal => literal.Token.ValueText)))
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray();
                if (baseline is null)
                {
                    baseline = contracts;
                }
                else
                {
                    Check.True(baseline.SequenceEqual(contracts), "Member-class source/class/jar constructor descriptors and typed enclosingInstance arguments must match exactly.");
                }
            }
        }

        /// <summary>
        /// Requires class scalar settings to override intentionally invalid assembly settings.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="workspace">The configured fixture toolchain.</param>
        /// <param name="name">The isolated case name.</param>
        private static void AssertConfigurationPrecedence(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var source = runner.RootDeclarations(new[] { workspace.JarPath }, workspace.DocumentationPaths, assemblyConfiguration: true, explicitToolchain: true);
            source = source.Replace("AndroidSdkPath = " + GeneratorRunner.Literal(workspace.Options.AndroidSdk),
                "AndroidSdkPath = " + GeneratorRunner.Literal(Path.Combine(workspace.Root, "bad-assembly-sdk")));
            source = source.Replace("JavaHome = " + GeneratorRunner.Literal(workspace.Options.JavaHome),
                "JavaHome = " + GeneratorRunner.Literal(Path.Combine(workspace.Root, "bad-assembly-jdk")));
            source = source.Replace(", IncludeInheritedMembers =", ", AndroidSdkPath = " + GeneratorRunner.Literal(workspace.Options.AndroidSdk)
                + ", JavaHome = " + GeneratorRunner.Literal(workspace.Options.JavaHome) + ", IncludeInheritedMembers =");
            var run = runner.Run(runner.CreateCompilation(source, name), name);
            ApiAssertions.AssertRootApi(run);
        }

        /// <summary>
        /// Requires jar dependencies supplied via ClassPath to work without duplicate Sources.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="workspace">The prepared jar and documentation paths.</param>
        /// <param name="name">The isolated case name.</param>
        private static void AssertClasspathOnly(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var source = runner.RootDeclarations(Array.Empty<string>(), workspace.DocumentationPaths);
            source = source.Replace("ClassPath = " + GeneratorRunner.StringArray(new[] { workspace.Options.AndroidJar }),
                "ClassPath = " + GeneratorRunner.StringArray(new[] { workspace.JarPath }));
            var run = runner.Run(runner.CreateCompilation(source, name), name);
            ApiAssertions.AssertRootApi(run);
        }

        /// <summary>
        /// Requires identical hint names and text across reused and fresh generator drivers.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="workspace">The prepared archive input.</param>
        /// <param name="name">The isolated case name.</param>
        private static void AssertDeterminism(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var source = runner.RootDeclarations(new[] { workspace.JarPath }, workspace.DocumentationPaths);
            var input = runner.CreateCompilation(source, name);
            var first = runner.Run(input, name + "-first");
            first.AssertCompiles();
            var reused = runner.Run(input, name + "-reused", first.Driver);
            var fresh = runner.Run(input, name + "-fresh");
            reused.AssertCompiles();
            fresh.AssertCompiles();
            Check.True(first.GeneratedSources.SequenceEqual(reused.GeneratedSources), "Reused driver output must be byte-for-byte deterministic.");
            Check.True(first.GeneratedSources.SequenceEqual(fresh.GeneratedSources), "Fresh driver output must be byte-for-byte deterministic.");
            Check.True(first.Diagnostics.Select(diagnostic => diagnostic.ToString()).SequenceEqual(fresh.Diagnostics.Select(diagnostic => diagnostic.ToString())),
                "Diagnostics must be deterministic across fresh runs.");
        }

        /// <summary>
        /// Requires the same source paths to be re-extracted after a tracked input's text changes.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="workspace">The fixture workspace used to make a private source copy.</param>
        /// <param name="name">The isolated case name.</param>
        private static void AssertSourceRefresh(GeneratorRunner runner, FixtureWorkspace workspace, string name)
        {
            var refreshDirectory = workspace.CreateRefreshSources();
            var sourceFiles = Directory.GetFiles(refreshDirectory, "*.java", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var source = runner.RootDeclarations(sourceFiles, new[] { refreshDirectory });
            var input = runner.CreateCompilation(source, name);
            var first = runner.Run(input, name + "-before");
            first.AssertCompiles();
            ApiAssertions.AssertConstant(ApiAssertions.Type(first, "WidgetWrapper"), "Revision", 1, SpecialType.System_Int32);
            var widgetPath = Path.Combine(refreshDirectory, "fixtures", "Widget.java");
            var oldText = File.ReadAllText(widgetPath);
            var newText = oldText.Replace("REVISION = 1", "REVISION = 2");
            Check.True(oldText != newText, "Refresh fixture replacement must change the tracked Java constant.");
            File.WriteAllText(widgetPath, newText, new UTF8Encoding(false));
            // Replaying identical immutable compiler inputs does not invoke the generator.
            // This cached-driver limitation is explicit, not a promise of file watching.
            var cached = runner.Run(input, name + "-cached", first.Driver);
            cached.AssertCompiles();
            ApiAssertions.AssertConstant(ApiAssertions.Type(cached, "WidgetWrapper"), "Revision", 1, SpecialType.System_Int32);
            Check.True(first.GeneratedSources.SequenceEqual(cached.GeneratedSources), "An unchanged Roslyn driver/compilation may replay old external-file output without executing the generator.");

            // A fresh driver must inspect the changed Java file even when C# inputs are identical.
            var fresh = runner.Run(input, name + "-fresh");
            fresh.AssertCompiles();
            ApiAssertions.AssertConstant(ApiAssertions.Type(fresh, "WidgetWrapper"), "Revision", 2, SpecialType.System_Int32);
            Check.True(!first.GeneratedSources.SequenceEqual(fresh.GeneratedSources), "A fresh compiler driver must not retain static generator metadata from the previous Java file contents.");

            // Reparse identical C# text to model a new script compilation for the reused driver.
            // Only Java contents changed; annotation text, source paths, and input names did not.
            var nextCompilation = runner.CreateCompilation(source, name);
            Check.True(!ReferenceEquals(input.SyntaxTrees.Last(), nextCompilation.SyntaxTrees.Last()), "Rebuilt compilation must have a new immutable wrapper syntax tree.");
            Check.Equal(input.SyntaxTrees.Last().ToString(), nextCompilation.SyntaxTrees.Last().ToString(), "Rebuilt C# wrapper declaration text must remain identical");
            var refreshed = runner.Run(nextCompilation, name + "-rebuilt", cached.Driver);
            refreshed.AssertCompiles();
            ApiAssertions.AssertConstant(ApiAssertions.Type(refreshed, "WidgetWrapper"), "Revision", 2, SpecialType.System_Int32);
            Check.True(fresh.GeneratedSources.SequenceEqual(refreshed.GeneratedSources), "Reused driver on a rebuilt compilation must agree with fresh-driver extraction of the same updated Java files.");
        }

        /// <summary>
        /// Requires an invalid input array entry to produce an actionable configuration diagnostic.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="workspace">The fixture workspace containing no such input.</param>
        /// <param name="name">The isolated case name.</param>
        /// <param name="property">The attribute array property under test.</param>
        /// <param name="fileName">The deliberately nonexistent path component.</param>
        private static void AssertMissingInput(GeneratorRunner runner, FixtureWorkspace workspace, string name, string property, string fileName)
        {
            var propertyValue = property + " = " + GeneratorRunner.StringArray(new[] { Path.Combine(workspace.Root, fileName) });
            RunNegative(runner, name, SingleDeclaration(workspace, "fixtures.Widget", "public partial class InvalidWrapper", propertyValue), "AJG003");
        }

        /// <summary>
        /// Creates one minimal annotated class without introducing substitute attributes.
        /// </summary>
        /// <param name="workspace">The real fixture paths and toolchain.</param>
        /// <param name="javaName">The Java binary name to request.</param>
        /// <param name="declaration">The exact class declaration under validation.</param>
        /// <param name="overrideOptions">Optional property assignments replacing default assignments of the same name.</param>
        /// <returns>A nullable-enabled block-namespace wrapper source.</returns>
        private static string SingleDeclaration(FixtureWorkspace workspace, string javaName, string declaration, string? overrideOptions = null)
        {
            var sources = "Sources = " + GeneratorRunner.StringArray(new[] { workspace.JarPath });
            var api = "ApiLevel = " + workspace.Options.ApiLevel;
            if (overrideOptions is not null && overrideOptions.Contains("Sources =", StringComparison.Ordinal))
            {
                sources = string.Empty;
            }
            if (overrideOptions is not null && overrideOptions.Contains("ApiLevel =", StringComparison.Ordinal))
            {
                api = string.Empty;
            }
            var options = new[] { sources, api, "IncludeInheritedMembers = true", overrideOptions ?? string.Empty }.Where(value => value.Length > 0);
            return "#nullable enable\r\nusing MajdataPlay.Platform.Android;\r\nnamespace Fixtures.Wrappers\r\n{\r\n"
                + "    [JavaClass(" + GeneratorRunner.Literal(javaName) + ", " + string.Join(", ", options) + ")]\r\n"
                + "    " + declaration + "\r\n    {\r\n    }\r\n}\r\n";
        }

        /// <summary>
        /// Runs a negative case and checks the actual generator's stable diagnostic IDs.
        /// </summary>
        /// <param name="runner">The real generator driver factory.</param>
        /// <param name="name">The isolated case name.</param>
        /// <param name="source">The deliberately invalid wrapper request.</param>
        /// <param name="diagnosticIds">The accepted actionable IDs, never full message text.</param>
        private static void RunNegative(GeneratorRunner runner, string name, string source, params string[] diagnosticIds)
        {
            var input = runner.CreateCompilation(source, name);
            var inputErrors = input.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Check.True(inputErrors.Length == 0, "Negative request must remain legal C# before generator validation: " + string.Join("\r\n", inputErrors.Select(error => error.ToString())));
            var run = runner.Run(input, name);
            run.AssertDiagnostic(diagnosticIds);
        }

        /// <summary>
        /// Exercises pure managed array mapping without requiring a JVM or Unity native execution.
        /// </summary>
        private static void AssertMapArray()
        {
            var calls = 0;
            var nullResult = AndroidJni.MapArray<string?, string?>(null, value =>
            {
                calls++;
                return value;
            });
            Check.True(nullResult is null && calls == 0, "Null arrays must stay null without calling the converter.");
            var values = new string?[] { null, "first", null, "last" };
            var mapped = Check.NotNull(AndroidJni.MapArray<string?, string?>(values, value => value), "mapped nullable elements");
            Check.True(mapped.SequenceEqual(values), "Null elements and element order must be preserved.");
            Check.True(!ReferenceEquals(mapped, values), "MapArray must return a distinct result array.");
            var empty = Check.NotNull(AndroidJni.MapArray<int, int>(Array.Empty<int>(), value => value + 1), "mapped empty array");
            Check.Equal(0, empty.Length, "empty array result length");
            var primitive = Check.NotNull(AndroidJni.MapArray<sbyte, int>(new sbyte[] { -128, 0, 127 }, value => value), "mapped signed bytes");
            Check.True(primitive.SequenceEqual(new[] { -128, 0, 127 }), "Signed bytes must not be reinterpreted as unsigned values.");
            var jagged = new string?[]?[] { null, new string?[] { null, "nested" }, Array.Empty<string?>() };
            var jaggedMapped = Check.NotNull(AndroidJni.MapArray<string?[]?, string?[]?>(jagged,
                row => AndroidJni.MapArray<string?, string?>(row, value => value)), "mapped jagged array");
            Check.True(jaggedMapped[0] is null && jaggedMapped[1]![0] is null && jaggedMapped[1]![1] == "nested" && jaggedMapped[2]!.Length == 0,
                "Jagged null rows, null leaves, values, and empty rows must survive recursive mapping.");
            Check.Throws<ArgumentNullException>(() => AndroidJni.MapArray<int, int>(new[] { 1 }, null!), "null MapArray converter");
            Check.Throws<ArgumentNullException>(() => AndroidJni.MapArray<int, int>(null, null!), "null converter on null source");
            Check.Throws<InvalidOperationException>(() => AndroidJni.MapArray<int, int>(new[] { 1 }, value =>
            {
                throw new InvalidOperationException("Expected managed converter failure.");
            }), "converter exception propagation");
            var factoryCalls = 0;
            var wrapped = AndroidJni.Wrap<ManagedOnlyWrapper>(null, value =>
            {
                factoryCalls++;
                throw new InvalidOperationException("Factory must not execute for null.");
            });
            Check.True(wrapped is null && factoryCalls == 0, "Null Java references must not invoke wrapper construction.");
            Check.Throws<ArgumentNullException>(() => AndroidJni.Wrap<ManagedOnlyWrapper>(null, null!), "null wrapper factory");
        }

        /// <summary>
        /// Requires non-Android runtime guards to reject calls before accessing Unity native bindings.
        /// </summary>
        private static void AssertPlatformGuard()
        {
            Check.Throws<PlatformNotSupportedException>(() => AndroidJni.Construct("fixtures.Widget", "()V"), "Construct outside Android");
            Check.Throws<PlatformNotSupportedException>(() => AndroidJni.Call<int>(null, "fixtures.Widget", "intValue", "(I)I", 1), "generic Call outside Android");
            Check.Throws<PlatformNotSupportedException>(() => AndroidJni.Call(null, "fixtures.Widget", "consume", "(I)V", 1), "void Call outside Android");
            Check.Throws<PlatformNotSupportedException>(() => AndroidJni.GetField<int>(null, "fixtures.Widget", "mutableCount", "I"), "GetField outside Android");
            Check.Throws<ArgumentNullException>(() => new ManagedOnlyWrapper(null!), "null borrowed Java reference");
        }

        /// <summary>
        /// Supplies a type parameter for pure managed null-wrapper tests, never a fake Unity reference.
        /// </summary>
        private sealed class ManagedOnlyWrapper : JavaObject
        {
            /// <summary>
            /// Invokes the real production wrapping constructor without native reference creation.
            /// </summary>
            /// <param name="value">The reference; tests only supply null to check argument rejection.</param>
            /// <exception cref="ArgumentNullException">The supplied reference is null.</exception>
            public ManagedOnlyWrapper(UnityEngine.AndroidJavaObject value)
                : base(value, ownsReference: false)
            {
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Threading;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks pure source-analysis boundaries without compiling the source under test.</summary>
    internal static partial class Program
    {
        /// <summary>Validates unnamed packages and runtime-name collisions under a custom configured root.</summary>
        /// <param name="settings">The standard and actual Unity compiler references.</param>
        /// <param name="workspace">The exclusively owned directory used for Java source fixtures.</param>
        /// <exception cref="InvalidOperationException">Namespace placement or generated source compilation fails.</exception>
        /// <exception cref="IOException">Writing an isolated Java source fixture fails.</exception>
        private static void VerifySourceNamespaces(ValidationSettings settings, string workspace)
        {
            var directory = Path.Combine(workspace, "source-namespaces");
            Directory.CreateDirectory(directory);
            var options = new GeneratorOptions
            {
                NoSdk = true,
                RootNamespace = "NamespaceChecks.Custom.Runtime",
                OutputDirectory = Path.Combine(directory, "generated")
            };
            var runtimeNames = RuntimeEmitter.Generate(options.RootNamespace).Keys.Select(Path.GetFileNameWithoutExtension).ToArray();
            foreach (var name in runtimeNames)
            {
                var path = Path.Combine(directory, name + ".java");
                File.WriteAllText(path, "public class " + name + " { public " + name + " echo(" + name + " value) { return value; } }");
                options.Inputs.Add(path);
            }

            var packagePath = Path.Combine(directory, "Packaged.java");
            File.WriteAllText(packagePath, "package runtime; public class Packaged { public Packaged echo(Packaged value) { return value; } }");
            options.Inputs.Add(packagePath);
            var collisionDirectory = Path.Combine(directory, "runtime");
            Directory.CreateDirectory(collisionDirectory);
            var collisionPath = Path.Combine(collisionDirectory, "AndroidJni.java");
            File.WriteAllText(collisionPath, "package runtime; public class AndroidJni { public AndroidJni echo(AndroidJni value) { return value; } }");
            options.Inputs.Add(collisionPath);
            var sources = Generate(JavaApiScanner.Scan(options, CancellationToken.None), options);
            VerifyNamespaceLayout(sources, options.RootNamespace);
            foreach (var name in runtimeNames)
            {
                var declaration = CSharpSyntaxTree.ParseText(sources["Bindings/" + name + ".g.cs"]).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
                Require(declaration.Name.ToString() == options.RootNamespace + ".Bindings"
                    && declaration.Members.OfType<ClassDeclarationSyntax>().Any(type => type.Identifier.ValueText == name), "Unnamed Java types retain their names without colliding with runtime support: " + name);
            }

            var packaged = CSharpSyntaxTree.ParseText(sources["Bindings/Runtime/Packaged.g.cs"]).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
            Require(packaged.Name.ToString() == options.RootNamespace + ".Bindings.Runtime", "Java package names remain below Bindings even when they match the runtime directory");
            var legacyCollisionName = "AndroidJni_e6dbd6f1ab646fed";
            Require(sources.TryGetValue("Bindings/Runtime/" + legacyCollisionName + ".g.cs", out var collisionSource), "Existing Java runtime-package collision aliases move below the Bindings directory");
            var collision = CSharpSyntaxTree.ParseText(collisionSource!).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
            Require(collision.Name.ToString() == options.RootNamespace + ".Bindings.Runtime"
                && collision.Members.OfType<ClassDeclarationSyntax>().Any(type => type.Identifier.ValueText == legacyCollisionName), "Java runtime-package collision aliases retain their historical type names beneath Bindings");
            var javaObject = CSharpSyntaxTree.ParseText(sources["Bindings/Java/Lang/JavaObject.g.cs"]).GetRoot().DescendantNodes().OfType<NamespaceDeclarationSyntax>().Single();
            Require(javaObject.Name.ToString() == options.RootNamespace + ".Bindings.Java.Lang", "SDK-free Java BCL fallbacks use the Bindings.Java package mapping");
            CompileGenerated(settings, sources, false, false);
            CompileGenerated(settings, sources, true, false);
        }

        /// <summary>Validates unresolved imports and body skipping using owned temporary sources.</summary>
        /// <param name="settings">The SDK used as the declaration-resolution classpath.</param>
        /// <param name="workspace">The exclusively owned test directory.</param>
        private static void VerifySourceAnalysis(ValidationSettings settings, string workspace)
        {
            var source = Path.Combine(workspace, "Unresolved.java");
            File.WriteAllText(source, "package errors; public class Unresolved { public Missing value; }");
            var unresolved = CreateOptions(settings, Path.Combine(workspace, "unresolved"));
            unresolved.Inputs.Add(source);
            Expect<InvalidDataException>(() => JavaApiScanner.Scan(unresolved, CancellationToken.None), "Unresolved source names fail instead of inventing JNI descriptors");

            File.WriteAllText(source, "package errors; public class Unresolved { public external.Outer.Inner value; }");
            Expect<InvalidDataException>(() => JavaApiScanner.Scan(unresolved, CancellationToken.None), "Missing qualified source metadata cannot silently confuse nested and package names");

            var first = Path.Combine(workspace, "First.java");
            var second = Path.Combine(workspace, "Second.java");
            var consumer = Path.Combine(workspace, "Consumer.java");
            File.WriteAllText(first, "package first; public class Thing {}");
            File.WriteAllText(second, "package second; public class Thing {}");
            File.WriteAllText(consumer, "package errors; import first.*; import second.*; public class Consumer { public Thing value; }");
            var ambiguous = CreateOptions(settings, Path.Combine(workspace, "ambiguous"));
            ambiguous.Inputs.AddRange(new[] { first, second, consumer });
            Expect<InvalidDataException>(() => JavaApiScanner.Scan(ambiguous, CancellationToken.None), "Ambiguous wildcard source imports are rejected");

            var body = Path.Combine(workspace, "Body.java");
            File.WriteAllText(body, "package standalone; public class Body { public String label; public int run() { notValidJavaAtAll +++; return 1; } }");
            var pure = new GeneratorOptions { NoSdk = true, RootNamespace = "Source.Only", OutputDirectory = Path.Combine(workspace, "body") };
            pure.Inputs.Add(body);
            var api = JavaApiScanner.Scan(pure, CancellationToken.None);
            Require(api.Types["standalone.Body"].Fields[0].Descriptor == "Ljava/lang/String;", "Standalone source resolves standard Java names without JDK tools");
            Require(api.Types["standalone.Body"].Methods.Exists(method => method.Name == "run" && method.Descriptor == "()I"), "Source API extraction skips method-body semantic compilation");
            CompileGenerated(settings, Generate(api, pure), false, false);

            File.WriteAllText(body, "package standalone; public record Pair(int value, String label) {}");
            var recordApi = JavaApiScanner.Scan(pure, CancellationToken.None);
            var record = recordApi.Types["standalone.Pair"];
            Require(record.SuperName == "java.lang.Record", "Records retain their JVM superclass");
            Require(record.Methods.Exists(method => method.Name == "<init>" && method.Descriptor == "(ILjava/lang/String;)V"), "Record canonical constructor retains component descriptors");
            Require(record.Methods.Exists(method => method.Name == "label" && method.Descriptor == "()Ljava/lang/String;"), "Record component accessor is generated");
            CompileGenerated(settings, Generate(recordApi, pure), false, false);
        }
    }
}

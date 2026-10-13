using System;
using System.IO;
using System.Threading;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks pure source-analysis boundaries without compiling the source under test.</summary>
    internal static partial class Program
    {
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

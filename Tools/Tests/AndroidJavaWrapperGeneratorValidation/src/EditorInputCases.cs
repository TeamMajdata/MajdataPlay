using System;
using System.IO;
using System.Linq;
using System.Threading;
using MajdataPlay.Platform.Android.Editor;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Validates the complete SDK together with the production project and Unity Java inputs.</summary>
    internal static partial class Program
    {
        /// <summary>Scans actual Editor inputs without Java tools and compiles their complete generated bindings.</summary>
        /// <param name="settings">The pinned Unity installation and standard reference assemblies.</param>
        /// <param name="workspace">The isolated validation output directory.</param>
        private static void VerifyProjectJavaInputs(ValidationSettings settings, string workspace)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
            var request = JavaWrapperGenerationRequest.Create(projectRoot, settings.UnityData, settings.Sdk, true);
            Require(request.Inputs.Any(path => path.EndsWith("StorageAccess.java", StringComparison.Ordinal)), "Editor inputs include production Plugins/Android Java sources");
            Require(request.Inputs.Any(path => path.EndsWith("UnityPlayerActivity.java", StringComparison.Ordinal))
                && request.Inputs.Any(path => path.EndsWith("UnityPlayerGameActivity.java", StringComparison.Ordinal)), "Editor inputs include both Unity Activity Java sources");
            Require(request.Inputs.Any(path => path.EndsWith("classes.jar", StringComparison.Ordinal)), "Editor source analysis includes the Unity runtime class archive");
            var options = new GeneratorOptions
            {
                SdkDirectory = settings.Sdk,
                OutputDirectory = Path.Combine(workspace, "editor-inputs"),
                RootNamespace = request.RootNamespace,
                Documentation = DocumentationMode.None,
                Jobs = 4
            };
            options.Inputs.AddRange(request.Inputs);
            Console.WriteLine("Scanning real SDK, Plugins/Android, Unity sources, and Unity runtime dependencies...");
            var api = JavaApiScanner.Scan(options, CancellationToken.None);
            var expectedTypes = new[]
            {
                "java.lang.Object",
                "java.lang.Integer",
                "android.net.Uri",
                "com.unity3d.player.UnityPlayer",
                "com.unity3d.player.UnityPlayerActivity",
                "com.unity3d.player.UnityPlayerGameActivity",
                "net.majdata.majdataplay.MajdataPlayActivity",
                "net.majdata.majdataplay.StorageAccess",
                "net.majdata.majdataplay.CSharpOnNewIntentCallback",
                "net.majdata.majdataplay.runtime.SystemCAImporter"
            };
            Require(expectedTypes.All(name => api.Types.TryGetValue(name, out var type) && type.IsPublic), "All requested SDK, project, and Unity public API declarations are present");
            Require(api.Types["net.majdata.majdataplay.MajdataPlayActivity"].SuperName == "com.unity3d.player.UnityPlayerActivity", "Project Activity preserves its Unity source superclass");
            Require(api.Types["com.unity3d.player.UnityPlayerGameActivity"].SuperName == "com.google.androidgamesdk.GameActivity", "Unity GameActivity preserves its real dependency superclass");
            var generated = Generate(api, options);
            VerifyNamespaceLayout(generated, options.RootNamespace);
            CompileGenerated(settings, generated, false, false);
            CompileGenerated(settings, generated, true, false);
            options.Jobs = 1;
            var serialApi = JavaApiScanner.Scan(options, CancellationToken.None);
            var serial = Generate(serialApi, options);
            Require(ApiFingerprint(serialApi) == ApiFingerprint(api), "Real Editor input metadata is deterministic for one and four workers");
            Require(generated.Count == serial.Count && generated.All(item => serial[item.Key] == item.Value), "Complete Editor bindings are byte-identical for one and four workers");
            GeneratedOutput.Write(options, api.PlatformJar, generated, CancellationToken.None);
            var publishDirectory = Path.Combine(workspace, "editor-published");
            Require(JavaWrapperOutput.Publish(options.OutputDirectory, publishDirectory) == generated.Count, "Actual complete CLI output publishes through the Editor ownership bridge");
            var probeSource = Path.Combine(projectRoot, "Tools", "Tests", "AndroidStorageDeviceValidation", "DeviceJniProbe.java");
            JavaSourceParser.AddSources(api, new[] { probeSource }, CancellationToken.None);
            var consumerSources = Generate(api, options);
            var libraryBytes = VerifyProjectModeBootstrap(settings, workspace, projectRoot, consumerSources, probeSource);
            VerifyMigratedConsumers(settings, projectRoot, libraryBytes);
            Console.WriteLine("Editor input compilation/publication passed: " + generated.Count + " C# files from " + request.Inputs.Count + " additional inputs.");
        }
    }
}

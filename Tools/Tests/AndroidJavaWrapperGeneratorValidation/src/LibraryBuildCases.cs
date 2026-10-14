using System;
using System.IO;
using System.Reflection;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks fast user extension rebuilds without any Android SDK or Java input discovery.</summary>
    internal static partial class Program
    {
        /// <summary>Builds a tiny standalone source project, preserves repeated assets, and retains the last library after a bad extension.</summary>
        /// <param name="settings">The installed pinned Unity Editor whose real references are required.</param>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="IOException">Preparing or inspecting the isolated standalone project fails.</exception>
        /// <exception cref="InvalidOperationException">The build-only, partial extension, or publication contract fails.</exception>
        private static void VerifyStandaloneWrapperBuild(ValidationSettings settings, string workspace)
        {
            var root = Path.Combine(workspace, "standalone-wrapper-build");
            var pinDirectory = Path.Combine(root, "ProjectSettings");
            Directory.CreateDirectory(pinDirectory);
            var production = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
            File.Copy(Path.Combine(production, "ProjectSettings", "ProjectVersion.txt"), Path.Combine(pinDirectory, "ProjectVersion.txt"));
            var sources = Path.Combine(root, "Tools", "AndroidJavaWrapper", "src");
            Directory.CreateDirectory(sources);
            File.WriteAllText(Path.Combine(sources, "Example.g.cs"), StandaloneBindingSource);
            var extension = Path.Combine(sources, "Example.e.cs");
            File.WriteAllText(extension, StandaloneExtensionSource);
            var arguments = new[]
            {
                "--build-wrapper", root, "--unity-data", settings.UnityData,
                "--sdk", Path.Combine(root, "SDK does not exist")
            };
            var first = CaptureProjectModeCommand(arguments);
            Require(first.ExitCode == 0, "Build-only mode compiles user sources with no SDK or Java input prerequisites: " + first.Error);
            var android = Path.Combine(root, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            var library = Path.Combine(android, "MajdataPlay.Platform.Android.Runtime.dll");
            var compiled = Assembly.Load(File.ReadAllBytes(library));
            var example = compiled.GetType(EditorRootNamespace + ".Bindings.Validation.Example", true)!;
            Require((int)example.GetProperty("ManagedValue", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)! == 42, "Build-only mode includes the handwritten partial extension in the runtime DLL");
            SetEditorFixtureTimestamps(android);
            var installed = SnapshotEditorFiles(android);
            var repeated = CaptureProjectModeCommand(arguments);
            Require(repeated.ExitCode == 0, "Build-only mode repeats using an independent compiler intermediate directory: " + repeated.Error);
            RequireEditorFilesUnchanged(android, installed, "Repeated isolated compilation retains DLL and XML bytes, importer GUIDs, and timestamps");
            var metadata = File.ReadAllText(library + ".meta");
            File.WriteAllText(extension, StandaloneExtensionSource.Replace("return 42;", "return 43;", StringComparison.Ordinal));
            var changed = CaptureProjectModeCommand(arguments);
            Require(changed.ExitCode == 0, "Build-only mode publishes an edited user partial extension: " + changed.Error);
            var updated = Assembly.Load(File.ReadAllBytes(library));
            Require((int)updated.GetType(EditorRootNamespace + ".Bindings.Validation.Example", true)!.GetProperty("ManagedValue", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)! == 43
                && File.ReadAllText(library + ".meta") == metadata, "An extension rebuild updates the DLL member while retaining its Unity plugin identity");
            var working = SnapshotEditorFiles(android);
            File.AppendAllText(extension, "\r\n#error Deliberately invalid user extension\r\n");
            var rejected = CaptureProjectModeCommand(arguments);
            Require(rejected.ExitCode == 1 && !rejected.Output.Contains("Published ", StringComparison.Ordinal), "Build-only mode rejects a bad user extension before DLL publication");
            RequireEditorFilesUnchanged(android, working, "Failed build-only compilation retains the working DLL, XML, metadata, and timestamps");
        }

        /// <summary>Provides the small generated declaration used to validate build-only behavior.</summary>
        private const string StandaloneBindingSource = @"#nullable enable
namespace MajdataPlay.Platform.Android.Runtime.Bindings.Validation
{
    /// <summary>Represents a generated Java wrapper declaration.</summary>
    public partial class Example
    {
    }
}
";

        /// <summary>Provides the editable user extension compiled together with the generated declaration.</summary>
        private const string StandaloneExtensionSource = @"#nullable enable
namespace MajdataPlay.Platform.Android.Runtime.Bindings.Validation
{
    /// <summary>Extends the generated wrapper with a managed convenience member.</summary>
    public partial class Example
    {
        /// <summary>Returns the known value used to inspect the independent library.</summary>
        public static int ManagedValue
        {
            get
            {
                return 42;
            }
        }
    }
}
";
    }
}

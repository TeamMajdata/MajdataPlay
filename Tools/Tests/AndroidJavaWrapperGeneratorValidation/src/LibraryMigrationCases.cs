using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks retirement of the former Unity sources before relying on the compiled wrapper plugin.</summary>
    internal static partial class Program
    {
        /// <summary>Checks both legacy roots, metadata pairing, user files, and conflicts before source retirement.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="IOException">Preparing or inspecting an isolated ownership fixture fails.</exception>
        /// <exception cref="InvalidOperationException">Source ownership or metadata retention regresses.</exception>
        private static void VerifyLegacyLibrarySourceCleanup(string workspace)
        {
            var root = Path.Combine(workspace, "library-source-cleanup");
            foreach (var name in new[] { "Runtime", "Generated" })
            {
                var output = CreateLegacyLibraryFixture(root, name);
                File.WriteAllText(Path.Combine(output, "Bindings", "Shared", "Handwritten.e.cs"), "// unowned user extension\r\n");
                File.WriteAllText(Path.Combine(output, "Bindings", "Shared", "Handwritten.e.cs.meta"), "user extension GUID");
                File.WriteAllText(output + ".meta", "former root GUID");
            }
            var android = Path.Combine(root, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            var before = SnapshotEditorFiles(android);
            using (var cleanup = LegacyWrapperSources.Prepare(root))
            {
                RequireEditorFilesUnchanged(android, before, "Legacy source retirement preflight does not modify Unity assets");
                cleanup.Cleanup();
            }
            foreach (var name in new[] { "Runtime", "Generated" })
            {
                var output = Path.Combine(android, name);
                Require(!File.Exists(Path.Combine(output, "AndroidJni.cs")) && !File.Exists(Path.Combine(output, "AndroidJni.cs.meta"))
                    && !File.Exists(Path.Combine(output, EditorManifestName)) && !File.Exists(Path.Combine(output, EditorManifestName + ".meta")), "DLL migration removes paired owned helpers and ownership manifests: " + name);
                Require(!Directory.Exists(Path.Combine(output, "Bindings", "Empty")) && !File.Exists(Path.Combine(output, "Bindings", "Empty.meta")), "DLL migration prunes empty owned folders together with their metadata: " + name);
                Require(File.ReadAllText(Path.Combine(output, "Bindings", "Shared", "Handwritten.e.cs")) == "// unowned user extension\r\n"
                    && File.ReadAllText(Path.Combine(output, "Bindings", "Shared", "Handwritten.e.cs.meta")) == "user extension GUID"
                    && File.ReadAllText(Path.Combine(output, "Bindings", "Shared.meta")) == "owned folder GUID"
                    && File.ReadAllText(output + ".meta") == "former root GUID", "DLL migration retains user sources and identities of nonempty or root folders: " + name);
            }

            foreach (var mode in new[] { "modified-source", "readonly-source-meta", "readonly-manifest-meta", "readonly-folder-meta", "source-after-preflight", "manifest-after-preflight", "metadata-after-preflight" })
            {
                var conflictRoot = Path.Combine(workspace, "library-cleanup-" + mode);
                var output = CreateLegacyLibraryFixture(conflictRoot, "Runtime");
                string? readOnly = null;
                LegacyWrapperSources? cleanup = null;
                try
                {
                    if (mode.EndsWith("after-preflight", StringComparison.Ordinal))
                    {
                        cleanup = LegacyWrapperSources.Prepare(conflictRoot);
                    }
                    switch (mode)
                    {
                        case "modified-source":
                        case "source-after-preflight":
                            File.AppendAllText(Path.Combine(output, "AndroidJni.cs"), "// user changes\r\n");
                            break;
                        case "manifest-after-preflight":
                            File.AppendAllText(Path.Combine(output, EditorManifestName), "\r\n");
                            break;
                        case "metadata-after-preflight":
                            File.AppendAllText(Path.Combine(output, "Bindings", "Empty.meta"), " changed GUID");
                            break;
                        case "readonly-source-meta":
                            readOnly = Path.Combine(output, "AndroidJni.cs.meta");
                            break;
                        case "readonly-manifest-meta":
                            readOnly = Path.Combine(output, EditorManifestName + ".meta");
                            break;
                        case "readonly-folder-meta":
                            readOnly = Path.Combine(output, "Bindings", "Empty.meta");
                            break;
                    }
                    if (readOnly != null)
                    {
                        File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
                    }
                    var unchanged = SnapshotEditorFiles(output);
                    Expect<IOException>(() =>
                    {
                        if (cleanup != null)
                        {
                            cleanup.Cleanup();
                        }
                        else
                        {
                            using var rejected = LegacyWrapperSources.Prepare(conflictRoot);
                        }
                    }, "DLL migration rejects conflicting owned sources or metadata: " + mode);
                    RequireEditorFilesUnchanged(output, unchanged, "Rejected DLL source retirement preserves every legacy file: " + mode);
                }
                finally
                {
                    cleanup?.Dispose();
                    if (readOnly != null && File.Exists(readOnly))
                    {
                        File.SetAttributes(readOnly, File.GetAttributes(readOnly) & ~FileAttributes.ReadOnly);
                    }
                }
            }
        }

        /// <summary>Creates a small generator-owned Unity source tree with paired source, folder, and manifest metadata.</summary>
        /// <param name="projectRoot">The exclusively owned scratch Unity project.</param>
        /// <param name="directoryName">The supported former Runtime or Generated output directory.</param>
        /// <returns>The absolute former wrapper output directory.</returns>
        /// <exception cref="IOException">A fixture source or metadata file cannot be written.</exception>
        private static string CreateLegacyLibraryFixture(string projectRoot, string directoryName)
        {
            var output = Path.Combine(projectRoot, "Assets", "Plugins", "MajdataPlay", "Platform", "Android", directoryName);
            var sources = new Dictionary<string, string>
            {
                { "AndroidJni.cs", "// previously generated helper\r\n" },
                { "Bindings/Empty/Widget.g.cs", "// previously generated wrapper\r\n" },
                { "Bindings/Shared/Kept.g.cs", "// previously generated neighboring wrapper\r\n" }
            };
            GeneratedOutput.Write(new GeneratorOptions { RootNamespace = EditorRootNamespace, OutputDirectory = output }, null, sources, CancellationToken.None);
            foreach (var path in sources.Keys.Append(EditorManifestName))
            {
                File.WriteAllText(Path.Combine(output, path + ".meta"), "owned source GUID");
            }
            foreach (var path in new[] { "Bindings", "Bindings/Empty", "Bindings/Shared" })
            {
                File.WriteAllText(Path.Combine(output, path + ".meta"), "owned folder GUID");
            }
            return output;
        }
    }
}

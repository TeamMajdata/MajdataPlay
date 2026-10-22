using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MajdataPlay.Platform.Android.Editor;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    /// <summary>Checks publication of CLI output into Unity assets without invoking Unity.</summary>
    internal static partial class Program
    {
        /// <summary>Names the CLI ownership file used by the Editor publication fixtures.</summary>
        private const string EditorManifestName = "java-wrappers.manifest.json";

        /// <summary>Identifies the isolated namespace requested by the Editor CLI integration.</summary>
        private const string EditorRootNamespace = "MajdataPlay.Platform.Android.Runtime";

        /// <summary>Validates source publication, Unity metadata retention, and failures before any destination changes.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">A publication regression contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        private static void VerifyEditorOutput(string workspace)
        {
            VerifyProjectModeArguments(workspace);
            VerifyLegacyLibrarySourceCleanup(workspace);
            VerifyEditorPublicationLifecycle(workspace);
            VerifyEditorPublicationRecovery(workspace);
            VerifyEditorNamespaceMigration(workspace);
            VerifyEditorMigrationConflicts(workspace);
            VerifyEditorOutputRootMigration(workspace);
            VerifyEditorOutputRootMigrationConflicts(workspace);
            VerifyEditorPublicationConflicts(workspace);
            VerifyEditorPublicationCorruption(workspace);
            VerifyEditorPublicationLongPaths(workspace);
        }

        /// <summary>Checks migration from the former Generated root into Runtime for both supported directory layouts.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">Cross-root publication or asset preservation fails.</exception>
        /// <exception cref="IOException">Preparing, publishing, or inspecting a fixture fails.</exception>
        private static void VerifyEditorOutputRootMigration(string workspace)
        {
            foreach (var layout in new[] { "previous-package", "current-package", "current-namespace" })
            {
                var root = Path.Combine(workspace, "editor-root-migration-" + layout);
                var output = Path.Combine(root, "Runtime");
                var former = Path.Combine(root, "Generated");
                var namespaceLayout = layout == "current-namespace";
                var paths = new Dictionary<string, string>
                {
                    { namespaceLayout ? "AndroidJni.cs" : "Runtime/AndroidJni.cs", "AndroidJni.cs" },
                    { namespaceLayout ? "Bindings/Z/Late.g.cs" : "Z/Late.g.cs", "Bindings/Z/Late.g.cs" },
                    { namespaceLayout ? "Bindings/Bindings/Z/Late.g.cs" : "Bindings/Z/Late.g.cs", "Bindings/Bindings/Z/Late.g.cs" },
                    { namespaceLayout ? "Bindings/Bindings/OnlyExisting.g.cs" : "Bindings/OnlyExisting.g.cs", "Bindings/Bindings/OnlyExisting.g.cs" },
                    { namespaceLayout ? "Bindings/DefaultPackage.g.cs" : "DefaultPackage.g.cs", "Bindings/DefaultPackage.g.cs" },
                    { namespaceLayout ? "Bindings/Missing.g.cs" : "Missing.g.cs", "Bindings/Missing.g.cs" }
                };
                var previousSources = new Dictionary<string, string>();
                var updatedSources = new Dictionary<string, string>();
                foreach (var path in paths)
                {
                    var source = "// unchanged generated identity for " + path.Value + "\r\n";
                    previousSources.Add(path.Key, source);
                    updatedSources.Add(path.Value, source);
                }

                var stalePath = namespaceLayout ? "Bindings/Stale.g.cs" : "Stale.g.cs";
                previousSources.Add(stalePath, "// stale generated binding\r\n");
                updatedSources["AndroidJni.cs"] = "// changed JNI helper under the runtime namespace\r\n";
                updatedSources.Add("Bindings/OnlyExisting.g.cs", "// new unnamed-package binding with no former source identity\r\n");
                var previous = new GeneratorOptions
                {
                    NoSdk = true,
                    RootNamespace = layout == "previous-package" ? EditorRootNamespace + ".Bindings" : EditorRootNamespace,
                    OutputDirectory = former
                };
                GeneratedOutput.Write(previous, null, previousSources, CancellationToken.None);
                foreach (var source in previousSources)
                {
                    File.WriteAllText(Path.Combine(former, source.Key) + ".meta", "fileFormatVersion: 2\r\nguid: " + Guid.NewGuid().ToString("N") + "\r\n");
                }

                File.WriteAllText(Path.Combine(former, EditorManifestName + ".meta"), "manifest asset GUID");
                File.WriteAllText(Path.Combine(former, "LegacyExtension.cs"), "// unowned extension remains in the former root\r\n");
                File.WriteAllText(Path.Combine(former, "LegacyExtension.cs.meta"), "unowned former extension GUID");
                File.WriteAllText(former + ".meta", "former root folder GUID");
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "Handwritten.cs"), "// unowned runtime extension\r\n");
                File.WriteAllText(Path.Combine(output, "Handwritten.cs.meta"), "unowned runtime extension GUID");
                File.Delete(Path.Combine(former, namespaceLayout ? "Bindings/Missing.g.cs" : "Missing.g.cs"));
                SetEditorFixtureTimestamps(root);
                var before = SnapshotEditorFiles(former);
                var handwrittenBefore = SnapshotEditorFiles(output);
                var folderBytes = File.ReadAllBytes(former + ".meta");
                var folderTimestamp = File.GetLastWriteTimeUtc(former + ".meta");
                var stage = WriteEditorStaging(root, "stage", updatedSources);
                Require(JavaWrapperOutput.Publish(stage, output, former) == updatedSources.Count, "Editor moves former output ownership into Runtime for layout " + layout);
                foreach (var path in paths)
                {
                    var source = Path.Combine(output, path.Value);
                    var oldMetadata = before[(path.Key + ".meta").Replace('/', Path.DirectorySeparatorChar)];
                    Require(File.ReadAllText(source) == updatedSources[path.Value], "Cross-root migration publishes the generated source: " + path.Value);
                    Require(File.ReadAllBytes(source + ".meta").SequenceEqual(oldMetadata.Bytes)
                        && File.GetLastWriteTimeUtc(source + ".meta") == oldMetadata.Modified, "Cross-root migration preserves source metadata identity: " + path.Value);
                    Require(!File.Exists(Path.Combine(former, path.Key)) && !File.Exists(Path.Combine(former, path.Key + ".meta")), "Cross-root migration removes only the migrated old paths: " + path.Key);
                    if (path.Value != "AndroidJni.cs" && path.Value != "Bindings/Missing.g.cs")
                    {
                        Require(File.GetLastWriteTimeUtc(source) == before[path.Key.Replace('/', Path.DirectorySeparatorChar)].Modified, "Cross-root migration preserves an unchanged source timestamp: " + path.Value);
                    }
                }

                Require(!File.Exists(Path.Combine(former, stalePath)) && !File.Exists(Path.Combine(former, stalePath + ".meta")), "Cross-root migration removes stale owned sources and metadata");
                Require(File.ReadAllText(Path.Combine(output, "Bindings/OnlyExisting.g.cs")) == updatedSources["Bindings/OnlyExisting.g.cs"]
                    && !File.Exists(Path.Combine(output, "Bindings/OnlyExisting.g.cs.meta")), "A new unnamed-package binding never consumes the old bindings-package source or GUID");
                Require(!File.Exists(Path.Combine(former, EditorManifestName)) && !File.Exists(Path.Combine(former, EditorManifestName + ".meta")), "Cross-root migration removes the former ownership manifest and metadata paths");
                Require(File.ReadAllBytes(Path.Combine(output, EditorManifestName)).SequenceEqual(File.ReadAllBytes(Path.Combine(stage, EditorManifestName))), "Cross-root migration publishes the new ownership manifest");
                Require(File.ReadAllBytes(Path.Combine(output, EditorManifestName + ".meta")).SequenceEqual(before[EditorManifestName + ".meta"].Bytes)
                    && File.GetLastWriteTimeUtc(Path.Combine(output, EditorManifestName + ".meta")) == before[EditorManifestName + ".meta"].Modified, "Cross-root migration preserves the manifest Unity identity");
                foreach (var relativePath in new[] { "LegacyExtension.cs", "LegacyExtension.cs.meta" })
                {
                    var path = Path.Combine(former, relativePath);
                    Require(File.ReadAllBytes(path).SequenceEqual(before[relativePath].Bytes)
                        && File.GetLastWriteTimeUtc(path) == before[relativePath].Modified, "Cross-root migration preserves unowned former assets: " + relativePath);
                }

                foreach (var file in handwrittenBefore)
                {
                    var path = Path.Combine(output, file.Key);
                    Require(File.ReadAllBytes(path).SequenceEqual(file.Value.Bytes)
                        && File.GetLastWriteTimeUtc(path) == file.Value.Modified, "Cross-root migration preserves unowned Runtime assets: " + file.Key);
                }

                Require(Directory.Exists(former) && File.ReadAllBytes(former + ".meta").SequenceEqual(folderBytes)
                    && File.GetLastWriteTimeUtc(former + ".meta") == folderTimestamp, "Cross-root migration preserves the former root folder and its GUID");
                var migrated = SnapshotEditorFiles(output);
                var formerAfter = SnapshotEditorFiles(former);
                JavaWrapperOutput.Publish(stage, output, former);
                RequireEditorFilesUnchanged(output, migrated, "Repeated publication after moving the output root");
                RequireEditorFilesUnchanged(former, formerAfter, "Repeated publication preserves unowned former output assets");
            }
        }

        /// <summary>Checks that conflicts and overlapping roots reject output migration before either directory changes.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">A cross-root ownership or preflight contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        private static void VerifyEditorOutputRootMigrationConflicts(string workspace)
        {
            var modes = new[]
            {
                "target-source", "target-metadata", "target-manifest-metadata", "modified-source", "both-owned",
                "readonly-source", "readonly-metadata", "readonly-manifest", "readonly-manifest-metadata", "invalid-former-manifest"
            };
            foreach (var mode in modes)
            {
                var root = Path.Combine(workspace, "editor-root-migration-conflict-" + mode);
                var output = Path.Combine(root, "Runtime");
                var former = WriteEditorStaging(root, "Generated", new Dictionary<string, string>
                {
                    { "Runtime/AndroidJni.cs", "// previous JNI helper\r\n" },
                    { "Z/Late.g.cs", "// previous late binding\r\n" },
                    { "Stale.g.cs", "// previous stale binding\r\n" }
                });
                File.WriteAllText(Path.Combine(former, "Runtime/AndroidJni.cs.meta"), "previous helper GUID");
                File.WriteAllText(Path.Combine(former, "Z/Late.g.cs.meta"), "previous binding GUID");
                File.WriteAllText(Path.Combine(former, "Stale.g.cs.meta"), "previous stale GUID");
                File.WriteAllText(Path.Combine(former, EditorManifestName + ".meta"), "previous manifest GUID");
                Directory.CreateDirectory(Path.Combine(output, "Bindings", "Z"));
                File.WriteAllText(Path.Combine(output, "Handwritten.cs"), "// handwritten runtime source\r\n");
                string? readOnlyPath = null;
                switch (mode)
                {
                    case "target-source":
                        File.WriteAllText(Path.Combine(output, "Bindings/Z/Late.g.cs"), "// handwritten target source\r\n");
                        break;
                    case "target-metadata":
                        File.WriteAllText(Path.Combine(output, "Bindings/Z/Late.g.cs.meta"), "unowned target source GUID");
                        break;
                    case "target-manifest-metadata":
                        File.WriteAllText(Path.Combine(output, EditorManifestName + ".meta"), "unowned target manifest GUID");
                        break;
                    case "modified-source":
                        File.AppendAllText(Path.Combine(former, "Z/Late.g.cs"), "// user modification");
                        break;
                    case "both-owned":
                        WriteEditorStaging(root, "Runtime", new Dictionary<string, string> { { "AlreadyOwned.cs", "// already owned runtime source\r\n" } });
                        break;
                    case "readonly-source":
                        readOnlyPath = Path.Combine(former, "Z/Late.g.cs");
                        break;
                    case "readonly-metadata":
                        readOnlyPath = Path.Combine(former, "Z/Late.g.cs.meta");
                        break;
                    case "readonly-manifest":
                        readOnlyPath = Path.Combine(former, EditorManifestName);
                        break;
                    case "readonly-manifest-metadata":
                        readOnlyPath = Path.Combine(former, EditorManifestName + ".meta");
                        break;
                    case "invalid-former-manifest":
                        MutateEditorManifest(former, manifest =>
                        {
                            manifest.RootNamespace = EditorRootNamespace + ".Unrecognized";
                        });
                        break;
                }

                SetEditorFixtureTimestamps(root);
                if (readOnlyPath != null)
                {
                    File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
                }

                var stage = WriteEditorStaging(root, "stage", new Dictionary<string, string>
                {
                    { "AndroidJni.cs", "// changed helper\r\n" },
                    { "Bindings/Z/Late.g.cs", "// changed binding\r\n" }
                });
                var outputBefore = SnapshotEditorFiles(output);
                var formerBefore = SnapshotEditorFiles(former);
                var directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                try
                {
                    Expect<IOException>(() => JavaWrapperOutput.Publish(stage, output, former), "Editor rejects output-root migration conflict: " + mode);
                    RequireEditorFilesUnchanged(output, outputBefore, "Rejected output-root migration preserves Runtime: " + mode);
                    RequireEditorFilesUnchanged(former, formerBefore, "Rejected output-root migration preserves Generated: " + mode);
                    Require(Directory.GetDirectories(root, "*", SearchOption.AllDirectories).SequenceEqual(directories), "Rejected output-root migration preserves the directory tree: " + mode);
                }
                finally
                {
                    if (readOnlyPath != null)
                    {
                        File.SetAttributes(readOnlyPath, FileAttributes.Normal);
                    }
                }
            }

            var overlapRoot = Path.Combine(workspace, "editor-root-migration-overlap");
            var validStage = WriteEditorStaging(overlapRoot, "stage", new Dictionary<string, string> { { "AndroidJni.cs", "// generated JNI helper\r\n" } });
            var validOutput = Path.Combine(overlapRoot, "Runtime");
            Directory.CreateDirectory(validOutput);
            var stageBefore = SnapshotEditorFiles(validStage);
            var outputSnapshot = SnapshotEditorFiles(validOutput);
            foreach (var invalidFormer in new[]
            {
                validStage, validOutput, Path.Combine(validStage, "child"), Path.Combine(validOutput, "child"), overlapRoot
            })
            {
                Expect<IOException>(() => JavaWrapperOutput.Publish(validStage, validOutput, invalidFormer), "Editor rejects a former output root overlapping staging or Runtime: " + invalidFormer);
                RequireEditorFilesUnchanged(validStage, stageBefore, "Rejected former output overlap preserves staging");
                RequireEditorFilesUnchanged(validOutput, outputSnapshot, "Rejected former output overlap preserves Runtime");
            }
        }

        /// <summary>Checks publication through package paths longer than the Windows legacy MAX_PATH boundary.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        private static void VerifyEditorPublicationLongPaths(string workspace)
        {
            var root = Path.Combine(workspace, "editor-long-paths", new string('a', 70), new string('b', 70));
            var output = Path.Combine(root, "Assets");
            var relative = "Android/Accessibilityservice/AccessibilityButtonController.g.cs";
            var sources = new Dictionary<string, string> { { relative, "// generated long-path version one\r\n" } };
            var stage = WriteEditorStaging(root, "stage-one", sources);
            var destination = Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar));
            Require(destination.Length > 260, "Editor long-path fixture exceeds the legacy Windows path boundary");
            Require(JavaWrapperOutput.Publish(stage, output) == 1 && File.ReadAllText(destination) == sources[relative], "Editor publication can read and write deeply nested package files");
            File.WriteAllText(destination + ".meta", "preserved long-path asset GUID");
            sources[relative] = "// generated long-path version two\r\n";
            stage = WriteEditorStaging(root, "stage-two", sources);
            Require(JavaWrapperOutput.Publish(stage, output) == 1 && File.ReadAllText(destination) == sources[relative], "Editor publication updates long-path sources");
            Require(File.ReadAllText(destination + ".meta") == "preserved long-path asset GUID", "Long-path source updates preserve metadata");
            stage = WriteEditorStaging(root, "stage-empty", new Dictionary<string, string>());
            Require(JavaWrapperOutput.Publish(stage, output) == 0 && !File.Exists(destination) && !File.Exists(destination + ".meta"), "Editor publication removes stale long-path sources and metadata");
        }

        /// <summary>Checks first publication, unchanged timestamps, source replacement, stale cleanup, and Unity GUID retention.</summary>
        /// <param name="workspace">The exclusively owned validation directory.</param>
        /// <exception cref="InvalidOperationException">A publication lifecycle contract fails.</exception>
        /// <exception cref="IOException">Creating, publishing, or reading a fixture fails.</exception>
        private static void VerifyEditorPublicationLifecycle(string workspace)
        {
            var root = Path.Combine(workspace, "editor-output-lifecycle");
            var output = Path.Combine(root, "assets");
            var initialSources = new Dictionary<string, string>
            {
                { "android/os/AChange.cs", "// generated version one\r\n" },
                { "android/os/Keep.cs", "// unchanged generated source\r\n" },
                { "only-stale/Stale.cs", "// stale generated source\r\n" },
                { "only-missing/Missing.cs", "// manually removed generated source\r\n" }
            };
            var initial = WriteEditorStaging(root, "initial-stage", initialSources);
            File.WriteAllText(Path.Combine(initial, "Ignored.cs"), "// not in the CLI manifest");
            File.WriteAllText(Path.Combine(initial, "Ignored.cs.meta"), "unlisted staging metadata");
            File.WriteAllText(Path.Combine(initial, "Notes.txt"), "unlisted staging content");
            Directory.CreateDirectory(Path.Combine(output, "android", "os"));
            var handwritten = Path.Combine(output, "android", "os", "Extensions.cs");
            var unrelated = Path.Combine(output, "Notes.txt");
            var orphanMetadata = Path.Combine(output, "Handwritten.cs.meta");
            var folderMetadata = Path.Combine(output, "only-stale.meta");
            File.WriteAllText(handwritten, "// handwritten partial extension\r\n");
            File.WriteAllText(handwritten + ".meta", "handwritten extension GUID");
            File.WriteAllText(unrelated, "handwritten non-source asset");
            File.WriteAllText(orphanMetadata, "unrelated handwritten metadata");
            File.WriteAllText(folderMetadata, "Unity folder GUID");

            Require(JavaWrapperOutput.Publish(initial, output) == initialSources.Count, "Editor first publication returns the manifest C# count");
            foreach (var source in initialSources)
            {
                var destination = Path.Combine(output, source.Key);
                Require(File.ReadAllBytes(destination).SequenceEqual(File.ReadAllBytes(Path.Combine(initial, source.Key))), "Editor copies the exact verified CLI bytes: " + source.Key);
                Require(!File.Exists(destination + ".meta"), "Editor publication leaves creation of new source GUIDs to Unity: " + source.Key);
                File.WriteAllText(destination + ".meta", "fileFormatVersion: 2\r\nguid: " + Guid.NewGuid().ToString("N") + "\r\n");
            }

            Require(!File.Exists(Path.Combine(output, "Ignored.cs")) && !File.Exists(Path.Combine(output, "Ignored.cs.meta")), "Editor copies only C# paths listed by the CLI manifest");
            Require(File.ReadAllText(unrelated) == "handwritten non-source asset", "Editor preserves unrelated assets instead of copying staging extras");
            Require(File.ReadAllBytes(Path.Combine(output, EditorManifestName)).SequenceEqual(File.ReadAllBytes(Path.Combine(initial, EditorManifestName))), "Editor preserves the CLI manifest bytes and provenance");
            SetEditorFixtureTimestamps(output);
            var unchanged = SnapshotEditorFiles(output);
            Require(JavaWrapperOutput.Publish(initial, output) == initialSources.Count, "Editor repeat publication counts unchanged sources");
            RequireEditorFilesUnchanged(output, unchanged, "Identical Editor publication");

            var keepPath = Path.Combine(output, "android/os/Keep.cs");
            var keepMetadata = File.ReadAllBytes(keepPath + ".meta");
            var keepMetadataTimestamp = File.GetLastWriteTimeUtc(keepPath + ".meta");
            File.Delete(keepPath);
            Require(JavaWrapperOutput.Publish(initial, output) == initialSources.Count, "Editor restores a missing source still present in the new manifest");
            Require(File.ReadAllText(keepPath) == initialSources["android/os/Keep.cs"], "Editor restores the exact generated source");
            Require(File.ReadAllBytes(keepPath + ".meta").SequenceEqual(keepMetadata)
                && File.GetLastWriteTimeUtc(keepPath + ".meta") == keepMetadataTimestamp, "Restoring a still-generated source reuses its existing Unity GUID and metadata timestamp");
            File.SetLastWriteTimeUtc(keepPath, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            var keepTimestamp = File.GetLastWriteTimeUtc(keepPath);
            var changePath = Path.Combine(output, "android/os/AChange.cs");
            var changeMetadata = File.ReadAllBytes(changePath + ".meta");
            var changeMetadataTimestamp = File.GetLastWriteTimeUtc(changePath + ".meta");
            var handwrittenBytes = File.ReadAllBytes(handwritten);
            var handwrittenMetadata = File.ReadAllBytes(handwritten + ".meta");
            var handwrittenTimestamp = File.GetLastWriteTimeUtc(handwritten);
            var handwrittenMetadataTimestamp = File.GetLastWriteTimeUtc(handwritten + ".meta");
            File.Delete(Path.Combine(output, "only-missing/Missing.cs"));

            var updatedSources = new Dictionary<string, string>
            {
                { "android/os/AChange.cs", "// generated version two\r\n" },
                { "android/os/Keep.cs", initialSources["android/os/Keep.cs"] },
                { "android/os/New.cs", "// new generated source\r\n" }
            };
            var updated = WriteEditorStaging(root, "updated-stage", updatedSources);
            Require(JavaWrapperOutput.Publish(updated, output) == updatedSources.Count, "Editor publishes the complete new manifest source count");
            Require(File.ReadAllText(changePath) == updatedSources["android/os/AChange.cs"], "Editor replaces an unchanged owned source with the new generated content");
            Require(File.ReadAllBytes(changePath + ".meta").SequenceEqual(changeMetadata)
                && File.GetLastWriteTimeUtc(changePath + ".meta") == changeMetadataTimestamp, "Replacing an owned source retains its Unity GUID and metadata timestamp");
            Require(File.GetLastWriteTimeUtc(keepPath) == keepTimestamp, "An unchanged source retains its mtime during a publication that changes other files");
            Require(!File.Exists(Path.Combine(output, "only-stale/Stale.cs"))
                && !File.Exists(Path.Combine(output, "only-stale/Stale.cs.meta")), "Editor removes an unchanged stale owned source together with its Unity metadata");
            Require(!File.Exists(Path.Combine(output, "only-missing/Missing.cs.meta")), "Editor removes stale manifest-owned metadata even after the user removed its source");
            Require(Directory.Exists(Path.Combine(output, "only-stale")) && Directory.Exists(Path.Combine(output, "only-missing")), "Editor leaves empty generated directories for Unity to handle");
            Require(File.ReadAllBytes(handwritten).SequenceEqual(handwrittenBytes)
                && File.GetLastWriteTimeUtc(handwritten) == handwrittenTimestamp
                && File.ReadAllBytes(handwritten + ".meta").SequenceEqual(handwrittenMetadata)
                && File.GetLastWriteTimeUtc(handwritten + ".meta") == handwrittenMetadataTimestamp, "Editor preserves handwritten sources and their Unity metadata");
            Require(File.ReadAllText(orphanMetadata) == "unrelated handwritten metadata"
                && File.ReadAllText(folderMetadata) == "Unity folder GUID", "Editor removes metadata only for stale manifest-owned C# paths");
            Require(File.ReadAllBytes(Path.Combine(output, EditorManifestName)).SequenceEqual(File.ReadAllBytes(Path.Combine(updated, EditorManifestName))), "Editor updates ownership after source replacement and stale cleanup");
            Require(!File.Exists(Path.Combine(output, "android/os/New.cs.meta")), "Editor does not synthesize a GUID for a newly published source");
        }

        /// <summary>Checks that both previous manifest versions migrate generated paths without losing Unity asset identity.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">Namespace migration or GUID preservation fails.</exception>
        /// <exception cref="IOException">Preparing, publishing, or inspecting a fixture fails.</exception>
        private static void VerifyEditorNamespaceMigration(string workspace)
        {
            foreach (var previousRoot in new[] { EditorRootNamespace, EditorRootNamespace + ".Bindings" })
            {
                var root = Path.Combine(workspace, "editor-namespace-migration-" + previousRoot);
                var output = Path.Combine(root, "assets");
                var previousSources = new Dictionary<string, string>
                {
                    { "Runtime/AndroidJni.cs", "// previous JNI helper\r\n" },
                    { "Runtime/IJavaObject.cs", "// previous Java object interface\r\n" },
                    { "Runtime/JavaInvocationException.cs", "// previous JNI exception\r\n" },
                    { "Runtime/JavaObjectReference.cs", "// previous Java reference\r\n" },
                    { "Android/Net/Uri.g.cs", "// unchanged Android binding\r\n" },
                    { "Java/Lang/Object.g.cs", "// previous Java binding\r\n" },
                    { "DefaultPackage.g.cs", "// unchanged default-package binding\r\n" },
                    { "Missing.g.cs", "// removed source with retained Unity metadata\r\n" },
                    { "Android/Stale.g.cs", "// stale owned binding\r\n" }
                };
                var previous = new GeneratorOptions
                {
                    NoSdk = true,
                    RootNamespace = previousRoot,
                    OutputDirectory = output
                };
                GeneratedOutput.Write(previous, null, previousSources, CancellationToken.None);
                foreach (var source in previousSources)
                {
                    File.WriteAllText(Path.Combine(output, source.Key) + ".meta", "fileFormatVersion: 2\r\nguid: " + Guid.NewGuid().ToString("N") + "\r\n");
                }

                var handwritten = Path.Combine(output, "Extensions.cs");
                File.WriteAllText(handwritten, "// handwritten extension retained during migration\r\n");
                File.WriteAllText(handwritten + ".meta", "handwritten extension GUID");
                File.WriteAllText(Path.Combine(output, "Runtime.meta"), "old runtime folder GUID");
                File.Delete(Path.Combine(output, "Missing.g.cs"));
                SetEditorFixtureTimestamps(output);
                var before = SnapshotEditorFiles(output);
                var updatedSources = new Dictionary<string, string>
                {
                    { "AndroidJni.cs", "namespace MajdataPlay.Platform.Android.Runtime { }\r\n" },
                    { "IJavaObject.cs", "// current Java object interface\r\n" },
                    { "JavaInvocationException.cs", "// current JNI exception\r\n" },
                    { "JavaObjectReference.cs", "// current Java reference\r\n" },
                    { "Bindings/Android/Net/Uri.g.cs", previousSources["Android/Net/Uri.g.cs"] },
                    { "Bindings/Java/Lang/Object.g.cs", "// current Java binding\r\n" },
                    { "Bindings/DefaultPackage.g.cs", previousSources["DefaultPackage.g.cs"] },
                    { "Bindings/Missing.g.cs", previousSources["Missing.g.cs"] }
                };
                var paths = new Dictionary<string, string>
                {
                    { "Runtime/AndroidJni.cs", "AndroidJni.cs" },
                    { "Runtime/IJavaObject.cs", "IJavaObject.cs" },
                    { "Runtime/JavaInvocationException.cs", "JavaInvocationException.cs" },
                    { "Runtime/JavaObjectReference.cs", "JavaObjectReference.cs" },
                    { "Android/Net/Uri.g.cs", "Bindings/Android/Net/Uri.g.cs" },
                    { "Java/Lang/Object.g.cs", "Bindings/Java/Lang/Object.g.cs" },
                    { "DefaultPackage.g.cs", "Bindings/DefaultPackage.g.cs" },
                    { "Missing.g.cs", "Bindings/Missing.g.cs" }
                };
                var stage = WriteEditorStaging(root, "updated-stage", updatedSources);
                Require(JavaWrapperOutput.Publish(stage, output) == updatedSources.Count, "Editor migrates generated paths from manifest root " + previousRoot);
                foreach (var path in paths)
                {
                    var previousMetadata = before[(path.Key + ".meta").Replace('/', Path.DirectorySeparatorChar)];
                    var metadata = Path.Combine(output, path.Value + ".meta");
                    Require(File.ReadAllText(Path.Combine(output, path.Value)) == updatedSources[path.Value], "Directory migration publishes the new namespace source: " + path.Value);
                    Require(File.ReadAllBytes(metadata).SequenceEqual(previousMetadata.Bytes)
                        && File.GetLastWriteTimeUtc(metadata) == previousMetadata.Modified, "Directory migration retains the Unity GUID and metadata timestamp: " + path.Value);
                    Require(!File.Exists(Path.Combine(output, path.Key)) && !File.Exists(Path.Combine(output, path.Key + ".meta")), "Directory migration removes the previous source and metadata paths: " + path.Key);
                }

                foreach (var previousPath in new[] { "Android/Net/Uri.g.cs", "DefaultPackage.g.cs" })
                {
                    var wrapper = Path.Combine(output, paths[previousPath]);
                    Require(File.GetLastWriteTimeUtc(wrapper) == before[previousPath.Replace('/', Path.DirectorySeparatorChar)].Modified, "Moving an unchanged binding preserves its source timestamp: " + previousPath);
                }

                Require(!File.Exists(Path.Combine(output, "Android/Stale.g.cs"))
                    && !File.Exists(Path.Combine(output, "Android/Stale.g.cs.meta")), "Directory migration still removes stale owned bindings and metadata");
                foreach (var relativePath in new[] { "Extensions.cs", "Extensions.cs.meta", "Runtime.meta" })
                {
                    var path = Path.Combine(output, relativePath);
                    Require(File.ReadAllBytes(path).SequenceEqual(before[relativePath].Bytes)
                        && File.GetLastWriteTimeUtc(path) == before[relativePath].Modified, "Directory migration preserves unrelated source and folder assets: " + relativePath);
                }

                Require(File.ReadAllBytes(Path.Combine(output, EditorManifestName)).SequenceEqual(File.ReadAllBytes(Path.Combine(stage, EditorManifestName))), "Directory migration publishes the current namespace ownership manifest");
                var migrated = SnapshotEditorFiles(output);
                JavaWrapperOutput.Publish(stage, output);
                RequireEditorFilesUnchanged(output, migrated, "Repeated directory migration publication");

                updatedSources.Add("Bindings/Bindings/Android/Net/Uri.g.cs", "// newly added binding whose Java package starts with bindings\r\n");
                var expanded = WriteEditorStaging(root, "expanded-stage", updatedSources);
                Require(JavaWrapperOutput.Publish(expanded, output) == updatedSources.Count, "Current directory layout accepts a new Java package named bindings");
                foreach (var file in migrated)
                {
                    if (file.Key == EditorManifestName)
                    {
                        continue;
                    }

                    var path = Path.Combine(output, file.Key);
                    Require(File.ReadAllBytes(path).SequenceEqual(file.Value.Bytes)
                        && File.GetLastWriteTimeUtc(path) == file.Value.Modified, "Adding a bindings package preserves all previously migrated identities: " + file.Key);
                }

                var expandedSnapshot = SnapshotEditorFiles(output);
                JavaWrapperOutput.Publish(expanded, output);
                RequireEditorFilesUnchanged(output, expandedSnapshot, "Repeated publication with a bindings package");
            }
        }

        /// <summary>Checks that generated path migration rejects conflicts before writing or moving any asset.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">A migration ownership or preflight contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        private static void VerifyEditorMigrationConflicts(string workspace)
        {
            var modes = new[]
            {
                "destination-source", "destination-metadata", "modified-helper", "modified-binding",
                "source-directory", "metadata-directory", "file-ancestor", "readonly-source", "readonly-metadata",
                "overlapping-bindings", "overlapping-filtered-bindings", "mixed-helper-layout"
            };
            foreach (var previousRoot in new[] { EditorRootNamespace, EditorRootNamespace + ".Bindings" })
            {
                foreach (var mode in modes)
                {
                    var root = Path.Combine(workspace, "editor-migration-conflict-" + previousRoot + "-" + mode);
                    var output = Path.Combine(root, "assets");
                    var previous = new GeneratorOptions
                    {
                        NoSdk = true,
                        RootNamespace = previousRoot,
                        OutputDirectory = output
                    };
                    var previousSources = new Dictionary<string, string>
                    {
                        { "Runtime/AndroidJni.cs", "// previous helper\r\n" },
                        { "Stale.g.cs", "// previous stale binding\r\n" },
                        { "Z/Late.g.cs", "// previous late binding\r\n" }
                    };
                    if (mode == "overlapping-bindings" || mode == "overlapping-filtered-bindings")
                    {
                        previousSources.Add("Bindings/Z/Late.g.cs", "// old binding whose Java package starts with bindings\r\n");
                    }
                    else if (mode == "mixed-helper-layout")
                    {
                        previousSources.Add("IJavaObject.cs", "// inconsistently migrated runtime helper\r\n");
                    }

                    GeneratedOutput.Write(previous, null, previousSources, CancellationToken.None);
                    File.WriteAllText(Path.Combine(output, "Runtime/AndroidJni.cs.meta"), "runtime helper GUID");
                    File.WriteAllText(Path.Combine(output, "Z/Late.g.cs.meta"), "late binding GUID");
                    File.WriteAllText(Path.Combine(output, "Stale.g.cs.meta"), "stale binding GUID");
                    var source = Path.Combine(output, "Bindings/Z/Late.g.cs");
                    string? readOnlyPath = null;
                    if (mode == "file-ancestor")
                    {
                        File.WriteAllText(Path.Combine(output, "Bindings"), "handwritten file blocking the binding directory");
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                        switch (mode)
                        {
                            case "destination-source":
                                File.WriteAllText(source, "// handwritten source at the new binding path\r\n");
                                break;
                            case "destination-metadata":
                                File.WriteAllText(source + ".meta", "unowned destination GUID");
                                break;
                            case "modified-helper":
                                File.AppendAllText(Path.Combine(output, "Runtime/AndroidJni.cs"), "// user modification");
                                break;
                            case "modified-binding":
                                File.AppendAllText(Path.Combine(output, "Z/Late.g.cs"), "// user modification");
                                break;
                            case "source-directory":
                                Directory.CreateDirectory(source);
                                break;
                            case "metadata-directory":
                                Directory.CreateDirectory(source + ".meta");
                                break;
                            case "readonly-source":
                                readOnlyPath = Path.Combine(output, "Z/Late.g.cs");
                                break;
                            case "readonly-metadata":
                                readOnlyPath = Path.Combine(output, "Z/Late.g.cs.meta");
                                break;
                        }
                    }

                    SetEditorFixtureTimestamps(output);
                    if (readOnlyPath != null)
                    {
                        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
                    }

                    var updatedSources = new Dictionary<string, string>
                    {
                        { "AndroidJni.cs", "// updated helper\r\n" },
                        { "Bindings/Z/Late.g.cs", "// updated late binding\r\n" }
                    };
                    if (mode == "overlapping-bindings")
                    {
                        updatedSources.Add("Bindings/Bindings/Z/Late.g.cs", "// current binding whose Java package starts with bindings\r\n");
                    }

                    var stage = WriteEditorStaging(root, "updated-stage", updatedSources);
                    var before = SnapshotEditorFiles(output);
                    var directories = Directory.GetDirectories(output, "*", SearchOption.AllDirectories);
                    try
                    {
                        Expect<IOException>(() => JavaWrapperOutput.Publish(stage, output), "Editor rejects path migration conflict " + mode + " from " + previousRoot);
                        RequireEditorFilesUnchanged(output, before, "Rejected path migration conflict " + mode);
                        Require(Directory.GetDirectories(output, "*", SearchOption.AllDirectories).SequenceEqual(directories), "Rejected path migration conflict preserves the directory structure: " + mode);
                    }
                    finally
                    {
                        if (readOnlyPath != null)
                        {
                            File.SetAttributes(readOnlyPath, FileAttributes.Normal);
                        }
                    }
                }
            }
        }

        /// <summary>Checks that every destination ownership and shape conflict is rejected before any prepared write.</summary>
        /// <param name="workspace">The exclusively owned validation directory.</param>
        /// <exception cref="InvalidOperationException">An ownership or preflight contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        private static void VerifyEditorPublicationConflicts(string workspace)
        {
            var modes = new[] { "unowned", "empty-manifest", "modified-owned", "modified-stale", "directory-destination", "file-ancestor", "metadata-directory", "orphan-metadata-directory" };
            foreach (var mode in modes)
            {
                var root = Path.Combine(workspace, "editor-output-conflict-" + mode);
                var output = Path.Combine(root, "assets");
                Directory.CreateDirectory(output);
                var initialSources = new Dictionary<string, string>
                {
                    { "AChange.cs", "// previous source\r\n" },
                    { "Stale.cs", "// previous stale source\r\n" },
                    { "ZConflict.cs", "// previous conflict source\r\n" }
                };
                var nextSources = new Dictionary<string, string>
                {
                    { "AChange.cs", "// changed source\r\n" },
                    { "New.cs", "// new source\r\n" },
                    { "ZConflict.cs", "// changed conflict source\r\n" }
                };
                if (mode == "unowned" || mode == "empty-manifest")
                {
                    if (mode == "empty-manifest")
                    {
                        WriteEditorStaging(root, "assets", new Dictionary<string, string>());
                    }

                    File.WriteAllText(Path.Combine(output, "ZConflict.cs"), nextSources["ZConflict.cs"]);
                }
                else
                {
                    var initial = WriteEditorStaging(root, "initial-stage", initialSources);
                    JavaWrapperOutput.Publish(initial, output);
                    if (mode == "modified-owned")
                    {
                        File.AppendAllText(Path.Combine(output, "ZConflict.cs"), "// user modification");
                    }
                    else if (mode == "modified-stale")
                    {
                        File.AppendAllText(Path.Combine(output, "Stale.cs"), "// user modification");
                    }
                    else if (mode == "directory-destination")
                    {
                        Directory.CreateDirectory(Path.Combine(output, "New.cs"));
                    }
                    else if (mode == "file-ancestor")
                    {
                        File.WriteAllText(Path.Combine(output, "Blocked"), "handwritten file used as a would-be parent");
                        nextSources.Add("Blocked/Child.cs", "// generated child\r\n");
                    }
                    else if (mode == "metadata-directory" || mode == "orphan-metadata-directory")
                    {
                        if (mode == "orphan-metadata-directory")
                        {
                            File.Delete(Path.Combine(output, "Stale.cs"));
                        }

                        Directory.CreateDirectory(Path.Combine(output, "Stale.cs.meta"));
                    }
                }

                File.WriteAllText(Path.Combine(output, "ZConflict.cs.meta"), "existing source GUID");
                File.WriteAllText(Path.Combine(output, "Handwritten.cs"), "// handwritten extension");
                File.WriteAllText(Path.Combine(output, "Handwritten.cs.meta"), "handwritten GUID");
                SetEditorFixtureTimestamps(output);
                var next = WriteEditorStaging(root, "next-stage", nextSources);
                var before = SnapshotEditorFiles(output);
                Expect<IOException>(() => JavaWrapperOutput.Publish(next, output), "Editor refuses the destination conflict: " + mode);
                RequireEditorFilesUnchanged(output, before, "Rejected Editor destination conflict " + mode);
            }
        }

        /// <summary>Checks malformed stage and destination manifests, damaged sources, traversal, and overlapping roots.</summary>
        /// <param name="workspace">The exclusively owned validation directory.</param>
        /// <exception cref="InvalidOperationException">An invalid-input or preflight contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        private static void VerifyEditorPublicationCorruption(string workspace)
        {
            var modes = new[]
            {
                "version", "generator", "namespace", "legacy-staged-namespace", "null-files", "missing-fields", "lowercase-hash", "short-hash", "null-hash",
                "invalid-json", "trailing-json", "missing-manifest", "modified-source", "missing-source", "duplicate-path",
                "destination-version", "destination-namespace", "destination-traversal", "destination-non-source"
            };
            foreach (var mode in modes)
            {
                var root = Path.Combine(workspace, "editor-output-corrupt-" + mode);
                var output = Path.Combine(root, "assets");
                var initial = WriteEditorStaging(root, "initial-stage", new Dictionary<string, string>
                {
                    { "Api/AChange.cs", "// original source\r\n" },
                    { "Api/ZConflict.cs", "// original late source\r\n" }
                });
                JavaWrapperOutput.Publish(initial, output);
                File.WriteAllText(Path.Combine(output, "Api/AChange.cs.meta"), "retained source GUID");
                File.WriteAllText(Path.Combine(output, "Handwritten.cs.meta"), "handwritten metadata");
                var stage = WriteEditorStaging(root, "next-stage", new Dictionary<string, string>
                {
                    { "Api/AChange.cs", "// changed early source\r\n" },
                    { "Api/ZConflict.cs", "// changed late source\r\n" }
                });
                switch (mode)
                {
                    case "version":
                    case "destination-version":
                        MutateEditorManifest(mode.StartsWith("destination-", StringComparison.Ordinal) ? output : stage, manifest =>
                        {
                            manifest.FormatVersion = 2;
                        });
                        break;
                    case "generator":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.Generator = "UnrelatedGenerator";
                        });
                        break;
                    case "legacy-staged-namespace":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.RootNamespace = EditorRootNamespace + ".Bindings";
                        });
                        break;
                    case "namespace":
                    case "destination-namespace":
                        MutateEditorManifest(mode.StartsWith("destination-", StringComparison.Ordinal) ? output : stage, manifest =>
                        {
                            manifest.RootNamespace = "MajdataPlay.Platform.Android.Runtime.Invalid";
                        });
                        break;
                    case "null-files":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.Files = null!;
                        });
                        break;
                    case "missing-fields":
                        File.WriteAllText(Path.Combine(stage, EditorManifestName), "{\"Files\":{}}");
                        break;
                    case "lowercase-hash":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.Files["Api/ZConflict.cs"] = manifest.Files["Api/ZConflict.cs"].ToLowerInvariant();
                        });
                        break;
                    case "short-hash":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.Files["Api/ZConflict.cs"] = new string('A', 63);
                        });
                        break;
                    case "null-hash":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.Files["Api/ZConflict.cs"] = null!;
                        });
                        break;
                    case "invalid-json":
                        File.WriteAllText(Path.Combine(stage, EditorManifestName), "{");
                        break;
                    case "trailing-json":
                        File.AppendAllText(Path.Combine(stage, EditorManifestName), "\r\n{\"extra\":true}");
                        break;
                    case "missing-manifest":
                        File.Delete(Path.Combine(stage, EditorManifestName));
                        break;
                    case "modified-source":
                        File.AppendAllText(Path.Combine(stage, "Api/ZConflict.cs"), "// corrupt staged bytes");
                        break;
                    case "missing-source":
                        File.Delete(Path.Combine(stage, "Api/ZConflict.cs"));
                        break;
                    case "duplicate-path":
                        MutateEditorManifest(stage, manifest =>
                        {
                            manifest.Files.Add("Api\\ZConflict.cs", manifest.Files["Api/ZConflict.cs"]);
                        });
                        break;
                    case "destination-traversal":
                        MutateEditorManifest(output, manifest =>
                        {
                            manifest.Files.Add("../escape.cs", manifest.Files["Api/ZConflict.cs"]);
                        });
                        break;
                    case "destination-non-source":
                        MutateEditorManifest(output, manifest =>
                        {
                            manifest.Files.Add("Handwritten.cs.meta", manifest.Files["Api/ZConflict.cs"]);
                        });
                        break;
                    default:
                        throw new InvalidOperationException("Unknown Editor corruption fixture: " + mode);
                }

                SetEditorFixtureTimestamps(output);
                var before = SnapshotEditorFiles(output);
                Expect<IOException>(() => JavaWrapperOutput.Publish(stage, output), "Editor refuses corrupted input: " + mode);
                RequireEditorFilesUnchanged(output, before, "Rejected Editor corruption " + mode);
            }

            var traversalRoot = Path.Combine(workspace, "editor-output-traversal");
            var traversalOutput = Path.Combine(traversalRoot, "assets");
            var valid = WriteEditorStaging(traversalRoot, "initial-stage", new Dictionary<string, string>
            {
                { "Api/AChange.cs", "// unchanged baseline\r\n" }
            });
            JavaWrapperOutput.Publish(valid, traversalOutput);
            var outside = Path.Combine(traversalRoot, "escape.cs");
            File.WriteAllText(outside, "// outside staging and output\r\n");
            File.WriteAllText(outside + ".meta", "outside Unity GUID");
            var paths = new[]
            {
                "../escape.cs", "Api/../../escape.cs", "..\\escape.cs", "Api\\..\\..\\escape.cs", outside,
                "/escape.cs", "Api//Escape.cs", "Api/./Escape.cs", "Api/Escape.cs:stream.cs", "Api/Escape.cs ",
                "Api/Escape.txt", "Api/Escape.cs.meta"
            };
            for (var index = 0; index < paths.Length; index++)
            {
                var stage = WriteEditorStaging(traversalRoot, "invalid-stage-" + index, new Dictionary<string, string>
                {
                    { "Api/AChange.cs", "// changed early source\r\n" }
                });
                MutateEditorManifest(stage, manifest =>
                {
                    manifest.Files.Add(paths[index], manifest.Files["Api/AChange.cs"]);
                });
                var before = SnapshotEditorFiles(traversalOutput);
                var outsideBytes = File.ReadAllBytes(outside);
                var outsideMetadata = File.ReadAllBytes(outside + ".meta");
                var outsideTimestamp = File.GetLastWriteTimeUtc(outside);
                Expect<IOException>(() => JavaWrapperOutput.Publish(stage, traversalOutput), "Editor rejects an unsafe or non-C# manifest path: " + paths[index]);
                RequireEditorFilesUnchanged(traversalOutput, before, "Rejected unsafe Editor manifest path");
                Require(File.ReadAllBytes(outside).SequenceEqual(outsideBytes)
                    && File.GetLastWriteTimeUtc(outside) == outsideTimestamp
                    && File.ReadAllBytes(outside + ".meta").SequenceEqual(outsideMetadata), "Unsafe manifests leave sources and metadata outside the output root untouched");
            }

            var stageBefore = SnapshotEditorFiles(valid);
            Expect<IOException>(() => JavaWrapperOutput.Publish(valid, valid), "Editor rejects identical staging and output roots");
            Expect<IOException>(() => JavaWrapperOutput.Publish(valid, Path.Combine(valid, "child")), "Editor rejects output inside staging");
            Expect<IOException>(() => JavaWrapperOutput.Publish(valid, traversalRoot), "Editor rejects staging inside output");
            RequireEditorFilesUnchanged(valid, stageBefore, "Rejected overlapping Editor roots");
            Require(!Directory.Exists(Path.Combine(valid, "child")), "Overlapping roots are rejected before creating a destination directory");
        }

        /// <summary>Creates an independent CLI staging output with the namespace used by Unity.</summary>
        /// <param name="root">The parent directory owned by this fixture.</param>
        /// <param name="name">The child staging directory name.</param>
        /// <param name="sources">Relative C# paths and complete source content.</param>
        /// <returns>The CLI output directory containing a real ownership manifest.</returns>
        /// <exception cref="IOException">Writing a staging fixture fails.</exception>
        private static string WriteEditorStaging(string root, string name, IReadOnlyDictionary<string, string> sources)
        {
            var output = Path.Combine(root, name);
            var options = new GeneratorOptions
            {
                NoSdk = true,
                RootNamespace = EditorRootNamespace,
                OutputDirectory = output
            };
            GeneratedOutput.Write(options, null, sources, CancellationToken.None);
            return output;
        }

        /// <summary>Corrupts one real CLI manifest without changing the associated source fixtures.</summary>
        /// <param name="directory">The stage or destination directory containing the manifest.</param>
        /// <param name="mutate">The independently selected invalid manifest change.</param>
        /// <exception cref="InvalidOperationException">The fixture manifest cannot be deserialized.</exception>
        /// <exception cref="IOException">Reading or rewriting the fixture fails.</exception>
        /// <exception cref="JsonException">The fixture manifest is not valid JSON.</exception>
        private static void MutateEditorManifest(string directory, Action<GeneratedOutput.OutputManifest> mutate)
        {
            var path = Path.Combine(directory, EditorManifestName);
            var manifest = JsonSerializer.Deserialize<GeneratedOutput.OutputManifest>(File.ReadAllBytes(path))
                ?? throw new InvalidOperationException("The CLI fixture manifest is absent.");
            mutate(manifest);
            File.WriteAllText(path, JsonSerializer.Serialize(manifest));
        }

        /// <summary>Assigns old fixed timestamps so unintended writes are observable without sleeping.</summary>
        /// <param name="directory">The isolated fixture directory whose files are timestamped.</param>
        /// <exception cref="IOException">Enumerating or timestamping a fixture fails.</exception>
        private static void SetEditorFixtureTimestamps(string directory)
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                File.SetLastWriteTimeUtc(file, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            }
        }

        /// <summary>Captures all destination bytes and timestamps, including unrelated sources and metadata.</summary>
        /// <param name="directory">The isolated publication directory to snapshot.</param>
        /// <returns>Relative file paths mapped to their exact bytes and last-write timestamps.</returns>
        /// <exception cref="IOException">Enumerating or reading a fixture fails.</exception>
        private static Dictionary<string, (byte[] Bytes, DateTime Modified)> SnapshotEditorFiles(string directory)
        {
            var snapshot = new Dictionary<string, (byte[] Bytes, DateTime Modified)>(StringComparer.Ordinal);
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                snapshot.Add(Path.GetRelativePath(directory, file), (File.ReadAllBytes(file), File.GetLastWriteTimeUtc(file)));
            }

            return snapshot;
        }

        /// <summary>Requires rejection or an identical publication to preserve the complete prior file snapshot.</summary>
        /// <param name="directory">The isolated publication directory to inspect.</param>
        /// <param name="before">The complete snapshot taken before the operation.</param>
        /// <param name="description">The regression context included in failure messages.</param>
        /// <exception cref="InvalidOperationException">Any file is created, removed, changed, or rewritten.</exception>
        /// <exception cref="IOException">Enumerating or reading a fixture fails.</exception>
        private static void RequireEditorFilesUnchanged(string directory, IReadOnlyDictionary<string, (byte[] Bytes, DateTime Modified)> before, string description)
        {
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            Require(files.Length == before.Count, description + " preserves the file count");
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(directory, file);
                Require(before.TryGetValue(relativePath, out var previous), description + " preserves the file path: " + relativePath);
                Require(File.ReadAllBytes(file).SequenceEqual(previous.Bytes) && File.GetLastWriteTimeUtc(file) == previous.Modified,
                    description + " preserves file bytes and mtime: " + relativePath);
            }
        }
    }
}

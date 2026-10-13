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
        private const string EditorBindingsNamespace = "MajdataPlay.Platform.Android.Runtime.Bindings";

        /// <summary>Validates source publication, Unity metadata retention, and failures before any destination changes.</summary>
        /// <param name="workspace">The exclusively owned ignored validation directory.</param>
        /// <exception cref="InvalidOperationException">A publication regression contract fails.</exception>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        private static void VerifyEditorOutput(string workspace)
        {
            VerifyEditorPublicationLifecycle(workspace);
            VerifyEditorPublicationConflicts(workspace);
            VerifyEditorPublicationCorruption(workspace);
            VerifyEditorPublicationLongPaths(workspace);
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
                "version", "generator", "namespace", "null-files", "missing-fields", "lowercase-hash", "short-hash", "null-hash",
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
                    case "namespace":
                    case "destination-namespace":
                        MutateEditorManifest(mode.StartsWith("destination-", StringComparison.Ordinal) ? output : stage, manifest =>
                        {
                            manifest.RootNamespace = "MajdataPlay.Platform.Android.Runtime";
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
                RootNamespace = EditorBindingsNamespace,
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

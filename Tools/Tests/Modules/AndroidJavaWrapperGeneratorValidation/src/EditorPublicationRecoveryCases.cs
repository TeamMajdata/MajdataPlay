using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Platform.Android.Editor;
using MajdataPlay.Tools.AndroidJavaWrapperGenerator;

#nullable enable

namespace MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation
{
    internal static partial class Program
    {
        /// <summary>Names the publisher's temporary ownership record used by independent interrupted-state fixtures.</summary>
        private const string PendingEditorPublicationName = ".java-wrappers.publication.json";

        /// <summary>Checks retry after partial source writes, changed inputs, completed commits and genuine user edits.</summary>
        /// <param name="workspace">The exclusively owned ignored validation root.</param>
        /// <exception cref="IOException">Preparing or inspecting an isolated fixture fails.</exception>
        /// <exception cref="InvalidOperationException">A publication recovery or preservation contract fails.</exception>
        private static void VerifyEditorPublicationRecovery(string workspace)
        {
            VerifyEditorTransientReplacement(workspace);
            var modes = new[] { "retry", "new-inputs", "edited-owned", "edited-new", "invalid-record", "wrong-manifest", "unsafe-record", "completed-record", "initial-output" };
            foreach (var mode in modes)
            {
                var root = Path.Combine(workspace, "editor-publication-recovery-" + mode);
                var output = Path.Combine(root, "output");
                var initialSources = new Dictionary<string, string>
                {
                    { "AChanged.cs", "// previous first source\r\n" },
                    { "Stale.cs", "// stale owned source\r\n" },
                    { "Unchanged.cs", "// retained source\r\n" },
                    { "ZChanged.cs", "// previous final source\r\n" }
                };
                var nextSources = new Dictionary<string, string>
                {
                    { "AChanged.cs", "// updated first source\r\n" },
                    { "Added.cs", "// newly generated source\r\n" },
                    { "Unchanged.cs", initialSources["Unchanged.cs"] },
                    { "ZChanged.cs", "// updated final source\r\n" }
                };
                var initial = WriteEditorStaging(root, "initial", initialSources);
                var next = WriteEditorStaging(root, "next", nextSources);
                Directory.CreateDirectory(output);
                if (mode != "initial-output")
                {
                    JavaWrapperOutput.Publish(initial, output);
                }
                File.WriteAllText(Path.Combine(output, "AChanged.cs.meta"), "retained source GUID");
                if (mode != "initial-output")
                {
                    File.WriteAllText(Path.Combine(output, "Stale.cs.meta"), "stale source GUID");
                }
                File.WriteAllText(Path.Combine(output, "Extension.e.cs"), "// handwritten partial extension");
                SetEditorFixtureTimestamps(output);
                var sourceMetadata = File.ReadAllBytes(Path.Combine(output, "AChanged.cs.meta"));
                var metadataTime = File.GetLastWriteTimeUtc(Path.Combine(output, "AChanged.cs.meta"));
                var manifestPath = Path.Combine(output, EditorManifestName);
                var previousManifest = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : Array.Empty<byte>();

                if (mode == "retry" && OperatingSystem.IsWindows())
                {
                    using (var locked = new FileStream(Path.Combine(output, "ZChanged.cs"), FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        Expect<IOException>(() => JavaWrapperOutput.Publish(next, output),
                            "A late atomic-replace failure leaves a recoverable publication record");
                    }
                    Require(File.Exists(Path.Combine(output, PendingEditorPublicationName))
                        && File.ReadAllText(Path.Combine(output, "AChanged.cs")) == nextSources["AChanged.cs"]
                        && File.ReadAllText(Path.Combine(output, "Added.cs")) == nextSources["Added.cs"]
                        && File.ReadAllBytes(manifestPath).SequenceEqual(previousManifest),
                        "The actual failed publication wrote its prefix and retained the previous final manifest");
                }
                else
                {
                    WriteEditorPublicationRecord(output, previousManifest, next);
                    File.Copy(Path.Combine(next, "AChanged.cs"), Path.Combine(output, "AChanged.cs"), true);
                    File.Copy(Path.Combine(next, "Added.cs"), Path.Combine(output, "Added.cs"), true);
                    File.Delete(Path.Combine(output, "Stale.cs"));
                }
                var partialTimestamp = File.GetLastWriteTimeUtc(Path.Combine(output, "AChanged.cs"));
                if (mode == "edited-owned" || mode == "edited-new")
                {
                    File.AppendAllText(Path.Combine(output, mode == "edited-owned" ? "AChanged.cs" : "Added.cs"), "// user modification");
                }
                else if (mode == "invalid-record")
                {
                    File.WriteAllText(Path.Combine(output, PendingEditorPublicationName), "{}");
                }
                else if (mode == "wrong-manifest")
                {
                    WriteEditorPublicationRecord(output, previousManifest, next, new string('0', 64));
                }
                else if (mode == "unsafe-record")
                {
                    WriteEditorPublicationRecord(output, previousManifest, next, unsafePath: true);
                }
                else if (mode == "completed-record")
                {
                    JavaWrapperOutput.Publish(next, output);
                    WriteEditorPublicationRecord(output, previousManifest, next);
                }
                else if (mode == "new-inputs")
                {
                    nextSources["AChanged.cs"] = "// next generation after interruption\r\n";
                    nextSources.Remove("Added.cs");
                    nextSources.Add("Latest.cs", "// newest generated source\r\n");
                    next = WriteEditorStaging(root, "latest", nextSources);
                    if (OperatingSystem.IsWindows())
                    {
                        using var locked = new FileStream(Path.Combine(output, "ZChanged.cs"), FileMode.Open, FileAccess.Read, FileShare.Read);
                        Expect<IOException>(() => JavaWrapperOutput.Publish(next, output),
                            "A second interrupted generation retains ownership introduced by the first");
                        Require(File.ReadAllText(Path.Combine(output, "AChanged.cs")) == nextSources["AChanged.cs"],
                            "The second interruption reached its updated source before the late failure");
                    }
                }

                var before = SnapshotEditorFiles(output);
                if (mode == "edited-owned" || mode == "edited-new" || mode == "invalid-record" || mode == "wrong-manifest" || mode == "unsafe-record")
                {
                    Expect<IOException>(() => JavaWrapperOutput.Publish(next, output),
                        "Interrupted publication rejects genuine source edits and invalid ownership: " + mode);
                    RequireEditorFilesUnchanged(output, before, "Rejected interrupted publication " + mode);
                    continue;
                }
                Require(JavaWrapperOutput.Publish(next, output, validateOnly: true) == nextSources.Count,
                    "Interrupted publication can be preflighted: " + mode);
                RequireEditorFilesUnchanged(output, before, "Recovery preflight " + mode);
                Require(JavaWrapperOutput.Publish(next, output) == nextSources.Count,
                    "Interrupted publication resumes: " + mode);
                foreach (var source in nextSources)
                {
                    Require(File.ReadAllText(Path.Combine(output, source.Key)) == source.Value,
                        "Recovery installs the requested generated content: " + mode + "/" + source.Key);
                }
                Require(!File.Exists(Path.Combine(output, PendingEditorPublicationName))
                    && !File.Exists(Path.Combine(output, "Stale.cs")) && !File.Exists(Path.Combine(output, "Stale.cs.meta")),
                    "Recovery commits the manifest, retires its record and removes stale owned metadata: " + mode);
                Require(File.ReadAllBytes(Path.Combine(output, "AChanged.cs.meta")).SequenceEqual(sourceMetadata)
                    && File.GetLastWriteTimeUtc(Path.Combine(output, "AChanged.cs.meta")) == metadataTime
                    && File.ReadAllText(Path.Combine(output, "Extension.e.cs")) == "// handwritten partial extension",
                    "Recovery retains source GUID metadata and handwritten extensions: " + mode);
                if (mode == "retry" || mode == "initial-output")
                {
                    Require(File.GetLastWriteTimeUtc(Path.Combine(output, "AChanged.cs")) == partialTimestamp,
                        "Recovery does not rewrite an already published source: " + mode);
                }
                if (mode == "new-inputs")
                {
                    Require(!File.Exists(Path.Combine(output, "Added.cs")),
                        "A source owned only by the interrupted generation is retired by newer inputs");
                }
                var completed = SnapshotEditorFiles(output);
                JavaWrapperOutput.Publish(next, output);
                RequireEditorFilesUnchanged(output, completed, "Repeated recovered publication " + mode);
            }
        }

        /// <summary>Checks that a briefly held Windows reader is retried without losing atomic publication or metadata.</summary>
        /// <param name="workspace">The exclusively owned ignored validation root.</param>
        /// <exception cref="IOException">Preparing, publishing or inspecting the isolated fixture fails.</exception>
        /// <exception cref="InvalidOperationException">The bounded retry or publication contract fails.</exception>
        private static void VerifyEditorTransientReplacement(string workspace)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var root = Path.Combine(workspace, "editor-transient-replacement");
            var output = Path.Combine(root, "output");
            var initial = WriteEditorStaging(root, "initial", new Dictionary<string, string> { { "Value.cs", "// original\r\n" } });
            var next = WriteEditorStaging(root, "next", new Dictionary<string, string> { { "Value.cs", "// replacement\r\n" } });
            JavaWrapperOutput.Publish(initial, output);
            var target = Path.Combine(output, "Value.cs");
            using var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var started = new ManualResetEventSlim();
            using var lifetime = new CancellationTokenSource();
            var release = Task.Run(() =>
            {
                started.Set();
                while (!File.Exists(Path.Combine(output, PendingEditorPublicationName)) && !lifetime.IsCancellationRequested)
                {
                    Thread.Sleep(5);
                }
                if (!lifetime.IsCancellationRequested)
                {
                    Thread.Sleep(50);
                    locked.Dispose();
                }
            });
            try
            {
                Require(started.Wait(TimeSpan.FromSeconds(5)), "The bounded transient-lock fixture worker starts");
                Require(JavaWrapperOutput.Publish(next, output) == 1
                    && File.ReadAllText(target) == "// replacement\r\n"
                    && !File.Exists(Path.Combine(output, PendingEditorPublicationName)),
                    "A brief Windows reader lock clears during atomic-replace retry and publication commits");
            }
            finally
            {
                lifetime.Cancel();
                release.GetAwaiter().GetResult();
            }
        }

        /// <summary>Writes an independent preflight ownership record representing a process interrupted before commit.</summary>
        /// <param name="output">The isolated output directory.</param>
        /// <param name="previousManifest">The manifest bytes that were current before source publication.</param>
        /// <param name="next">The complete independently generated staging directory.</param>
        /// <param name="previousHash">An optional deliberately incorrect binding for a rejection test.</param>
        /// <param name="unsafePath">Whether to inject an unsafe retained ownership path.</param>
        /// <exception cref="IOException">Reading or writing the fixture fails.</exception>
        private static void WriteEditorPublicationRecord(string output, byte[] previousManifest, string next,
            string? previousHash = null, bool unsafePath = false)
        {
            var previous = previousManifest.Length == 0 ? new GeneratedOutput.OutputManifest()
                : JsonSerializer.Deserialize<GeneratedOutput.OutputManifest>(previousManifest)!;
            var previousFiles = new Dictionary<string, string>(previous.Files, StringComparer.Ordinal);
            if (unsafePath)
            {
                previousFiles.Add("../escape.cs", new string('A', 64));
            }
            var record = new
            {
                PreviousManifestHash = previousHash ?? Convert.ToHexString(SHA256.HashData(previousManifest)),
                PreviousFiles = previousFiles,
                NextManifest = File.ReadAllText(Path.Combine(next, EditorManifestName))
            };
            File.WriteAllText(Path.Combine(output, PendingEditorPublicationName), JsonSerializer.Serialize(record));
        }
    }
}

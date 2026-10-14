using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using MajdataPlay.Platform.Android.Editor;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Retires only unchanged generator-owned Unity sources after a compiled library has been published.</summary>
    internal sealed class LegacyWrapperSources : IDisposable
    {
        /// <summary>Compares absolute paths using the host filesystem's casing convention.</summary>
        private static readonly StringComparison s_pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        /// <summary>Compares collected folder paths using the host filesystem's casing convention.</summary>
        private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        /// <summary>Contains the separate empty generator output used to preflight and retire former sources.</summary>
        private readonly string? _stagingDirectory;

        /// <summary>Contains former output directories whose validated manifests establish source ownership.</summary>
        private readonly IReadOnlyList<LegacyDirectory> _directories;

        /// <summary>Captures the already validated former sources without changing Unity assets.</summary>
        /// <param name="stagingDirectory">The tool-local empty ownership stage, or null when no legacy output exists.</param>
        /// <param name="directories">The complete set of former output directories and metadata snapshots.</param>
        private LegacyWrapperSources(string? stagingDirectory, IReadOnlyList<LegacyDirectory> directories)
        {
            _stagingDirectory = stagingDirectory;
            _directories = directories;
        }

        /// <summary>Preflights both former Unity source layouts before a replacement runtime library can be published.</summary>
        /// <param name="projectRoot">The Unity repository root containing the former Runtime and Generated directories.</param>
        /// <returns>A disposable cleanup plan that retains every unowned or modified source.</returns>
        /// <exception cref="ArgumentException">The project root cannot be resolved.</exception>
        /// <exception cref="IOException">A legacy manifest, owned source, metadata, or path is unsafe or modified.</exception>
        /// <exception cref="UnauthorizedAccessException">The former output or tool-local stage cannot be accessed.</exception>
        internal static LegacyWrapperSources Prepare(string projectRoot)
        {
            var root = Path.GetFullPath(projectRoot);
            var androidDirectory = Path.Combine(root, "Assets", "Plugins", "MajdataPlay", "Platform", "Android");
            var legacyRoots = new[] { Path.Combine(androidDirectory, "Runtime"), Path.Combine(androidDirectory, "Generated") };
            var directories = new List<LegacyDirectory>();
            string? stagingDirectory = null;
            try
            {
                foreach (var legacyRoot in legacyRoots)
                {
                    var manifestPath = Path.Combine(legacyRoot, GeneratedOutput.ManifestName);
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    if (stagingDirectory == null)
                    {
                        stagingDirectory = Path.Combine(root, "Tools", "CSharp", "AndroidJavaWrapperGenerator", ".work",
                            "legacy-cleanup", Guid.NewGuid().ToString("N"));
                        VerifyPath(root, stagingDirectory);
                        GeneratedOutput.Write(new GeneratorOptions
                        {
                            OutputDirectory = stagingDirectory,
                            RootNamespace = "MajdataPlay.Platform.Android.Runtime"
                        }, null, new Dictionary<string, string>(), CancellationToken.None);
                    }

                    JavaWrapperOutput.Publish(stagingDirectory, legacyRoot, validateOnly: true);
                    var manifestBytes = File.ReadAllBytes(manifestPath);
                    var manifest = JsonSerializer.Deserialize<GeneratedOutput.OutputManifest>(manifestBytes)!;
                    var ownedDirectories = new HashSet<string>(s_pathComparer);
                    foreach (var relativePath in manifest.Files.Keys)
                    {
                        var current = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(legacyRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));
                        while (current != null && !string.Equals(current, legacyRoot, s_pathComparison))
                        {
                            VerifyPath(legacyRoot, current);
                            ownedDirectories.Add(current);
                            current = Path.GetDirectoryName(current);
                        }
                    }

                    var orderedDirectories = new List<string>(ownedDirectories);
                    orderedDirectories.Sort((left, right) => right.Length.CompareTo(left.Length));
                    var metadata = new Dictionary<string, byte[]>(s_pathComparer);
                    CaptureMetadata(legacyRoot, manifestPath + ".meta", metadata);
                    foreach (var directory in orderedDirectories)
                    {
                        VerifyPath(legacyRoot, directory);
                        VerifyWritable(directory);
                        CaptureMetadata(legacyRoot, directory + ".meta", metadata);
                    }
                    directories.Add(new LegacyDirectory(legacyRoot, manifestBytes, orderedDirectories.AsReadOnly(), metadata));
                }

                return new LegacyWrapperSources(stagingDirectory, directories.AsReadOnly());
            }
            catch
            {
                RemoveStaging(stagingDirectory);
                throw;
            }
        }

        /// <summary>Rechecks the preflight snapshots and removes only owned sources, paired metadata, and empty owned folders.</summary>
        /// <remarks>Call only after the replacement runtime DLL has been successfully published.</remarks>
        /// <exception cref="IOException">Former output changed after preflight, a path became unsafe, or cleanup failed.</exception>
        /// <exception cref="UnauthorizedAccessException">A former source, metadata file, or directory cannot be removed.</exception>
        internal void Cleanup()
        {
            if (_stagingDirectory == null)
            {
                return;
            }

            foreach (var directory in _directories)
            {
                var manifestPath = Path.Combine(directory.Root, GeneratedOutput.ManifestName);
                if (!File.ReadAllBytes(manifestPath).AsSpan().SequenceEqual(directory.ManifestBytes))
                {
                    throw new IOException("Former wrapper ownership changed after cleanup preflight: " + manifestPath);
                }
                JavaWrapperOutput.Publish(_stagingDirectory, directory.Root, validateOnly: true);
                foreach (var metadata in directory.Metadata)
                {
                    VerifyPath(directory.Root, metadata.Key);
                    VerifyWritable(metadata.Key);
                    if (!File.ReadAllBytes(metadata.Key).AsSpan().SequenceEqual(metadata.Value))
                    {
                        throw new IOException("Former wrapper metadata changed after cleanup preflight: " + metadata.Key);
                    }
                }
                foreach (var ownedDirectory in directory.OwnedDirectories)
                {
                    VerifyPath(directory.Root, ownedDirectory);
                    VerifyWritable(ownedDirectory);
                }
            }

            foreach (var directory in _directories)
            {
                JavaWrapperOutput.Publish(_stagingDirectory, directory.Root);
                var manifestPath = Path.Combine(directory.Root, GeneratedOutput.ManifestName);
                File.Delete(manifestPath);
                if (directory.Metadata.ContainsKey(manifestPath + ".meta"))
                {
                    File.Delete(manifestPath + ".meta");
                }

                foreach (var ownedDirectory in directory.OwnedDirectories)
                {
                    VerifyPath(directory.Root, ownedDirectory);
                    if (!Directory.Exists(ownedDirectory))
                    {
                        continue;
                    }
                    var isEmpty = false;
                    using (var entries = Directory.EnumerateFileSystemEntries(ownedDirectory).GetEnumerator())
                    {
                        isEmpty = !entries.MoveNext();
                    }
                    if (!isEmpty)
                    {
                        continue;
                    }
                    Directory.Delete(ownedDirectory, false);
                    if (directory.Metadata.ContainsKey(ownedDirectory + ".meta"))
                    {
                        File.Delete(ownedDirectory + ".meta");
                    }
                }
            }
        }

        /// <summary>Releases only the tool-local empty stage retained by this cleanup plan.</summary>
        public void Dispose()
        {
            RemoveStaging(_stagingDirectory);
        }

        /// <summary>Records writable, non-symbolic metadata whose paired owned source or folder may be retired.</summary>
        /// <param name="root">The validated former source root containing this metadata.</param>
        /// <param name="path">The manifest or folder metadata file to inspect.</param>
        /// <param name="metadata">The snapshot collection used to detect changes before cleanup.</param>
        /// <exception cref="IOException">The metadata is symbolic, read-only, or names a directory.</exception>
        /// <exception cref="UnauthorizedAccessException">The metadata cannot be read.</exception>
        private static void CaptureMetadata(string root, string path, IDictionary<string, byte[]> metadata)
        {
            VerifyPath(root, path);
            VerifyWritable(path);
            if (Directory.Exists(path))
            {
                throw new IOException("Former wrapper metadata cannot name a directory: " + path);
            }
            if (File.Exists(path))
            {
                metadata.Add(path, File.ReadAllBytes(path));
            }
        }

        /// <summary>Rejects known read-only paths before the replacement DLL is installed.</summary>
        /// <param name="path">The former file or owned directory that may be removed.</param>
        /// <exception cref="IOException">The existing path is read-only or cannot be inspected.</exception>
        /// <exception cref="UnauthorizedAccessException">The existing path cannot be inspected.</exception>
        private static void VerifyWritable(string path)
        {
            var attributes = ReadAttributes(path);
            if (attributes.HasValue && (attributes.Value & FileAttributes.ReadOnly) != 0)
            {
                throw new IOException("Refusing to retire a read-only wrapper asset: " + path);
            }
        }

        /// <summary>Checks absolute containment and rejects symbolic paths before any cleanup operation.</summary>
        /// <param name="root">The absolute allowed directory, which is never recursively removed.</param>
        /// <param name="path">The derived absolute source, metadata, folder, or scratch path.</param>
        /// <exception cref="IOException">The derived path escapes its root or traverses a symbolic link or junction.</exception>
        /// <exception cref="UnauthorizedAccessException">A path component cannot be inspected.</exception>
        private static void VerifyPath(string root, string path)
        {
            var absoluteRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var absolute = Path.GetFullPath(path);
            if (!string.Equals(absolute, absoluteRoot, s_pathComparison)
                && !absolute.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, s_pathComparison))
            {
                throw new IOException("Former wrapper cleanup escapes its verified directory: " + path);
            }
            for (var current = absolute; current != null; current = Path.GetDirectoryName(current))
            {
                var attributes = ReadAttributes(current);
                if (attributes.HasValue && (attributes.Value & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Former wrapper cleanup cannot traverse a symbolic link or junction: " + current);
                }
                if (string.Equals(current, absoluteRoot, s_pathComparison))
                {
                    break;
                }
            }
        }

        /// <summary>Inspects existing paths without mistaking absence for an inaccessible asset.</summary>
        /// <param name="path">The absolute file or directory to inspect.</param>
        /// <returns>The existing path attributes, or null when the path is absent.</returns>
        /// <exception cref="IOException">The path cannot be inspected for another reason.</exception>
        /// <exception cref="UnauthorizedAccessException">The path cannot be inspected.</exception>
        private static FileAttributes? ReadAttributes(string path)
        {
            try
            {
                return File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <summary>Deletes the separately created empty stage without recursively removing any directory.</summary>
        /// <param name="directory">The validated, uniquely created scratch directory, or null when none was needed.</param>
        /// <exception cref="IOException">Scratch output changed or cannot be removed.</exception>
        /// <exception cref="UnauthorizedAccessException">Scratch output cannot be removed.</exception>
        private static void RemoveStaging(string? directory)
        {
            if (directory == null || !Directory.Exists(directory))
            {
                return;
            }
            VerifyPath(directory, directory);
            var manifestPath = Path.Combine(directory, GeneratedOutput.ManifestName);
            VerifyPath(directory, manifestPath);
            File.Delete(manifestPath);
            Directory.Delete(directory, false);
        }

        /// <summary>Retains one validated ownership record and its potentially removable parent folders.</summary>
        private sealed class LegacyDirectory
        {
            /// <summary>Gets the former generated source root.</summary>
            internal string Root { get; }

            /// <summary>Gets the exact manifest bytes validated before compilation and publication.</summary>
            internal byte[] ManifestBytes { get; }

            /// <summary>Gets manifest-entry parent directories in child-before-parent removal order.</summary>
            internal IReadOnlyList<string> OwnedDirectories { get; }

            /// <summary>Gets paired metadata snapshots, excluding all unowned files.</summary>
            internal IReadOnlyDictionary<string, byte[]> Metadata { get; }

            /// <summary>Captures one former output directory without modifying any asset.</summary>
            /// <param name="root">The absolute former source root.</param>
            /// <param name="manifestBytes">The complete validated ownership record.</param>
            /// <param name="ownedDirectories">The potentially removable owned parent folders.</param>
            /// <param name="metadata">The paired manifest and folder metadata snapshots.</param>
            internal LegacyDirectory(string root, byte[] manifestBytes, IReadOnlyList<string> ownedDirectories,
                IReadOnlyDictionary<string, byte[]> metadata)
            {
                Root = root;
                ManifestBytes = manifestBytes;
                OwnedDirectories = ownedDirectories;
                Metadata = metadata;
            }
        }
    }
}

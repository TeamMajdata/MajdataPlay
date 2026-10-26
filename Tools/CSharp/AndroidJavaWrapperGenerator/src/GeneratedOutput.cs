using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Writes owned generated files without replacing handwritten or modified sources.</summary>
    internal static class GeneratedOutput
    {
        /// <summary>Identifies the file used to track generation ownership.</summary>
        internal const string ManifestName = "java-wrappers.manifest.json";

        /// <summary>Encodes generated text without a byte-order marker.</summary>
        private static readonly UTF8Encoding s_encoding = new UTF8Encoding(false);

        /// <summary>Writes sources and removes unchanged stale files recorded by the previous manifest.</summary>
        /// <param name="options">The destination and namespace settings.</param>
        /// <param name="platformJar">The selected SDK archive, or null for standalone inputs.</param>
        /// <param name="sources">Relative source paths and their generated text.</param>
        /// <param name="cancellationToken">The generation cancellation token.</param>
        /// <exception cref="IOException">A destination is handwritten, modified, unsafe, or inaccessible.</exception>
        /// <exception cref="OperationCanceledException">Generation was canceled.</exception>
        internal static void Write(GeneratorOptions options, string? platformJar, IReadOnlyDictionary<string, string> sources, CancellationToken cancellationToken)
        {
            var directory = Path.GetFullPath(options.OutputDirectory);
            var manifestPath = ResolvePath(directory, ManifestName);
            var previous = ReadManifest(manifestPath);
            var prepared = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var distinctPaths = new HashSet<string>(comparison);
            var ownedFiles = new Dictionary<string, string>(previous.Files, comparison);
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = source.Key.Replace('\\', '/');
                if (!relativePath.EndsWith(".cs", StringComparison.Ordinal) || !distinctPaths.Add(relativePath))
                {
                    throw new IOException("Invalid or duplicate generated source path: " + relativePath);
                }

                var absolutePath = ResolvePath(directory, relativePath);
                var bytes = s_encoding.GetBytes(source.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
                VerifyOwnership(absolutePath, relativePath, ownedFiles);
                prepared.Add(relativePath, bytes);
            }

            foreach (var file in previous.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifyOwnership(ResolvePath(directory, file.Key), file.Key, ownedFiles);
            }

            var manifest = new OutputManifest
            {
                RootNamespace = options.RootNamespace,
                PlatformJar = platformJar
            };
            Directory.CreateDirectory(directory);
            foreach (var file in prepared)
            {
                // Cancellation is honored before the write phase so a manifest always describes a completed update.
                var destination = ResolvePath(directory, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                WriteAtomically(destination, file.Value);
                manifest.Files.Add(file.Key, Hash(file.Value));
            }

            foreach (var stale in previous.Files.Keys.Where(item => !distinctPaths.Contains(item)))
            {
                var stalePath = ResolvePath(directory, stale);
                if (File.Exists(stalePath))
                {
                    File.Delete(stalePath);
                }
            }

            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            WriteAtomically(manifestPath, s_encoding.GetBytes(json + Environment.NewLine));
        }

        /// <summary>Reads a prior ownership manifest and rejects unrelated or malformed JSON.</summary>
        /// <param name="path">The absolute manifest path.</param>
        /// <returns>The existing manifest, or an empty manifest for a new output directory.</returns>
        /// <exception cref="IOException">An existing file is not a valid generator manifest.</exception>
        private static OutputManifest ReadManifest(string path)
        {
            if (!File.Exists(path))
            {
                return new OutputManifest();
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<OutputManifest>(File.ReadAllText(path));
                if (manifest == null || manifest.FormatVersion != 1 || manifest.Generator != "AndroidJavaWrapperGenerator" || manifest.Files == null)
                {
                    throw new IOException("Unrecognized output manifest: " + path);
                }

                return manifest;
            }
            catch (JsonException exception)
            {
                throw new IOException("Invalid output manifest: " + path, exception);
            }
        }

        /// <summary>Checks that an existing source still matches the last generator-owned content.</summary>
        /// <param name="absolutePath">The destination file.</param>
        /// <param name="relativePath">The source's manifest key.</param>
        /// <param name="ownedFiles">The last owned fingerprints using the destination filesystem comparison.</param>
        /// <exception cref="IOException">The file is not owned or its content changed.</exception>
        private static void VerifyOwnership(string absolutePath, string relativePath, IReadOnlyDictionary<string, string> ownedFiles)
        {
            if (!File.Exists(absolutePath))
            {
                return;
            }

            if (!ownedFiles.TryGetValue(relativePath, out var expected) || !string.Equals(expected, Hash(File.ReadAllBytes(absolutePath)), StringComparison.Ordinal))
            {
                throw new IOException("Refusing to overwrite handwritten or modified file: " + absolutePath + ". Put extensions in separate partial-class files, or select a new output directory.");
            }
        }

        /// <summary>Resolves a relative path while rejecting traversal and symbolic destinations.</summary>
        /// <param name="directory">The absolute output root.</param>
        /// <param name="relativePath">A generator-owned relative file path.</param>
        /// <returns>An absolute path inside the output root.</returns>
        /// <exception cref="IOException">The path escapes the output root or uses a reparse point.</exception>
        private static string ResolvePath(string directory, string relativePath)
        {
            if (Path.IsPathRooted(relativePath) || relativePath.Split('/', '\\').Any(item => item == ".." || item == "." || item.Length == 0))
            {
                throw new IOException("Unsafe generated output path: " + relativePath);
            }

            var root = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(directory, relativePath));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root, comparison))
            {
                throw new IOException("Generated output escapes its root: " + relativePath);
            }

            for (var current = path; current != null && current.StartsWith(root, comparison); current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Generated output cannot traverse a symbolic link or junction: " + current);
                }
            }

            return path;
        }

        /// <summary>Computes an exact content fingerprint for ownership checks.</summary>
        /// <param name="bytes">The on-disk or prepared bytes.</param>
        /// <returns>The hexadecimal SHA-256 digest.</returns>
        private static string Hash(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        /// <summary>Replaces a file using a temporary sibling and an atomic rename.</summary>
        /// <param name="path">The destination inside the output root.</param>
        /// <param name="bytes">The complete file content.</param>
        /// <exception cref="IOException">Writing or replacing the file fails.</exception>
        private static void WriteAtomically(string path, byte[] bytes)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>Describes the exact files owned by one completed generation.</summary>
        internal sealed class OutputManifest
        {
            /// <summary>Initializes an empty generator ownership manifest.</summary>
            public OutputManifest()
            {
            }

            /// <summary>Gets or sets the manifest schema version.</summary>
            public int FormatVersion { get; set; } = 1;

            /// <summary>Gets or sets the generator identity.</summary>
            public string Generator { get; set; } = "AndroidJavaWrapperGenerator";

            /// <summary>Gets or sets the root namespace of the generated API.</summary>
            public string RootNamespace { get; set; } = string.Empty;

            /// <summary>Gets or sets the SDK archive used during extraction.</summary>
            public string? PlatformJar { get; set; }

            /// <summary>Gets or sets the relative paths and byte fingerprints of owned files.</summary>
            public SortedDictionary<string, string> Files { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }
    }
}

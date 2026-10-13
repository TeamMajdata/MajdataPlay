using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Xml;

#nullable enable

namespace MajdataPlay.Platform.Android.Editor
{
    /// <summary>Publishes validated CLI sources while preserving Unity asset identity and handwritten files.</summary>
    internal static class JavaWrapperOutput
    {
        /// <summary>Names the CLI ownership manifest copied after a completed publication.</summary>
        private const string ManifestName = "java-wrappers.manifest.json";

        /// <summary>Identifies the only generator whose ownership records are accepted.</summary>
        private const string GeneratorName = "AndroidJavaWrapperGenerator";

        /// <summary>Identifies the namespace requested by the Unity Editor integration.</summary>
        private const string RootNamespace = "MajdataPlay.Platform.Android.Runtime.Bindings";

        /// <summary>Compares paths using the host filesystem's Windows casing convention.</summary>
        private static readonly StringComparison s_pathComparison = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        /// <summary>Compares manifest keys after directory separators have been normalized.</summary>
        private static readonly StringComparer s_pathComparer = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        /// <summary>Validates a complete publication plan before changing the Unity output directory.</summary>
        /// <param name="stagingDirectory">The separate CLI output directory containing sources and its manifest.</param>
        /// <param name="outputDirectory">The Unity asset directory receiving only manifest-owned C# sources.</param>
        /// <returns>The number of generated C# files listed by the staging manifest, including unchanged files.</returns>
        /// <exception cref="ArgumentException">A directory argument is empty or cannot be resolved.</exception>
        /// <exception cref="IOException">A manifest, source, destination, ownership record, or path is invalid, or publication fails.</exception>
        /// <exception cref="UnauthorizedAccessException">A source or destination is inaccessible.</exception>
        internal static int Publish(string stagingDirectory, string outputDirectory)
        {
            var stagingRoot = ResolveDirectory(stagingDirectory);
            var outputRoot = ResolveDirectory(outputDirectory);
            if (!Directory.Exists(stagingRoot))
            {
                throw new IOException("The CLI staging directory does not exist: " + stagingRoot);
            }

            if (string.Equals(stagingRoot, outputRoot, s_pathComparison)
                || stagingRoot.StartsWith(DirectoryPrefix(outputRoot), s_pathComparison)
                || outputRoot.StartsWith(DirectoryPrefix(stagingRoot), s_pathComparison))
            {
                throw new IOException("The staging and Unity output directories must be separate.");
            }

            var stagingManifestPath = ResolveFile(stagingRoot, ManifestName);
            var manifestBytes = File.ReadAllBytes(stagingManifestPath);
            var generatedFiles = ReadManifest(manifestBytes, stagingManifestPath);
            var sources = new Dictionary<string, byte[]>(s_pathComparer);
            foreach (var file in generatedFiles)
            {
                var sourcePath = ResolveFile(stagingRoot, file.Key);
                var bytes = File.ReadAllBytes(sourcePath);
                if (!string.Equals(file.Value, Hash(bytes), StringComparison.Ordinal))
                {
                    throw new IOException("The staged source does not match its manifest: " + sourcePath);
                }

                sources.Add(file.Key, bytes);
            }

            var outputManifestPath = ResolveFile(outputRoot, ManifestName);
            var previousManifestBytes = File.Exists(outputManifestPath) ? File.ReadAllBytes(outputManifestPath) : null;
            var ownedFiles = previousManifestBytes == null
                ? new Dictionary<string, string>(s_pathComparer)
                : ReadManifest(previousManifestBytes, outputManifestPath);
            var existingFiles = new HashSet<string>(s_pathComparer);
            foreach (var file in ownedFiles)
            {
                var destination = ResolveFile(outputRoot, file.Key);
                if (!File.Exists(destination))
                {
                    continue;
                }

                if (!string.Equals(file.Value, Hash(File.ReadAllBytes(destination)), StringComparison.Ordinal))
                {
                    throw new IOException("Refusing to change a modified generated source: " + destination);
                }

                existingFiles.Add(file.Key);
            }

            var writes = new List<KeyValuePair<string, byte[]>>();
            var deletions = new List<string>();
            foreach (var file in generatedFiles)
            {
                var destination = ResolveFile(outputRoot, file.Key);
                var exists = File.Exists(destination);
                if (exists && !existingFiles.Contains(file.Key))
                {
                    throw new IOException("Refusing to overwrite a handwritten source: " + destination);
                }

                if (!exists || !string.Equals(file.Value, ownedFiles[file.Key], StringComparison.Ordinal))
                {
                    VerifyWritableFile(destination);
                    writes.Add(new KeyValuePair<string, byte[]>(destination, sources[file.Key]));
                }
            }

            foreach (var file in ownedFiles)
            {
                if (generatedFiles.ContainsKey(file.Key))
                {
                    continue;
                }

                var destination = ResolveFile(outputRoot, file.Key);
                var metadata = ResolveFile(outputRoot, file.Key + ".meta");
                if (existingFiles.Contains(file.Key))
                {
                    VerifyWritableFile(destination);
                    deletions.Add(destination);
                }

                if (File.Exists(metadata))
                {
                    VerifyWritableFile(metadata);
                    deletions.Add(metadata);
                }
            }

            if (previousManifestBytes == null || !BytesEqual(previousManifestBytes, manifestBytes))
            {
                VerifyWritableFile(outputManifestPath);
                writes.Add(new KeyValuePair<string, byte[]>(outputManifestPath, manifestBytes));
            }

            // Manifest publication is last, after every planned source write and stale-file removal.
            foreach (var write in writes)
            {
                if (!string.Equals(write.Key, outputManifestPath, s_pathComparison))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(write.Key)!);
                    WriteAtomically(write.Key, write.Value);
                }
            }

            foreach (var deletion in deletions)
            {
                File.Delete(deletion);
            }

            foreach (var write in writes)
            {
                if (string.Equals(write.Key, outputManifestPath, s_pathComparison))
                {
                    Directory.CreateDirectory(outputRoot);
                    WriteAtomically(write.Key, write.Value);
                }
            }

            return generatedFiles.Count;
        }

        /// <summary>Reads the CLI JSON contract and validates every ownership path and content fingerprint.</summary>
        /// <param name="bytes">The complete manifest bytes read before publication starts.</param>
        /// <param name="path">The manifest path included in diagnostics.</param>
        /// <returns>Normalized relative C# paths and uppercase SHA-256 content fingerprints.</returns>
        /// <exception cref="IOException">The JSON, schema, generator, namespace, paths, or hashes are invalid.</exception>
        private static Dictionary<string, string> ReadManifest(byte[] bytes, string path)
        {
            ValidateManifestEnvelope(bytes, path);
            OutputManifest? manifest;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(OutputManifest), new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
                using var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max);
                manifest = serializer.ReadObject(reader) as OutputManifest;
            }
            catch (Exception exception) when (exception is SerializationException || exception is XmlException || exception is ArgumentException || exception is FormatException)
            {
                throw new IOException("Invalid output manifest: " + path, exception);
            }

            if (manifest == null || manifest.FormatVersion != 1 || manifest.Generator != GeneratorName
                || manifest.RootNamespace != RootNamespace || manifest.Files == null)
            {
                throw new IOException("Unrecognized output manifest schema, generator, or root namespace: " + path);
            }

            var files = new Dictionary<string, string>(s_pathComparer);
            foreach (var file in manifest.Files)
            {
                var relativePath = ValidateRelativePath(file.Key);
                if (!relativePath.EndsWith(".cs", StringComparison.Ordinal) || !IsHash(file.Value)
                    || files.ContainsKey(relativePath))
                {
                    throw new IOException("Invalid or duplicate generated C# manifest entry: " + file.Key + " in " + path);
                }

                files.Add(relativePath, file.Value);
            }

            foreach (var file in files.Keys)
            {
                for (var separator = file.LastIndexOf('/'); separator >= 0; separator = file.LastIndexOf('/', separator - 1))
                {
                    if (files.ContainsKey(file.Substring(0, separator)))
                    {
                        throw new IOException("A generated source is also used as a directory: " + file + " in " + path);
                    }
                }
            }

            return files;
        }

        /// <summary>Rejects truncated JSON and additional roots that the framework JSON reader may otherwise ignore.</summary>
        /// <param name="bytes">The complete UTF-8 manifest bytes produced by the CLI.</param>
        /// <param name="path">The manifest path included in diagnostics.</param>
        /// <exception cref="IOException">The bytes do not contain exactly one complete JSON object.</exception>
        private static void ValidateManifestEnvelope(byte[] bytes, string path)
        {
            var depth = 0;
            var quoted = false;
            var escaped = false;
            var started = false;
            var completed = false;
            foreach (var value in bytes)
            {
                if (!started || completed)
                {
                    if (value == ' ' || value == '\r' || value == '\n' || value == '\t')
                    {
                        continue;
                    }

                    if (completed || value != '{')
                    {
                        throw new IOException("The manifest must contain exactly one JSON object: " + path);
                    }

                    started = true;
                    depth = 1;
                    continue;
                }

                if (quoted)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (value == '\\')
                    {
                        escaped = true;
                    }
                    else if (value == '"')
                    {
                        quoted = false;
                    }
                }
                else if (value == '"')
                {
                    quoted = true;
                }
                else if (value == '{' || value == '[')
                {
                    depth++;
                }
                else if (value == '}' || value == ']')
                {
                    depth--;
                    completed = depth == 0;
                }
            }

            if (!completed)
            {
                throw new IOException("The output manifest is incomplete: " + path);
            }
        }

        /// <summary>Validates a relative path without accepting traversal or ambiguous portable filenames.</summary>
        /// <param name="relativePath">The path supplied by an ownership manifest.</param>
        /// <returns>The safe relative path using forward slashes.</returns>
        /// <exception cref="IOException">The path is rooted, contains traversal, or has an invalid filename component.</exception>
        private static string ValidateRelativePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new IOException("Unsafe generated output path: " + relativePath);
            }

            var normalized = relativePath.Replace('\\', '/');
            foreach (var component in normalized.Split('/'))
            {
                if (component.Length == 0 || component == "." || component == ".."
                    || component[component.Length - 1] == '.' || component[component.Length - 1] == ' ')
                {
                    throw new IOException("Unsafe generated output path: " + relativePath);
                }

                foreach (var character in component)
                {
                    if (character < ' ' || "<>:\"|?*".IndexOf(character) >= 0)
                    {
                        throw new IOException("Invalid generated output filename: " + relativePath);
                    }
                }
            }

            return normalized;
        }

        /// <summary>Uses Windows extended paths so Unity Mono can access deeply nested generated package files.</summary>
        /// <param name="path">The absolute or relative filesystem path to normalize.</param>
        /// <returns>An absolute native I/O path, with the Windows extended prefix when required.</returns>
        /// <exception cref="ArgumentException">The filesystem path is invalid.</exception>
        internal static string GetFileSystemPath(string path)
        {
            var absolute = Path.GetFullPath(path);
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || absolute.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return absolute;
            }
            if (absolute.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + absolute.Substring(2);
            }
            return @"\\?\" + absolute;
        }

        /// <summary>Resolves and validates a root directory without following symbolic links or junctions.</summary>
        /// <param name="directory">The requested staging or Unity output directory.</param>
        /// <returns>The absolute directory path with only filesystem-root trailing separators retained.</returns>
        /// <exception cref="ArgumentException">The directory is empty or has an invalid path.</exception>
        /// <exception cref="IOException">An existing directory component is a file or a reparse point.</exception>
        /// <exception cref="UnauthorizedAccessException">A directory component cannot be inspected.</exception>
        private static string ResolveDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A publication directory is required.", nameof(directory));
            }

            var absolute = GetFileSystemPath(directory);
            var root = Path.GetPathRoot(absolute)!;
            if (absolute.Length > root.Length)
            {
                absolute = absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            VerifyDirectoryHierarchy(absolute);
            return absolute;
        }

        /// <summary>Appends the directory separator required for an unambiguous containment comparison.</summary>
        /// <param name="directory">An absolute directory, possibly a filesystem root.</param>
        /// <returns>The directory with exactly its necessary trailing separator.</returns>
        private static string DirectoryPrefix(string directory)
        {
            return directory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? directory
                : directory + Path.DirectorySeparatorChar;
        }

        /// <summary>Resolves a safe file path and inspects every existing directory and file component.</summary>
        /// <param name="directory">The validated absolute root.</param>
        /// <param name="relativePath">The manifest-relative source, metadata, or manifest path.</param>
        /// <returns>An absolute non-symbolic file path inside the supplied root.</returns>
        /// <exception cref="IOException">The path escapes its root, traverses a reparse point, or names a directory.</exception>
        /// <exception cref="UnauthorizedAccessException">A path component cannot be inspected.</exception>
        private static string ResolveFile(string directory, string relativePath)
        {
            var normalized = ValidateRelativePath(relativePath);
            var absolute = Path.GetFullPath(Path.Combine(directory, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!absolute.StartsWith(DirectoryPrefix(directory), s_pathComparison))
            {
                throw new IOException("Generated output escapes its root: " + relativePath);
            }

            VerifyDirectoryHierarchy(Path.GetDirectoryName(absolute)!);
            var attributes = ReadAttributes(absolute);
            if (attributes.HasValue && (attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new IOException("Generated output cannot target a directory or symbolic file: " + absolute);
            }

            return absolute;
        }

        /// <summary>Checks existing ancestors before any directory creation or source publication.</summary>
        /// <param name="directory">The absolute directory whose ancestors must remain real directories.</param>
        /// <exception cref="IOException">An ancestor is a file or a reparse point.</exception>
        /// <exception cref="UnauthorizedAccessException">An ancestor cannot be inspected.</exception>
        private static void VerifyDirectoryHierarchy(string directory)
        {
            var current = directory;
            while (!string.IsNullOrEmpty(current))
            {
                var attributes = ReadAttributes(current);
                if (attributes.HasValue && ((attributes.Value & FileAttributes.Directory) == 0
                    || (attributes.Value & FileAttributes.ReparsePoint) != 0))
                {
                    throw new IOException("Publication cannot traverse a file, symbolic link, or junction: " + current);
                }
                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, s_pathComparison))
                {
                    break;
                }
                current = parent;
            }
        }

        /// <summary>Inspects paths without treating missing files and directories as existing ownership.</summary>
        /// <param name="path">The absolute filesystem path to inspect.</param>
        /// <returns>The existing path attributes, or null when a file or parent directory is absent.</returns>
        /// <exception cref="IOException">Inspecting the path fails for a reason other than its absence.</exception>
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

        /// <summary>Rejects known read-only destinations while the complete publication plan is still being prepared.</summary>
        /// <param name="path">The validated file scheduled for replacement or deletion.</param>
        /// <exception cref="IOException">The existing file is marked read-only or its attributes cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">The path cannot be inspected.</exception>
        private static void VerifyWritableFile(string path)
        {
            var attributes = ReadAttributes(path);
            if (attributes.HasValue && (attributes.Value & FileAttributes.ReadOnly) != 0)
            {
                throw new IOException("Refusing to change a read-only publication file: " + path);
            }
        }

        /// <summary>Checks the CLI's canonical uppercase SHA-256 representation.</summary>
        /// <param name="value">The manifest content fingerprint.</param>
        /// <returns>True when the value contains exactly 64 uppercase hexadecimal characters.</returns>
        private static bool IsHash(string? value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            foreach (var character in value)
            {
                if ((character < '0' || character > '9') && (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Computes the CLI-compatible content fingerprint using the .NET Standard cryptography API.</summary>
        /// <param name="bytes">The exact bytes from a staged or existing source.</param>
        /// <returns>The uppercase hexadecimal SHA-256 digest.</returns>
        private static string Hash(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        /// <summary>Compares complete manifest bytes to avoid rewriting an identical ownership record.</summary>
        /// <param name="left">The existing manifest bytes.</param>
        /// <param name="right">The validated staging manifest bytes.</param>
        /// <returns>True when every byte and the array lengths match.</returns>
        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Replaces one source or manifest through a temporary sibling without touching its Unity metadata.</summary>
        /// <param name="path">The validated absolute publication destination.</param>
        /// <param name="bytes">The complete content prepared before publication starts.</param>
        /// <exception cref="IOException">Writing, replacing, moving, or cleaning up the temporary file fails.</exception>
        /// <exception cref="UnauthorizedAccessException">The destination cannot be written.</exception>
        private static void WriteAtomically(string path, byte[] bytes)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>Deserializes the independent CLI ownership contract without referencing its tool assembly.</summary>
        [DataContract]
        private sealed class OutputManifest
        {
            /// <summary>Gets or sets the required CLI manifest schema version.</summary>
            [DataMember(IsRequired = true)]
            public int FormatVersion { get; set; }

            /// <summary>Gets or sets the required identity of the CLI generator.</summary>
            [DataMember(IsRequired = true)]
            public string? Generator { get; set; }

            /// <summary>Gets or sets the required namespace used by every generated wrapper.</summary>
            [DataMember(IsRequired = true)]
            public string? RootNamespace { get; set; }

            /// <summary>Gets or sets the required relative C# paths and their exact content fingerprints.</summary>
            [DataMember(IsRequired = true)]
            public Dictionary<string, string>? Files { get; set; }
        }
    }
}

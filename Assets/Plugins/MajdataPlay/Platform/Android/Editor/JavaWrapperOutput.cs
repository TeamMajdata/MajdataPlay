using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;

#nullable enable

namespace MajdataPlay.Platform.Android.Editor
{
    /// <summary>Publishes validated CLI sources while preserving Unity asset identity and handwritten files.</summary>
    internal static class JavaWrapperOutput
    {
        /// <summary>Names the CLI ownership manifest copied after a completed publication.</summary>
        private const string ManifestName = "java-wrappers.manifest.json";

        /// <summary>Names the recoverable ownership record written before same-layout source publication.</summary>
        private const string PendingPublicationName = ".java-wrappers.publication.json";

        /// <summary>Identifies the only generator whose ownership records are accepted.</summary>
        private const string GeneratorName = "AndroidJavaWrapperGenerator";

        /// <summary>Identifies the namespace requested by the Unity Editor integration.</summary>
        private const string RootNamespace = "MajdataPlay.Platform.Android.Runtime";

        /// <summary>Identifies the previous namespace accepted only when migrating existing owned output.</summary>
        private const string PreviousRootNamespace = "MajdataPlay.Platform.Android.Runtime.Bindings";

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
        /// <param name="previousOutputDirectory">An optional separate former output directory whose owned assets migrate when the new directory has no manifest.</param>
        /// <param name="validateOnly">Whether to validate the complete publication without changing any output files.</param>
        /// <returns>The number of generated C# files listed by the staging manifest, including unchanged files.</returns>
        /// <exception cref="ArgumentException">A directory argument is empty or cannot be resolved.</exception>
        /// <exception cref="IOException">A manifest, source, destination, ownership record, or path is invalid, or publication fails.</exception>
        /// <exception cref="UnauthorizedAccessException">A source or destination is inaccessible.</exception>
        internal static int Publish(string stagingDirectory, string outputDirectory, string? previousOutputDirectory = null,
            bool validateOnly = false)
        {
            var stagingRoot = ResolveDirectory(stagingDirectory);
            var outputRoot = ResolveDirectory(outputDirectory);
            var formerRoot = previousOutputDirectory == null ? null : ResolveDirectory(previousOutputDirectory);
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

            if (formerRoot != null && (DirectoriesOverlap(formerRoot, stagingRoot) || DirectoriesOverlap(formerRoot, outputRoot)))
            {
                throw new IOException("The former output directory must be separate from staging and the current Unity output directory.");
            }

            var stagingManifestPath = ResolveFile(stagingRoot, ManifestName);
            var manifestBytes = File.ReadAllBytes(stagingManifestPath);
            var generatedFiles = ReadManifest(manifestBytes, stagingManifestPath, false);
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
            var formerManifestPath = formerRoot == null ? null : ResolveFile(formerRoot, ManifestName);
            var formerManifestBytes = formerManifestPath != null && File.Exists(formerManifestPath) ? File.ReadAllBytes(formerManifestPath) : null;
            var formerOwnedFiles = formerManifestBytes == null
                ? new Dictionary<string, string>(s_pathComparer)
                : ReadManifest(formerManifestBytes, formerManifestPath!, true);
            if (previousManifestBytes != null && formerOwnedFiles.Count != 0)
            {
                throw new IOException("Both the current and former output directories own generated sources; refusing an ambiguous output migration.");
            }

            var migratingOutputRoot = previousManifestBytes == null && formerManifestBytes != null;
            var ownershipRoot = migratingOutputRoot ? formerRoot! : outputRoot;
            var ownedFiles = migratingOutputRoot
                ? formerOwnedFiles
                : previousManifestBytes == null
                    ? new Dictionary<string, string>(s_pathComparer)
                    : ReadManifest(previousManifestBytes, outputManifestPath, true);
            var pendingPath = ResolveFile(outputRoot, PendingPublicationName);
            Dictionary<string, string>? pendingFiles = null;
            if (File.Exists(pendingPath))
            {
                if (migratingOutputRoot)
                {
                    throw new IOException("An interrupted publication must finish before migrating the output root: " + pendingPath);
                }

                var pending = ReadPendingPublication(File.ReadAllBytes(pendingPath), pendingPath);
                var pendingManifestBytes = Encoding.UTF8.GetBytes(pending.NextManifest!);
                var pendingSources = ReadManifest(pendingManifestBytes, pendingPath, false);
                var previousSources = ValidateManifestFiles(pending.PreviousFiles!, pendingPath);
                if (string.Equals(pending.PreviousManifestHash, Hash(previousManifestBytes ?? Array.Empty<byte>()), StringComparison.Ordinal))
                {
                    foreach (var file in ownedFiles)
                    {
                        if (!previousSources.ContainsKey(file.Key))
                        {
                            throw new IOException("The interrupted publication omitted previous ownership: " + file.Key);
                        }
                    }

                    ownedFiles = previousSources;
                    foreach (var file in pendingSources)
                    {
                        if (!ownedFiles.ContainsKey(file.Key))
                        {
                            ownedFiles.Add(file.Key, file.Value);
                        }
                    }
                    pendingFiles = pendingSources;
                }
                else if (previousManifestBytes == null || !BytesEqual(previousManifestBytes, pendingManifestBytes))
                {
                    throw new IOException("The interrupted publication does not match the current ownership manifest: " + pendingPath);
                }
            }
            VerifyWritableFile(pendingPath);

            var usesNamespaceDirectories = UsesNamespaceDirectories(ownedFiles);
            var existingFiles = new HashSet<string>(s_pathComparer);
            var verifiedHashes = new Dictionary<string, string>(s_pathComparer);
            foreach (var file in ownedFiles)
            {
                var destination = ResolveFile(ownershipRoot, file.Key);
                if (!File.Exists(destination))
                {
                    continue;
                }

                var actualHash = Hash(File.ReadAllBytes(destination));
                if (!string.Equals(file.Value, actualHash, StringComparison.Ordinal)
                    && (pendingFiles == null || !pendingFiles.TryGetValue(file.Key, out var pendingHash)
                        || !string.Equals(pendingHash, actualHash, StringComparison.Ordinal)))
                {
                    throw new IOException("Refusing to change a modified generated source: " + destination);
                }

                verifiedHashes.Add(file.Key, actualHash);
                existingFiles.Add(file.Key);
            }

            foreach (var file in verifiedHashes)
            {
                ownedFiles[file.Key] = file.Value;
            }

            var writes = new List<KeyValuePair<string, byte[]>>();
            var moves = new List<KeyValuePair<string, string>>();
            var deletions = new List<string>();
            var migratedFiles = new HashSet<string>(s_pathComparer);
            foreach (var file in generatedFiles)
            {
                var destination = ResolveFile(outputRoot, file.Key);
                var exists = File.Exists(destination);
                if (exists && (migratingOutputRoot || !existingFiles.Contains(file.Key)))
                {
                    throw new IOException("Refusing to overwrite a handwritten source: " + destination);
                }

                var previousPath = GetPreviousGeneratedPath(file.Key);
                var sourceKey = file.Key;
                if (!usesNamespaceDirectories && previousPath != null)
                {
                    if (!migratingOutputRoot && (ownedFiles.ContainsKey(file.Key)
                        || (ownedFiles.ContainsKey(previousPath) && generatedFiles.ContainsKey(previousPath))))
                    {
                        throw new IOException("Generated source migration has overlapping old and new asset paths; use a separate output directory: " + previousPath + " -> " + file.Key);
                    }

                    sourceKey = previousPath;
                }

                if (migratingOutputRoot || sourceKey != file.Key)
                {
                    var metadata = ResolveFile(outputRoot, file.Key + ".meta");
                    if (File.Exists(metadata))
                    {
                        throw new IOException("Refusing to overwrite unowned Unity metadata during generated source migration: " + metadata);
                    }

                    VerifyWritableFile(destination);
                    if (!ownedFiles.TryGetValue(sourceKey, out var previousHash))
                    {
                        writes.Add(new KeyValuePair<string, byte[]>(destination, sources[file.Key]));
                        continue;
                    }

                    var previousSource = ResolveFile(ownershipRoot, sourceKey);
                    var previousMetadata = ResolveFile(ownershipRoot, sourceKey + ".meta");
                    if (existingFiles.Contains(sourceKey))
                    {
                        VerifyWritableFile(previousSource);
                        if (string.Equals(file.Value, previousHash, StringComparison.Ordinal))
                        {
                            moves.Add(new KeyValuePair<string, string>(previousSource, destination));
                        }
                        else
                        {
                            writes.Add(new KeyValuePair<string, byte[]>(destination, sources[file.Key]));
                            deletions.Add(previousSource);
                        }
                    }
                    else
                    {
                        writes.Add(new KeyValuePair<string, byte[]>(destination, sources[file.Key]));
                    }

                    if (File.Exists(previousMetadata))
                    {
                        VerifyWritableFile(previousMetadata);
                        moves.Add(new KeyValuePair<string, string>(previousMetadata, metadata));
                    }

                    migratedFiles.Add(sourceKey);
                    continue;
                }

                if (!exists || !string.Equals(file.Value, ownedFiles[file.Key], StringComparison.Ordinal))
                {
                    VerifyWritableFile(destination);
                    writes.Add(new KeyValuePair<string, byte[]>(destination, sources[file.Key]));
                }
            }

            foreach (var file in ownedFiles)
            {
                if ((!migratingOutputRoot && generatedFiles.ContainsKey(file.Key)) || migratedFiles.Contains(file.Key))
                {
                    continue;
                }

                var destination = ResolveFile(ownershipRoot, file.Key);
                var metadata = ResolveFile(ownershipRoot, file.Key + ".meta");
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

            if (migratingOutputRoot)
            {
                VerifyWritableFile(formerManifestPath!);
                var formerMetadata = ResolveFile(ownershipRoot, ManifestName + ".meta");
                var metadata = ResolveFile(outputRoot, ManifestName + ".meta");
                if (File.Exists(metadata))
                {
                    throw new IOException("Refusing to overwrite unowned Unity manifest metadata during output migration: " + metadata);
                }

                if (File.Exists(formerMetadata))
                {
                    VerifyWritableFile(formerMetadata);
                    moves.Add(new KeyValuePair<string, string>(formerMetadata, metadata));
                }
            }

            if (previousManifestBytes == null || !BytesEqual(previousManifestBytes, manifestBytes))
            {
                VerifyWritableFile(outputManifestPath);
                writes.Add(new KeyValuePair<string, byte[]>(outputManifestPath, manifestBytes));
            }

            if (validateOnly)
            {
                return generatedFiles.Count;
            }

            if (!migratingOutputRoot && migratedFiles.Count == 0 && (writes.Count != 0 || deletions.Count != 0))
            {
                var pending = new PendingPublication
                {
                    PreviousManifestHash = Hash(previousManifestBytes ?? Array.Empty<byte>()),
                    PreviousFiles = ownedFiles,
                    NextManifest = Encoding.UTF8.GetString(manifestBytes)
                };
                var serializer = new DataContractJsonSerializer(typeof(PendingPublication), new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
                using var buffer = new MemoryStream();
                serializer.WriteObject(buffer, pending);
                Directory.CreateDirectory(outputRoot);
                WriteAtomically(pendingPath, buffer.ToArray());
            }

            // Manifest publication is last, after every planned source write, asset move, and stale-file removal.
            foreach (var write in writes)
            {
                if (!string.Equals(write.Key, outputManifestPath, s_pathComparison))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(write.Key)!);
                    WriteAtomically(write.Key, write.Value);
                }
            }

            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Value)!);
                File.Move(move.Key, move.Value);
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

            if (migratingOutputRoot)
            {
                File.Delete(formerManifestPath!);
            }

            File.Delete(pendingPath);
            return generatedFiles.Count;
        }

        /// <summary>Checks whether two normalized roots share any files through equality or directory containment.</summary>
        /// <param name="left">The first absolute directory root.</param>
        /// <param name="right">The second absolute directory root.</param>
        /// <returns>True when the roots are equal or one directory contains the other.</returns>
        private static bool DirectoriesOverlap(string left, string right)
        {
            return string.Equals(left, right, s_pathComparison)
                || left.StartsWith(DirectoryPrefix(right), s_pathComparison)
                || right.StartsWith(DirectoryPrefix(left), s_pathComparison);
        }

        /// <summary>Distinguishes already migrated ownership from the old package-relative layout using runtime helper paths.</summary>
        /// <param name="ownedFiles">The normalized source paths in the existing ownership manifest.</param>
        /// <returns>True when runtime helpers are already owned directly in the output root.</returns>
        /// <exception cref="IOException">The ownership manifest mixes old and current runtime helper locations.</exception>
        private static bool UsesNamespaceDirectories(IReadOnlyDictionary<string, string> ownedFiles)
        {
            var currentHelpers = false;
            var previousHelpers = false;
            foreach (var name in new[] { "AndroidJni.cs", "IJavaObject.cs", "JavaInvocationException.cs", "JavaObjectReference.cs", "JavaWrapperConverter.cs", "AndroidJavaObjectExtensions.cs", "JavaWrapperConverter.Exceptions.cs", "JavaObjectExtensions.cs" })
            {
                currentHelpers |= ownedFiles.ContainsKey(name);
                previousHelpers |= ownedFiles.ContainsKey("Runtime/" + name);
            }

            if (currentHelpers && previousHelpers)
            {
                throw new IOException("Generated ownership mixes old and current runtime helper locations; use a separate output directory.");
            }

            return currentHelpers;
        }

        /// <summary>Finds the old package-relative path for a source now placed under its namespace directory.</summary>
        /// <param name="relativePath">The normalized source path in the new staging manifest.</param>
        /// <returns>The previous source path for a runtime helper or binding, or null for other files.</returns>
        private static string? GetPreviousGeneratedPath(string relativePath)
        {
            switch (relativePath)
            {
                case "AndroidJni.cs":
                case "IJavaObject.cs":
                case "JavaInvocationException.cs":
                case "JavaObjectReference.cs":
                case "JavaWrapperConverter.cs":
                case "AndroidJavaObjectExtensions.cs":
                case "JavaWrapperConverter.Exceptions.cs":
                case "JavaObjectExtensions.cs":
                    return "Runtime/" + relativePath;
            }

            if (relativePath.StartsWith("Bindings/", StringComparison.Ordinal)
                && relativePath.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                return relativePath.Substring("Bindings/".Length);
            }

            return null;
        }

        /// <summary>Reads the CLI JSON contract and validates every ownership path and content fingerprint.</summary>
        /// <param name="bytes">The complete manifest bytes read before publication starts.</param>
        /// <param name="path">The manifest path included in diagnostics.</param>
        /// <param name="allowPreviousRootNamespace">Whether this is an existing ownership manifest that may use the previous root namespace.</param>
        /// <returns>Normalized relative C# paths and uppercase SHA-256 content fingerprints.</returns>
        /// <exception cref="IOException">The JSON, schema, generator, namespace, paths, or hashes are invalid.</exception>
        private static Dictionary<string, string> ReadManifest(byte[] bytes, string path, bool allowPreviousRootNamespace)
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
                || manifest.Files == null
                || (manifest.RootNamespace != RootNamespace
                    && (!allowPreviousRootNamespace || manifest.RootNamespace != PreviousRootNamespace)))
            {
                throw new IOException("Unrecognized output manifest schema, generator, or root namespace: " + path);
            }

            return ValidateManifestFiles(manifest.Files, path);
        }

        /// <summary>Validates and normalizes all source ownership entries, including interrupted publications.</summary>
        /// <param name="sourceFiles">The manifest-relative paths and their expected source hashes.</param>
        /// <param name="path">The ownership record path included in diagnostics.</param>
        /// <returns>Unique safe paths with valid uppercase SHA-256 hashes.</returns>
        /// <exception cref="IOException">A path, hash, duplicate or directory relationship is invalid.</exception>
        private static Dictionary<string, string> ValidateManifestFiles(IReadOnlyDictionary<string, string> sourceFiles, string path)
        {
            var files = new Dictionary<string, string>(s_pathComparer);
            foreach (var file in sourceFiles)
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

        /// <summary>Reads the ownership snapshots of a fully preflighted, interrupted same-layout publication.</summary>
        /// <param name="bytes">The complete UTF-8 publication record.</param>
        /// <param name="path">The record path included in diagnostics.</param>
        /// <returns>The previous ownership and intended complete output manifest.</returns>
        /// <exception cref="IOException">The record is malformed or omits its required ownership snapshots.</exception>
        private static PendingPublication ReadPendingPublication(byte[] bytes, string path)
        {
            ValidateManifestEnvelope(bytes, path);
            PendingPublication? pending;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(PendingPublication), new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });
                using var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max);
                pending = serializer.ReadObject(reader) as PendingPublication;
            }
            catch (Exception exception) when (exception is SerializationException || exception is XmlException || exception is ArgumentException || exception is FormatException)
            {
                throw new IOException("Invalid interrupted publication record: " + path, exception);
            }

            if (pending == null || !IsHash(pending.PreviousManifestHash)
                || pending.PreviousFiles == null || string.IsNullOrEmpty(pending.NextManifest))
            {
                throw new IOException("Incomplete interrupted publication record: " + path);
            }
            return pending;
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
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            File.Replace(temporary, path, null);
                            break;
                        }
                        catch (IOException exception) when (attempt < 3 && IsRetryableReplacementError(exception))
                        {
                            Thread.Sleep((attempt + 1) * 100);
                        }
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch (IOException exception)
            {
                throw new IOException("Unable to publish generated file '" + path + "' (I/O code "
                    + (exception.HResult & 0xFFFF) + "): " + exception.Message, exception);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        /// <summary>Recognizes Windows replace failures that preserve both original filenames and may clear on retry.</summary>
        /// <param name="exception">The I/O failure raised by the atomic replacement.</param>
        /// <returns>True for Windows sharing, byte-range locking or unable-to-remove-replaced errors.</returns>
        private static bool IsRetryableReplacementError(IOException exception)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return false;
            }

            var error = exception.HResult & 0xFFFF;
            return error == 32 || error == 33 || error == 1175;
        }

        /// <summary>Retains the accepted pre-write ownership and intended manifest until publication commits.</summary>
        [DataContract]
        private sealed class PendingPublication
        {
            /// <summary>Gets or sets the hash binding this record to the pre-publication ownership manifest.</summary>
            [DataMember(IsRequired = true)]
            public string? PreviousManifestHash { get; set; }

            /// <summary>Gets or sets source hashes validated before the first source is changed.</summary>
            [DataMember(IsRequired = true)]
            public Dictionary<string, string>? PreviousFiles { get; set; }

            /// <summary>Gets or sets the complete intended CLI ownership manifest as UTF-8 text.</summary>
            [DataMember(IsRequired = true)]
            public string? NextManifest { get; set; }
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

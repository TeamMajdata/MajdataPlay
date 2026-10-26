using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Scans SDK and explicit JVM declarations before resolving Java source APIs.</summary>
    internal static class JavaApiScanner
    {
        /// <summary>Scans the entire Android runtime and all additional inputs without filtering packages.</summary>
        /// <remarks>
        /// Explicit definitions replace SDK declarations with a warning. Byte-identical repeated
        /// additional definitions are accepted; different additional definitions of one binary name fail.
        /// Source declarations are added after bytecode dependencies through JavaSourceParser.
        /// </remarks>
        /// <param name="options">The SDK, platform, and additional input configuration.</param>
        /// <param name="cancellationToken">The scan cancellation token.</param>
        /// <returns>All public and nonpublic metadata required for emission and dependency resolution.</returns>
        /// <exception cref="ArgumentNullException">The options are null.</exception>
        /// <exception cref="ArgumentException">An input path or API level is invalid.</exception>
        /// <exception cref="FileNotFoundException">An input or requested Android platform is missing.</exception>
        /// <exception cref="DirectoryNotFoundException">No SDK can be discovered.</exception>
        /// <exception cref="InvalidDataException">A class, archive, nesting chain, or duplicate definition is invalid.</exception>
        /// <exception cref="IOException">An input cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">An input is inaccessible.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        internal static JavaApi Scan(GeneratorOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(options);
            cancellationToken.ThrowIfCancellationRequested();
            return new ScanSession(options, cancellationToken).Scan();
        }

        /// <summary>Owns the metadata and resource limits for one independent scan.</summary>
        private sealed class ScanSession
        {
            /// <summary>Bounds the size of an input archive before its central directory is loaded.</summary>
            private const long MaxArchiveBytes = 512L * 1024 * 1024;

            /// <summary>Bounds the archive entry count, including entries that are not class files.</summary>
            private const int MaxArchiveEntries = 100000;

            /// <summary>Bounds one in-memory nested JAR from an Android library archive.</summary>
            private const long MaxNestedArchiveBytes = 64L * 1024 * 1024;

            /// <summary>Bounds uncompressed class bytes across the complete scan.</summary>
            private const long MaxTotalClassBytes = 512L * 1024 * 1024;

            /// <summary>Bounds buffered class bytes; a larger permitted class occupies its own batch.</summary>
            private const long MaxBatchBytes = 32L * 1024 * 1024;

            /// <summary>Bounds class buffers and result slots retained by one CPU work batch.</summary>
            private const int MaxBatchItems = 128;

            /// <summary>Bounds suspicious compression ratios for class entries larger than one MiB.</summary>
            private const long MaxCompressionRatio = 1000;

            /// <summary>Bounds malicious or cyclic enclosing chains without recursive traversal.</summary>
            private const int MaxEnclosingDepth = 1024;

            /// <summary>Contains the caller's discovery configuration.</summary>
            private readonly GeneratorOptions _options;

            /// <summary>Allows file, archive, and hierarchy scanning to be canceled.</summary>
            private readonly CancellationToken _cancellationToken;

            /// <summary>Contains the complete discovered API.</summary>
            private readonly JavaApi _api = new JavaApi();

            /// <summary>Tracks class origins and content hashes for deterministic duplicate handling.</summary>
            private readonly Dictionary<string, ScannedClass> _definitions = new Dictionary<string, ScannedClass>(StringComparer.Ordinal);

            /// <summary>Suppresses repeated enclosing-metadata warnings between visibility passes.</summary>
            private readonly HashSet<string> _visibilityWarnings = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>Tracks class bytes committed from all inputs; pending batches reserve the remaining budget.</summary>
            private long _totalClassBytes;

            /// <summary>Creates an isolated scan session.</summary>
            /// <param name="options">The input and SDK configuration.</param>
            /// <param name="cancellationToken">The scan cancellation token.</param>
            internal ScanSession(GeneratorOptions options, CancellationToken cancellationToken)
            {
                _options = options;
                _cancellationToken = cancellationToken;
            }

            /// <summary>Loads bytecode first, resolves sources second, and finishes enclosing visibility.</summary>
            /// <returns>The unfiltered metadata graph.</returns>
            /// <exception cref="ArgumentException">An input configuration is invalid.</exception>
            /// <exception cref="IOException">An input, platform, or declaration cannot be read or is invalid.</exception>
            /// <exception cref="UnauthorizedAccessException">An input is inaccessible.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            internal JavaApi Scan()
            {
                var inputs = CollectInputs();
                _api.PlatformJar = JavaToolchain.FindPlatformJar(_options, _cancellationToken);
                if (_api.PlatformJar is not null)
                {
                    ScanArchive(_api.PlatformJar, false);
                    if (!_api.Types.ContainsKey("java.lang.Object"))
                    {
                        throw new InvalidDataException($"Android platform jar '{_api.PlatformJar}' does not contain java.lang.Object; supply a valid Android SDK platform.");
                    }
                }
                var sources = new List<string>();
                var classBatch = new ClassBatch(true);
                foreach (var input in inputs)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(input);
                    if (extension.Equals(".java", StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(input);
                    }
                    else if (extension.Equals(".class", StringComparison.OrdinalIgnoreCase))
                    {
                        ScanClassFile(input, classBatch);
                    }
                    else
                    {
                        ProcessClassBatch(classBatch);
                        ScanArchive(input, true);
                    }
                }
                ProcessClassBatch(classBatch);
                ApplyVisibility();
                if (sources.Count != 0)
                {
                    JavaSourceParser.AddSources(_api, sources, _cancellationToken, _options.Jobs);
                    ValidateSourceOverrides();
                    ApplyVisibility();
                }
                _cancellationToken.ThrowIfCancellationRequested();
                return _api;
            }

            /// <summary>Expands directories while preserving explicit input order and avoiding directory links.</summary>
            /// <returns>Deduplicated absolute source, class, and jar paths.</returns>
            /// <exception cref="ArgumentException">An input has an unsupported extension.</exception>
            /// <exception cref="FileNotFoundException">An input does not exist.</exception>
            /// <exception cref="InvalidDataException">SDK-free scanning has no inputs or an input directory is empty.</exception>
            /// <exception cref="IOException">An input directory cannot be enumerated.</exception>
            /// <exception cref="UnauthorizedAccessException">An input directory is inaccessible.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private List<string> CollectInputs()
            {
                var result = new List<string>();
                var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                var seen = new HashSet<string>(comparison);
                foreach (var configured in _options.Inputs)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var input = JavaToolchain.FullPath(configured, "Java input");
                    if (Directory.Exists(input))
                    {
                        var files = new List<string>();
                        var enumeration = new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = false,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        };
                        foreach (var file in Directory.EnumerateFiles(input, "*", enumeration))
                        {
                            _cancellationToken.ThrowIfCancellationRequested();
                            if (IsSupportedInput(file))
                            {
                                files.Add(Path.GetFullPath(file));
                            }
                        }
                        files.Sort(StringComparer.Ordinal);
                        if (files.Count == 0)
                        {
                            throw new InvalidDataException($"Java input directory '{input}' contains no .java, .class, .jar, or .aar/.zip files.");
                        }
                        foreach (var file in files)
                        {
                            if (seen.Add(file))
                            {
                                result.Add(file);
                            }
                        }
                    }
                    else if (!File.Exists(input))
                    {
                        throw new FileNotFoundException($"Java input '{input}' does not exist.", input);
                    }
                    else if (!IsSupportedInput(input))
                    {
                        throw new ArgumentException($"Java input '{input}' has an unsupported extension. Supply .java, .class, .jar, .aar, .zip, or a directory containing those files.");
                    }
                    else if (seen.Add(input))
                    {
                        result.Add(input);
                    }
                }
                if (_options.NoSdk && result.Count == 0)
                {
                    throw new InvalidDataException("SDK-free scanning requires at least one .java, .class, or .jar input.");
                }
                return result;
            }

            /// <summary>Checks extensions recognized by the scanner without interpreting package layout.</summary>
            /// <param name="path">The candidate input file.</param>
            /// <returns>Whether the extension identifies a source, class, or archive.</returns>
            private static bool IsSupportedInput(string path)
            {
                var extension = Path.GetExtension(path);
                return extension.Equals(".java", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".class", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".jar", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".aar", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>Buffers a standalone class using its internal binary name rather than its filename.</summary>
            /// <param name="path">The class file at any filesystem location.</param>
            /// <param name="batch">The pending classes with matching input precedence.</param>
            /// <exception cref="InvalidDataException">A class is malformed, exceeds limits, or conflicts.</exception>
            /// <exception cref="IOException">The class cannot be read.</exception>
            /// <exception cref="UnauthorizedAccessException">The file is inaccessible.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void ScanClassFile(string path, ClassBatch batch)
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    BufferClass(batch, stream, stream.Length, path);
                }
                catch
                {
                    ProcessClassBatch(batch);
                    throw;
                }
            }

            /// <summary>Scans bounded archive class entries in deterministic order without extracting files.</summary>
            /// <remarks>Android uses base jar declarations; META-INF/versions entries are excluded with a warning.</remarks>
            /// <param name="path">The SDK or explicit jar path.</param>
            /// <param name="additional">Whether declarations override SDK metadata.</param>
            /// <exception cref="InvalidDataException">The archive is malformed or exceeds scan limits.</exception>
            /// <exception cref="IOException">The archive cannot be read.</exception>
            /// <exception cref="UnauthorizedAccessException">The archive is inaccessible.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void ScanArchive(string path, bool additional)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                using var file = File.OpenRead(path);
                if (file.Length > MaxArchiveBytes)
                {
                    throw new InvalidDataException($"Java archive '{path}' exceeds the 512 MiB archive limit.");
                }
                try
                {
                    using var archive = new ZipArchive(file, ZipArchiveMode.Read, false);
                    var remainingEntries = MaxArchiveEntries;
                    if (Path.GetExtension(path).Equals(".aar", StringComparison.OrdinalIgnoreCase))
                    {
                        ScanAndroidArchive(archive, path, additional, ref remainingEntries);
                    }
                    else if (ScanClassArchive(archive, path, additional, ref remainingEntries) == 0 && additional)
                    {
                        throw new InvalidDataException($"Java archive '{path}' contains no base .class declarations.");
                    }
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException($"Cannot scan Java archive '{path}': {exception.Message}", exception);
                }
            }

            /// <summary>Scans only classes.jar and direct libs/*.jar entries without extracting Android resources.</summary>
            /// <param name="archive">The already opened Android library archive.</param>
            /// <param name="path">The library path used in diagnostics.</param>
            /// <param name="additional">Whether these declarations override SDK metadata.</param>
            /// <param name="remainingEntries">The shared outer and nested ZIP entry budget.</param>
            /// <exception cref="InvalidDataException">An archive, nested JAR, or declaration exceeds scan limits or is invalid.</exception>
            /// <exception cref="IOException">An archive entry cannot be read.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void ScanAndroidArchive(ZipArchive archive, string path, bool additional, ref int remainingEntries)
            {
                ReserveArchiveEntries(archive, path, ref remainingEntries);
                var jars = new List<ZipArchiveEntry>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in archive.Entries)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName;
                    if (name != "classes.jar" && !(name.StartsWith("libs/", StringComparison.Ordinal)
                        && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    if (name.StartsWith("libs/", StringComparison.Ordinal)
                        && (name.IndexOf('/', "libs/".Length) >= 0 || name.IndexOf('\\') >= 0))
                    {
                        throw new InvalidDataException($"Android archive '{path}' contains an invalid nested JAR path '{name}'; use direct libs/*.jar entries.");
                    }
                    if (!names.Add(name))
                    {
                        throw new InvalidDataException($"Android archive '{path}' repeats nested JAR entry '{name}'.");
                    }
                    jars.Add(entry);
                }
                jars.Sort(CompareEntries);
                var classCount = 0;
                long nestedBytes = 0;
                foreach (var entry in jars)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var source = path + "!" + entry.FullName;
                    CheckCompressionRatio(entry, source);
                    if (entry.Length < 0 || entry.Length > MaxNestedArchiveBytes)
                    {
                        throw new InvalidDataException($"Nested Java archive '{source}' exceeds the 64 MiB archive limit.");
                    }
                    if (entry.Length > (MaxArchiveBytes - nestedBytes))
                    {
                        throw new InvalidDataException($"Android archive '{path}' exceeds the 512 MiB total nested JAR limit.");
                    }
                    nestedBytes += entry.Length;
                    using var input = entry.Open();
                    var bytes = ReadNestedArchive(input, entry.Length, source);
                    using var buffer = new MemoryStream(bytes, false);
                    using var nested = new ZipArchive(buffer, ZipArchiveMode.Read, false);
                    classCount += ScanClassArchive(nested, source, additional, ref remainingEntries);
                }
                if (classCount == 0)
                {
                    throw new InvalidDataException($"Android archive '{path}' contains no base .class declarations in classes.jar or libs/*.jar.");
                }
            }

            /// <summary>Scans an ordinary or nested JAR using the same ordered class batches and byte limits.</summary>
            /// <param name="archive">The opened JAR or ZIP containing class declarations.</param>
            /// <param name="path">The complete outer and nested archive provenance.</param>
            /// <param name="additional">Whether these declarations override SDK metadata.</param>
            /// <param name="remainingEntries">The shared outer and nested ZIP entry budget.</param>
            /// <returns>The number of base class entries considered.</returns>
            /// <exception cref="InvalidDataException">An archive or class is malformed or exceeds scan limits.</exception>
            /// <exception cref="IOException">A class entry cannot be read.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private int ScanClassArchive(ZipArchive archive, string path, bool additional, ref int remainingEntries)
            {
                ReserveArchiveEntries(archive, path, ref remainingEntries);
                var entries = new List<ZipArchiveEntry>();
                var versioned = false;
                foreach (var entry in archive.Entries)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (entry.FullName.StartsWith("META-INF/versions/", StringComparison.Ordinal))
                    {
                        versioned = true;
                        continue;
                    }
                    entries.Add(entry);
                }
                entries.Sort(CompareEntries);
                var batch = new ClassBatch(additional);
                try
                {
                    foreach (var entry in entries)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        var source = path + "!" + entry.FullName;
                        CheckCompressionRatio(entry, source);
                        using var stream = entry.Open();
                        BufferClass(batch, stream, entry.Length, source);
                    }
                    ProcessClassBatch(batch);
                }
                catch
                {
                    ProcessClassBatch(batch);
                    throw;
                }
                if (versioned)
                {
                    _api.Warnings.Add($"Java archive '{path}' contains META-INF/versions classes; only base declarations are scanned for Android.");
                }
                return entries.Count;
            }

            /// <summary>Charges outer and nested central-directory entries against one bounded archive budget.</summary>
            /// <param name="archive">The archive whose central directory has been opened.</param>
            /// <param name="path">The archive provenance used in diagnostics.</param>
            /// <param name="remainingEntries">The entry count available for this archive and later nested JARs.</param>
            /// <exception cref="InvalidDataException">The aggregate ZIP entry count exceeds the archive limit.</exception>
            private static void ReserveArchiveEntries(ZipArchive archive, string path, ref int remainingEntries)
            {
                if (archive.Entries.Count > remainingEntries)
                {
                    throw new InvalidDataException($"Java archive '{path}' exceeds the {MaxArchiveEntries} aggregate entry limit.");
                }
                remainingEntries -= archive.Entries.Count;
            }

            /// <summary>Applies the same compression-ratio check to class entries and nested JAR entries.</summary>
            /// <param name="entry">The entry to decompress.</param>
            /// <param name="source">The archive and entry provenance used in diagnostics.</param>
            /// <exception cref="InvalidDataException">The entry has an excessive declared compression ratio.</exception>
            private static void CheckCompressionRatio(ZipArchiveEntry entry, string source)
            {
                if (entry.Length > 1024 * 1024 && (entry.CompressedLength == 0
                    || (entry.Length / entry.CompressedLength) > MaxCompressionRatio))
                {
                    throw new InvalidDataException($"Java archive entry '{source}' exceeds the permitted compression ratio.");
                }
            }

            /// <summary>Reads one already bounded nested JAR with exact-length and cancellation checks.</summary>
            /// <param name="stream">The decompressed Android archive entry stream.</param>
            /// <param name="length">The validated uncompressed nested JAR length.</param>
            /// <param name="source">The outer archive and nested JAR provenance.</param>
            /// <returns>The complete bytes of the nested JAR.</returns>
            /// <exception cref="InvalidDataException">The entry length is inconsistent.</exception>
            /// <exception cref="IOException">The nested archive cannot be read.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private byte[] ReadNestedArchive(Stream stream, long length, string source)
            {
                var bytes = new byte[(int)length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var count = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
                    if (count == 0)
                    {
                        throw new InvalidDataException($"Nested Java archive '{source}' is truncated: expected {length} bytes, read {offset}.");
                    }
                    offset += count;
                }
                _cancellationToken.ThrowIfCancellationRequested();
                if (stream.ReadByte() != -1)
                {
                    throw new InvalidDataException($"Nested Java archive '{source}' is larger than its declared {length} byte length.");
                }
                return bytes;
            }
            /// <summary>Compares archive entry names using JVM-relevant ordinal ordering.</summary>
            /// <param name="left">The first archive entry.</param>
            /// <param name="right">The second archive entry.</param>
            /// <returns>The ordinal ordering of the full entry names.</returns>
            private static int CompareEntries(ZipArchiveEntry left, ZipArchiveEntry right)
            {
                return StringComparer.Ordinal.Compare(left.FullName, right.FullName);
            }

            /// <summary>Reads one class on the caller's thread, flushing a full batch before allocation.</summary>
            /// <param name="batch">The buffered classes with matching input precedence.</param>
            /// <param name="stream">The standalone file or exclusively read archive entry stream.</param>
            /// <param name="length">The declared uncompressed class size.</param>
            /// <param name="source">The input used in diagnostics.</param>
            /// <exception cref="InvalidDataException">A class is invalid, exceeds limits, or conflicts.</exception>
            /// <exception cref="IOException">The stream cannot be read.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void BufferClass(ClassBatch batch, Stream stream, long length, string source)
            {
                if (batch.Items.Count != 0 && (batch.Items.Count == MaxBatchItems
                    || length > (MaxBatchBytes - batch.Bytes)))
                {
                    ProcessClassBatch(batch);
                }
                var bytes = ReadClassBytes(stream, length, source, batch.Bytes);
                batch.Items.Add(new BufferedClass(bytes, source));
                batch.Bytes += bytes.Length;
            }

            /// <summary>Parses and hashes private buffers in parallel, then commits every result in input order.</summary>
            /// <param name="batch">The bounded buffers and their input precedence.</param>
            /// <exception cref="ArgumentOutOfRangeException">The configured worker count is not positive.</exception>
            /// <exception cref="InvalidDataException">A class is malformed or duplicate definitions conflict.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void ProcessClassBatch(ClassBatch batch)
            {
                if (batch.Items.Count == 0)
                {
                    return;
                }
                try
                {
                    // Keep parse failures in their slots so earlier duplicate conflicts win.
                    // Readers observe the scan token; the helper must finish before ordered commit.
                    ParallelWork.For(batch.Items.Count, _options.Jobs, index =>
                    {
                        var input = batch.Items[index];
                        try
                        {
                            var info = JavaClassReader.Read(input.Bytes, input.Source, _cancellationToken);
                            var hash = Convert.ToHexString(SHA256.HashData(input.Bytes));
                            _cancellationToken.ThrowIfCancellationRequested();
                            input.Definition = new ScannedClass(info, hash, input.Source, batch.Additional);
                        }
                        catch (Exception exception)
                        {
                            input.Failure = ExceptionDispatchInfo.Capture(exception);
                        }
                    }, CancellationToken.None);
                    foreach (var input in batch.Items)
                    {
                        _totalClassBytes += input.Bytes.Length;
                        input.Failure?.Throw();
                        _cancellationToken.ThrowIfCancellationRequested();
                        AddClass(input.Definition!);
                    }
                }
                finally
                {
                    batch.Items.Clear();
                    batch.Bytes = 0;
                }
            }

            /// <summary>Reads a declared class length with bounded allocations and cancellation checkpoints.</summary>
            /// <param name="stream">The standalone file or decompressed archive stream.</param>
            /// <param name="length">The declared uncompressed class size.</param>
            /// <param name="source">The input used in diagnostics.</param>
            /// <param name="bufferedBytes">The earlier class bytes awaiting ordered commit.</param>
            /// <returns>The exact complete class bytes.</returns>
            /// <exception cref="InvalidDataException">The size is excessive or inconsistent.</exception>
            /// <exception cref="IOException">The stream cannot be read.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private byte[] ReadClassBytes(Stream stream, long length, string source, long bufferedBytes)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (length < 0 || length > JavaClassReader.MaxClassBytes)
                {
                    throw new InvalidDataException($"Java class '{source}' exceeds the 64 MiB class-file limit.");
                }
                if (length > (MaxTotalClassBytes - _totalClassBytes - bufferedBytes))
                {
                    throw new InvalidDataException($"Scanning '{source}' would exceed the 512 MiB total uncompressed class limit.");
                }
                var bytes = new byte[(int)length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var count = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
                    if (count == 0)
                    {
                        throw new InvalidDataException($"Java class '{source}' is truncated: expected {length} bytes, read {offset}.");
                    }
                    offset += count;
                }
                _cancellationToken.ThrowIfCancellationRequested();
                if (stream.ReadByte() != -1)
                {
                    throw new InvalidDataException($"Java class '{source}' is larger than its declared {length} byte length.");
                }
                return bytes;
            }

            /// <summary>Retains a parsed declaration and applies explicit-over-SDK duplicate precedence.</summary>
            /// <param name="definition">The parsed metadata and complete byte identity in input order.</param>
            /// <exception cref="InvalidDataException">Additional definitions conflict.</exception>
            private void AddClass(ScannedClass definition)
            {
                var name = definition.Info.Type.Name;
                if (_definitions.TryGetValue(name, out var previous))
                {
                    if (definition.Additional && !previous.Additional)
                    {
                        _api.Warnings.Add($"Explicit Java input '{definition.Source}' overrides SDK declaration '{name}' from '{previous.Source}'.");
                    }
                    else if (previous.Hash == definition.Hash)
                    {
                        return;
                    }
                    else
                    {
                        throw new InvalidDataException($"Conflicting Java definitions of '{name}' in '{previous.Source}' and '{definition.Source}'. Repeated additional definitions must have identical class bytes.");
                    }
                }
                _definitions[name] = definition;
                _api.Types[name] = definition.Info.Type;
            }

            /// <summary>Applies the same duplicate policy when the source parser replaces bytecode declarations.</summary>
            /// <exception cref="InvalidDataException">A source replaces an explicit class or jar declaration.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void ValidateSourceOverrides()
            {
                foreach (var definition in _definitions.Values)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (_api.Types.TryGetValue(definition.Info.Type.Name, out var current)
                        && !ReferenceEquals(current, definition.Info.Type))
                    {
                        if (definition.Additional)
                        {
                            throw new InvalidDataException($"Java source conflicts with additional bytecode definition '{current.Name}' from '{definition.Source}'. Supply one additional definition per binary name.");
                        }
                        _api.Warnings.Add($"Explicit Java source overrides SDK declaration '{current.Name}' from '{definition.Source}'.");
                    }
                }
            }

            /// <summary>Computes public accessibility through all available enclosing declarations.</summary>
            /// <exception cref="InvalidDataException">An enclosing chain cycles or exceeds the depth limit.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private void ApplyVisibility()
            {
                foreach (var type in _api.Types.Values)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    type.IsPublic = IsEffectivelyPublic(type);
                }
            }

            /// <summary>Checks declaration flags and enclosing types, using InnerClasses hints for missing parents.</summary>
            /// <param name="type">The bytecode or source declaration to evaluate.</param>
            /// <returns>Whether this declaration and every enclosing declaration are public.</returns>
            /// <exception cref="InvalidDataException">The enclosing chain is cyclic or unreasonably deep.</exception>
            /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
            private bool IsEffectivelyPublic(JavaType type)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                _definitions.TryGetValue(type.Name, out var original);
                var hints = original is not null && ReferenceEquals(original.Info.Type, type) ? original.Info.Nesting : null;
                string? name = type.Name;
                while (name is not null)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    if (!seen.Add(name) || seen.Count > MaxEnclosingDepth)
                    {
                        throw new InvalidDataException($"Invalid enclosing chain for Java type '{type.Name}': cycle or more than {MaxEnclosingDepth} enclosing declarations.");
                    }
                    if (_api.Types.TryGetValue(name, out var enclosing))
                    {
                        if ((enclosing.AccessFlags & 0x0001) == 0)
                        {
                            return false;
                        }
                        name = enclosing.EnclosingType;
                    }
                    else if (hints is not null && hints.TryGetValue(name, out var hint))
                    {
                        if ((hint.AccessFlags & 0x0001) == 0)
                        {
                            return false;
                        }
                        name = hint.EnclosingType;
                    }
                    else
                    {
                        var warning = $"Cannot verify public enclosing type '{name}' for '{type.Name}'; the nested declaration is retained as nonpublic metadata.";
                        if (_visibilityWarnings.Add(warning))
                        {
                            _api.Warnings.Add(warning);
                        }
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>Owns a bounded set of class buffers awaiting CPU analysis and ordered commit.</summary>
        private sealed class ClassBatch
        {
            /// <summary>Gets the class inputs in their original scan order.</summary>
            internal List<BufferedClass> Items { get; } = new List<BufferedClass>();

            /// <summary>Gets or sets the uncompressed bytes awaiting ordered commit.</summary>
            internal long Bytes { get; set; }

            /// <summary>Gets whether these declarations came from explicit inputs.</summary>
            internal bool Additional { get; }

            /// <summary>Creates a batch with one consistent duplicate precedence.</summary>
            /// <param name="additional">Whether the inputs override SDK declarations.</param>
            internal ClassBatch(bool additional)
            {
                Additional = additional;
            }
        }

        /// <summary>Owns one class buffer and the result or failure written only by its CPU worker.</summary>
        private sealed class BufferedClass
        {
            /// <summary>Gets the exclusively read complete class bytes.</summary>
            internal byte[] Bytes { get; }

            /// <summary>Gets the input path or archive entry used in diagnostics.</summary>
            internal string Source { get; }

            /// <summary>Gets or sets the independently parsed declaration and byte identity.</summary>
            internal ScannedClass? Definition { get; set; }

            /// <summary>Gets or sets a parse failure to rethrow during ordered commit.</summary>
            internal ExceptionDispatchInfo? Failure { get; set; }

            /// <summary>Creates an input slot before parallel analysis begins.</summary>
            /// <param name="bytes">The bounded complete class bytes.</param>
            /// <param name="source">The originating path or archive entry.</param>
            internal BufferedClass(byte[] bytes, string source)
            {
                Bytes = bytes;
                Source = source;
            }
        }

        /// <summary>Keeps a chosen definition's content identity and input precedence.</summary>
        private sealed class ScannedClass
        {
            /// <summary>Gets the parsed declaration and enclosing hints.</summary>
            internal JavaClassInfo Info { get; }

            /// <summary>Gets the SHA-256 identity of the complete original class bytes.</summary>
            internal string Hash { get; }

            /// <summary>Gets the originating file or archive entry.</summary>
            internal string Source { get; }

            /// <summary>Gets whether the declaration came from an explicit input.</summary>
            internal bool Additional { get; }

            /// <summary>Creates provenance for one chosen class definition.</summary>
            /// <param name="info">The parsed declaration.</param>
            /// <param name="hash">The complete class content identity.</param>
            /// <param name="source">The original file or archive entry.</param>
            /// <param name="additional">Whether the declaration was explicitly supplied.</param>
            internal ScannedClass(JavaClassInfo info, string hash, string source, bool additional)
            {
                Info = info;
                Hash = hash;
                Source = source;
                Additional = additional;
            }
        }
    }
}

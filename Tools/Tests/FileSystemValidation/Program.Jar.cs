#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;
using NativeFile = System.IO.File;
using StorageFacade = MajdataPlay.IO.Storage.FileSystem;

namespace MajdataPlay.Tests.FileSystemValidation
{
    /// <summary>Validates the production read-only APK/JAR backend without Unity or an Android device.</summary>
    internal static partial class Program
    {
        /// <summary>The Unicode fixture content shared by synchronous and asynchronous text reads.</summary>
        private const string JarText = "APK 内资源: café 🎵\r\nSecond line\n";

        /// <summary>The archive display name which requires escaping inside a JAR URI.</summary>
        private const string JarUnicodeName = "音符 #50% café.txt";

        /// <summary>Checks automatic selection, ZIP metadata and all supported read helpers.</summary>
        /// <returns>A task completing after synchronous and asynchronous reads.</returns>
        /// <exception cref="InvalidOperationException">An archive read assertion fails.</exception>
        private static async Task TestJarReads()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var text = StorageFacade.File.Open(JarLocation(archivePath, "assets/text.txt"));
            var binary = StorageFacade.File.Open(JarLocation(archivePath, "assets/nested/deep.bin"));
            var directory = StorageFacade.Directory.Open(JarLocation(archivePath, "assets"));
            Check(ReferenceEquals(text.FileSystem, directory.FileSystem), "jar files and directories automatically select one shared backend");
            Check(!ReferenceEquals(text.FileSystem, workspace.CreateLocalDirectory().FileSystem), "jar locations select a distinct backend from native paths");
            Check(text.Exists && !StorageFacade.Directory.Open(text.Location).Exists, "jar file metadata preserves its entry type");
            Check(directory.Exists && !StorageFacade.File.Open(directory.Location).Exists, "implicit archive directories preserve their entry type");
            Check(Entry(text).Name == "text.txt" && Entry(text).Length == Encoding.UTF8.GetByteCount(JarText), "jar file metadata exposes the display name and uncompressed size");
            Check(Entry(text).LastWriteTimeUtc is { Kind: DateTimeKind.Utc }, "ZIP timestamps are exposed as UTC metadata");
            Check(!Entry(text).IsSymbolicLink && Entry(directory).Length is null, "archive metadata reports ordinary files and directories");
            Check(text.ReadAllText() == JarText, "synchronous ZIP text reads preserve Unicode and line endings");
            EqualBytes(Payload(), binary.ReadAllBytes(), "compressed entries support reads beyond one transfer buffer");
            Check(await text.ReadAllTextAsync() == JarText, "asynchronous ZIP text reads preserve Unicode and line endings");
            EqualBytes(Payload(), await binary.ReadAllBytesAsync(), "asynchronous compressed reads return complete data");
            var utf16 = StorageFacade.File.Open(JarLocation(archivePath, "assets/utf16.txt"));
            Check(utf16.ReadAllText() == JarText && await utf16.ReadAllTextAsync() == JarText, "archive text helpers detect a UTF-16 BOM");
            var empty = StorageFacade.File.Open(JarLocation(archivePath, "assets/empty.bin"));
            EqualBytes(Array.Empty<byte>(), empty.ReadAllBytes(), "empty stored entries are readable");
            EqualBytes(Array.Empty<byte>(), await empty.ReadAllBytesAsync(), "empty stored entries are asynchronously readable");
            using var direct = StorageFacade.File.OpenRead(text.Location);
            using var reader = new StreamReader(direct, Encoding.UTF8);
            Check(reader.ReadToEnd() == JarText, "facade OpenRead routes directly to archive content");
        }

        /// <summary>Checks implicit ZIP directories, immediate enumeration and exactly-once URI escaping.</summary>
        /// <returns>A completed task after directory and child-name checks.</returns>
        /// <exception cref="InvalidOperationException">An archive navigation assertion fails.</exception>
        private static Task TestJarChildren()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var root = StorageFacade.Directory.Open(JarLocation(archivePath, string.Empty));
            EqualNames(new[] { "assets", "manifest.xml" }, root.EnumerateEntries().Select(entry => entry.Name), "jar root lists only immediate entries");
            var assets = NotNull(root.FindDirectory("assets"), "an archive prefix creates an implicit directory");
            EqualNames(new[] { "text.txt", "utf16.txt", "empty.bin", JarUnicodeName, "%2F.txt", "%5C.txt" },
                assets.EnumerateFiles().Select(file => Entry(file).Name), "archive file enumeration excludes all descendant files");
            EqualNames(new[] { "nested", "empty directory" }, assets.EnumerateDirectories().Select(directory => Entry(directory).Name),
                "archive directory enumeration includes implicit and explicit directories once");
            var nested = NotNull(assets.FindDirectory("nested"), "implicit nested directory lookup succeeds");
            EqualNames(new[] { "deep.bin" }, nested.EnumerateEntries().Select(entry => entry.Name), "nested archive enumeration stays immediate");
            Check(NotNull(assets.FindDirectory("empty directory"), "explicit empty directory exists").EnumerateEntries().Count() == 0,
                "explicit empty directories have no fabricated children");
            Check(assets.FindFile("nested") is null && assets.FindDirectory("text.txt") is null, "typed archive lookups reject mismatching entry types");
            Check(assets.FindFile("missing") is null && assets.FindDirectory("missing") is null, "missing archive children return null");
            Check(assets.FindFile("TEXT.TXT") is null, "ZIP entry lookup remains case-sensitive on every host");
            foreach (var name in new[] { JarUnicodeName, "%2F.txt", "%5C.txt" })
            {
                var child = NotNull(assets.FindFile(name), "escaped archive display name can be located");
                Check(Entry(child).Name == name && child.ReadAllText() == name, "escaped archive display names are decoded exactly once");
                Check(StorageFacade.File.Open(child.Location).ReadAllText() == name, "returned archive child URIs can be reopened through the facade");
                Check(StorageFacade.File.Open(JarLocation(archivePath, "assets/" + name)).ReadAllText() == name,
                    "explicit escaped entry URI addresses the same display name");
            }
            Check(NotNull(assets.FindFile("%2F.txt"), "literal percent-slash name exists").Location.Contains("%252F", StringComparison.OrdinalIgnoreCase),
                "literal percent sequences are escaped rather than interpreted as separators");
            foreach (var invalid in new[] { "..", ".", "../text.txt", "nested/deep.bin", "nested\\deep.bin", "/text.txt", "bad\0name" })
            {
                Throws<ArgumentException>(() =>
                {
                    assets.FindFile(invalid);
                }, "archive child lookup rejects traversal and multi-component names");
            }
            return Task.CompletedTask;
        }

        /// <summary>Checks relative-path descendant lookup walks implicit archive directories without creating anything.</summary>
        /// <returns>A completed task after archive descendant validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestJarDescendants()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var root = StorageFacade.Directory.Open(JarLocation(archivePath, string.Empty));
            var deep = NotNull(root.FindFileAt("assets/nested/deep.bin"), "archive descendant file lookup walks implicit directories");
            Check(deep.Location == StorageFacade.File.Open(JarLocation(archivePath, "assets/nested/deep.bin")).Location, "archive descendant lookup returns the canonical entry URI");
            Check(NotNull(root.FindDirectoryAt("assets\\empty directory"), "archive descendant directory lookup accepts backslashes").EnumerateEntries().Count() == 0,
                "archive descendant directory lookup resolves explicit empty directories");
            Check(root.FindFileAt("assets/missing/deep.bin") is null && root.FindDirectoryAt("assets/nested/deep.bin") is null, "missing or mistyped archive descendants return null");
            Check(NotNull(root.FindFileAt("assets/" + JarUnicodeName), "escaped archive display names resolve through relative paths").ReadAllText() == JarUnicodeName,
                "relative lookup escapes archive entry names exactly once");
            Throws<NotSupportedException>(() =>
            {
                root.CreateDirectoryAt("assets/new directory");
            }, "relative creation inside a read-only archive is rejected");
            Check(root.FindDirectoryAt("assets/new directory") is null, "rejected archive creation leaves no entry");
            return Task.CompletedTask;
        }

        /// <summary>Checks facade and handle extraction into native paths and opaque provider documents.</summary>
        /// <returns>A task completing after both synchronous and asynchronous copy directions.</returns>
        /// <exception cref="InvalidOperationException">An extraction assertion fails.</exception>
        private static async Task TestJarExtraction()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var originalArchive = NativeFile.ReadAllBytes(archivePath);
            var source = StorageFacade.File.Open(JarLocation(archivePath, "assets/nested/deep.bin"));
            var local = workspace.CreateLocalDirectory();
            var provider = RegisterMemoryProvider();
            var content = new StorageDirectory(provider, provider.RootLocation);
            var nativeCopy = StorageFacade.File.Copy(source.Location, workspace.GetPath("facade.bin"));
            EqualBytes(Payload(), nativeCopy.ReadAllBytes(), "facade extracts archive content into a new native path");
            var fileUriCopy = StorageFacade.File.Copy(source.Location, new Uri(workspace.GetPath("facade #名称.bin")).AbsoluteUri);
            EqualBytes(Payload(), fileUriCopy.ReadAllBytes(), "facade extracts archive content into an encoded native file URI");
            foreach (var asynchronous in new[] { false, true })
            {
                var suffix = asynchronous ? "async" : "sync";
                var extracted = await Copy(source, local, "extracted-" + suffix + ".bin", asynchronous);
                EqualBytes(Payload(), extracted.ReadAllBytes(), "archive-to-native handle extraction returns all bytes");
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(source, local, Entry(extracted).Name, asynchronous);
                }, "archive extraction requires explicit overwrite for an existing native child");
                extracted.WriteAllBytes(new byte[] { 1, 2, 3 });
                EqualBytes(Payload(), (await Copy(source, local, Entry(extracted).Name, asynchronous, overwrite: true)).ReadAllBytes(),
                    "archive extraction supports native overwrite without source mutation");
                var remote = await Copy(source, content, "opaque-" + suffix + ".bin", asynchronous);
                Check(ReferenceEquals(remote.FileSystem, provider), "archive extraction retains the opaque destination backend");
                EqualBytes(Payload(), remote.ReadAllBytes(), "archive extraction works with non-seekable opaque destination streams");
                var existing = content.CreateFile("existing-" + suffix + ".bin");
                existing.WriteAllText("old");
                var replaced = source.CopyTo(existing, overwrite: true);
                Check(replaced.Location == existing.Location, "archive-to-existing-document overwrite retains the authoritative identity");
                EqualBytes(Payload(), existing.ReadAllBytes(), "archive-to-existing-document overwrite returns the complete source");
            }
            var facadeChild = StorageFacade.File.Copy(source.Location, content.Location, "facade-child.bin");
            EqualBytes(Payload(), facadeChild.ReadAllBytes(), "facade child extraction creates an opaque destination document");
            var facadeTarget = content.CreateFile("facade-target.bin");
            StorageFacade.File.Copy(source.Location, facadeTarget.Location, overwrite: true);
            EqualBytes(Payload(), facadeTarget.ReadAllBytes(), "facade extraction supports existing opaque document locations");
            provider.NewFileWriteFailureAfterBytes = 7;
            foreach (var asynchronous in new[] { false, true })
            {
                var failedName = "failed-extraction-" + asynchronous + ".bin";
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(source, content, failedName, asynchronous);
                }, "archive extraction reports an opaque destination write failure");
                Check(content.FindFile(failedName) is null, "failed archive extraction removes only its new destination after stream closure");
            }
            StreamsClosed(provider);
            EqualBytes(originalArchive, NativeFile.ReadAllBytes(archivePath), "all extraction operations preserve the source archive bytes");
            using var exclusive = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(exclusive.Length > 0, "successful and failed extractions release every archive source stream");
        }

        /// <summary>Checks pre-cancelled reads and copies before archive streams or destination writes are opened.</summary>
        /// <returns>A task completing after cancellation assertions.</returns>
        /// <exception cref="InvalidOperationException">A cancellation assertion fails.</exception>
        private static async Task TestJarCancellation()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var source = StorageFacade.File.Open(JarLocation(archivePath, "assets/nested/deep.bin"));
            var local = workspace.CreateLocalDirectory();
            var provider = RegisterMemoryProvider();
            var content = new StorageDirectory(provider, provider.RootLocation);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return source.ReadAllBytesAsync(cancellation.Token);
            }, "pre-cancelled archive binary reads report cancellation");
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return source.ReadAllTextAsync(cancellationToken: cancellation.Token);
            }, "pre-cancelled archive text reads report cancellation");
            foreach (var destination in new[] { local, content })
            {
                foreach (var asynchronous in new[] { false, true })
                {
                    var existing = destination.CreateFile("existing-" + asynchronous + ".bin");
                    existing.WriteAllText("preserved");
                    var writesBefore = provider.OpenedWriteStreamCount;
                    await ThrowsAsync<OperationCanceledException>(() =>
                    {
                        return Copy(source, destination, "cancelled-" + asynchronous + ".bin", asynchronous, cancellationToken: cancellation.Token);
                    }, "pre-cancelled archive extraction does not create a new destination");
                    Check(destination.FindFile("cancelled-" + asynchronous + ".bin") is null, "cancelled extraction leaves no newly created file");
                    await ThrowsAsync<OperationCanceledException>(() =>
                    {
                        return Copy(source, destination, Entry(existing).Name, asynchronous, overwrite: true, cancellationToken: cancellation.Token);
                    }, "pre-cancelled archive extraction refuses an existing destination before truncation");
                    Check(existing.ReadAllText() == "preserved", "pre-cancelled extraction preserves destination bytes");
                    Check(provider.OpenedWriteStreamCount == writesBefore, "pre-cancelled extraction opens no writable provider stream");
                }
            }
            StreamsClosed(provider);
            using var exclusive = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(exclusive.Length > 0, "cancelled archive helpers leave no underlying archive handles open");
        }

        /// <summary>Checks read-only backend operations and replacement preflight before any destination mutation.</summary>
        /// <returns>A task completing after synchronous and asynchronous read-only checks.</returns>
        /// <exception cref="InvalidOperationException">A mutation is accepted or changes archive or destination bytes.</exception>
        private static async Task TestJarReadOnly()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var before = NativeFile.ReadAllBytes(archivePath);
            var source = StorageFacade.File.Open(JarLocation(archivePath, "assets/text.txt"));
            var assets = StorageFacade.Directory.Open(JarLocation(archivePath, "assets"));
            Check(StorageFacade.Directory.Create(assets.Location).Location == assets.Location,
                "opening an existing jar directory through Create is an idempotent operation");
            var local = workspace.CreateLocalDirectory();
            var localSource = local.CreateFile("local.bin");
            localSource.WriteAllText("native preserved");
            var provider = RegisterMemoryProvider();
            var content = new StorageDirectory(provider, provider.RootLocation);
            var remoteTarget = content.CreateFile("remote.bin");
            remoteTarget.WriteAllText("opaque preserved");
            var mutations = new Action[]
            {
                () => source.OpenWrite().Dispose(),
                () => source.OpenWrite(append: true).Dispose(),
                () => source.Delete(),
                () => source.Rename("renamed.txt"),
                () => source.SetAttributes(FileAttributes.Hidden),
                () => source.SetLastWriteTime(DateTime.UtcNow),
                () => assets.CreateFile("new.bin"),
                () => assets.CreateDirectory("new directory"),
                () => assets.Delete(recursive: true),
                () => assets.Rename("new assets"),
                () => assets.SetAttributes(FileAttributes.Hidden),
                () => assets.SetLastWriteTime(DateTime.UtcNow),
                () => StorageFacade.File.Create(source.Location, overwrite: true),
                () => StorageFacade.File.Create(assets.Location, "created.bin"),
                () => StorageFacade.Directory.Create(JarLocation(archivePath, "assets/missing directory")),
                () => StorageFacade.Directory.Create(assets.Location, "created directory"),
                () => StorageFacade.File.OpenWrite(source.Location, overwrite: true).Dispose(),
                () => StorageFacade.File.Move(source.Location, workspace.GetPath("moved.txt")),
                () => StorageFacade.File.Move(source.Location, assets.Location, "moved.txt"),
                () => StorageFacade.Directory.Move(assets.Location, workspace.GetPath("moved directory")),
                () => StorageFacade.Directory.Move(assets.Location, assets.Location, "moved directory"),
                () => StorageFacade.File.Replace(source.Location, localSource.Location),
                () => StorageFacade.File.Replace(source.Location, remoteTarget.Location),
                () => StorageFacade.File.Replace(localSource.Location, source.Location),
                () => StorageFacade.File.Copy(localSource.Location, source.Location, overwrite: true),
                () => StorageFacade.File.Copy(localSource.Location, assets.Location, "new-copy.bin"),
            };
            foreach (var mutation in mutations)
            {
                Throws<NotSupportedException>(mutation, "jar mutation is rejected as read-only");
            }
            await ThrowsAsync<NotSupportedException>(() =>
            {
                return source.WriteAllBytesAsync(new byte[] { 1 });
            }, "asynchronous archive writes remain read-only");
            await ThrowsAsync<NotSupportedException>(() =>
            {
                return localSource.CopyToAsync(assets, "text.txt", overwrite: true);
            }, "asynchronous copies cannot overwrite an archive entry");
            EqualBytes(before, NativeFile.ReadAllBytes(archivePath), "rejected mutations never change the archive bytes");
            Check(localSource.ReadAllText() == "native preserved", "read-only replacement rejection preserves native destination and source bytes");
            Check(remoteTarget.ReadAllText() == "opaque preserved", "read-only replacement rejection preserves an opaque destination");
            Check(source.ReadAllText() == JarText && assets.FindFile("new-copy.bin") is null, "archive entries and directory contents remain unchanged");
            StreamsClosed(provider);
        }

        /// <summary>Checks malformed and unsafe JAR locations before querying an archive.</summary>
        /// <returns>A completed task after validating both facade entry points.</returns>
        /// <exception cref="InvalidOperationException">An invalid URI is accepted or misclassified.</exception>
        private static Task TestJarLocations()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var archiveUri = new Uri(archivePath).AbsoluteUri;
            foreach (var invalid in new[]
            {
                "jar:", "jar:!/assets/text.txt", "jar:file://!/assets/text.txt", "jar:relative.apk!/assets/text.txt",
                "jar:" + archiveUri, "jar:" + archiveUri + "!assets/text.txt",
                "jar:" + archiveUri + "!/assets/../text.txt", "jar:" + archiveUri + "!/assets/./text.txt",
                "jar:" + archiveUri + "!/assets/%2E%2E/text.txt", "jar:" + archiveUri + "!/assets/a%2Fb.txt",
                "jar:" + archiveUri + "!/assets/a%5Cb.txt", "jar:" + archiveUri + "!/assets//text.txt",
                "jar:" + archiveUri + "!//assets/text.txt", "jar:" + archiveUri + "!/assets/bad%00name",
                "jar:" + archiveUri + "!/assets/bad%2Gname", "jar:" + archiveUri + "!/assets/trailing%",
                "jar:" + archiveUri + "!/assets/text.txt#fragment", "jar:" + archiveUri + "!/assets/text.txt?query",
                "jar:" + archiveUri + "?query!/assets/text.txt", "jar:" + archiveUri + "#fragment!/assets/text.txt",
            })
            {
                Throws<ArgumentException>(() =>
                {
                    StorageFacade.File.Open(invalid);
                }, "file handles reject malformed or unsafe jar URI: " + invalid);
                Throws<ArgumentException>(() =>
                {
                    StorageFacade.Directory.Open(invalid);
                }, "directory handles reject malformed or unsafe jar URI: " + invalid);
            }
            foreach (var unsupported in new[]
            {
                "jar:https://example.invalid/application.apk!/assets/text.txt",
                "jar:content://example.invalid/application.apk!/assets/text.txt",
                "jar:jar:" + archiveUri + "!/nested.jar!/assets/text.txt",
            })
            {
                Throws<NotSupportedException>(() =>
                {
                    StorageFacade.File.Open(unsupported);
                }, "remote and nested jar archives are unsupported");
                Throws<NotSupportedException>(() =>
                {
                    StorageFacade.Directory.Open(unsupported);
                }, "directory routing rejects remote and nested jar archives");
            }
            return Task.CompletedTask;
        }

        /// <summary>Checks archive path escaping independently of ZIP entry escaping and canonical handle reopening.</summary>
        /// <returns>A completed task after checking archive names supported by the native host.</returns>
        /// <exception cref="InvalidOperationException">Canonicalization changes which native archive file is addressed.</exception>
        private static Task TestJarArchiveNames()
        {
            using var workspace = new TemporaryWorkspace();
            var originalArchive = CreateJarArchive(workspace);
            foreach (var name in new[] { "application%20literal.apk", "application%2Fliteral.apk", "application #!literal.apk", "application ?literal.apk" })
            {
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    continue;
                }
                var path = workspace.GetPath(name);
                NativeFile.Copy(originalArchive, path);
                var location = JarLocation(path, "assets/text.txt");
                if (name.Contains("%20", StringComparison.Ordinal))
                {
                    Check(location.Contains("%2520", StringComparison.OrdinalIgnoreCase), "fixture URI preserves the native archive filename's literal percent sequence");
                }
                var file = StorageFacade.File.Open(location);
                Check(file.ReadAllText() == JarText, "archive names retain escaped-looking text and reserved characters during opening");
                Check(StorageFacade.File.Open(file.Location).ReadAllText() == JarText, "canonical archive file handles reopen the same native filename");
                Check(StorageFacade.File.Open(Entry(file).Location).ReadAllText() == JarText, "authoritative metadata retains the same native archive filename");
                var root = StorageFacade.Directory.Open(JarLocation(path, string.Empty));
                var assets = NotNull(root.FindDirectory("assets"), "archive-name escaping survives directory child lookup");
                Check(NotNull(assets.FindFile("text.txt"), "escaped archive still contains its child file").ReadAllText() == JarText,
                    "enumerated and looked-up children keep the canonical escaped archive filename");
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Check(exclusive.Length > 0, "canonical archive metadata and navigation release their native handles");
            }
            return Task.CompletedTask;
        }

        /// <summary>Checks corrupt ZIP data and duplicate or colliding records without creating or truncating extraction targets.</summary>
        /// <returns>A task completing after synchronous and asynchronous extraction failures.</returns>
        /// <exception cref="InvalidOperationException">An invalid archive is treated as absent or mutates a destination.</exception>
        private static async Task TestJarInvalidArchives()
        {
            using var workspace = new TemporaryWorkspace();
            var local = workspace.CreateLocalDirectory();
            var corruptPath = workspace.GetPath("corrupt.apk");
            NativeFile.WriteAllText(corruptPath, "This is not a ZIP archive.");
            var duplicatePath = workspace.GetPath("duplicate.apk");
            var collisionPath = workspace.GetPath("collision.apk");
            foreach (var path in new[] { duplicatePath, collisionPath })
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                AddJarEntry(archive, "assets/text.txt", Encoding.UTF8.GetBytes(JarText));
                AddJarEntry(archive, path == duplicatePath ? "assets/text.txt" : "assets", Encoding.UTF8.GetBytes("ambiguous"));
            }
            foreach (var path in new[] { corruptPath, duplicatePath, collisionPath })
            {
                var source = StorageFacade.File.Open(JarLocation(path, "assets/text.txt"));
                var root = StorageFacade.Directory.Open(JarLocation(path, string.Empty));
                var failureType = path == corruptPath ? typeof(InvalidDataException) : typeof(IOException);
                try
                {
                    _ = source.Entry;
                    Check(false, "invalid archive metadata cannot be mistaken for a missing entry");
                }
                catch (Exception exception) when (exception.GetType() == failureType)
                {
                    Check(true, "invalid archive metadata preserves its corruption or ambiguity exception");
                }
                try
                {
                    root.EnumerateEntries().ToArray();
                    Check(false, "invalid archive directories cannot be enumerated");
                }
                catch (Exception exception) when (exception.GetType() == failureType)
                {
                    Check(true, "archive enumeration preserves its corruption or ambiguity exception");
                }
                foreach (var asynchronous in new[] { false, true })
                {
                    var name = Path.GetFileName(path) + "-" + asynchronous + ".bin";
                    var existing = local.CreateFile("existing-" + name);
                    existing.WriteAllText("preserved");
                    foreach (var overwrite in new[] { false, true })
                    {
                        var targetName = overwrite ? Entry(existing).Name : name;
                        try
                        {
                            await Copy(source, local, targetName, asynchronous, overwrite);
                            Check(false, "invalid archive extraction cannot succeed");
                        }
                        catch (Exception exception) when (exception.GetType() == failureType)
                        {
                            Check(true, "archive extraction preserves the source corruption or ambiguity exception");
                        }
                    }
                    Check(local.FindFile(name) is null, "source archive failure creates no extraction destination");
                    Check(existing.ReadAllText() == "preserved", "source archive failure never truncates an existing extraction destination");
                }
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Check(exclusive.Length > 0, "invalid archive metadata, enumeration and copy failures release all native handles");
            }
        }

        /// <summary>Checks missing entries and deterministic archive stream ownership through independent readers.</summary>
        /// <returns>A completed task after ownership and absence assertions.</returns>
        /// <exception cref="InvalidOperationException">An archive stream is leaked or an entry is misclassified.</exception>
        private static Task TestJarStreamOwnership()
        {
            using var workspace = new TemporaryWorkspace();
            var archivePath = CreateJarArchive(workspace);
            var source = StorageFacade.File.Open(JarLocation(archivePath, "assets/nested/deep.bin"));
            var missing = StorageFacade.File.Open(JarLocation(archivePath, "assets/missing.bin"));
            Check(!missing.Exists && missing.Entry is null, "missing archive files have no metadata");
            Throws<FileNotFoundException>(() =>
            {
                missing.OpenRead().Dispose();
            }, "missing archive file streams fail with FileNotFoundException");
            var missingDirectory = StorageFacade.Directory.Open(JarLocation(archivePath, "missing directory"));
            Check(!missingDirectory.Exists, "missing archive directories do not exist");
            Throws<DirectoryNotFoundException>(() =>
            {
                missingDirectory.EnumerateEntries().ToArray();
            }, "enumerating a missing archive directory reports its absence");
            var absentArchive = StorageFacade.File.Open(JarLocation(workspace.GetPath("missing.apk"), "assets/file.bin"));
            Check(!absentArchive.Exists && absentArchive.Entry is null, "a missing archive has no entry metadata");
            Throws<FileNotFoundException>(() =>
            {
                absentArchive.OpenRead().Dispose();
            }, "opening a missing archive reports FileNotFoundException");
            Throws<NotSupportedException>(() =>
            {
                StorageFacade.File.Open(JarLocation(archivePath, "assets")).OpenRead().Dispose();
            }, "an archive directory cannot be opened as a binary file");
            using var first = source.OpenRead();
            using (var second = source.OpenRead())
            {
                var expected = Payload();
                Check(first.CanRead && !first.CanWrite && second.CanRead && !second.CanWrite, "archive readers are independently readable and never writable");
                Check(first.ReadByte() == expected[0] && second.ReadByte() == expected[0], "parallel archive streams maintain independent positions");
                Check(source.Exists && StorageFacade.Directory.Open(JarLocation(archivePath, "assets")).EnumerateEntries().Any(),
                    "metadata queries and enumeration do not close caller-owned archive streams");
                first.Dispose();
                first.Dispose();
                Check(!first.CanRead, "disposing an entry stream closes that reader and permits repeated disposal");
                Check(second.ReadByte() == expected[1], "disposing one entry reader leaves another archive reader usable");
                using var remainder = new MemoryStream();
                second.CopyTo(remainder);
                EqualBytes(expected.Skip(2).ToArray(), remainder.ToArray(), "the remaining reader keeps its archive alive until it is disposed");
            }
            using var exclusive = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Check(exclusive.Length > 0, "archive queries, failed opens and disposed entry streams release every native file handle");
            return Task.CompletedTask;
        }

        /// <summary>Creates a portable APK-shaped ZIP entirely inside the harness-owned temporary directory.</summary>
        /// <param name="workspace">The existing temporary root responsible for safe cleanup.</param>
        /// <returns>The absolute native archive path.</returns>
        /// <exception cref="IOException">Creating or writing the temporary archive fails.</exception>
        private static string CreateJarArchive(TemporaryWorkspace workspace)
        {
            var archivePath = workspace.GetPath("application #100% café.apk");
            using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            AddJarEntry(archive, "manifest.xml", Encoding.UTF8.GetBytes("<manifest />"));
            AddJarEntry(archive, "assets/text.txt", Encoding.UTF8.GetBytes(JarText));
            AddJarEntry(archive, "assets/nested/deep.bin", Payload());
            AddJarEntry(archive, "assets/empty.bin", Array.Empty<byte>(), CompressionLevel.NoCompression);
            AddJarEntry(archive, "assets/utf16.txt", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(JarText)).ToArray());
            AddJarEntry(archive, "assets/empty directory/", Array.Empty<byte>());
            foreach (var name in new[] { JarUnicodeName, "%2F.txt", "%5C.txt" })
            {
                AddJarEntry(archive, "assets/" + name, Encoding.UTF8.GetBytes(name));
            }
            return archivePath;
        }

        /// <summary>Adds one exact ZIP name and byte sequence without rewriting its path components.</summary>
        /// <param name="archive">The temporary archive currently being created.</param>
        /// <param name="name">The exact ZIP entry name, using forward slash separators.</param>
        /// <param name="bytes">The complete uncompressed content.</param>
        /// <param name="compression">The compression mode, including stored-entry coverage.</param>
        /// <exception cref="IOException">Writing the temporary archive entry fails.</exception>
        private static void AddJarEntry(ZipArchive archive, string name, byte[] bytes, CompressionLevel compression = CompressionLevel.Optimal)
        {
            var entry = archive.CreateEntry(name, compression);
            entry.LastWriteTime = new DateTimeOffset(2026, 10, 10, 12, 34, 56, TimeSpan.Zero);
            using var output = entry.Open();
            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Builds a JAR URI by independently escaping each exact ZIP path component once.</summary>
        /// <param name="archivePath">The absolute native APK/ZIP file path.</param>
        /// <param name="entryName">The unescaped ZIP entry name, or empty for the archive root.</param>
        /// <returns>A jar:file URI which retains literal percent sequences and Unicode names.</returns>
        private static string JarLocation(string archivePath, string entryName)
        {
            var escapedName = string.Join("/", entryName.Split('/').Select(Uri.EscapeDataString));
            return "jar:" + new Uri(archivePath).AbsoluteUri + "!/" + escapedName;
        }
    }
}

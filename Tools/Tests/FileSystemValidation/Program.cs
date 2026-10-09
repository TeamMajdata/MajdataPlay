#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO.Storage;
using NativeDirectory = System.IO.Directory;
using NativeFile = System.IO.File;
using StorageFacade = MajdataPlay.IO.Storage.FileSystem;

namespace MajdataPlay.Tests.FileSystemValidation
{
    /// <summary>Runs package-free regressions against the linked production storage implementations.</summary>
    internal static class Program
    {
        /// <summary>Counts assertions across all independent cases.</summary>
        private static int s_assertions;

        /// <summary>Counts cases which passed without a platform skip.</summary>
        private static int s_passed;

        /// <summary>Counts cases which failed and must produce a nonzero exit code.</summary>
        private static int s_failed;

        /// <summary>Counts unavailable symbolic-link capabilities.</summary>
        private static int s_skipped;

        /// <summary>Runs cases sequentially so global content-provider registration is deterministic.</summary>
        /// <returns>Zero on success, or one if any regression fails.</returns>
        private static async Task<int> Main()
        {
            var cases = new (string Name, Func<Task> Run)[]
            {
                ("facade: unregistered provider and unsupported schemes", TestFacadeErrors),
                ("entry metadata contracts", TestMetadata),
                ("facade: hidden local backend and automatic selection", TestFacadeEncapsulation),
                ("local: facade, creation, encoded file URIs", TestLocalLocations),
                ("local: bytes, existing-only writes, append and truncate", TestLocalByteIO),
                ("local: non-ASCII text, BOM and explicit encodings", TestLocalTextIO),
                ("local: missing entries and type collisions", TestLocalErrors),
                ("local: immediate children, lookup and rename", TestLocalChildren),
                ("local: copy, overwrite and self-copy aliases", TestLocalCopy),
                ("local: single-child name and traversal validation", TestLocalNames),
                ("local: asynchronous I/O and pre-cancellation", TestLocalAsync),
                ("local: non-recursive and recursive deletion", TestLocalDelete),
                ("local: directory links do not delete outside targets", TestDirectoryLinks),
                ("local: file links do not delete outside targets", TestFileLinks),
                ("content: dispatch, opaque IDs and nullable metadata", TestContentDispatch),
                ("content: sequential non-seekable byte and text I/O", TestContentIO),
                ("content: authoritative children and changed rename IDs", TestContentChildren),
                ("content: single-child names remain URI-safe", TestContentNames),
                ("content: collisions and recursive deletion", TestContentDelete),
                ("content: permission and I/O errors are not absence", TestContentErrors),
                ("content: asynchronous I/O and pre-cancellation", TestContentAsync),
                ("content: mid-operation asynchronous read cancellation", TestContentReadCancellation),
                ("copy: distinct URI aliases with shared global ResourceId", TestResourceAliases),
                ("copy: both cross-provider directions, sync and async", TestCrossCopies),
                ("copy: new destination cleanup after source failure", TestCopyReadFailure),
                ("copy: new destination cleanup after destination failure", TestCopyWriteFailure),
                ("copy: pre- and mid-transfer cancellation cleanup", TestCopyCancellation),
                ("copy: failed overwrite never deletes an existing destination", TestCopyOverwriteFailure),
            };
            foreach (var test in cases)
            {
                await RunCase(test.Name, test.Run);
            }
            var marker = s_failed == 0 ? "FILESYSTEM_VALIDATION_PASSED" : "FILESYSTEM_VALIDATION_FAILED";
            Console.WriteLine($"{marker} ({s_passed} passed, {s_failed} failed, {s_skipped} skipped, {s_assertions} assertions)");
            return s_failed == 0 ? 0 : 1;
        }

        /// <summary>Reports failures independently instead of abandoning later coverage.</summary>
        /// <param name="name">The descriptive case name.</param>
        /// <param name="run">The synchronous or asynchronous regression case.</param>
        /// <returns>A task completing after reporting this case's result.</returns>
        private static async Task RunCase(string name, Func<Task> run)
        {
            try
            {
                await run();
                s_passed++;
                Console.WriteLine($"PASS {name}");
            }
            catch (SkipTestException exception)
            {
                s_skipped++;
                Console.WriteLine($"SKIP {name}: {exception.Message}");
            }
            catch (Exception exception)
            {
                s_failed++;
                Console.Error.WriteLine($"FAIL {name}{Environment.NewLine}{exception}");
            }
        }

        /// <summary>Checks scheme handling before any case registers a content provider.</summary>
        /// <returns>A completed task when the dispatch errors match the contract.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestFacadeErrors()
        {
            var uri = "content://filesystem.validation/document/unregistered%3Aroot%2F42";
            Throws<NotSupportedException>(() =>
            {
                StorageFacade.OpenFile(uri);
            }, "content files require a registered provider");
            Throws<NotSupportedException>(() =>
            {
                StorageFacade.OpenDirectory(uri);
            }, "content directories require a registered provider");
            foreach (var unsupported in new[] { "https://example.invalid/a", "ftp://example.invalid/a", "saf-test://authority/id" })
            {
                Throws<NotSupportedException>(() =>
                {
                    StorageFacade.OpenFile(unsupported);
                }, "unsupported file schemes are rejected");
                Throws<NotSupportedException>(() =>
                {
                    StorageFacade.OpenDirectory(unsupported);
                }, "unsupported directory schemes are rejected");
            }
            foreach (var invalid in new[] { null!, string.Empty })
            {
                Throws<ArgumentException>(() =>
                {
                    StorageFacade.OpenFile(invalid);
                }, "invalid file locations are rejected");
                Throws<ArgumentException>(() =>
                {
                    StorageFacade.OpenDirectory(invalid);
                }, "invalid directory locations are rejected");
            }
            return Task.CompletedTask;
        }

        /// <summary>Checks nullable metadata and validation without inventing a native path for a URI.</summary>
        /// <returns>A completed task after checking metadata invariants.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestMetadata()
        {
            var entry = new FileSystemEntry("content://example/document/id%2Fopaque", "谱面", false);
            Check(entry.Length is null && entry.LastWriteTimeUtc is null, "provider metadata can be unknown");
            Check(!entry.IsDirectory && !entry.IsSymbolicLink, "default entry flags describe a regular file");
            Check(entry.ResourceId is null, "resource identity can remain unknown");
            var identified = new FileSystemEntry("content://example/document/grant-alias%2F42", "identified", false, resourceId: "content://example/document/shared%3A42");
            Check(identified.ResourceId == "content://example/document/shared%3A42", "metadata retains its globally scoped comparison identity");
            var directory = new FileSystemEntry("opaque-directory", "directory", true, 99);
            Check(directory.Length is null, "directory length is always unknown");
            var utc = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
            var known = new FileSystemEntry("local-file", "file", false, 0, utc, true);
            Check(known.Length == 0 && known.LastWriteTimeUtc == utc && known.IsSymbolicLink, "known metadata is preserved");
            Throws<ArgumentOutOfRangeException>(() =>
            {
                _ = new FileSystemEntry("location", "name", false, -1);
            }, "negative lengths are rejected");
            Throws<ArgumentException>(() =>
            {
                _ = new FileSystemEntry("location", "name", false, lastWriteTimeUtc: DateTime.SpecifyKind(utc, DateTimeKind.Unspecified));
            }, "non-UTC timestamps are rejected");
            Throws<ArgumentException>(() =>
            {
                _ = new FileSystemEntry(string.Empty, "name", false);
            }, "empty metadata locations are rejected");
            Throws<ArgumentNullException>(() =>
            {
                _ = new FileSystemEntry("location", null!, false);
            }, "null display names are rejected");
            return Task.CompletedTask;
        }

        /// <summary>Verifies local implementation encapsulation and automatic path/file-URI backend selection.</summary>
        /// <returns>A completed task after checking the public facade and inferred local handles.</returns>
        /// <exception cref="InvalidOperationException">A visibility or backend-selection assertion fails.</exception>
        private static Task TestFacadeEncapsulation()
        {
            using var workspace = new TemporaryWorkspace();
            var path = workspace.GetPath("automatic", "file #名称.txt");
            var file = StorageFacade.OpenFile(path);
            var directory = StorageFacade.OpenDirectory(Path.GetDirectoryName(path)!);
            var uriFile = StorageFacade.OpenFile(new Uri(path).AbsoluteUri);
            var backendType = file.FileSystem.GetType();
            Check(backendType.IsNotPublic && !backendType.IsVisible, "local backend is internal and not externally visible");
            Check(!typeof(StorageFacade).Assembly.GetExportedTypes().Contains(backendType), "local backend is not exported from its assembly");
            Check(typeof(StorageFacade).GetMember("Local").Length == 0, "facade exposes no public Local backend entry point");
            Check(ReferenceEquals(file.FileSystem, directory.FileSystem), "native file and directory paths automatically select the same backend");
            Check(ReferenceEquals(file.FileSystem, uriFile.FileSystem), "encoded file URIs automatically select the same backend as native paths");
            Check(file.Location == path && uriFile.Location == path, "automatic selection preserves normalized native locations");
            Check(!file.Exists && !directory.Exists, "automatic selection does not create missing entries");
            return Task.CompletedTask;
        }

        /// <summary>Checks native paths and escaped file URI dispatch with portable special characters.</summary>
        /// <returns>A completed task after verifying creation and location round trips.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalLocations()
        {
            using var workspace = new TemporaryWorkspace();
            var path = workspace.GetPath("parent", "谱面 #50% café");
            var directory = StorageFacade.CreateLocalDirectory(path);
            Check(directory.Exists && NativeDirectory.Exists(path), "facade creates nested native directories");
            Check(ReferenceEquals(directory.FileSystem, StorageFacade.OpenDirectory(path).FileSystem), "created directory uses the automatically selected local backend");
            Check(ReferenceEquals(directory.FileSystem, StorageFacade.OpenFile(Path.Combine(path, "missing.bin")).FileSystem), "file and directory resolutions share a stable backend");
            Check(Entry(directory).IsDirectory, "local directory metadata has the correct type");
            var file = directory.CreateFile("音符 #50% café.txt");
            file.WriteAllText("内容: café 🎵");
            var uri = new Uri(file.Location).AbsoluteUri;
            Check(uri.Contains("%23", StringComparison.Ordinal) && uri.Contains("%25", StringComparison.Ordinal), "test exercises escaped URI characters");
            var reopened = StorageFacade.OpenFile(uri);
            Check(ReferenceEquals(reopened.FileSystem, directory.FileSystem), "file URI dispatch stays local");
            Check(reopened.ReadAllText() == "内容: café 🎵", "encoded file URI addresses the same bytes");
            Check(Path.GetFullPath(Entry(reopened).Location) == Path.GetFullPath(file.Location), "URI decoding preserves the native location");
            var directoryUri = new Uri(directory.Location + Path.DirectorySeparatorChar).AbsoluteUri;
            Check(StorageFacade.OpenDirectory(directoryUri).FindFile(Entry(file).Name) is not null, "encoded directory URI finds its immediate child");
            Check(StorageFacade.CreateLocalDirectory(path).Exists, "local directory creation is idempotent");
            Check(StorageFacade.OpenDirectory(path).Exists, "plain native directory path dispatch succeeds");
            return Task.CompletedTask;
        }

        /// <summary>Checks existing-only stream writes, truncation, append and refreshed metadata.</summary>
        /// <returns>A completed task after checking local binary I/O.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalByteIO()
        {
            using var workspace = new TemporaryWorkspace();
            var directory = workspace.CreateLocalDirectory();
            var file = directory.CreateFile("bytes.bin");
            Check(file.Exists && file.ReadAllBytes().Length == 0, "new local files are empty and exist");
            Check(ReferenceEquals(file.FileSystem, directory.FileSystem), "file handles retain their provider");
            var bytes = new byte[] { 0, 1, 127, 128, 255, 42 };
            file.WriteAllBytes(bytes);
            EqualBytes(bytes, file.ReadAllBytes(), "byte convenience methods preserve all byte values");
            Check(Entry(file).Length == bytes.Length, "entry length is refreshed after writing");
            Check(Entry(file).LastWriteTimeUtc?.Kind == DateTimeKind.Utc, "local timestamps are UTC");
            using (var read = file.OpenRead())
            {
                Check(read.ReadByte() == 0 && read.ReadByte() == 1, "OpenRead exposes caller-owned binary data");
            }
            using (var append = file.OpenWrite(append: true))
            {
                append.Write(new byte[] { 9, 10 }, 0, 2);
            }
            EqualBytes(bytes.Concat(new byte[] { 9, 10 }).ToArray(), file.ReadAllBytes(), "append preserves the old prefix");
            using (var truncate = file.OpenWrite())
            {
                truncate.WriteByte(7);
            }
            EqualBytes(new byte[] { 7 }, file.ReadAllBytes(), "default OpenWrite truncates old trailing bytes");
            file.WriteAllBytes(Array.Empty<byte>());
            Check(file.ReadAllBytes().Length == 0 && Entry(file).Length == 0, "empty writes truncate to zero");
            return Task.CompletedTask;
        }

        /// <summary>Checks BOM decoding, Unicode round trips and explicit text encodings.</summary>
        /// <returns>A completed task after verifying local text I/O.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalTextIO()
        {
            using var workspace = new TemporaryWorkspace();
            VerifyTextIO(workspace.CreateLocalDirectory().CreateFile("text.txt"));
            return Task.CompletedTask;
        }

        /// <summary>Checks missing entries and file/directory collisions without accidental creation.</summary>
        /// <returns>A completed task after checking local errors.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalErrors()
        {
            using var workspace = new TemporaryWorkspace();
            var directory = workspace.CreateLocalDirectory();
            var file = directory.CreateFile("occupied");
            file.WriteAllText("keep");
            var missing = StorageFacade.OpenFile(workspace.GetPath("local", "missing.bin"));
            VerifyMissingFile(missing);
            Throws<IOException>(() =>
            {
                directory.CreateFile("occupied");
            }, "exclusive creation cannot overwrite a file");
            Throws<IOException>(() =>
            {
                directory.CreateDirectory("occupied");
            }, "directory creation cannot replace a file");
            Throws<IOException>(() =>
            {
                StorageFacade.CreateLocalDirectory(file.Location);
            }, "facade directory creation rejects a file collision");
            var child = directory.CreateDirectory("child");
            Throws<IOException>(() =>
            {
                directory.CreateFile("child");
            }, "file creation cannot replace a directory");
            ThrowsIo(() =>
            {
                using var stream = StorageFacade.OpenFile(child.Location).OpenRead();
            }, "a directory cannot be opened as a file");
            ThrowsIo(() =>
            {
                StorageFacade.OpenFile(child.Location).Delete();
            }, "file deletion cannot remove a directory");
            Check(child.Exists && file.ReadAllText() == "keep", "type errors preserve existing entries");
            var absentDirectory = StorageFacade.OpenDirectory(workspace.GetPath("absent"));
            Check(!absentDirectory.Exists, "a missing directory reports absence");
            Throws<DirectoryNotFoundException>(() =>
            {
                _ = absentDirectory.EnumerateEntries().ToArray();
            }, "enumerating a missing parent fails");
            Throws<DirectoryNotFoundException>(() =>
            {
                absentDirectory.CreateFile("child");
            }, "creating a file does not create missing parents");
            Throws<DirectoryNotFoundException>(() =>
            {
                absentDirectory.CreateDirectory("child");
            }, "creating a child directory does not create missing parents");
            Throws<DirectoryNotFoundException>(() =>
            {
                absentDirectory.Delete();
            }, "deleting a missing directory reports the missing parent");
            return Task.CompletedTask;
        }

        /// <summary>Checks immediate enumeration, typed lookup and non-overwriting local rename.</summary>
        /// <returns>A completed task after checking child navigation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalChildren()
        {
            using var workspace = new TemporaryWorkspace();
            var directory = workspace.CreateLocalDirectory();
            VerifyChildren(directory);
            return Task.CompletedTask;
        }

        /// <summary>Checks overwrite truncation, type collisions and canonical self-copy prevention.</summary>
        /// <returns>A completed task after checking synchronous local copies.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalCopy()
        {
            using var workspace = new TemporaryWorkspace();
            var directory = workspace.CreateLocalDirectory();
            var source = directory.CreateFile("source.bin");
            var payload = Payload();
            source.WriteAllBytes(payload);
            var target = directory.CreateDirectory("target");
            var copied = source.CopyTo(target, "copied.bin");
            EqualBytes(payload, copied.ReadAllBytes(), "local copy preserves source data");
            Throws<IOException>(() =>
            {
                source.CopyTo(target, "copied.bin");
            }, "copy defaults to exclusive destination creation");
            EqualBytes(payload, copied.ReadAllBytes(), "collision does not alter the destination");
            source.WriteAllBytes(new byte[] { 3, 4 });
            EqualBytes(new byte[] { 3, 4 }, source.CopyTo(target, "copied.bin", overwrite: true).ReadAllBytes(), "overwrite truncates a longer destination");
            target.CreateDirectory("directory.bin");
            Throws<IOException>(() =>
            {
                source.CopyTo(target, "directory.bin", overwrite: true);
            }, "overwrite never replaces a directory");
            foreach (var overwrite in new[] { false, true })
            {
                Throws<IOException>(() =>
                {
                    source.CopyTo(directory, "source.bin", overwrite);
                }, "self-copy is rejected before truncation");
                var alias = StorageFacade.OpenFile(Path.Combine(directory.Location, ".", "source.bin"));
                Throws<IOException>(() =>
                {
                    alias.CopyTo(directory, "source.bin", overwrite);
                }, "canonical path aliases cannot bypass self-copy protection");
                var uriAlias = StorageFacade.OpenFile(new Uri(source.Location).AbsoluteUri);
                Throws<IOException>(() =>
                {
                    uriAlias.CopyTo(directory, "source.bin", overwrite);
                }, "a file URI alias cannot bypass self-copy protection");
            }
            EqualBytes(new byte[] { 3, 4 }, source.ReadAllBytes(), "all self-copy rejections preserve the source");
            return Task.CompletedTask;
        }

        /// <summary>Checks all child-taking operations before they can escape their parent directory.</summary>
        /// <returns>A task completing after synchronous and asynchronous name validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestLocalNames()
        {
            using var workspace = new TemporaryWorkspace();
            var directory = workspace.CreateLocalDirectory();
            var outside = workspace.GetPath("outside.bin");
            NativeFile.WriteAllText(outside, "outside sentinel");
            await VerifyInvalidNames(directory, outside);
            Check(NativeFile.ReadAllText(outside) == "outside sentinel", "traversal attempts do not modify the outside sibling");
            Check(NativeDirectory.EnumerateFileSystemEntries(workspace.RootPath).Count() == 2, "invalid names create no workspace siblings");
        }

        /// <summary>Checks successful asynchronous methods and cancellation before streams are opened.</summary>
        /// <returns>A task completing after local asynchronous validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestLocalAsync()
        {
            using var workspace = new TemporaryWorkspace();
            await VerifyAsyncIO(workspace.CreateLocalDirectory());
        }

        /// <summary>Checks non-recursive failures, recursive removal and harmless repeated file deletion.</summary>
        /// <returns>A completed task after local deletion validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestLocalDelete()
        {
            using var workspace = new TemporaryWorkspace();
            VerifyDelete(workspace.CreateLocalDirectory());
            return Task.CompletedTask;
        }

        /// <summary>Checks recursive deletion unlinks a directory symlink rather than walking its target.</summary>
        /// <returns>A completed task, or a platform capability skip if link creation is unavailable.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        /// <exception cref="SkipTestException">This host cannot create symbolic links.</exception>
        private static Task TestDirectoryLinks()
        {
            using var workspace = new TemporaryWorkspace();
            var outside = workspace.GetPath("outside-directory");
            NativeDirectory.CreateDirectory(outside);
            var sentinel = Path.Combine(outside, "keep.txt");
            NativeFile.WriteAllText(sentinel, "outside directory survives");
            var directory = workspace.CreateLocalDirectory();
            var linkPath = Path.Combine(directory.Location, "linked-directory");
            TryCreateLink(() =>
            {
                NativeDirectory.CreateSymbolicLink(linkPath, outside);
            });
            var link = NotNull(directory.FindDirectory("linked-directory"), "directory link is visible as an immediate entry");
            Check(Entry(link).IsSymbolicLink, "directory link metadata marks the reparse point");
            Check(directory.EnumerateEntries().Count() == 1, "enumeration does not recurse into a link target");
            link.Delete(recursive: true);
            Check(StorageFacade.OpenDirectory(linkPath).Entry is null, "direct deletion removes the directory link itself");
            Check(NativeFile.ReadAllText(sentinel) == "outside directory survives", "direct link deletion keeps its target intact");
            TryCreateLink(() =>
            {
                NativeDirectory.CreateSymbolicLink(linkPath, outside);
            });
            directory.Delete(recursive: true);
            Check(!NativeDirectory.Exists(directory.Location), "recursive deletion removes the containing directory");
            Check(NativeFile.ReadAllText(sentinel) == "outside directory survives", "recursive deletion never follows an outside directory link");
            Check(NativeDirectory.EnumerateFileSystemEntries(outside).Single() == sentinel, "outside target has no added or removed children");
            return Task.CompletedTask;
        }

        /// <summary>Checks direct and recursive file-link deletion preserve the outside file.</summary>
        /// <returns>A completed task, or a platform capability skip if link creation is unavailable.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        /// <exception cref="SkipTestException">This host cannot create symbolic links.</exception>
        private static async Task TestFileLinks()
        {
            using var workspace = new TemporaryWorkspace();
            var outside = workspace.GetPath("outside-file.txt");
            NativeFile.WriteAllText(outside, "outside file survives");
            var directory = workspace.CreateLocalDirectory();
            var linkPath = Path.Combine(directory.Location, "linked-file.txt");
            TryCreateLink(() =>
            {
                NativeFile.CreateSymbolicLink(linkPath, outside);
            });
            var link = NotNull(directory.FindFile("linked-file.txt"), "file link is visible as an immediate entry");
            Check(Entry(link).IsSymbolicLink, "file link metadata marks the reparse point");
            var source = directory.CreateFile("source.txt");
            source.WriteAllText("copy source must not overwrite a link target");
            foreach (var asynchronous in new[] { false, true })
            {
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(source, directory, "linked-file.txt", asynchronous, overwrite: true);
                }, "copy overwrite refuses a direct symbolic-link destination");
                Check(NativeFile.ReadAllText(outside) == "outside file survives", "rejected link overwrite does not truncate the outside target");
            }
            link.Delete();
            Check(StorageFacade.OpenFile(linkPath).Entry is null, "direct deletion removes the file link itself");
            Check(NativeFile.ReadAllText(outside) == "outside file survives", "direct file-link deletion preserves the outside file");
            TryCreateLink(() =>
            {
                NativeFile.CreateSymbolicLink(linkPath, outside);
            });
            directory.Delete(recursive: true);
            Check(NativeFile.ReadAllText(outside) == "outside file survives", "recursive deletion preserves an outside file-link target");
        }

        /// <summary>Checks provider registration, exact opaque locations and nullable metadata dispatch.</summary>
        /// <returns>A completed task after verifying content facade resolution.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestContentDispatch()
        {
            var provider = RegisterMemoryProvider();
            var directory = StorageFacade.OpenDirectory(provider.RootLocation);
            Check(ReferenceEquals(directory.FileSystem, provider), "content directory dispatch uses the registered backend");
            Check(directory.Location == provider.RootLocation, "content root URI is retained byte-for-byte");
            Check(directory.Location.Contains("%2F", StringComparison.Ordinal) && directory.Location.Contains("%23", StringComparison.Ordinal), "document IDs contain encoded reserved characters");
            provider.NextCreatedFileName = "provider assigned #名称.txt";
            var file = directory.CreateFile("requested.txt", "text/plain");
            Check(provider.LastMimeType == "text/plain", "MIME type is forwarded to the provider");
            Check(Entry(file).Name == "provider assigned #名称.txt", "creation trusts the actual provider-assigned display name");
            Check(!file.Location.StartsWith(directory.Location, StringComparison.Ordinal), "child IDs are not descendants of the parent URI");
            Check(file.Location == Entry(file).Location, "created handles retain the authoritative returned location");
            Check(Entry(file).Length is null && Entry(file).LastWriteTimeUtc is null, "file metadata can remain unknown even for a readable stream");
            file.WriteAllText("retained provider");
            var opened = StorageFacade.OpenFile(file.Location);
            Check(ReferenceEquals(opened.FileSystem, provider) && opened.ReadAllText() == "retained provider", "content file dispatch uses the exact provider ID");
            Throws<NotSupportedException>(() =>
            {
                StorageFacade.CreateLocalDirectory(provider.RootLocation);
            }, "local creation cannot reinterpret a content URI as a path");
            Throws<ArgumentNullException>(() =>
            {
                StorageFacade.RegisterContentProvider(null!);
            }, "a null provider registration is rejected");
            var replacement = RegisterMemoryProvider();
            Check(ReferenceEquals(StorageFacade.OpenDirectory(replacement.RootLocation).FileSystem, replacement), "new registrations affect subsequent facade resolutions");
            Check(ReferenceEquals(opened.FileSystem, provider) && opened.ReadAllText() == "retained provider", "existing handles retain their original provider after replacement");
            StreamsClosed(provider);
            return Task.CompletedTask;
        }

        /// <summary>Checks convenience operations never assume stream seek, length or known metadata.</summary>
        /// <returns>A completed task after sequential content I/O validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestContentIO()
        {
            var provider = RegisterMemoryProvider();
            var directory = new StorageDirectory(provider, provider.RootLocation);
            var file = directory.CreateFile("bytes.bin");
            var payload = Payload();
            file.WriteAllBytes(payload);
            EqualBytes(payload, file.ReadAllBytes(), "unknown-size content files support complete sequential reads");
            using (var read = file.OpenRead())
            {
                Check(read.CanRead && !read.CanWrite && !read.CanSeek, "provider read streams are sequential and read-only");
                Throws<NotSupportedException>(() =>
                {
                    _ = read.Length;
                }, "length queries are deliberately unsupported");
                Throws<NotSupportedException>(() =>
                {
                    _ = read.Position;
                }, "position queries are deliberately unsupported");
                Check(read.ReadByte() == payload[0], "a non-seekable stream remains readable");
            }
            using (var append = file.OpenWrite(append: true))
            {
                Check(!append.CanSeek, "provider append does not require caller-side seeking");
                append.WriteByte(88);
            }
            EqualBytes(payload.Concat(new byte[] { 88 }).ToArray(), file.ReadAllBytes(), "provider handles append itself");
            file.WriteAllBytes(new byte[] { 6 });
            EqualBytes(new byte[] { 6 }, file.ReadAllBytes(), "provider write convenience method truncates");
            VerifyTextIO(directory.CreateFile("text.txt"));
            StreamsClosed(provider);
            return Task.CompletedTask;
        }

        /// <summary>Checks child lookup and rename use provider-returned IDs instead of URI concatenation.</summary>
        /// <returns>A completed task after opaque navigation validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestContentChildren()
        {
            var provider = RegisterMemoryProvider();
            var directory = new StorageDirectory(provider, provider.RootLocation);
            VerifyChildren(directory);
            var child = directory.CreateDirectory("child: #?% 名称");
            var file = child.CreateFile("literal%2F..%5C.txt");
            file.WriteAllText("literal display name");
            var oldFileUri = file.Location;
            var renamed = file.Rename("renamed: #?% 文件.txt");
            Check(renamed.Location != oldFileUri && provider.GetEntry(oldFileUri) is null, "file rename can replace its opaque identity");
            Check(renamed.ReadAllText() == "literal display name", "renamed handle uses the new provider URI");
            var oldDirectoryUri = child.Location;
            var renamedDirectory = child.Rename("renamed directory: #?%");
            Check(renamedDirectory.Location != oldDirectoryUri && provider.GetEntry(oldDirectoryUri) is null, "directory rename can replace its opaque identity");
            Check(NotNull(renamedDirectory.FindFile(Entry(renamed).Name), "renamed directory resolves its child").Location == renamed.Location, "descendant IDs remain independent of a renamed ancestor");
            var destination = directory.CreateDirectory("copy target");
            provider.NextCreatedFileName = "assigned copy #名称.txt";
            var copied = renamed.CopyTo(destination, "requested copy.txt");
            Check(Entry(copied).Name == "assigned copy #名称.txt", "copy returns the provider-assigned destination name");
            Check(copied.ReadAllText() == "literal display name", "copy opens the authoritative newly created URI");
            foreach (var overwrite in new[] { false, true })
            {
                Throws<IOException>(() =>
                {
                    renamed.CopyTo(renamedDirectory, Entry(renamed).Name, overwrite);
                }, "opaque content self-copy is rejected");
            }
            Check(renamed.ReadAllText() == "literal display name", "content self-copy never truncates the source");
            StreamsClosed(provider);
            return Task.CompletedTask;
        }

        /// <summary>Checks provider child names are validated without URI-decoding literal percent sequences.</summary>
        /// <returns>A task completing after content name validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestContentNames()
        {
            var provider = RegisterMemoryProvider();
            await VerifyInvalidNames(new StorageDirectory(provider, provider.RootLocation), "content://outside.invalid/document/id%2F42");
            StreamsClosed(provider);
        }

        /// <summary>Checks file/directory collisions and provider-owned recursive deletion.</summary>
        /// <returns>A completed task after content deletion validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestContentDelete()
        {
            var provider = RegisterMemoryProvider();
            var root = new StorageDirectory(provider, provider.RootLocation);
            var file = root.CreateFile("occupied");
            file.WriteAllText("keep");
            Throws<IOException>(() =>
            {
                root.CreateFile("occupied");
            }, "content creation cannot replace a file");
            Throws<IOException>(() =>
            {
                root.CreateDirectory("occupied");
            }, "content directory creation cannot replace a file");
            var directory = root.CreateDirectory("directory");
            Throws<IOException>(() =>
            {
                root.CreateFile("directory");
            }, "content file creation cannot replace a directory");
            Throws<IOException>(() =>
            {
                new StorageFile(provider, directory.Location).Delete();
            }, "content file deletion cannot delete a directory");
            Check(directory.Exists && file.ReadAllText() == "keep", "content type collisions leave entries intact");
            VerifyMissingFile(new StorageFile(provider, provider.RootLocation + "%2Fmissing"));
            VerifyDelete(root.CreateDirectory("deletion"));
            StreamsClosed(provider);
            return Task.CompletedTask;
        }

        /// <summary>Checks revoked grants and backend errors propagate through metadata, lookup and enumeration.</summary>
        /// <returns>A completed task after error propagation validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static Task TestContentErrors()
        {
            var provider = RegisterMemoryProvider();
            var root = new StorageDirectory(provider, provider.RootLocation);
            var file = root.CreateFile("file.bin");
            foreach (var exception in new Exception[] { new UnauthorizedAccessException("revoked grant"), new IOException("provider unavailable") })
            {
                provider.QueryException = exception;
                ThrowsSame(exception, () =>
                {
                    _ = file.Exists;
                }, "file Exists does not conflate failures with absence");
                ThrowsSame(exception, () =>
                {
                    _ = file.Entry;
                }, "file metadata propagates provider failure");
                ThrowsSame(exception, () =>
                {
                    _ = root.Exists;
                }, "directory Exists does not conflate failures with absence");
                ThrowsSame(exception, () =>
                {
                    _ = root.FindFile("file.bin");
                }, "child lookup propagates provider failure");
                ThrowsSame(exception, () =>
                {
                    _ = root.EnumerateEntries().ToArray();
                }, "enumeration propagates provider failure");
                provider.QueryException = null;
            }
            Check(file.Exists && root.Exists, "clearing a transient error restores normal queries");
            StreamsClosed(provider);
            return Task.CompletedTask;
        }

        /// <summary>Checks all asynchronous byte/text helpers on streams with unknown lengths.</summary>
        /// <returns>A task completing after provider asynchronous I/O validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestContentAsync()
        {
            var provider = RegisterMemoryProvider();
            await VerifyAsyncIO(new StorageDirectory(provider, provider.RootLocation));
            StreamsClosed(provider);
        }

        /// <summary>Checks byte/text reads observe deterministic cancellation after the first provider read.</summary>
        /// <returns>A task completing after mid-operation cancellation and disposal assertions.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestContentReadCancellation()
        {
            var provider = RegisterMemoryProvider();
            var root = new StorageDirectory(provider, provider.RootLocation);
            var file = root.CreateFile("read-cancellation.txt");
            var text = new string('谱', 32768);
            file.WriteAllText(text);
            foreach (var readText in new[] { false, true })
            {
                using var cancellation = new CancellationTokenSource();
                provider.SetReadBehavior(file.Location, afterFirstRead: cancellation.Cancel);
                await ThrowsAsync<OperationCanceledException>(() =>
                {
                    if (readText)
                    {
                        return file.ReadAllTextAsync(cancellationToken: cancellation.Token);
                    }
                    return file.ReadAllBytesAsync(cancellationToken: cancellation.Token);
                }, "mid-operation asynchronous reads observe cancellation");
                Check(cancellation.IsCancellationRequested, "the first-read hook cancelled the asynchronous operation");
                StreamsClosed(provider);
                provider.SetReadBehavior(file.Location);
                Check(file.ReadAllText() == text, "cancelled asynchronous reads leave source bytes intact");
            }
        }
        /// <summary>Rejects self-copy across distinct grant URIs and backend instances using global identity.</summary>
        /// <returns>A task completing after sync/async alias rejection and identity-scope controls.</returns>
        /// <exception cref="InvalidOperationException">An alias copy truncates data or distinct resources are conflated.</exception>
        private static async Task TestResourceAliases()
        {
            const string SharedResourceId = "content://shared.documents.validation/document/primary%3Ashared%2Fscore.bin";
            const string OtherAuthorityId = "content://other.documents.validation/document/primary%3Ashared%2Fscore.bin";
            const string Sentinel = "same resource via different grant URI aliases: 谱面 🎵";
            foreach (var differentProviders in new[] { false, true })
            {
                foreach (var asynchronous in new[] { false, true })
                {
                    var sourceProvider = RegisterMemoryProvider();
                    var targetProvider = differentProviders ? new MemoryFileSystem() : sourceProvider;
                    var sourceRoot = new StorageDirectory(sourceProvider, sourceProvider.RootLocation);
                    var targetRoot = new StorageDirectory(targetProvider, targetProvider.RootLocation);
                    var source = sourceRoot.CreateFile("source alias.bin");
                    var target = targetRoot.CreateFile("target alias.bin");
                    source.WriteAllText(Sentinel);
                    target.WriteAllText(Sentinel);
                    sourceProvider.SetResourceId(source.Location, SharedResourceId);
                    targetProvider.SetResourceId(target.Location, SharedResourceId);
                    Check(source.Location != target.Location, "shared-resource aliases use different authoritative URIs");
                    Check(Entry(source).ResourceId == Entry(target).ResourceId, "alias metadata reports one global resource identity");
                    Check(ReferenceEquals(source.FileSystem, target.FileSystem) != differentProviders, "alias matrix includes separate backend instances");
                    var targetLocation = target.Location;
                    var writesBefore = targetProvider.OpenedWriteStreamCount;
                    await ThrowsAsync<IOException>(() =>
                    {
                        return Copy(source, targetRoot, "target alias.bin", asynchronous, overwrite: true);
                    }, "shared ResourceId rejects overwrite even when grant URIs and backend instances differ");
                    Check(targetProvider.OpenedWriteStreamCount == writesBefore, "resource-identity rejection happens before any target truncate/open");
                    Check(target.ReadAllText() == Sentinel && source.ReadAllText() == Sentinel, "alias self-copy rejection preserves all resource bytes");
                    Check(NotNull(targetRoot.FindFile("target alias.bin"), "alias target still exists").Location == targetLocation, "alias rejection preserves the target grant URI");
                    Check(targetProvider.DeletedLocations.Count == 0, "alias self-copy rejection never deletes the destination");
                    StreamsClosed(sourceProvider);
                    StreamsClosed(targetProvider);
                    targetProvider.SetResourceId(target.Location, OtherAuthorityId);
                    source.WriteAllText("different authority may receive a copy");
                    var copied = await Copy(source, targetRoot, "target alias.bin", asynchronous, overwrite: true);
                    Check(copied.Location == targetLocation, "different globally scoped identities can overwrite the existing target");
                    Check(copied.ReadAllText() == "different authority may receive a copy", "identical document IDs on different authorities are not conflated");
                    StreamsClosed(sourceProvider);
                    StreamsClosed(targetProvider);
                }
            }
        }
        /// <summary>Checks local/content copies in both directions for synchronous and asynchronous callers.</summary>
        /// <returns>A task completing after the cross-provider matrix.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestCrossCopies()
        {
            using var workspace = new TemporaryWorkspace();
            var provider = RegisterMemoryProvider();
            var local = workspace.CreateLocalDirectory();
            var content = new StorageDirectory(provider, provider.RootLocation);
            var payload = Payload();
            foreach (var asynchronous in new[] { false, true })
            {
                var suffix = asynchronous ? "async" : "sync";
                var localSource = local.CreateFile($"source-{suffix}.bin");
                localSource.WriteAllBytes(payload);
                var remoteCopy = await Copy(localSource, content, $"content #名称-{suffix}.bin", asynchronous);
                Check(ReferenceEquals(remoteCopy.FileSystem, provider), "local-to-content copy retains its destination provider");
                EqualBytes(payload, remoteCopy.ReadAllBytes(), "local-to-content copy works without seeking");
                var localCopy = await Copy(remoteCopy, local, $"returned #名称-{suffix}.bin", asynchronous);
                Check(ReferenceEquals(localCopy.FileSystem, local.FileSystem), "content-to-local copy retains its destination provider");
                EqualBytes(payload, localCopy.ReadAllBytes(), "content-to-local copy works with unknown source size");
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(localSource, content, Entry(remoteCopy).Name, asynchronous);
                }, "cross-provider copy cannot overwrite by default");
                localSource.WriteAllBytes(new byte[] { 19, 20 });
                var overwritten = await Copy(localSource, content, Entry(remoteCopy).Name, asynchronous, overwrite: true);
                EqualBytes(new byte[] { 19, 20 }, overwritten.ReadAllBytes(), "cross-provider overwrite truncates an old destination");
                var localOverwrite = await Copy(overwritten, local, Entry(localCopy).Name, asynchronous, overwrite: true);
                EqualBytes(new byte[] { 19, 20 }, localOverwrite.ReadAllBytes(), "content-to-local overwrite truncates old trailing bytes");
                content.CreateDirectory($"collision-{suffix}");
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(localSource, content, $"collision-{suffix}", asynchronous, overwrite: true);
                }, "cross-provider overwrite cannot replace a directory");
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(remoteCopy, content, Entry(remoteCopy).Name, asynchronous, overwrite: true);
                }, "asynchronous content self-copy cannot destroy its source");
                EqualBytes(new byte[] { 19, 20 }, remoteCopy.ReadAllBytes(), "self-copy rejection preserves the content source");
            }
            StreamsClosed(provider);
        }

        /// <summary>Checks partially created local/content destinations are removed after source read failure.</summary>
        /// <returns>A task completing after synchronous and asynchronous cleanup cases.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestCopyReadFailure()
        {
            foreach (var asynchronous in new[] { false, true })
            {
                foreach (var localDestination in new[] { false, true })
                {
                    using var workspace = new TemporaryWorkspace();
                    var provider = RegisterMemoryProvider();
                    var root = new StorageDirectory(provider, provider.RootLocation);
                    var source = root.CreateFile("source.bin");
                    source.WriteAllBytes(Payload());
                    provider.SetReadBehavior(source.Location, failAfterBytes: 7);
                    var destination = localDestination ? workspace.CreateLocalDirectory() : root.CreateDirectory("target");
                    await ThrowsAsync<IOException>(() =>
                    {
                        return Copy(source, destination, "failed.bin", asynchronous);
                    }, "source read failure is observable");
                    Check(destination.FindFile("failed.bin") is null, "source failure removes a newly created destination");
                    Check(source.Exists, "source failure never deletes the source");
                    StreamsClosed(provider);
                    provider.SetReadBehavior(source.Location);
                    EqualBytes(Payload(), source.ReadAllBytes(), "source failure preserves all source bytes");
                    Check(destination.EnumerateEntries().Count() == 0, "source failure leaves no partial destination or hidden sibling");
                }
            }
        }

        /// <summary>Checks provider write failures roll back newly created destinations and release streams.</summary>
        /// <returns>A task completing after local/content source and sync/async cases.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestCopyWriteFailure()
        {
            foreach (var asynchronous in new[] { false, true })
            {
                foreach (var localSource in new[] { false, true })
                {
                    using var workspace = new TemporaryWorkspace();
                    var provider = RegisterMemoryProvider();
                    var root = new StorageDirectory(provider, provider.RootLocation);
                    var sourceDirectory = localSource ? workspace.CreateLocalDirectory() : root.CreateDirectory("source directory");
                    var source = sourceDirectory.CreateFile("source.bin");
                    source.WriteAllBytes(Payload());
                    var target = root.CreateDirectory("target");
                    provider.NewFileWriteFailureAfterBytes = 7;
                    await ThrowsAsync<IOException>(() =>
                    {
                        return Copy(source, target, "failed.bin", asynchronous);
                    }, "destination write failure is observable");
                    Check(target.FindFile("failed.bin") is null, "destination failure removes only the newly created file");
                    Check(provider.DeletedLocations.Count == 1, "new failed provider destination is explicitly cleaned up");
                    StreamsClosed(provider);
                    EqualBytes(Payload(), source.ReadAllBytes(), "destination failure preserves source bytes");
                    Check(target.EnumerateEntries().Count() == 0, "failed write leaves no partial or hidden child");
                }
            }
        }

        /// <summary>Checks cancellation before creation and during non-seekable copy, including cleanup.</summary>
        /// <returns>A task completing after cancellation matrix validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestCopyCancellation()
        {
            foreach (var asynchronous in new[] { false, true })
            {
                foreach (var localDestination in new[] { false, true })
                {
                    using var workspace = new TemporaryWorkspace();
                    var provider = RegisterMemoryProvider();
                    var root = new StorageDirectory(provider, provider.RootLocation);
                    var source = root.CreateFile("source.bin");
                    source.WriteAllBytes(Payload());
                    var destination = localDestination ? workspace.CreateLocalDirectory() : root.CreateDirectory("target");
                    using var before = new CancellationTokenSource();
                    before.Cancel();
                    var streamCount = provider.OpenedStreamCount;
                    await ThrowsAsync<OperationCanceledException>(() =>
                    {
                        return Copy(source, destination, "before.bin", asynchronous, cancellationToken: before.Token);
                    }, "pre-cancelled copies report cancellation");
                    Check(destination.FindFile("before.bin") is null, "pre-cancellation creates no destination");
                    Check(provider.OpenedStreamCount == streamCount, "pre-cancellation opens no source stream");
                    using var during = new CancellationTokenSource();
                    provider.SetReadBehavior(source.Location, afterFirstRead: during.Cancel);
                    await ThrowsAsync<OperationCanceledException>(() =>
                    {
                        return Copy(source, destination, "during.bin", asynchronous, cancellationToken: during.Token);
                    }, "mid-transfer copies report cancellation");
                    Check(during.IsCancellationRequested, "the deterministic first-read cancellation hook ran");
                    Check(destination.FindFile("during.bin") is null, "cancellation removes a newly created destination");
                    StreamsClosed(provider);
                    provider.SetReadBehavior(source.Location);
                    EqualBytes(Payload(), source.ReadAllBytes(), "cancellation leaves the source intact");
                    Check(destination.EnumerateEntries().Count() == 0, "cancelled copy leaves no hidden or partial child");
                }
            }
        }

        /// <summary>Checks existing destinations survive read failure, write failure and cancellation.</summary>
        /// <returns>A task completing after overwrite failure validation.</returns>
        /// <exception cref="InvalidOperationException">A regression assertion fails.</exception>
        private static async Task TestCopyOverwriteFailure()
        {
            foreach (var asynchronous in new[] { false, true })
            {
                foreach (var localDestination in new[] { false, true })
                {
                    foreach (var cancel in new[] { false, true })
                    {
                        using var workspace = new TemporaryWorkspace();
                        var provider = RegisterMemoryProvider();
                        var root = new StorageDirectory(provider, provider.RootLocation);
                        var source = root.CreateFile("source.bin");
                        source.WriteAllBytes(Payload());
                        var target = localDestination ? workspace.CreateLocalDirectory() : root.CreateDirectory("target");
                        var existing = target.CreateFile("existing.bin");
                        existing.WriteAllText("existing destination");
                        var oldLocation = existing.Location;
                        using var cancellation = new CancellationTokenSource();
                        if (cancel)
                        {
                            provider.SetReadBehavior(source.Location, afterFirstRead: cancellation.Cancel);
                            await ThrowsAsync<OperationCanceledException>(() =>
                            {
                                return Copy(source, target, "existing.bin", asynchronous, overwrite: true, cancellationToken: cancellation.Token);
                            }, "cancelled overwrite reports cancellation");
                        }
                        else
                        {
                            provider.SetReadBehavior(source.Location, failAfterBytes: 7);
                            await ThrowsAsync<IOException>(() =>
                            {
                                return Copy(source, target, "existing.bin", asynchronous, overwrite: true);
                            }, "failed overwrite reports the source error");
                        }
                        Check(NotNull(target.FindFile("existing.bin"), "existing destination survives failure").Location == oldLocation, "failed overwrite preserves destination identity");
                        Check(!provider.DeletedLocations.Contains(oldLocation), "rollback never deletes a pre-existing destination");
                        StreamsClosed(provider);
                    }
                }
                var writeProvider = RegisterMemoryProvider();
                var writeRoot = new StorageDirectory(writeProvider, writeProvider.RootLocation);
                var writeSource = writeRoot.CreateFile("source.bin");
                writeSource.WriteAllBytes(Payload());
                var writeTarget = writeRoot.CreateFile("existing.bin");
                writeTarget.WriteAllText("existing destination");
                writeProvider.SetWriteFailure(writeTarget.Location, 7);
                await ThrowsAsync<IOException>(() =>
                {
                    return Copy(writeSource, writeRoot, "existing.bin", asynchronous, overwrite: true);
                }, "failed existing-destination writes report an I/O error");
                Check(writeTarget.Exists && !writeProvider.DeletedLocations.Contains(writeTarget.Location), "write failure never deletes an existing document");
                StreamsClosed(writeProvider);
            }
        }

        /// <summary>Checks encoding behavior shared by local and content convenience methods.</summary>
        /// <param name="file">An existing writable file on either backend.</param>
        /// <exception cref="InvalidOperationException">An encoding or BOM invariant fails.</exception>
        private static void VerifyTextIO(StorageFile file)
        {
            const string Text = "谱面 café 🎵\r\nline two\n";
            file.WriteAllText(Text);
            Check(file.ReadAllText() == Text, "default text preserves non-ASCII characters and line endings");
            EqualBytes(new UTF8Encoding(false).GetBytes(Text), file.ReadAllBytes(), "default text writes UTF-8 without a BOM");
            foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32 })
            {
                var encoded = encoding.GetPreamble().Concat(encoding.GetBytes(Text)).ToArray();
                file.WriteAllBytes(encoded);
                Check(file.ReadAllText() == Text, "default reads detect UTF BOMs rather than returning the preamble as text");
                file.WriteAllText(Text, encoding);
                EqualBytes(encoded, file.ReadAllBytes(), "explicit encoding writes the requested preamble and byte representation");
                Check(file.ReadAllText(encoding) == Text, "explicit encoding reads round trip the text");
                file.WriteAllBytes(encoding.GetPreamble());
                Check(file.ReadAllText() == string.Empty, "a BOM-only file contains empty text");
            }
            var noBomUtf16 = new UnicodeEncoding(false, false);
            file.WriteAllText(Text, noBomUtf16);
            Check(file.ReadAllText(noBomUtf16) == Text, "an explicit BOM-free UTF-16 encoding is honored");
            file.WriteAllText(string.Empty);
            Check(file.ReadAllBytes().Length == 0 && file.ReadAllText() == string.Empty, "empty default text truncates old content");
        }

        /// <summary>Checks a missing file is never implicitly created by read, write, append or rename.</summary>
        /// <param name="missing">A handle whose exact location is currently absent.</param>
        /// <exception cref="InvalidOperationException">An existing-only contract fails.</exception>
        private static void VerifyMissingFile(StorageFile missing)
        {
            Check(!missing.Exists && missing.Entry is null, "missing file metadata reports absence");
            Throws<FileNotFoundException>(() =>
            {
                using var stream = missing.OpenRead();
            }, "missing file read fails");
            foreach (var append in new[] { false, true })
            {
                Throws<FileNotFoundException>(() =>
                {
                    using var stream = missing.OpenWrite(append);
                }, "missing file write and append never create a file");
            }
            Throws<FileNotFoundException>(() =>
            {
                _ = missing.ReadAllBytes();
            }, "missing byte reads fail");
            Throws<FileNotFoundException>(() =>
            {
                _ = missing.ReadAllText();
            }, "missing text reads fail");
            Throws<FileNotFoundException>(() =>
            {
                missing.WriteAllBytes(new byte[] { 1 });
            }, "missing byte writes fail without creating the file");
            Throws<FileNotFoundException>(() =>
            {
                missing.WriteAllText("not created");
            }, "missing text writes fail without creating the file");
            Throws<FileNotFoundException>(() =>
            {
                missing.Rename("not-created.bin");
            }, "renaming a missing file fails");
            missing.Delete();
            missing.Delete();
            Check(!missing.Exists, "missing-file deletion is an idempotent no-op");
        }

        /// <summary>Checks immediate enumeration, typed lookup and rename across both backend types.</summary>
        /// <param name="directory">An empty existing directory on either backend.</param>
        /// <exception cref="InvalidOperationException">A navigation or rename invariant fails.</exception>
        private static void VerifyChildren(StorageDirectory directory)
        {
            var first = directory.CreateFile("a #谱面.txt");
            first.WriteAllText("rename data");
            var second = directory.CreateFile("b.bin");
            second.WriteAllText("collision sentinel");
            var folder = directory.CreateDirectory("folder");
            folder.CreateFile("nested.txt").WriteAllText("nested sentinel");
            var entries = directory.EnumerateEntries().ToArray();
            Check(entries.Length == 3, "enumeration returns only immediate children");
            EqualNames(new[] { "a #谱面.txt", "b.bin", "folder" }, entries.Select(entry => entry.Name), "enumeration includes files and directories without nested descendants");
            EqualNames(new[] { "a #谱面.txt", "b.bin" }, directory.EnumerateFiles().Select(file => Entry(file).Name), "file enumeration filters out directories");
            EqualNames(new[] { "folder" }, directory.EnumerateDirectories().Select(child => Entry(child).Name), "directory enumeration filters out files");
            Check(NotNull(directory.FindFile("a #谱面.txt"), "existing child is found").Location == first.Location, "file lookup retains the backend's authoritative location");
            Check(NotNull(directory.FindDirectory("folder"), "existing directory is found").Location == folder.Location, "directory lookup retains the backend's authoritative location");
            Check(directory.FindFile("folder") is null && directory.FindDirectory("b.bin") is null, "typed lookup does not return the wrong entry kind");
            Check(directory.FindFile("absent") is null && directory.FindDirectory("absent") is null, "missing typed lookup returns null");
            Check(directory.CreateDirectory("folder").Location == folder.Location, "existing child-directory creation is idempotent");
            var oldFileLocation = first.Location;
            var renamed = first.Rename("renamed #谱面.txt");
            Check(ReferenceEquals(renamed.FileSystem, directory.FileSystem), "rename retains its backend");
            Check(renamed.Exists && Entry(renamed).Name == "renamed #谱面.txt", "rename returns the new display name and existing handle");
            Check(directory.FileSystem.GetEntry(oldFileLocation) is null, "old file location no longer identifies the renamed file");
            Check(renamed.ReadAllText() == "rename data", "rename preserves the original bytes");
            Throws<IOException>(() =>
            {
                renamed.Rename("b.bin");
            }, "file rename never overwrites a sibling");
            Throws<IOException>(() =>
            {
                folder.Rename("b.bin");
            }, "directory rename never overwrites a file sibling");
            Throws<IOException>(() =>
            {
                renamed.Rename("folder");
            }, "file rename never replaces a directory sibling");
            Check(second.ReadAllText() == "collision sentinel", "rename collisions preserve destination data");
            var renamedFolder = folder.Rename("renamed folder");
            Check(Entry(renamedFolder).Name == "renamed folder", "directory rename returns its new name");
            Check(NotNull(renamedFolder.FindFile("nested.txt"), "renamed directory retains its child").ReadAllText() == "nested sentinel", "directory rename preserves its descendants");
            Check(directory.FindDirectory("folder") is null, "old directory display name is absent after rename");
        }

        /// <summary>Checks every single-child API rejects traversal, absolute paths and invalid separators.</summary>
        /// <param name="directory">An empty existing directory on either backend.</param>
        /// <param name="outsideLocation">A path or URI which must not become a child name.</param>
        /// <returns>A task completing after all invalid-name assertions.</returns>
        /// <exception cref="InvalidOperationException">A child API accepts an invalid name or mutates a sibling.</exception>
        private static async Task VerifyInvalidNames(StorageDirectory directory, string outsideLocation)
        {
            var source = directory.CreateFile("source.bin");
            source.WriteAllText("source sentinel");
            var child = directory.CreateDirectory("child");
            var invalidNames = new[]
            {
                null!, string.Empty, ".", "..", "../escaped.bin", "..\\escaped.bin",
                "nested/leaf.bin", "nested\\leaf.bin", "nul\0name", outsideLocation,
            };
            foreach (var invalid in invalidNames)
            {
                Throws<ArgumentException>(() =>
                {
                    StorageName.Validate(invalid);
                }, "the portable name contract rejects traversal or separators");
                Throws<ArgumentException>(() =>
                {
                    directory.CreateFile(invalid);
                }, "CreateFile accepts one child name only");
                Throws<ArgumentException>(() =>
                {
                    directory.CreateDirectory(invalid);
                }, "CreateDirectory accepts one child name only");
                Throws<ArgumentException>(() =>
                {
                    _ = directory.FindFile(invalid);
                }, "FindFile accepts one child name only");
                Throws<ArgumentException>(() =>
                {
                    _ = directory.FindDirectory(invalid);
                }, "FindDirectory accepts one child name only");
                Throws<ArgumentException>(() =>
                {
                    source.Rename(invalid);
                }, "file Rename accepts one child name only");
                Throws<ArgumentException>(() =>
                {
                    child.Rename(invalid);
                }, "directory Rename accepts one child name only");
                Throws<ArgumentException>(() =>
                {
                    source.CopyTo(directory, invalid);
                }, "CopyTo accepts one destination name only");
                await ThrowsAsync<ArgumentException>(() =>
                {
                    return source.CopyToAsync(directory, invalid);
                }, "CopyToAsync accepts one destination name only");
            }
            const string LiteralName = "literal%2F..%5C.txt";
            var literal = directory.CreateFile(LiteralName);
            literal.WriteAllText("literal percent name");
            Check(Entry(literal).Name == LiteralName, "escaped-looking display names are never URI-decoded");
            Check(NotNull(directory.FindFile(LiteralName), "literal percent name is found").ReadAllText() == "literal percent name", "encoded separators remain literal display-name characters");
            Check(source.ReadAllText() == "source sentinel" && child.Exists, "invalid operations preserve the original entries");
            Check(directory.EnumerateEntries().Count() == 3, "invalid child operations create no additional entries");
        }

        /// <summary>Checks asynchronous helpers, existing-only semantics and pre-cancelled side-effect protection.</summary>
        /// <param name="directory">An empty existing directory on either backend.</param>
        /// <returns>A task completing after asynchronous success and failure validation.</returns>
        /// <exception cref="InvalidOperationException">An asynchronous storage invariant fails.</exception>
        private static async Task VerifyAsyncIO(StorageDirectory directory)
        {
            var file = directory.CreateFile("async.bin");
            var payload = Payload();
            await file.WriteAllBytesAsync(payload, cancellationToken: CancellationToken.None);
            EqualBytes(payload, await file.ReadAllBytesAsync(cancellationToken: CancellationToken.None), "asynchronous byte I/O round trips a multi-buffer payload");
            const string Text = "异步 café 🎵\r\n";
            await file.WriteAllTextAsync(Text, cancellationToken: CancellationToken.None);
            Check(await file.ReadAllTextAsync(cancellationToken: CancellationToken.None) == Text, "default asynchronous UTF-8 round trips Unicode");
            await file.WriteAllTextAsync(Text, Encoding.Unicode, cancellationToken: CancellationToken.None);
            Check(await file.ReadAllTextAsync(cancellationToken: CancellationToken.None) == Text, "asynchronous default reads detect a Unicode BOM");
            Check(await file.ReadAllTextAsync(Encoding.Unicode, cancellationToken: CancellationToken.None) == Text, "asynchronous explicit text encodings are honored");
            await file.WriteAllBytesAsync(Array.Empty<byte>());
            Check((await file.ReadAllBytesAsync()).Length == 0, "empty asynchronous byte writes truncate");
            await file.WriteAllTextAsync(string.Empty);
            Check(await file.ReadAllTextAsync() == string.Empty, "empty asynchronous text writes truncate");
            file.WriteAllText("pre-cancel sentinel");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var openedBefore = (directory.FileSystem as MemoryFileSystem)?.OpenedStreamCount;
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return file.ReadAllBytesAsync(cancellationToken: cancellation.Token);
            }, "pre-cancelled asynchronous byte reads fail");
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return file.ReadAllTextAsync(cancellationToken: cancellation.Token);
            }, "pre-cancelled asynchronous text reads fail");
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return file.WriteAllBytesAsync(payload, cancellationToken: cancellation.Token);
            }, "pre-cancelled asynchronous byte writes fail");
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return file.WriteAllTextAsync("must not truncate", cancellationToken: cancellation.Token);
            }, "pre-cancelled asynchronous text writes fail");
            if (openedBefore.HasValue)
            {
                Check(((MemoryFileSystem)directory.FileSystem).OpenedStreamCount == openedBefore.Value, "pre-cancelled asynchronous I/O opens no provider streams");
            }
            Check(file.ReadAllText() == "pre-cancel sentinel", "pre-cancelled writes preserve all original bytes");
            var existingTarget = directory.CreateFile("existing-target.bin");
            existingTarget.WriteAllText("destination sentinel");
            await ThrowsAsync<OperationCanceledException>(() =>
            {
                return file.CopyToAsync(directory, "existing-target.bin", overwrite: true, cancellationToken: cancellation.Token);
            }, "pre-cancelled overwrite reports cancellation before side effects");
            Check(existingTarget.ReadAllText() == "destination sentinel", "pre-cancelled overwrite cannot truncate an existing destination");
            var missing = directory.CreateFile("missing.bin");
            missing.Delete();
            await ThrowsAsync<FileNotFoundException>(() =>
            {
                return missing.ReadAllBytesAsync();
            }, "missing asynchronous byte reads fail");
            await ThrowsAsync<FileNotFoundException>(() =>
            {
                return missing.ReadAllTextAsync();
            }, "missing asynchronous text reads fail");
            await ThrowsAsync<FileNotFoundException>(() =>
            {
                return missing.WriteAllBytesAsync(payload);
            }, "missing asynchronous byte writes do not create a file");
            await ThrowsAsync<FileNotFoundException>(() =>
            {
                return missing.WriteAllTextAsync(Text);
            }, "missing asynchronous text writes do not create a file");
            Check(!missing.Exists, "all asynchronous missing-file operations leave the location absent");
        }

        /// <summary>Checks backend-owned deletion and non-recursive failure behavior.</summary>
        /// <param name="directory">An empty existing directory which may be removed by the test.</param>
        /// <exception cref="InvalidOperationException">A deletion invariant fails.</exception>
        private static void VerifyDelete(StorageDirectory directory)
        {
            var file = directory.CreateFile("delete.bin");
            file.Delete();
            file.Delete();
            Check(!file.Exists, "file deletion is idempotent");
            var empty = directory.CreateDirectory("empty");
            empty.Delete();
            Check(!empty.Exists, "empty directory deletion succeeds without recursion");
            var nested = directory.CreateDirectory("nested").CreateDirectory("deeper");
            var nestedFile = nested.CreateFile("keep.bin");
            nestedFile.WriteAllText("nested sentinel");
            Throws<IOException>(() =>
            {
                directory.Delete();
            }, "non-recursive deletion rejects a non-empty directory");
            Check(nestedFile.ReadAllText() == "nested sentinel", "failed non-recursive deletion preserves descendants");
            directory.Delete(recursive: true);
            Check(!directory.Exists && !nested.Exists && !nestedFile.Exists, "recursive deletion removes descendants and the root");
        }

        /// <summary>Selects the requested copy API without hiding synchronous exceptions.</summary>
        /// <param name="source">The existing source file.</param>
        /// <param name="destination">The existing destination directory.</param>
        /// <param name="name">The single destination display name.</param>
        /// <param name="asynchronous">Whether to invoke the asynchronous API.</param>
        /// <param name="overwrite">Whether an existing file may be truncated.</param>
        /// <param name="cancellationToken">The cancellation signal passed unchanged to production.</param>
        /// <returns>A task yielding the destination handle.</returns>
        /// <exception cref="IOException">The production copy reports an I/O error.</exception>
        /// <exception cref="OperationCanceledException">The production copy observes cancellation.</exception>
        private static Task<StorageFile> Copy(StorageFile source, StorageDirectory destination, string name,
            bool asynchronous, bool overwrite = false, CancellationToken cancellationToken = default)
        {
            if (asynchronous)
            {
                return source.CopyToAsync(destination, name, overwrite, cancellationToken);
            }
            return Task.FromResult(source.CopyTo(destination, name, overwrite, cancellationToken));
        }

        /// <summary>Creates and registers a new isolated content provider for one case.</summary>
        /// <returns>The registered provider containing only its root directory.</returns>
        private static MemoryFileSystem RegisterMemoryProvider()
        {
            var provider = new MemoryFileSystem();
            StorageFacade.RegisterContentProvider(provider);
            return provider;
        }

        /// <summary>Creates a deterministic payload larger than typical copy buffers.</summary>
        /// <returns>Non-uniform binary data suitable for detecting truncated or reordered copies.</returns>
        private static byte[] Payload()
        {
            var bytes = new byte[131089];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = unchecked((byte)((index * 31) + (index / 251)));
            }
            return bytes;
        }

        /// <summary>Requires metadata for an existing file.</summary>
        /// <param name="file">The existing file handle.</param>
        /// <returns>The refreshed authoritative metadata.</returns>
        /// <exception cref="InvalidOperationException">The existing file reports no metadata.</exception>
        private static FileSystemEntry Entry(StorageFile file)
        {
            return NotNull(file.Entry, "existing file metadata is available");
        }

        /// <summary>Requires metadata for an existing directory.</summary>
        /// <param name="directory">The existing directory handle.</param>
        /// <returns>The refreshed authoritative metadata.</returns>
        /// <exception cref="InvalidOperationException">The existing directory reports no metadata.</exception>
        private static FileSystemEntry Entry(StorageDirectory directory)
        {
            return NotNull(directory.Entry, "existing directory metadata is available");
        }

        /// <summary>Checks all caller-owned provider streams were released, including on copy failures.</summary>
        /// <param name="provider">The provider whose stream ownership counts are checked.</param>
        /// <exception cref="InvalidOperationException">A stream was leaked or disposed more than once.</exception>
        private static void StreamsClosed(MemoryFileSystem provider)
        {
            Check(provider.OpenStreamCount == 0, "all provider streams are disposed");
            Check(provider.OpenedStreamCount == provider.DisposedStreamCount, "each opened provider stream is disposed exactly once");
        }

        /// <summary>Records an assertion with a descriptive failure message.</summary>
        /// <param name="condition">Whether the expected invariant holds.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Requires a non-null reference while preserving nullable flow analysis.</summary>
        /// <typeparam name="T">The expected reference type.</typeparam>
        /// <param name="value">The possibly absent value.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <returns>The asserted non-null value.</returns>
        /// <exception cref="InvalidOperationException">The value is null.</exception>
        private static T NotNull<T>(T? value, string message) where T : class
        {
            Check(value is not null, message);
            return value ?? throw new InvalidOperationException(message);
        }

        /// <summary>Compares complete byte sequences rather than relying on stream length metadata.</summary>
        /// <param name="expected">The expected bytes.</param>
        /// <param name="actual">The bytes returned by production.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <exception cref="InvalidOperationException">The sequences differ.</exception>
        private static void EqualBytes(byte[] expected, byte[] actual, string message)
        {
            Check(expected.AsSpan().SequenceEqual(actual), message);
        }

        /// <summary>Compares child display names without assuming enumeration order.</summary>
        /// <param name="expected">The expected immediate child names.</param>
        /// <param name="actual">The actual immediate child names.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <exception cref="InvalidOperationException">The names differ.</exception>
        private static void EqualNames(IEnumerable<string> expected, IEnumerable<string> actual, string message)
        {
            Check(expected.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(actual.OrderBy(name => name, StringComparer.Ordinal)), message);
        }

        /// <summary>Checks a synchronous operation throws the specified exception type.</summary>
        /// <typeparam name="TException">The expected exception type or base type.</typeparam>
        /// <param name="action">The operation which must fail.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <exception cref="InvalidOperationException">No exception or an unexpected exception is thrown.</exception>
        private static void Throws<TException>(Action action, string message) where TException : Exception
        {
            s_assertions++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
            }
            throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}, but no exception was thrown.");
        }

        /// <summary>Checks task-producing operations fail synchronously or asynchronously with the expected type.</summary>
        /// <typeparam name="TException">The expected exception type or base type.</typeparam>
        /// <param name="action">The operation which must fail.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <returns>A task completing after checking the failure.</returns>
        /// <exception cref="InvalidOperationException">No exception or an unexpected exception is thrown.</exception>
        private static async Task ThrowsAsync<TException>(Func<Task> action, string message) where TException : Exception
        {
            s_assertions++;
            try
            {
                await action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
            }
            throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}, but no exception was thrown.");
        }

        /// <summary>Allows platforms to report opening a directory as either an I/O or access error.</summary>
        /// <param name="action">The invalid file operation.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <exception cref="InvalidOperationException">The operation succeeds or reports an unrelated error.</exception>
        private static void ThrowsIo(Action action, string message)
        {
            s_assertions++;
            try
            {
                action();
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        /// <summary>Checks metadata and lookup preserve the actual backend exception.</summary>
        /// <param name="expected">The configured backend exception instance.</param>
        /// <param name="action">The operation expected to propagate it.</param>
        /// <param name="message">The invariant reported on failure.</param>
        /// <exception cref="InvalidOperationException">The failure is swallowed, replaced or wrapped.</exception>
        private static void ThrowsSame(Exception expected, Action action, string message)
        {
            s_assertions++;
            try
            {
                action();
            }
            catch (Exception actual)
            {
                Check(ReferenceEquals(actual, expected), message);
                return;
            }
            throw new InvalidOperationException(message);
        }

        /// <summary>Skips only when the operating system or account cannot create symbolic links.</summary>
        /// <param name="create">The native link creation operation.</param>
        /// <exception cref="SkipTestException">Link creation is unavailable on this host.</exception>
        private static void TryCreateLink(Action create)
        {
            try
            {
                create();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException || exception is NotSupportedException || exception is IOException)
            {
                throw new SkipTestException($"symbolic-link creation unavailable: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    /// <summary>Marks an unavailable platform capability separately from a passing regression.</summary>
    internal sealed class SkipTestException : Exception
    {
        /// <summary>Initializes a capability-specific skip reason.</summary>
        /// <param name="message">The unavailable capability and its native error.</param>
        public SkipTestException(string message) : base(message)
        {
        }
    }

    /// <summary>Owns an OS-temp test root with guarded, non-link-following cleanup.</summary>
    internal sealed class TemporaryWorkspace : IDisposable
    {
        /// <summary>Identifies roots created exclusively by this harness.</summary>
        private const string RootPrefix = "MajdataPlay.FileSystemValidation-";

        /// <summary>Stores the canonical OS temporary directory used for the root.</summary>
        private readonly string _temporaryDirectory;

        /// <summary>Records whether cleanup has already completed.</summary>
        private bool _disposed;

        /// <summary>Gets the unique absolute directory owned by this fixture.</summary>
        public string RootPath { get; }

        /// <summary>Creates a uniquely named direct child of the operating system temporary directory.</summary>
        public TemporaryWorkspace()
        {
            _temporaryDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            RootPath = Path.Combine(_temporaryDirectory, RootPrefix + Guid.NewGuid().ToString("N"));
            NativeDirectory.CreateDirectory(RootPath);
        }

        /// <summary>Builds a guarded native test path, never a content-provider location.</summary>
        /// <param name="segments">Native relative path segments inside this fixture.</param>
        /// <returns>An absolute path within the owned root.</returns>
        /// <exception cref="InvalidOperationException">The requested path would escape the owned root.</exception>
        public string GetPath(params string[] segments)
        {
            var path = RootPath;
            foreach (var segment in segments)
            {
                path = Path.Combine(path, segment);
            }
            path = Path.GetFullPath(path);
            RequireOwnedPath(path);
            return path;
        }

        /// <summary>Creates the local directory exercised by a regression case.</summary>
        /// <returns>A production storage handle rooted inside this fixture.</returns>
        public StorageDirectory CreateLocalDirectory()
        {
            return StorageFacade.CreateLocalDirectory(GetPath("local"));
        }

        /// <summary>Removes only the validated unique root, unlinking rather than following reparse points.</summary>
        /// <exception cref="InvalidOperationException">The cleanup target no longer matches its safe root.</exception>
        /// <exception cref="IOException">Cleanup fails or the owned root was replaced by a link.</exception>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            var canonical = Path.GetFullPath(RootPath);
            var name = Path.GetFileName(canonical);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(canonical), _temporaryDirectory, comparison) ||
                !name.StartsWith(RootPrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.Substring(RootPrefix.Length), "N", out _) ||
                !string.Equals(canonical, RootPath, comparison))
            {
                throw new InvalidOperationException("Refusing cleanup outside the fixture's exact OS-temp root.");
            }
            if (NativeDirectory.Exists(canonical))
            {
                if ((NativeFile.GetAttributes(canonical) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Refusing to traverse an owned root replaced by a symbolic link.");
                }
                RemoveDirectoryContents(canonical);
                NativeDirectory.Delete(canonical, false);
            }
            _disposed = true;
        }

        /// <summary>Checks a normalized path is the owned root or a properly delimited descendant.</summary>
        /// <param name="path">The resolved absolute path to inspect before deletion or use.</param>
        /// <exception cref="InvalidOperationException">The path escapes the owned root.</exception>
        private void RequireOwnedPath(string path)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(path, RootPath, comparison) && !path.StartsWith(RootPath + Path.DirectorySeparatorChar, comparison))
            {
                throw new InvalidOperationException("The test path escapes its owned temporary root.");
            }
        }

        /// <summary>Deletes guarded children without using a recursive deletion API on link targets.</summary>
        /// <param name="directory">An already validated non-link directory inside the root.</param>
        /// <exception cref="InvalidOperationException">A child resolves outside the owned root.</exception>
        /// <exception cref="IOException">A child cannot be deleted.</exception>
        private void RemoveDirectoryContents(string directory)
        {
            foreach (var child in NativeDirectory.EnumerateFileSystemEntries(directory))
            {
                var canonical = Path.GetFullPath(child);
                RequireOwnedPath(canonical);
                var attributes = NativeFile.GetAttributes(canonical);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        RemoveDirectoryContents(canonical);
                    }
                    NativeDirectory.Delete(canonical, false);
                }
                else
                {
                    NativeFile.Delete(canonical);
                }
            }
        }
    }
}

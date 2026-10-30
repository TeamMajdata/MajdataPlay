#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers the read-only ZIP/APK backend selected for <c>jar:file://</c> locations.</summary>
    public class JarFileSystemTests
    {
        /// <summary>The temporary directory of the current test.</summary>
        private TemporaryDirectory _temp = null!;

        /// <summary>The standard fixture archive.</summary>
        private StorageFile _archive = null!;

        /// <summary>The jar location of the fixture archive root.</summary>
        private string _root = null!;

        /// <summary>Creates a temporary directory holding the standard fixture archive.</summary>
        [SetUp]
        public void SetUp()
        {
            _temp = new TemporaryDirectory();
            _archive = ZipFixture.Create(_temp.Directory, "base.apk",
                ("assets/", string.Empty),
                ("assets/Config/options.txt", "options"),
                ("assets/implicit/child.txt", "child"),
                ("assets/space name.txt", "space"),
                ("assets/100%.txt", "percent"),
                ("assets/中文.txt", "unicode"),
                ("top.txt", "top"),
                ("unsafe/../evil.txt", "evil"),
                ("./dot.txt", "dot"),
                ("back\\slash.txt", "backslash"),
                ("empty//segment.txt", "empty"));
            _root = ZipFixture.JarLocation(_archive);
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _temp.Dispose();
        }

        /// <summary>Checks that the archive root is a directory named after the archive file.</summary>
        [Test]
        public void Root_IsDirectoryNamedAfterArchive()
        {
            var root = FileSystem.Directory.Open(_root);

            Assert.That(root.FileSystem, Is.InstanceOf<JarFileSystem>());
            Assert.That(root.Exists, Is.True);
            Assert.That(root.Entry!.Name, Is.EqualTo("base.apk"));
            Assert.That(root.Location, Does.EndWith("base.apk!/"));
        }

        /// <summary>Checks that file metadata reports name, length and a UTC modification time.</summary>
        [Test]
        public void GetEntry_ReportsFileMetadata()
        {
            var entry = FileSystem.GetEntry(_root + "assets/Config/options.txt")!;

            Assert.That(entry.Name, Is.EqualTo("options.txt"));
            Assert.That(entry.IsDirectory, Is.False);
            Assert.That(entry.Length, Is.EqualTo(7));
            Assert.That(entry.LastWriteTimeUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(entry.LastWriteTimeUtc!.Value,
                Is.EqualTo(new DateTime(2022, 3, 4, 5, 6, 8, DateTimeKind.Local).ToUniversalTime()));
        }

        /// <summary>Checks that implicit directories exist without timestamps while explicit records keep theirs.</summary>
        [Test]
        public void GetEntry_SynthesizesImplicitDirectories()
        {
            var implicitDirectory = FileSystem.GetEntry(_root + "assets/implicit")!;
            var explicitDirectory = FileSystem.GetEntry(_root + "assets")!;

            Assert.That(implicitDirectory.IsDirectory, Is.True);
            Assert.That(implicitDirectory.LastWriteTimeUtc, Is.Null);
            Assert.That(implicitDirectory.Length, Is.Null);
            Assert.That(explicitDirectory.IsDirectory, Is.True);
            Assert.That(explicitDirectory.LastWriteTimeUtc, Is.Not.Null);
        }

        /// <summary>Checks that missing entries and missing archives report absence, and names are case-sensitive.</summary>
        [Test]
        public void GetEntry_ReturnsNullForMissingEntriesAndArchives()
        {
            var missingArchive = ZipFixture.JarLocation(FileSystem.File.Open(Path.Combine(_temp.Location, "missing.apk")), "a.txt");

            var missingDirectory = ZipFixture.JarLocation(
                FileSystem.File.Open(Path.Combine(_temp.Location, "missing-dir", "missing.apk")), "a.txt");

            Assert.That(FileSystem.GetEntry(_root + "missing.txt"), Is.Null);
            Assert.That(FileSystem.GetEntry(_root + "TOP.txt"), Is.Null);
            Assert.That(FileSystem.GetEntry(missingArchive), Is.Null);
            Assert.That(FileSystem.GetEntry(missingDirectory), Is.Null);
        }

        /// <summary>Checks that a trailing slash is accepted and removed from the canonical location.</summary>
        [Test]
        public void Location_TrailingSlashIsCanonicalized()
        {
            var directory = FileSystem.Directory.Open(_root + "assets/Config/");

            Assert.That(directory.Location, Is.EqualTo(_root + "assets/Config"));
            Assert.That(directory.Exists, Is.True);
        }

        /// <summary>Checks that children are found and enumerated with escaped authoritative locations.</summary>
        [Test]
        public void EnumerateEntries_ReturnsImmediateChildrenWithEscapedLocations()
        {
            var assets = FileSystem.Directory.Open(_root + "assets");

            var names = assets.EnumerateEntries().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var space = assets.FindFile("space name.txt")!;
            var percent = assets.FindFile("100%.txt")!;
            var unicode = assets.FindFile("中文.txt")!;

            Assert.That(names, Is.EqualTo(new[] { "100%.txt", "Config", "implicit", "space name.txt", "中文.txt" }));
            Assert.That(space.Location, Does.EndWith("assets/space%20name.txt"));
            Assert.That(percent.Location, Does.EndWith("assets/100%25.txt"));
            Assert.That(unicode.ReadAllText(), Is.EqualTo("unicode"));
            Assert.That(percent.ReadAllText(), Is.EqualTo("percent"));
            Assert.That(space.ReadAllText(), Is.EqualTo("space"));
        }

        /// <summary>Checks that unsafe archive records are never exposed at the root.</summary>
        [Test]
        public void EnumerateEntries_SkipsUnsafeRecords()
        {
            var names = FileSystem.Directory.Open(_root).EnumerateEntries().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal);

            Assert.That(names, Is.EqualTo(new[] { "assets", "top.txt" }));
            Assert.That(FileSystem.Directory.Open(_root + "unsafe").Exists, Is.False);
            Assert.That(FileSystem.Directory.Open(_root + "empty").Exists, Is.False);
        }

        /// <summary>Checks that child lookup and enumeration require an existing directory inside an existing archive.</summary>
        [Test]
        public void ChildOperations_RequireExistingDirectory()
        {
            var fs = new JarFileSystem();
            var missingArchive = ZipFixture.JarLocation(FileSystem.File.Open(Path.Combine(_temp.Location, "missing.apk")));

            Assert.Throws<DirectoryNotFoundException>(() => fs.GetChildEntry(missingArchive, "a"));
            Assert.Throws<DirectoryNotFoundException>(() => fs.GetChildEntry(_root + "missing", "a"));
            Assert.Throws<IOException>(() => fs.GetChildEntry(_root + "top.txt", "a"));
            Assert.Throws<DirectoryNotFoundException>(() => fs.EnumerateEntries(_root + "missing").ToArray());
            Assert.Throws<ArgumentException>(() => fs.GetChildEntry(_root, ".."));
        }

        /// <summary>Checks that descendant lookup resolves entries level by level inside the archive.</summary>
        [Test]
        public void FindFileAt_ResolvesDescendants()
        {
            var root = FileSystem.Directory.Open(_root);

            Assert.That(root.FindFileAt("assets/Config/options.txt")!.ReadAllText(), Is.EqualTo("options"));
            Assert.That(root.FindFileAt("assets/implicit/child.txt")!.ReadAllText(), Is.EqualTo("child"));
            Assert.That(root.FindFileAt("assets/missing/child.txt"), Is.Null);
            Assert.That(root.FindDirectoryAt("assets/Config"), Is.Not.Null);
        }

        /// <summary>Checks that reading reports a missing entry, a directory and a missing archive distinctly.</summary>
        [Test]
        public void OpenRead_ValidatesTarget()
        {
            var missingArchive = ZipFixture.JarLocation(FileSystem.File.Open(Path.Combine(_temp.Location, "missing.apk")), "a.txt");

            Assert.Throws<FileNotFoundException>(() => FileSystem.File.OpenRead(_root + "missing.txt"));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.OpenRead(_root + "assets"));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.OpenRead(_root + "assets/implicit"));
            Assert.Throws<FileNotFoundException>(() => FileSystem.File.OpenRead(missingArchive));
        }

        /// <summary>Checks the read-only capabilities of an archive stream and its idempotent disposal.</summary>
        [Test]
        public void ReadStream_IsReadOnlyAndOwnsArchive()
        {
            var stream = FileSystem.File.OpenRead(_root + "top.txt");
            var buffer = new byte[16];

            var read = stream.Read(buffer, 0, buffer.Length);

            Assert.That(Encoding.UTF8.GetString(buffer, 0, read), Is.EqualTo("top"));
            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanWrite, Is.False);
            Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, 1));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
            Assert.DoesNotThrow(stream.Flush);
            if (stream.CanSeek)
            {
                Assert.That(stream.Length, Is.EqualTo(3));
                stream.Position = 0;
                Assert.That(stream.Seek(1, SeekOrigin.Begin), Is.EqualTo(1));
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => _ = stream.Length);
                Assert.Throws<NotSupportedException>(() => _ = stream.Position);
                Assert.Throws<NotSupportedException>(() => stream.Position = 0);
                Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            }
            stream.Dispose();

            Assert.That(stream.CanRead, Is.False);
            Assert.That(stream.CanSeek, Is.False);
            Assert.DoesNotThrow(stream.Dispose);
        }

        /// <summary>Checks that every open stream owns its own archive, so streams are independent and the archive is released.</summary>
        [Test]
        public void ReadStreams_AreIndependent()
        {
            using (var first = new StreamReader(FileSystem.File.OpenRead(_root + "top.txt")))
            using (var second = new StreamReader(FileSystem.File.OpenRead(_root + "assets/Config/options.txt")))
            {
                Assert.That(first.ReadToEnd(), Is.EqualTo("top"));
                Assert.That(second.ReadToEnd(), Is.EqualTo("options"));
            }

            Assert.DoesNotThrow(() => _archive.Delete());
            Assert.That(_archive.Exists, Is.False);
        }

        /// <summary>Checks that one archive reader resolves literal names below its directory and reads many files.</summary>
        [Test]
        public void ArchiveReader_ReadsFilesBelowDirectory()
        {
            using var reader = FileSystem.Directory.OpenArchiveReader(_root + "assets");

            Assert.That(reader.Location, Is.EqualTo(_root + "assets"));
            Assert.That(reader.ReadAllBytes("Config/options.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("options")));
            Assert.That(reader.ReadAllBytes("implicit/child.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("child")));
            Assert.That(reader.ReadAllBytes("space name.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("space")));
            Assert.That(reader.ReadAllBytes("100%.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("percent")));
            Assert.That(reader.ReadAllBytes("中文.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("unicode")));
            Assert.That(reader.ReadAllBytes("Config/options.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("options")));
        }

        /// <summary>Checks that an archive reader rejects missing, directory, outside and malformed targets.</summary>
        [Test]
        public void ArchiveReader_ValidatesTargets()
        {
            using var reader = FileSystem.Directory.OpenArchiveReader(_root + "assets");

            Assert.Throws<FileNotFoundException>(() => reader.ReadAllBytes("missing.txt"));
            Assert.Throws<FileNotFoundException>(() => reader.ReadAllBytes("Config"));
            Assert.Throws<FileNotFoundException>(() => reader.ReadAllBytes("implicit"));
            Assert.Throws<FileNotFoundException>(() => reader.ReadAllBytes("top.txt"));
            Assert.Throws<ArgumentException>(() => reader.ReadAllBytes("../top.txt"));
            Assert.Throws<ArgumentException>(() => reader.ReadAllBytes("/top.txt"));
            Assert.Throws<ArgumentException>(() => reader.ReadAllBytes(string.Empty));
            Assert.Throws<ArgumentNullException>(() => reader.ReadAllBytes(null!));
        }

        /// <summary>Checks that opening an archive reader requires an existing jar directory.</summary>
        [Test]
        public void ArchiveReader_RequiresJarDirectory()
        {
            var missingArchive = ZipFixture.JarLocation(FileSystem.File.Open(Path.Combine(_temp.Location, "missing.apk")));

            Assert.Throws<ArgumentNullException>(() => FileSystem.Directory.OpenArchiveReader(null!));
            Assert.Throws<ArgumentException>(() => FileSystem.Directory.OpenArchiveReader(_temp.Location));
            Assert.Throws<DirectoryNotFoundException>(() => FileSystem.Directory.OpenArchiveReader(_root + "missing"));
            Assert.Throws<DirectoryNotFoundException>(() => FileSystem.Directory.OpenArchiveReader(missingArchive));
            Assert.Throws<IOException>(() => FileSystem.Directory.OpenArchiveReader(_root + "top.txt"));
        }

        /// <summary>Checks that an archive reader keeps the archive open until it is disposed, then refuses reads.</summary>
        [Test]
        public void ArchiveReader_OwnsArchiveUntilDisposed()
        {
            var reader = FileSystem.Directory.OpenArchiveReader(_root);
            Assert.That(reader.ReadAllBytes("top.txt"), Is.EqualTo(Encoding.UTF8.GetBytes("top")));

            reader.Dispose();

            Assert.DoesNotThrow(reader.Dispose);
            Assert.Throws<ObjectDisposedException>(() => reader.ReadAllBytes("top.txt"));
            Assert.DoesNotThrow(() => _archive.Delete());
            Assert.That(_archive.Exists, Is.False);
        }

        /// <summary>Checks asynchronous reads of an archive entry.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAllAsync_ReadsEntries() => UniTask.ToCoroutine(async () =>
        {
            var file = FileSystem.File.Open(_root + "assets/Config/options.txt");

            var text = await file.ReadAllTextAsync();
            var bytes = await file.ReadAllBytesAsync();

            Assert.That(text, Is.EqualTo("options"));
            Assert.That(bytes, Is.EqualTo(Encoding.UTF8.GetBytes("options")));
        });

        /// <summary>Checks that archive entries can be extracted to local storage synchronously and asynchronously.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Copy_ExtractsEntriesToLocalStorage() => UniTask.ToCoroutine(async () =>
        {
            var entry = FileSystem.File.Open(_root + "assets/Config/options.txt");

            var syncCopy = FileSystem.File.Copy(entry.Location, _temp.Location, "sync.txt");
            var asyncCopy = await entry.CopyToAsync(_temp.Directory, "async.txt");
            var locationCopy = FileSystem.File.Copy(entry.Location, Path.Combine(_temp.Location, "location.txt"));

            Assert.That(syncCopy.ReadAllText(), Is.EqualTo("options"));
            Assert.That(asyncCopy.ReadAllText(), Is.EqualTo("options"));
            Assert.That(locationCopy.ReadAllText(), Is.EqualTo("options"));
        });

        /// <summary>Checks that every mutation of archive entries is refused as read-only.</summary>
        [Test]
        public void Mutations_AreNotSupported()
        {
            var fs = new JarFileSystem();
            var file = _root + "top.txt";

            Assert.Throws<NotSupportedException>(() => fs.OpenWrite(file));
            Assert.Throws<NotSupportedException>(() => fs.CreateFile(_root, "a.txt"));
            Assert.Throws<NotSupportedException>(() => fs.CreateDirectory(_root, "a"));
            Assert.Throws<NotSupportedException>(() => fs.Rename(file, "b.txt"));
            Assert.Throws<NotSupportedException>(() => fs.Move(file, _root, "b.txt"));
            Assert.Throws<NotSupportedException>(() => fs.DeleteFile(file));
            Assert.Throws<NotSupportedException>(() => fs.DeleteDirectory(_root));
            Assert.Throws<NotSupportedException>(() => fs.SetAttributes(file, FileAttributes.Normal));
            Assert.Throws<NotSupportedException>(() => fs.SetLastWriteTime(file, DateTime.UtcNow));
        }

        /// <summary>Checks that facade operations refuse archive entries without changing the archive or destinations.</summary>
        [Test]
        public void FacadeMutations_RefuseArchiveOperands()
        {
            var local = _temp.WriteText("local.txt", "keep");
            var entry = _root + "top.txt";

            Assert.Throws<IOException>(() => FileSystem.File.Create(entry));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Create(entry, true));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Replace(local.Location, entry));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Replace(entry, local.Location));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Replace(local.Location, local.Location, _root + "backup.txt"));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Move(entry, _temp.Location, "moved.txt"));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Open(entry).Rename("x.txt"));
            Assert.Throws<NotSupportedException>(() => FileSystem.Directory.Open(_root).CreateFile("x.txt"));
            Assert.That(local.ReadAllText(), Is.EqualTo("keep"));
            Assert.That(FileSystem.File.Open(entry).ReadAllText(), Is.EqualTo("top"));
        }

        /// <summary>Checks that an existing archive directory can be opened through the idempotent create while a missing one cannot.</summary>
        [Test]
        public void DirectoryCreate_OpensOnlyExistingArchiveDirectories()
        {
            var opened = FileSystem.Directory.Create(_root + "assets");

            Assert.That(opened.Location, Is.EqualTo(_root + "assets"));
            Assert.Throws<NotSupportedException>(() => FileSystem.Directory.Create(_root + "missing"));
            Assert.Throws<IOException>(() => FileSystem.Directory.Create(_root + "top.txt"));
        }

        /// <summary>Checks that malformed, remote, nested and traversal jar locations are rejected.</summary>
        /// <param name="suffix">The location part after the archive file URI.</param>
        [TestCase("")]
        [TestCase("!/a\\b")]
        [TestCase("!/a?query")]
        [TestCase("!/a#fragment")]
        [TestCase("!/a/../b")]
        [TestCase("!/a%2Fb")]
        [TestCase("!/a%2")]
        [TestCase("!/a%zz")]
        [TestCase("!//")]
        [TestCase("!/a//b")]
        public void Location_RejectsMalformedEntryPaths(string suffix)
        {
            var location = "jar:" + new Uri(_archive.Location).AbsoluteUri + suffix;

            Assert.Throws(Is.InstanceOf<ArgumentException>(), () => FileSystem.File.Open(location));
        }

        /// <summary>Checks that archive URIs that are not absolute local file URIs are rejected.</summary>
        /// <param name="location">An invalid jar location.</param>
        [TestCase("jar:not a uri!/a.txt")]
        [TestCase("jar:file:/C:/archive.zip!/a.txt")]
        [TestCase("jar:file://server/share/archive.zip!/a.txt")]
        [TestCase("jar:file://!/a.txt")]
        [TestCase("jar:file:///C:/bad%2.zip!/a.txt")]
        public void Location_RejectsInvalidArchiveUris(string location)
        {
            Assert.Throws(Is.InstanceOf<ArgumentException>(), () => FileSystem.File.Open(location));
        }

        /// <summary>Checks that remote and nested archives are not supported.</summary>
        /// <param name="location">A jar location backed by a non-file archive.</param>
        [TestCase("jar:http://example.invalid/a.zip!/a.txt")]
        [TestCase("jar:jar:file:///C:/a.zip!/b.zip!/a.txt")]
        public void Location_RejectsRemoteAndNestedArchives(string location)
        {
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Open(location));
            Assert.Throws<NotSupportedException>(() => JarFileSystem.NormalizeLocation(location));
        }

        /// <summary>Checks that normalization requires a jar URI.</summary>
        [Test]
        public void NormalizeLocation_RequiresJarUri()
        {
            Assert.Throws<ArgumentNullException>(() => JarFileSystem.NormalizeLocation(null!));
            Assert.Throws<ArgumentException>(() => JarFileSystem.NormalizeLocation("file:///C:/a.zip!/a.txt"));
        }

        /// <summary>Checks that literal exclamation marks in the archive name are escaped in canonical locations.</summary>
        [Test]
        public void Location_EscapesExclamationMarkInArchiveName()
        {
            var archive = ZipFixture.Create(_temp.Directory, "a!b.zip", ("x.txt", "bang"));

            var file = FileSystem.File.Open(ZipFixture.JarLocation(archive, "x.txt"));

            Assert.That(file.Location, Does.Contain("a%21b.zip!/x.txt"));
            Assert.That(file.ReadAllText(), Is.EqualTo("bang"));
            Assert.That(FileSystem.File.Open(file.Location).Location, Is.EqualTo(file.Location));
        }

        /// <summary>Checks that a file that is not a ZIP archive is reported as invalid data rather than absence.</summary>
        [Test]
        public void CorruptArchive_IsInvalidData()
        {
            var corrupt = _temp.WriteText("corrupt.zip", "this is not a zip archive");
            var location = ZipFixture.JarLocation(corrupt, "a.txt");

            Assert.Throws<InvalidDataException>(() => FileSystem.GetEntry(location));
            Assert.Throws<InvalidDataException>(() => FileSystem.File.OpenRead(location));
        }

        /// <summary>Checks that duplicate records and file/directory collisions are reported as ambiguous.</summary>
        /// <param name="first">The first raw entry name.</param>
        /// <param name="second">The second raw entry name.</param>
        [TestCase("dup.txt", "dup.txt")]
        [TestCase("dir/", "dir/")]
        [TestCase("name", "name/child.txt")]
        [TestCase("name/child.txt", "name")]
        public void AmbiguousArchive_IsRejected(string first, string second)
        {
            var archive = ZipFixture.Create(_temp.Directory, "ambiguous.zip", (first, "1"), (second, "2"));

            Assert.Throws<IOException>(() => FileSystem.GetEntry(ZipFixture.JarLocation(archive)));
        }

        /// <summary>Checks that an explicit directory record after an implicit one is merged instead of rejected.</summary>
        [Test]
        public void ExplicitDirectoryAfterImplicit_IsMerged()
        {
            var archive = ZipFixture.Create(_temp.Directory, "merged.zip", ("dir/a.txt", "a"), ("dir/", string.Empty));

            var entry = FileSystem.GetEntry(ZipFixture.JarLocation(archive, "dir"))!;

            Assert.That(entry.IsDirectory, Is.True);
            Assert.That(entry.LastWriteTimeUtc, Is.Not.Null);
        }
    }
}

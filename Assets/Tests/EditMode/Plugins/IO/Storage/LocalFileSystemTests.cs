#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers the local backend contract directly, including metadata, streams and error mapping.</summary>
    public class LocalFileSystemTests
    {
        /// <summary>The backend under test.</summary>
        private LocalFileSystem _fs = null!;

        /// <summary>The temporary directory of the current test.</summary>
        private TemporaryDirectory _temp = null!;

        /// <summary>Creates the backend and an empty temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _fs = new LocalFileSystem();
            _temp = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _temp.Dispose();
        }

        /// <summary>Gets a native path below the temporary directory.</summary>
        /// <param name="name">The child name.</param>
        /// <returns>The combined native path.</returns>
        private string PathOf(string name)
        {
            return Path.Combine(_temp.Location, name);
        }

        /// <summary>Checks that file metadata reports length, UTC timestamps and no link or hidden flags.</summary>
        [Test]
        public void GetEntry_ReportsFileMetadata()
        {
            var file = _temp.WriteText("a.txt", "12345");

            var entry = _fs.GetEntry(file.Location)!;

            Assert.That(entry.Name, Is.EqualTo("a.txt"));
            Assert.That(entry.IsDirectory, Is.False);
            Assert.That(entry.Length, Is.EqualTo(5));
            Assert.That(entry.LastWriteTimeUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(entry.CreationTimeUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(entry.IsSymbolicLink, Is.False);
            Assert.That(entry.IsHidden, Is.False);
            Assert.That(entry.IsSystem, Is.False);
            Assert.That(entry.ResourceId, Is.Null);
        }

        /// <summary>Checks that directory metadata has no length but reports timestamps.</summary>
        [Test]
        public void GetEntry_ReportsDirectoryMetadata()
        {
            var entry = _fs.GetEntry(_temp.Location)!;

            Assert.That(entry.IsDirectory, Is.True);
            Assert.That(entry.Length, Is.Null);
            Assert.That(entry.LastWriteTimeUtc, Is.Not.Null);
            Assert.That(entry.Location, Is.EqualTo(_temp.Location));
        }

        /// <summary>Checks that a missing entry and an entry below a missing directory both report absence.</summary>
        [Test]
        public void GetEntry_ReturnsNullForMissingEntries()
        {
            Assert.That(_fs.GetEntry(PathOf("missing.txt")), Is.Null);
            Assert.That(_fs.GetEntry(Path.Combine(PathOf("missing"), "child.txt")), Is.Null);
        }

        /// <summary>Checks that hidden and system attributes are written and reported by later metadata queries.</summary>
        [Test]
        public void SetAttributes_ReportsHiddenAndSystemFlags()
        {
            var file = _temp.WriteText("flags.txt", "x");

            _fs.SetAttributes(file.Location, FileAttributes.Hidden | FileAttributes.System);
            var flagged = _fs.GetEntry(file.Location)!;
            _fs.SetAttributes(file.Location, FileAttributes.Normal);
            var cleared = _fs.GetEntry(file.Location)!;

            Assert.That(flagged.IsHidden, Is.True);
            Assert.That(flagged.IsSystem, Is.True);
            Assert.That(cleared.IsHidden, Is.False);
            Assert.That(cleared.IsSystem, Is.False);
        }

        /// <summary>Checks that a UTC modification time is stored exactly.</summary>
        [Test]
        public void SetLastWriteTime_StoresUtcTime()
        {
            var file = _temp.WriteText("time.txt", "x");
            var time = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);

            _fs.SetLastWriteTime(file.Location, time);

            Assert.That(_fs.GetEntry(file.Location)!.LastWriteTimeUtc, Is.EqualTo(time));
        }

        /// <summary>Checks that the backend itself rejects timestamps that are not UTC.</summary>
        [Test]
        public void SetLastWriteTime_RejectsNonUtcTime()
        {
            var file = _temp.WriteText("time.txt", "x");

            var exception = Assert.Throws<ArgumentException>(() => _fs.SetLastWriteTime(file.Location, DateTime.Now));

            Assert.That(exception.ParamName, Is.EqualTo("lastWriteTimeUtc"));
        }

        /// <summary>Checks that child lookup finds both kinds and returns null for a missing name.</summary>
        [Test]
        public void GetChildEntry_FindsFilesAndDirectories()
        {
            _temp.WriteText("file.txt", "x");
            _temp.Directory.CreateDirectory("dir");

            Assert.That(_fs.GetChildEntry(_temp.Location, "file.txt")!.IsDirectory, Is.False);
            Assert.That(_fs.GetChildEntry(_temp.Location, "dir")!.IsDirectory, Is.True);
            Assert.That(_fs.GetChildEntry(_temp.Location, "missing"), Is.Null);
        }

        /// <summary>Checks that platform-invalid names and Windows trailing dot or space aliases are rejected.</summary>
        /// <param name="name">A name that passes portable validation but is invalid locally.</param>
        [TestCase("a:b")]
        [TestCase("a*b")]
        [TestCase("a?b")]
        [TestCase("a|b")]
        [TestCase("trailing.")]
        [TestCase("trailing ")]
        public void GetChildEntry_RejectsLocallyInvalidNames(string name)
        {
            var exception = Assert.Throws<ArgumentException>(() => _fs.GetChildEntry(_temp.Location, name));

            Assert.That(exception.ParamName, Is.EqualTo("name"));
        }

        /// <summary>Checks that a missing parent and a file parent are reported with distinct exceptions.</summary>
        [Test]
        public void GetChildEntry_RequiresExistingDirectoryParent()
        {
            var file = _temp.WriteText("file.txt", "x");

            Assert.Throws<DirectoryNotFoundException>(() => _fs.GetChildEntry(PathOf("missing"), "a"));
            Assert.Throws<IOException>(() => _fs.GetChildEntry(file.Location, "a"));
        }

        /// <summary>Checks that enumeration returns only immediate children with authoritative locations.</summary>
        [Test]
        public void EnumerateEntries_ReturnsImmediateChildren()
        {
            _temp.WriteText("a.txt", "x");
            var sub = _temp.Directory.CreateDirectory("sub");
            sub.CreateFile("nested.txt");

            var entries = _fs.EnumerateEntries(_temp.Location).OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();

            Assert.That(entries.Select(x => x.Name), Is.EqualTo(new[] { "a.txt", "sub" }));
            Assert.That(entries[1].Location, Is.EqualTo(sub.Location));
            Assert.Throws<DirectoryNotFoundException>(() => _fs.EnumerateEntries(PathOf("missing")).ToArray());
        }

        /// <summary>Checks that opening a missing file for reading reports a missing file even when its parent is missing.</summary>
        [Test]
        public void OpenRead_ReportsMissingFiles()
        {
            Assert.Throws<FileNotFoundException>(() => _fs.OpenRead(PathOf("missing.txt")));
            Assert.Throws<FileNotFoundException>(() => _fs.OpenRead(Path.Combine(PathOf("missing"), "a.txt")));
        }

        /// <summary>Checks that the backend never creates a file when opening it for writing.</summary>
        [Test]
        public void OpenWrite_RequiresExistingFile()
        {
            Assert.Throws<FileNotFoundException>(() => _fs.OpenWrite(PathOf("missing.txt")));
            Assert.Throws<FileNotFoundException>(() => _fs.OpenWrite(Path.Combine(PathOf("missing"), "a.txt")));
            Assert.That(_fs.GetEntry(PathOf("missing.txt")), Is.Null);
        }

        /// <summary>Checks that writing truncates by default and appends on request.</summary>
        [Test]
        public void OpenWrite_TruncatesOrAppends()
        {
            var file = _temp.WriteText("a.txt", "original");

            using (var stream = _fs.OpenWrite(file.Location))
            {
                stream.Write(Encoding.ASCII.GetBytes("new"), 0, 3);
            }
            var truncated = file.ReadAllText();
            using (var stream = _fs.OpenWrite(file.Location, append: true))
            {
                stream.Write(Encoding.ASCII.GetBytes("+tail"), 0, 5);
            }

            Assert.That(truncated, Is.EqualTo("new"));
            Assert.That(file.ReadAllText(), Is.EqualTo("new+tail"));
        }

        /// <summary>Checks that a sharing violation fails the open without truncating the existing content.</summary>
        [Test]
        public void OpenWrite_SharingViolationDoesNotTruncate()
        {
            var file = _temp.WriteText("locked.txt", "keep");

            using (var reader = _fs.OpenRead(file.Location))
            {
                Assert.Throws(Is.InstanceOf<IOException>(), () => _fs.OpenWrite(file.Location).Dispose());
            }

            Assert.That(file.ReadAllText(), Is.EqualTo("keep"));
        }

        /// <summary>Checks that file creation returns the new entry and refuses an occupied name.</summary>
        [Test]
        public void CreateFile_CreatesEmptyFileWithoutOverwriting()
        {
            var created = _fs.CreateFile(_temp.Location, "new.txt", "text/plain");

            Assert.That(created.Length, Is.EqualTo(0));
            Assert.That(created.Location, Is.EqualTo(PathOf("new.txt")));
            Assert.Throws<IOException>(() => _fs.CreateFile(_temp.Location, "new.txt"));
        }

        /// <summary>Checks that file creation requires a content type and an existing parent directory.</summary>
        [Test]
        public void CreateFile_ValidatesArguments()
        {
            var mime = Assert.Throws<ArgumentException>(() => _fs.CreateFile(_temp.Location, "a.txt", " "));

            Assert.That(mime.ParamName, Is.EqualTo("mimeType"));
            Assert.Throws<DirectoryNotFoundException>(() => _fs.CreateFile(PathOf("missing"), "a.txt"));
            Assert.Throws<ArgumentException>(() => _fs.CreateFile(_temp.Location, "a:b"));
        }

        /// <summary>Checks that directory creation is idempotent and refuses a name occupied by a file.</summary>
        [Test]
        public void CreateDirectory_IsIdempotentButRefusesFiles()
        {
            var first = _fs.CreateDirectory(_temp.Location, "dir");
            var second = _fs.CreateDirectory(_temp.Location, "dir");
            _temp.WriteText("file", "x");

            Assert.That(second.Location, Is.EqualTo(first.Location));
            Assert.That(second.IsDirectory, Is.True);
            Assert.Throws(Is.InstanceOf<IOException>(), () => _fs.CreateDirectory(_temp.Location, "file"));
        }

        /// <summary>Checks that renaming moves the entry within its parent and returns the new metadata.</summary>
        [Test]
        public void Rename_MovesWithinParent()
        {
            var file = _temp.WriteText("old.txt", "x");

            var renamed = _fs.Rename(file.Location, "new.txt");

            Assert.That(renamed.Location, Is.EqualTo(PathOf("new.txt")));
            Assert.That(_fs.GetEntry(file.Location), Is.Null);
        }

        /// <summary>Checks that renaming to the current name returns the unchanged entry.</summary>
        [Test]
        public void Rename_ToSameNameIsNoOp()
        {
            var file = _temp.WriteText("same.txt", "x");

            var renamed = _fs.Rename(file.Location, "same.txt");

            Assert.That(renamed.Location, Is.EqualTo(file.Location));
            Assert.That(file.ReadAllText(), Is.EqualTo("x"));
        }

        /// <summary>Checks that a case-only rename is not mistaken for a sibling conflict on case-insensitive Windows volumes.</summary>
        [Test]
        public void Rename_CaseOnlyChangeIsAllowed()
        {
            var file = _temp.WriteText("case.txt", "x");

            var renamed = _fs.Rename(file.Location, "CASE.txt");

            Assert.That(renamed.Name, Is.EqualTo("CASE.txt"));
            Assert.That(_temp.Directory.EnumerateEntries().Single().Name, Is.EqualTo("CASE.txt"));
        }

        /// <summary>Checks that renaming refuses a missing source, an occupied sibling and a filesystem root.</summary>
        [Test]
        public void Rename_RejectsMissingSourceSiblingAndRoot()
        {
            var file = _temp.WriteText("a.txt", "a");
            _temp.WriteText("b.txt", "b");
            var root = Path.GetPathRoot(_temp.Location)!;

            Assert.Throws<FileNotFoundException>(() => _fs.Rename(PathOf("missing.txt"), "x.txt"));
            Assert.Throws<IOException>(() => _fs.Rename(file.Location, "b.txt"));
            Assert.Throws<IOException>(() => _fs.Rename(root, "renamed-root"));
            Assert.That(file.ReadAllText(), Is.EqualTo("a"));
        }

        /// <summary>Checks that moving a file and a directory into another directory keeps their content.</summary>
        [Test]
        public void Move_MovesFilesAndDirectories()
        {
            var target = _temp.Directory.CreateDirectory("target");
            var file = _temp.WriteText("a.txt", "content");
            var directory = _temp.Directory.CreateDirectory("dir");
            directory.CreateFile("child.txt");

            var movedFile = _fs.Move(file.Location, target.Location, "b.txt");
            var movedDirectory = _fs.Move(directory.Location, target.Location, "dir2");

            Assert.That(movedFile.Location, Is.EqualTo(Path.Combine(target.Location, "b.txt")));
            Assert.That(FileSystem.File.Open(movedFile.Location).ReadAllText(), Is.EqualTo("content"));
            Assert.That(movedDirectory.IsDirectory, Is.True);
            Assert.That(FileSystem.Directory.Open(movedDirectory.Location).FindFile("child.txt"), Is.Not.Null);
            Assert.That(directory.Exists, Is.False);
        }

        /// <summary>Checks that moving refuses a missing source, a missing destination and an occupied destination name.</summary>
        [Test]
        public void Move_RejectsInvalidOperands()
        {
            var target = _temp.Directory.CreateDirectory("target");
            target.CreateFile("taken.txt");
            var file = _temp.WriteText("a.txt", "x");

            Assert.Throws<FileNotFoundException>(() => _fs.Move(PathOf("missing"), target.Location, "a"));
            Assert.Throws<DirectoryNotFoundException>(() => _fs.Move(file.Location, PathOf("missing"), "a"));
            Assert.Throws<IOException>(() => _fs.Move(file.Location, target.Location, "taken.txt"));
            Assert.That(file.Exists, Is.True);
        }

        /// <summary>Checks that moving a directory into its own subtree fails without losing the directory.</summary>
        [Test]
        public void Move_RejectsMovingIntoOwnSubtree()
        {
            var directory = _temp.Directory.CreateDirectory("outer");
            var inner = directory.CreateDirectory("inner");

            Assert.Throws(Is.InstanceOf<IOException>(), () => _fs.Move(directory.Location, inner.Location, "loop"));
            Assert.That(inner.Exists, Is.True);
        }

        /// <summary>Checks that deleting a missing file or a file below a missing directory is a no-op.</summary>
        [Test]
        public void DeleteFile_IgnoresMissingFiles()
        {
            Assert.DoesNotThrow(() => _fs.DeleteFile(PathOf("missing.txt")));
            Assert.DoesNotThrow(() => _fs.DeleteFile(Path.Combine(PathOf("missing"), "a.txt")));
        }

        /// <summary>Checks that file deletion removes files but never deletes a directory.</summary>
        [Test]
        public void DeleteFile_DeletesFilesButRefusesDirectories()
        {
            var file = _temp.WriteText("a.txt", "x");
            var directory = _temp.Directory.CreateDirectory("dir");

            _fs.DeleteFile(file.Location);

            Assert.That(file.Exists, Is.False);
            Assert.Throws<IOException>(() => _fs.DeleteFile(directory.Location));
            Assert.That(directory.Exists, Is.True);
        }

        /// <summary>Checks that directory deletion validates the target and refuses non-empty directories without recursion.</summary>
        [Test]
        public void DeleteDirectory_ValidatesTarget()
        {
            var file = _temp.WriteText("a.txt", "x");
            var directory = _temp.Directory.CreateDirectory("dir");
            directory.CreateFile("child.txt");

            Assert.Throws<DirectoryNotFoundException>(() => _fs.DeleteDirectory(PathOf("missing")));
            Assert.Throws<IOException>(() => _fs.DeleteDirectory(file.Location));
            Assert.Throws(Is.InstanceOf<IOException>(), () => _fs.DeleteDirectory(directory.Location));
            Assert.That(directory.Exists, Is.True);
        }

        /// <summary>Checks that recursive deletion removes nested directories and files, and empty directories delete without recursion.</summary>
        [Test]
        public void DeleteDirectory_RemovesTrees()
        {
            var empty = _temp.Directory.CreateDirectory("empty");
            var tree = _temp.Directory.CreateDirectory("tree");
            tree.CreateDirectoryAt("a/b/c").CreateFile("deep.txt");
            tree.CreateFile("top.txt");

            _fs.DeleteDirectory(empty.Location);
            _fs.DeleteDirectory(tree.Location, true);

            Assert.That(empty.Exists, Is.False);
            Assert.That(tree.Exists, Is.False);
        }

        /// <summary>Checks that path equality ignores case on Windows and treats file URIs like their paths.</summary>
        [Test]
        public void LocationsEqual_ComparesNormalizedSpellings()
        {
            var path = PathOf("Name.txt");

            Assert.That(LocalFileSystem.LocationsEqual(path, path.ToUpperInvariant()), Is.True);
            Assert.That(LocalFileSystem.LocationsEqual(path, new Uri(path).AbsoluteUri), Is.True);
            Assert.That(LocalFileSystem.LocationsEqual(path, PathOf("Other.txt")), Is.False);
        }
    }
}

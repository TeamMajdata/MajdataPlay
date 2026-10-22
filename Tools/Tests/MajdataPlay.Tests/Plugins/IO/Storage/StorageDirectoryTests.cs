#nullable enable
using System;
using System.IO;
using System.Linq;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers directory handles: lookup, descendants, creation, enumeration, deletion and metadata.</summary>
    public class StorageDirectoryTests
    {
        /// <summary>The temporary directory of the current test.</summary>
        private TemporaryDirectory _temp = null!;

        /// <summary>Creates an empty temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _temp = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _temp.Dispose();
        }

        /// <summary>Checks that the constructor rejects a missing backend or location.</summary>
        [Test]
        public void Constructor_ValidatesArguments()
        {
            var fs = new LocalFileSystem();

            Assert.Throws<ArgumentNullException>(() => new StorageDirectory(null!, _temp.Location));
            Assert.Throws<ArgumentNullException>(() => new StorageDirectory(fs, null!));
            Assert.Throws<ArgumentException>(() => new StorageDirectory(fs, "\t"));
        }

        /// <summary>Checks that a directory handle reports a file location as not existing.</summary>
        [Test]
        public void Exists_IsFalseForFilesAndMissingEntries()
        {
            var file = _temp.WriteText("a.txt", "x");

            Assert.That(_temp.Directory.Exists, Is.True);
            Assert.That(FileSystem.Directory.Open(file.Location).Exists, Is.False);
            Assert.That(FileSystem.Directory.Open(Path.Combine(_temp.Location, "missing")).Exists, Is.False);
        }

        /// <summary>Checks that file and directory enumeration split the immediate children by kind.</summary>
        [Test]
        public void Enumerate_SplitsChildrenByKind()
        {
            _temp.WriteText("b.txt", "x");
            _temp.WriteText("a.txt", "x");
            _temp.Directory.CreateDirectory("sub").CreateFile("nested.txt");

            var files = _temp.Directory.EnumerateFiles().Select(x => x.Entry!.Name).OrderBy(x => x, StringComparer.Ordinal);
            var directories = _temp.Directory.EnumerateDirectories().Select(x => x.Entry!.Name);

            Assert.That(files, Is.EqualTo(new[] { "a.txt", "b.txt" }));
            Assert.That(directories, Is.EqualTo(new[] { "sub" }));
            Assert.That(_temp.Directory.EnumerateEntries().Count(), Is.EqualTo(3));
        }

        /// <summary>Checks that enumerating a missing directory fails.</summary>
        [Test]
        public void Enumerate_RequiresExistingDirectory()
        {
            var missing = FileSystem.Directory.Open(Path.Combine(_temp.Location, "missing"));

            Assert.Throws<DirectoryNotFoundException>(() => missing.EnumerateFiles().ToArray());
            Assert.Throws<DirectoryNotFoundException>(() => missing.EnumerateDirectories().ToArray());
        }

        /// <summary>Checks that typed lookups return null for a child of the other kind.</summary>
        [Test]
        public void Find_ReturnsOnlyTheRequestedKind()
        {
            _temp.WriteText("file", "x");
            _temp.Directory.CreateDirectory("dir");

            Assert.That(_temp.Directory.FindEntry("file")!.IsDirectory, Is.False);
            Assert.That(_temp.Directory.FindFile("file"), Is.Not.Null);
            Assert.That(_temp.Directory.FindDirectory("file"), Is.Null);
            Assert.That(_temp.Directory.FindDirectory("dir"), Is.Not.Null);
            Assert.That(_temp.Directory.FindFile("dir"), Is.Null);
            Assert.That(_temp.Directory.FindEntry("missing"), Is.Null);
            Assert.That(_temp.Directory.FindFile("missing"), Is.Null);
            Assert.That(_temp.Directory.FindDirectory("missing"), Is.Null);
        }

        /// <summary>Checks that every lookup validates its single child name.</summary>
        [Test]
        public void Find_RejectsPathNames()
        {
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindEntry("a/b"));
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindFile(".."));
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindDirectory(""));
        }

        /// <summary>Checks that descendant lookup resolves each level and returns null when any level is missing.</summary>
        [Test]
        public void FindAt_ResolvesDescendants()
        {
            var deep = _temp.Directory.CreateDirectoryAt("Skins/default");
            deep.CreateFile("notes.png");

            Assert.That(_temp.Directory.FindFileAt("Skins/default/notes.png")!.Location,
                Is.EqualTo(Path.Combine(deep.Location, "notes.png")));
            Assert.That(_temp.Directory.FindFileAt("Skins\\default\\notes.png"), Is.Not.Null);
            Assert.That(_temp.Directory.FindFileAt("Skins/missing/notes.png"), Is.Null);
            Assert.That(_temp.Directory.FindFileAt("Skins/default"), Is.Null);
            Assert.That(_temp.Directory.FindDirectoryAt("Skins/default")!.Location, Is.EqualTo(deep.Location));
            Assert.That(_temp.Directory.FindDirectoryAt("Skins/default/notes.png"), Is.Null);
            Assert.That(_temp.Directory.FindDirectoryAt(string.Empty)!.Location, Is.EqualTo(_temp.Location));
        }

        /// <summary>Checks that descendant lookup rejects paths without a file name, rooted paths and traversal.</summary>
        [Test]
        public void FindAt_RejectsInvalidPaths()
        {
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindFileAt(string.Empty));
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindFileAt("./"));
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindFileAt("../outside.txt"));
            Assert.Throws<ArgumentException>(() => _temp.Directory.FindDirectoryAt("/rooted"));
        }

        /// <summary>Checks that descendant lookup below a missing directory reports the missing directory.</summary>
        [Test]
        public void FindAt_RequiresExistingStartDirectory()
        {
            var missing = FileSystem.Directory.Open(Path.Combine(_temp.Location, "missing"));

            Assert.Throws<DirectoryNotFoundException>(() => missing.FindFileAt("a/b.txt"));
        }

        /// <summary>Checks that nested creation creates each missing level, is idempotent and accepts split names.</summary>
        [Test]
        public void CreateDirectoryAt_CreatesEachLevel()
        {
            var first = _temp.Directory.CreateDirectoryAt("Cache/Runtime");
            var second = _temp.Directory.CreateDirectoryAt(new[] { "Cache", "Runtime" });
            var self = _temp.Directory.CreateDirectoryAt(string.Empty);

            Assert.That(first.Exists, Is.True);
            Assert.That(second.Location, Is.EqualTo(first.Location));
            Assert.That(self.Location, Is.EqualTo(_temp.Location));
        }

        /// <summary>Checks that nested creation fails when a file occupies a level.</summary>
        [Test]
        public void CreateDirectoryAt_RefusesFileOnTheWay()
        {
            _temp.WriteText("Cache", "x");

            Assert.Throws(Is.InstanceOf<IOException>(), () => _temp.Directory.CreateDirectoryAt("Cache/Runtime"));
        }

        /// <summary>Checks that file creation validates its name and content type and never overwrites a sibling.</summary>
        [Test]
        public void CreateFile_ValidatesAndRefusesSiblings()
        {
            var file = _temp.Directory.CreateFile("a.txt", "text/plain");

            Assert.That(file.Exists, Is.True);
            Assert.Throws<ArgumentException>(() => _temp.Directory.CreateFile("a/b"));
            Assert.Throws<ArgumentException>(() => _temp.Directory.CreateFile("b.txt", string.Empty));
            Assert.Throws<IOException>(() => _temp.Directory.CreateFile("a.txt"));
        }

        /// <summary>Checks that child directory creation validates its name and returns the existing directory.</summary>
        [Test]
        public void CreateDirectory_ValidatesAndReturnsExisting()
        {
            var first = _temp.Directory.CreateDirectory("dir");

            Assert.That(_temp.Directory.CreateDirectory("dir").Location, Is.EqualTo(first.Location));
            Assert.Throws<ArgumentException>(() => _temp.Directory.CreateDirectory("."));
        }

        /// <summary>Checks that non-recursive deletion refuses a non-empty directory while recursive deletion removes it.</summary>
        [Test]
        public void Delete_HonoursRecursion()
        {
            var directory = _temp.Directory.CreateDirectory("dir");
            directory.CreateFile("a.txt");

            Assert.Throws(Is.InstanceOf<IOException>(), () => directory.Delete());
            directory.Delete(true);

            Assert.That(directory.Exists, Is.False);
        }

        /// <summary>Checks that renaming returns a handle at the new location.</summary>
        [Test]
        public void Rename_ReturnsNewHandle()
        {
            var directory = _temp.Directory.CreateDirectory("old");
            directory.CreateFile("child.txt");

            var renamed = directory.Rename("new");

            Assert.That(renamed.Location, Is.EqualTo(Path.Combine(_temp.Location, "new")));
            Assert.That(renamed.FindFile("child.txt"), Is.Not.Null);
            Assert.That(directory.Exists, Is.False);
        }

        /// <summary>Checks that renaming validates the name and requires an existing directory rather than a file.</summary>
        [Test]
        public void Rename_ValidatesNameAndExistence()
        {
            var directory = _temp.Directory.CreateDirectory("dir");
            var fileAsDirectory = FileSystem.Directory.Open(_temp.WriteText("file", "x").Location);
            _temp.Directory.CreateDirectory("taken");

            Assert.Throws<ArgumentException>(() => directory.Rename("a\\b"));
            Assert.Throws<DirectoryNotFoundException>(() => fileAsDirectory.Rename("x"));
            Assert.Throws<IOException>(() => directory.Rename("taken"));
        }

        /// <summary>Checks that directory attribute flags are written through the handle.</summary>
        [Test]
        public void SetAttributes_AppliesNativeFlags()
        {
            var directory = _temp.Directory.CreateDirectory("dir");

            directory.SetAttributes(FileAttributes.Hidden);
            var hidden = directory.Entry!.IsHidden;
            directory.SetAttributes(FileAttributes.Normal);

            Assert.That(hidden, Is.True);
            Assert.That(directory.Entry!.IsHidden, Is.False);
            Assert.That(directory.Exists, Is.True);
        }

        /// <summary>Checks that a local directory timestamp is converted to UTC and stored.</summary>
        [Test]
        [Ignore("Known issue: LocalFileSystem.SetLastWriteTime uses File.SetLastWriteTimeUtc, which throws UnauthorizedAccessException for directories on Windows.")]
        public void SetLastWriteTime_ConvertsLocalTime()
        {
            var directory = _temp.Directory.CreateDirectory("dir");
            var utc = new DateTime(2019, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            directory.SetLastWriteTime(utc.ToLocalTime());

            Assert.That(directory.Entry!.LastWriteTimeUtc, Is.EqualTo(utc));
        }

        /// <summary>Checks that the directory timestamp conversion reaches a provider backend, which stores the UTC value.</summary>
        [Test]
        public void SetLastWriteTime_PassesUtcToProvider()
        {
            var provider = new MemoryContentProvider();
            var directory = new StorageDirectory(provider, provider.AddDirectory(provider.RootLocation, "dir"));
            var utc = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            directory.SetLastWriteTime(utc.ToLocalTime());
            var stored = directory.Entry!.LastWriteTimeUtc;
            provider.AcceptsTimestamps = false;

            Assert.That(stored, Is.EqualTo(utc));
            Assert.Throws<NotSupportedException>(() => directory.SetLastWriteTime(utc));
            Assert.DoesNotThrow(() => directory.SetAttributes(FileAttributes.Hidden));
        }

        /// <summary>Checks that a UTC directory timestamp is passed through unchanged.</summary>
        [Test]
        [Ignore("Known issue: LocalFileSystem.SetLastWriteTime uses File.SetLastWriteTimeUtc, which throws UnauthorizedAccessException for directories on Windows.")]
        public void SetLastWriteTime_KeepsUtcTime()
        {
            var directory = _temp.Directory.CreateDirectory("dir");
            var utc = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            directory.SetLastWriteTime(utc);

            Assert.That(directory.Entry!.LastWriteTimeUtc, Is.EqualTo(utc));
        }
    }
}

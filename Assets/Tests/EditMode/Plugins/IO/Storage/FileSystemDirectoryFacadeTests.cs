#nullable enable
using System;
using System.IO;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers the location-based directory facade over the local backend and a registered content provider.</summary>
    public class FileSystemDirectoryFacadeTests
    {
        /// <summary>The temporary directory of the current test.</summary>
        private TemporaryDirectory _temp = null!;

        /// <summary>The in-memory provider registered for content URIs.</summary>
        private MemoryContentProvider _provider = null!;

        /// <summary>The provider registration restored after each test.</summary>
        private ContentProviderScope _scope = null!;

        /// <summary>Creates a temporary directory and registers a fresh content provider.</summary>
        [SetUp]
        public void SetUp()
        {
            _temp = new TemporaryDirectory();
            _provider = new MemoryContentProvider();
            _scope = new ContentProviderScope(_provider);
        }

        /// <summary>Restores the provider registration and deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _temp.Dispose();
        }

        /// <summary>Gets a native path below the temporary directory.</summary>
        /// <param name="name">The child name.</param>
        /// <returns>The combined native path.</returns>
        private string PathOf(string name)
        {
            return Path.Combine(_temp.Location, name);
        }

        /// <summary>Checks that opening a directory handle neither requires nor creates the directory.</summary>
        [Test]
        public void Open_DoesNotCreateDirectory()
        {
            var handle = FileSystem.Directory.Open(PathOf("lazy"));

            Assert.That(handle.Exists, Is.False);
            Assert.That(handle.Location, Is.EqualTo(PathOf("lazy")));
        }

        /// <summary>Checks that local creation by location creates missing parents and is idempotent.</summary>
        [Test]
        public void Create_CreatesLocalDirectoryAndParents()
        {
            var nested = Path.Combine(PathOf("a"), "b", "c");

            var created = FileSystem.Directory.Create(nested);
            var again = FileSystem.Directory.Create(nested);

            Assert.That(created.Exists, Is.True);
            Assert.That(again.Location, Is.EqualTo(created.Location));
        }

        /// <summary>Checks that local creation by location fails when a file occupies the path.</summary>
        [Test]
        public void Create_RefusesLocalFile()
        {
            var file = _temp.WriteText("file", "x");

            Assert.Throws(Is.InstanceOf<IOException>(), () => FileSystem.Directory.Create(file.Location));
        }

        /// <summary>Checks that an existing provider directory is opened, while a missing one or a file cannot be created by location.</summary>
        [Test]
        public void Create_OpensOnlyExistingProviderDirectories()
        {
            var file = _provider.AddFile(_provider.RootLocation, "a.txt", new byte[] { 1 });

            var root = FileSystem.Directory.Create(_provider.RootLocation);

            Assert.That(root.FileSystem, Is.SameAs(_provider));
            Assert.That(root.Location, Is.EqualTo(_provider.RootLocation));
            Assert.Throws<NotSupportedException>(() => FileSystem.Directory.Create(_provider.RootLocation + "9999"));
            Assert.Throws<IOException>(() => FileSystem.Directory.Create(file));
        }

        /// <summary>Checks that the parent-and-name overload creates children in both backends.</summary>
        [Test]
        public void CreateChild_CreatesInBothBackends()
        {
            var local = FileSystem.Directory.Create(_temp.Location, "child");
            var content = FileSystem.Directory.Create(_provider.RootLocation, "child");

            Assert.That(local.Location, Is.EqualTo(PathOf("child")));
            Assert.That(local.Exists, Is.True);
            Assert.That(content.Entry!.Name, Is.EqualTo("child"));
            Assert.That(content.Entry!.IsDirectory, Is.True);
            Assert.Throws<ArgumentException>(() => FileSystem.Directory.Create(_temp.Location, "a/b"));
            Assert.Throws<DirectoryNotFoundException>(() => FileSystem.Directory.Create(PathOf("missing"), "child"));
        }

        /// <summary>Checks that a location-based move moves local directories and refuses provider operands.</summary>
        [Test]
        public void Move_MovesLocalDirectoriesOnly()
        {
            var source = _temp.Directory.CreateDirectory("source");
            source.CreateFile("child.txt");

            var moved = FileSystem.Directory.Move(source.Location, PathOf("moved"));

            Assert.That(moved.FindFile("child.txt"), Is.Not.Null);
            Assert.That(source.Exists, Is.False);
            Assert.Throws<NotSupportedException>(() => FileSystem.Directory.Move(_provider.RootLocation, PathOf("x")));
            Assert.Throws<NotSupportedException>(() => FileSystem.Directory.Move(moved.Location, _provider.RootLocation));
            Assert.That(moved.Exists, Is.True);
        }

        /// <summary>Checks that the parent-and-name move works inside each backend and validates the source kind.</summary>
        [Test]
        public void MoveChild_MovesInsideOneBackend()
        {
            var source = _temp.Directory.CreateDirectory("source");
            var target = _temp.Directory.CreateDirectory("target");
            var file = _temp.WriteText("file.txt", "x");
            var contentSource = _provider.AddDirectory(_provider.RootLocation, "source");
            var contentTarget = _provider.AddDirectory(_provider.RootLocation, "target");

            var movedLocal = FileSystem.Directory.Move(source.Location, target.Location, "moved");
            var movedContent = FileSystem.Directory.Move(contentSource, contentTarget, "moved");

            Assert.That(movedLocal.Location, Is.EqualTo(Path.Combine(target.Location, "moved")));
            Assert.That(movedLocal.Exists, Is.True);
            Assert.That(movedContent.FileSystem, Is.SameAs(_provider));
            Assert.That(movedContent.Exists, Is.True);
            Assert.That(new StorageDirectory(_provider, contentTarget).FindDirectory("moved"), Is.Not.Null);
            Assert.Throws<IOException>(() => FileSystem.Directory.Move(file.Location, target.Location, "file"));
            Assert.Throws<NotSupportedException>(() => FileSystem.Directory.Move(movedLocal.Location, _provider.RootLocation, "x"));
        }

        /// <summary>Checks that the provider refuses moving a directory into its own subtree.</summary>
        [Test]
        public void MoveChild_RejectsProviderSubtree()
        {
            var outer = _provider.AddDirectory(_provider.RootLocation, "outer");
            var inner = _provider.AddDirectory(outer, "inner");

            Assert.Throws<IOException>(() => FileSystem.Directory.Move(outer, inner, "loop"));
            Assert.That(_provider.Contains(outer), Is.True);
        }
    }
}

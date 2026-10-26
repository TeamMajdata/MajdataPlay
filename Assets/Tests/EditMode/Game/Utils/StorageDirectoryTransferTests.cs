using System;
using System.IO;
using System.Linq;
using MajdataPlay.IO.Storage;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies recursive copies and same- and cross-backend moves in <see cref="StorageDirectoryTransfer"/>.</summary>
    public class StorageDirectoryTransferTests
    {
        /// <summary>A fixed UTC modification time applied to source files.</summary>
        private static readonly DateTime s_timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        /// <summary>The temporary directory holding source and destination trees.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>Creates the temporary directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
        }

        /// <summary>Deletes the temporary directory.</summary>
        [TearDown]
        public void TearDown()
        {
            _temporary.Dispose();
        }

        /// <summary>Checks that null arguments are rejected by both operations.</summary>
        [Test]
        public void NullArguments_Throw()
        {
            var directory = _temporary.Directory;

            Assert.Throws<ArgumentNullException>(() => StorageDirectoryTransfer.Copy(null!, directory));
            Assert.Throws<ArgumentNullException>(() => StorageDirectoryTransfer.Copy(directory, null!));
            Assert.Throws<ArgumentNullException>(() => StorageDirectoryTransfer.Move(null!, directory, "x"));
            Assert.Throws<ArgumentNullException>(() => StorageDirectoryTransfer.Move(directory, null!, "x"));
        }

        /// <summary>Checks that a nested tree is copied with its contents and file modification times.</summary>
        [Test]
        public void Copy_NestedTree_CopiesFilesDirectoriesAndTimestamps()
        {
            var source = CreateSampleTree("source");
            var destination = _temporary.Directory.CreateDirectory("destination");

            StorageDirectoryTransfer.Copy(source, destination);

            AssertSampleTree(destination);
            var copied = destination.FindFile("a.txt")!;
            Assert.That(copied.Entry!.LastWriteTimeUtc, Is.EqualTo(s_timestamp).Within(TimeSpan.FromSeconds(2)));
            AssertSampleTree(source);
        }

        /// <summary>Checks that files already present in the destination are overwritten.</summary>
        [Test]
        public void Copy_ExistingFile_IsOverwritten()
        {
            var source = CreateSampleTree("source");
            var destination = _temporary.Directory.CreateDirectory("destination");
            destination.CreateFile("a.txt").WriteAllText("stale content that is longer than the new one");

            StorageDirectoryTransfer.Copy(source, destination);

            Assert.That(destination.FindFile("a.txt")!.ReadAllText(), Is.EqualTo("alpha"));
        }

        /// <summary>Checks that a provider which refuses modification times still receives the complete files.</summary>
        [Test]
        public void Copy_ProviderRejectsTimestamps_StillCopiesContent()
        {
            var source = CreateSampleTree("source");
            var local = _temporary.Directory.CreateDirectory("destination");
            var provider = new DelegatingFileSystem(local.FileSystem)
            {
                RejectLastWriteTime = true,
            };
            var destination = new StorageDirectory(provider, local.Location);

            StorageDirectoryTransfer.Copy(source, destination);

            AssertSampleTree(local);
        }

        /// <summary>Checks that a move inside one backend relocates the directory natively.</summary>
        [Test]
        public void Move_SameBackend_MovesTheDirectory()
        {
            var source = CreateSampleTree("source");
            var parent = _temporary.Directory.CreateDirectory("parent");

            var moved = StorageDirectoryTransfer.Move(source, parent, "moved");

            Assert.That(source.Exists, Is.False);
            Assert.That(moved.Exists, Is.True);
            AssertSampleTree(parent.FindDirectory("moved")!);
        }

        /// <summary>Checks that a cross-backend move copies the tree, deletes the source and leaves no staging directory.</summary>
        [Test]
        public void Move_AcrossBackends_CopiesThenDeletesSource()
        {
            var source = CreateSampleTree("source");
            var localParent = _temporary.Directory.CreateDirectory("parent");
            var provider = new DelegatingFileSystem(localParent.FileSystem);
            var parent = new StorageDirectory(provider, localParent.Location);

            var moved = StorageDirectoryTransfer.Move(source, parent, "moved");

            Assert.That(moved.FileSystem, Is.SameAs(provider));
            Assert.That(source.Exists, Is.False);
            AssertSampleTree(localParent.FindDirectory("moved")!);
            Assert.That(localParent.EnumerateEntries().Select(x => x.Name), Is.EquivalentTo(new[] { "moved" }));
        }

        /// <summary>Checks that a cross-backend move refuses an occupied destination name.</summary>
        [Test]
        public void Move_AcrossBackends_OccupiedName_Throws()
        {
            var source = CreateSampleTree("source");
            var localParent = _temporary.Directory.CreateDirectory("parent");
            localParent.CreateDirectory("moved");
            var parent = new StorageDirectory(new DelegatingFileSystem(localParent.FileSystem), localParent.Location);

            Assert.Throws<IOException>(() => StorageDirectoryTransfer.Move(source, parent, "moved"));
            Assert.That(source.Exists, Is.True);
        }

        /// <summary>Checks that a cross-backend move of a missing source reports it.</summary>
        [Test]
        public void Move_AcrossBackends_MissingSource_Throws()
        {
            var missing = new StorageDirectory(_temporary.Directory.FileSystem, Path.Combine(_temporary.Location, "missing"));
            var localParent = _temporary.Directory.CreateDirectory("parent");
            var parent = new StorageDirectory(new DelegatingFileSystem(localParent.FileSystem), localParent.Location);

            Assert.Throws<FileNotFoundException>(() => StorageDirectoryTransfer.Move(missing, parent, "moved"));
        }

        /// <summary>Checks that a cross-backend move rejects a source that is a file.</summary>
        [Test]
        public void Move_AcrossBackends_SourceIsFile_Throws()
        {
            var file = _temporary.WriteText("plain.txt", "text");
            var fileAsDirectory = new StorageDirectory(file.FileSystem, file.Location);
            var localParent = _temporary.Directory.CreateDirectory("parent");
            var parent = new StorageDirectory(new DelegatingFileSystem(localParent.FileSystem), localParent.Location);

            Assert.Throws<IOException>(() => StorageDirectoryTransfer.Move(fileAsDirectory, parent, "moved"));
            Assert.That(file.Exists, Is.True);
        }

        /// <summary>Checks that a failed rename removes the staging copy and keeps the source.</summary>
        [Test]
        public void Move_AcrossBackends_RenameFails_CleansUpStagingAndKeepsSource()
        {
            var source = CreateSampleTree("source");
            var localParent = _temporary.Directory.CreateDirectory("parent");
            var provider = new DelegatingFileSystem(localParent.FileSystem)
            {
                FailRename = true,
            };
            var parent = new StorageDirectory(provider, localParent.Location);

            Assert.Throws<IOException>(() => StorageDirectoryTransfer.Move(source, parent, "moved"));

            AssertSampleTree(source);
            Assert.That(provider.DeletedDirectories, Is.EqualTo(1));
            Assert.That(localParent.EnumerateEntries(), Is.Empty);
        }

        /// <summary>Checks that a failing cleanup does not hide the original transfer failure.</summary>
        [Test]
        public void Move_AcrossBackends_CleanupFails_ReportsOriginalFailure()
        {
            var source = CreateSampleTree("source");
            var localParent = _temporary.Directory.CreateDirectory("parent");
            var provider = new DelegatingFileSystem(localParent.FileSystem)
            {
                FailRename = true,
                FailDeleteDirectory = true,
            };
            var parent = new StorageDirectory(provider, localParent.Location);

            var exception = Assert.Throws<IOException>(() => StorageDirectoryTransfer.Move(source, parent, "moved"));

            Assert.That(exception.Message, Does.Contain("Rename"));
            var leftovers = localParent.EnumerateEntries().Select(x => x.Name).ToArray();
            Assert.That(leftovers, Has.Length.EqualTo(1));
            Assert.That(leftovers[0], Does.StartWith("moved.").And.EndWith(".moving"));
            AssertSampleTree(source);
        }

        /// <summary>Creates a directory holding a file, a subdirectory and a nested subdirectory.</summary>
        /// <param name="name">The directory name inside the temporary directory.</param>
        /// <returns>The created directory.</returns>
        private StorageDirectory CreateSampleTree(string name)
        {
            var root = _temporary.Directory.CreateDirectory(name);
            var a = root.CreateFile("a.txt");
            a.WriteAllText("alpha");
            a.SetLastWriteTime(s_timestamp);
            var sub = root.CreateDirectory("sub");
            sub.CreateFile("b.txt").WriteAllText("bravo");
            sub.CreateDirectory("deep").CreateFile("c.txt").WriteAllText("charlie");
            root.CreateDirectory("empty");
            return root;
        }

        /// <summary>Asserts that a directory contains the tree created by <see cref="CreateSampleTree"/>.</summary>
        /// <param name="root">The directory to inspect.</param>
        private static void AssertSampleTree(StorageDirectory root)
        {
            Assert.That(root.FindFile("a.txt")!.ReadAllText(), Is.EqualTo("alpha"));
            var sub = root.FindDirectory("sub")!;
            Assert.That(sub.FindFile("b.txt")!.ReadAllText(), Is.EqualTo("bravo"));
            Assert.That(sub.FindDirectory("deep")!.FindFile("c.txt")!.ReadAllText(), Is.EqualTo("charlie"));
            Assert.That(root.FindDirectory("empty"), Is.Not.Null);
        }
    }
}

using System;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the hidden <c>.MajdataPlay/id</c> marker handled by <see cref="DirectoryIdentity"/>.</summary>
    public class DirectoryIdentityTests
    {
        /// <summary>The temporary storage.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The directory whose identity is tested.</summary>
        private StorageDirectory _directory = null!;

        /// <summary>Creates the temporary storage and the tested directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _directory = _temporary.Directory.CreateDirectory("Chart");
        }

        /// <summary>Deletes the temporary storage.</summary>
        [TearDown]
        public void TearDown()
        {
            _temporary.Dispose();
        }

        /// <summary>Reads the text currently stored in the identity marker file.</summary>
        /// <returns>The stored text.</returns>
        private string ReadIdFile()
        {
            return _directory.FindDirectory(DirectoryIdentity.MarkerDirectoryName)!
                             .FindFile(DirectoryIdentity.IdFileName)!
                             .ReadAllText();
        }

        /// <summary>Checks that a missing marker is created hidden and holds a new, stable GUID.</summary>
        [Test]
        public void GetOrCreateId_CreatesHiddenMarkerWithStableGuid()
        {
            var first = DirectoryIdentity.GetOrCreateId(_directory);
            var second = DirectoryIdentity.GetOrCreateId(_directory);

            Assert.That(Guid.TryParse(first, out _), Is.True);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(ReadIdFile(), Is.EqualTo(first));
            var markerEntry = _directory.FindEntry(DirectoryIdentity.MarkerDirectoryName)!;
            Assert.That(markerEntry.IsDirectory, Is.True);
            Assert.That(markerEntry.IsHidden, Is.True);
        }

        /// <summary>Checks that a stored identity is returned trimmed even when it is not a GUID.</summary>
        [Test]
        public void GetOrCreateId_ReturnsStoredIdentity()
        {
            _directory.CreateDirectory(DirectoryIdentity.MarkerDirectoryName)
                      .CreateFile(DirectoryIdentity.IdFileName)
                      .WriteAllText(" server-id\n");

            Assert.That(DirectoryIdentity.GetOrCreateId(_directory), Is.EqualTo("server-id"));
        }

        /// <summary>Checks that a blank marker file is replaced by a newly generated GUID.</summary>
        [Test]
        public void GetOrCreateId_ReplacesBlankIdentity()
        {
            _directory.CreateDirectory(DirectoryIdentity.MarkerDirectoryName)
                      .CreateFile(DirectoryIdentity.IdFileName)
                      .WriteAllText("  ");

            var id = DirectoryIdentity.GetOrCreateId(_directory);

            Assert.That(Guid.TryParse(id, out _), Is.True);
            Assert.That(ReadIdFile(), Is.EqualTo(id));
        }

        /// <summary>Checks that a supplied identity is created and later replaced.</summary>
        [Test]
        public void WriteId_CreatesAndReplacesIdentity()
        {
            DirectoryIdentity.WriteId(_directory, "first");
            Assert.That(ReadIdFile(), Is.EqualTo("first"));
            Assert.That(_directory.FindEntry(DirectoryIdentity.MarkerDirectoryName)!.IsHidden, Is.True);

            DirectoryIdentity.WriteId(_directory, "second");
            Assert.That(ReadIdFile(), Is.EqualTo("second"));
            Assert.That(DirectoryIdentity.GetOrCreateId(_directory), Is.EqualTo("second"));
        }

        /// <summary>Checks that a supplied identity is written only when no identity is stored, and never replaces one.</summary>
        [Test]
        public void GetOrWriteId_WritesOnlyWhenMissingOrBlank()
        {
            Assert.That(DirectoryIdentity.GetOrWriteId(_directory, "first"), Is.EqualTo("first"));
            Assert.That(ReadIdFile(), Is.EqualTo("first"));
            Assert.That(_directory.FindEntry(DirectoryIdentity.MarkerDirectoryName)!.IsHidden, Is.True);

            Assert.That(DirectoryIdentity.GetOrWriteId(_directory, "second"), Is.EqualTo("first"));
            Assert.That(ReadIdFile(), Is.EqualTo("first"));

            _directory.FindDirectory(DirectoryIdentity.MarkerDirectoryName)!.FindFile(DirectoryIdentity.IdFileName)!.WriteAllText(" ");
            Assert.That(DirectoryIdentity.GetOrWriteId(_directory, "third"), Is.EqualTo("third"));
            Assert.That(ReadIdFile(), Is.EqualTo("third"));
        }

        /// <summary>Checks that reading a marker directory returns trimmed content, null for a missing or blank file, and creates nothing.</summary>
        [Test]
        public void ReadStoredId_ReadsWithoutCreating()
        {
            var markerDirectory = _directory.CreateDirectory(DirectoryIdentity.MarkerDirectoryName);

            Assert.That(DirectoryIdentity.ReadStoredId(markerDirectory), Is.Null);
            Assert.That(markerDirectory.FindFile(DirectoryIdentity.IdFileName), Is.Null);

            var idFile = markerDirectory.CreateFile(DirectoryIdentity.IdFileName);
            idFile.WriteAllText("\t");
            Assert.That(DirectoryIdentity.ReadStoredId(markerDirectory), Is.Null);

            idFile.WriteAllText(" server-id\n");
            Assert.That(DirectoryIdentity.ReadStoredId(markerDirectory), Is.EqualTo("server-id"));
        }

        /// <summary>Checks that a location identity is a GUID, repeatable for one location and different for another.</summary>
        [Test]
        public void CreateLocationId_IsStablePerLocation()
        {
            var first = DirectoryIdentity.CreateLocationId(_directory);
            var again = DirectoryIdentity.CreateLocationId(new StorageDirectory(_directory.FileSystem, _directory.Location));
            var other = DirectoryIdentity.CreateLocationId(_temporary.Directory.CreateDirectory("Other"));

            Assert.That(Guid.TryParse(first, out _), Is.True);
            Assert.That(again, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
            Assert.That(_directory.FindEntry(DirectoryIdentity.MarkerDirectoryName), Is.Null);
        }

        /// <summary>Checks that invalid arguments are rejected.</summary>
        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.GetOrCreateId(null!));
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.WriteId(_directory, null!));
            Assert.Throws<ArgumentException>(() => DirectoryIdentity.WriteId(_directory, " "));
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.GetOrWriteId(_directory, null!));
            Assert.Throws<ArgumentException>(() => DirectoryIdentity.GetOrWriteId(_directory, " "));
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.ReadStoredId(null!));
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.CreateLocationId(null!));
        }
    }
}

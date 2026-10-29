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

        /// <summary>Checks that invalid arguments are rejected.</summary>
        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.GetOrCreateId(null!));
            Assert.Throws<ArgumentNullException>(() => DirectoryIdentity.WriteId(_directory, null!));
            Assert.Throws<ArgumentException>(() => DirectoryIdentity.WriteId(_directory, " "));
        }
    }
}

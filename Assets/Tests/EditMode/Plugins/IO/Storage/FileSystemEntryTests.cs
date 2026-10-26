#nullable enable
using System;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers the immutable entry metadata snapshot and its argument validation.</summary>
    public class FileSystemEntryTests
    {
        /// <summary>Checks that every constructor argument is exposed unchanged for a file.</summary>
        [Test]
        public void Constructor_StoresFileMetadata()
        {
            var written = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            var created = new DateTime(2023, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            var entry = new FileSystemEntry("content://a/1", "a.txt", false, 42, written, true, "id-1", created, true, true);

            Assert.That(entry.Location, Is.EqualTo("content://a/1"));
            Assert.That(entry.Name, Is.EqualTo("a.txt"));
            Assert.That(entry.IsDirectory, Is.False);
            Assert.That(entry.Length, Is.EqualTo(42));
            Assert.That(entry.LastWriteTimeUtc, Is.EqualTo(written));
            Assert.That(entry.CreationTimeUtc, Is.EqualTo(created));
            Assert.That(entry.IsSymbolicLink, Is.True);
            Assert.That(entry.ResourceId, Is.EqualTo("id-1"));
            Assert.That(entry.IsHidden, Is.True);
            Assert.That(entry.IsSystem, Is.True);
        }

        /// <summary>Checks that optional metadata defaults to unknown values and false flags.</summary>
        [Test]
        public void Constructor_DefaultsOptionalMetadata()
        {
            var entry = new FileSystemEntry("content://a/1", string.Empty, false);

            Assert.That(entry.Name, Is.Empty);
            Assert.That(entry.Length, Is.Null);
            Assert.That(entry.LastWriteTimeUtc, Is.Null);
            Assert.That(entry.CreationTimeUtc, Is.Null);
            Assert.That(entry.ResourceId, Is.Null);
            Assert.That(entry.IsSymbolicLink, Is.False);
            Assert.That(entry.IsHidden, Is.False);
            Assert.That(entry.IsSystem, Is.False);
        }

        /// <summary>Checks that directories never report a length, even when one is supplied.</summary>
        [Test]
        public void Constructor_DropsDirectoryLength()
        {
            var entry = new FileSystemEntry("content://a/1", "dir", true, 10);

            Assert.That(entry.IsDirectory, Is.True);
            Assert.That(entry.Length, Is.Null);
        }

        /// <summary>Checks that a null or empty location is rejected.</summary>
        /// <param name="location">The invalid location.</param>
        [TestCase(null)]
        [TestCase("")]
        public void Constructor_RejectsMissingLocation(string? location)
        {
            var exception = Assert.Throws<ArgumentException>(() => new FileSystemEntry(location!, "a", false));

            Assert.That(exception.ParamName, Is.EqualTo("location"));
        }

        /// <summary>Checks that an empty resource identity is rejected while null means no identity.</summary>
        [Test]
        public void Constructor_RejectsEmptyResourceId()
        {
            var exception = Assert.Throws<ArgumentException>(() => new FileSystemEntry("x", "a", false, resourceId: string.Empty));

            Assert.That(exception.ParamName, Is.EqualTo("resourceId"));
        }

        /// <summary>Checks that a null name is rejected.</summary>
        [Test]
        public void Constructor_RejectsNullName()
        {
            Assert.Throws<ArgumentNullException>(() => new FileSystemEntry("x", null!, false));
        }

        /// <summary>Checks that a negative length is rejected.</summary>
        [Test]
        public void Constructor_RejectsNegativeLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemEntry("x", "a", false, -1));
        }

        /// <summary>Checks that modification and creation times must be UTC.</summary>
        /// <param name="kind">A non-UTC timestamp kind.</param>
        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        public void Constructor_RejectsNonUtcTimes(DateTimeKind kind)
        {
            var time = new DateTime(2024, 1, 1, 0, 0, 0, kind);

            var written = Assert.Throws<ArgumentException>(() => new FileSystemEntry("x", "a", false, lastWriteTimeUtc: time));
            var created = Assert.Throws<ArgumentException>(() => new FileSystemEntry("x", "a", false, creationTimeUtc: time));

            Assert.That(written.ParamName, Is.EqualTo("lastWriteTimeUtc"));
            Assert.That(created.ParamName, Is.EqualTo("creationTimeUtc"));
        }
    }
}

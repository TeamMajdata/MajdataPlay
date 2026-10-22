#nullable enable
using System;
using System.IO;
using System.Text;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers synchronous file handle operations on the local backend.</summary>
    public class StorageFileTests
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

            Assert.Throws<ArgumentNullException>(() => new StorageFile(null!, _temp.Location));
            Assert.Throws<ArgumentNullException>(() => new StorageFile(fs, null!));
            Assert.Throws<ArgumentException>(() => new StorageFile(fs, " "));
        }

        /// <summary>Checks that provider locations are kept verbatim while local locations are normalized.</summary>
        [Test]
        public void Constructor_NormalizesOnlyLocalLocations()
        {
            var provider = new MemoryContentProvider();
            var local = new StorageFile(new LocalFileSystem(), _temp.Location + "\\a.txt\\");
            var content = new StorageFile(provider, provider.RootLocation + "/");

            Assert.That(local.Location, Is.EqualTo(Path.Combine(_temp.Location, "a.txt")));
            Assert.That(content.Location, Is.EqualTo(provider.RootLocation + "/"));
        }

        /// <summary>Checks that a handle neither creates its file nor reports a directory as an existing file.</summary>
        [Test]
        public void Exists_DistinguishesMissingFilesAndDirectories()
        {
            var missing = FileSystem.File.Open(Path.Combine(_temp.Location, "missing.txt"));
            var directory = FileSystem.File.Open(_temp.Location);

            Assert.That(missing.Exists, Is.False);
            Assert.That(missing.Entry, Is.Null);
            Assert.That(directory.Exists, Is.False);
            Assert.That(directory.Entry!.IsDirectory, Is.True);
        }

        /// <summary>Checks that bytes round-trip through the whole-file helpers.</summary>
        [Test]
        public void WriteAllBytes_RoundTripsBytes()
        {
            var file = _temp.Directory.CreateFile("data.bin");
            var bytes = new byte[] { 0, 1, 2, 255 };

            file.WriteAllBytes(bytes);

            Assert.That(file.ReadAllBytes(), Is.EqualTo(bytes));
            Assert.That(file.Entry!.Length, Is.EqualTo(4));
        }

        /// <summary>Checks that the default text encoding is UTF-8 without a byte order mark.</summary>
        [Test]
        public void WriteAllText_DefaultsToUtf8WithoutBom()
        {
            var file = _temp.Directory.CreateFile("text.txt");

            file.WriteAllText("中");

            Assert.That(file.ReadAllBytes(), Is.EqualTo(new byte[] { 0xE4, 0xB8, 0xAD }));
            Assert.That(file.ReadAllText(), Is.EqualTo("中"));
        }

        /// <summary>Checks that reading detects a byte order mark written with an explicit encoding.</summary>
        [Test]
        public void ReadAllText_DetectsByteOrderMark()
        {
            var file = _temp.Directory.CreateFile("utf16.txt");

            file.WriteAllText("héllo", Encoding.Unicode);

            Assert.That(file.ReadAllBytes()[0], Is.EqualTo(0xFF));
            Assert.That(file.ReadAllText(), Is.EqualTo("héllo"));
        }

        /// <summary>Checks that an explicit fallback encoding is used when no byte order mark is present.</summary>
        [Test]
        public void ReadAllText_UsesFallbackEncoding()
        {
            var file = _temp.Directory.CreateFile("latin.txt");
            file.WriteAllBytes(new byte[] { 0x63, 0x61, 0x66, 0xE9 });

            Assert.That(file.ReadAllText(Encoding.GetEncoding("iso-8859-1")), Is.EqualTo("café"));
        }

        /// <summary>Checks that a strict encoding reports invalid bytes instead of replacing them.</summary>
        [Test]
        public void ReadAllText_StrictEncodingRejectsInvalidBytes()
        {
            var file = _temp.Directory.CreateFile("invalid.txt");
            file.WriteAllBytes(new byte[] { 0x61, 0xC3, 0x28 });

            Assert.Throws<DecoderFallbackException>(() => file.ReadAllText(new UTF8Encoding(false, true)));
        }

        /// <summary>Checks that whole-file writes require an existing file and non-null content.</summary>
        [Test]
        public void WriteAll_ValidatesArgumentsAndExistence()
        {
            var missing = FileSystem.File.Open(Path.Combine(_temp.Location, "missing.txt"));
            var file = _temp.Directory.CreateFile("a.txt");

            Assert.Throws<ArgumentNullException>(() => file.WriteAllBytes(null!));
            Assert.Throws<ArgumentNullException>(() => file.WriteAllText(null!));
            Assert.Throws<FileNotFoundException>(() => missing.WriteAllBytes(new byte[1]));
            Assert.Throws<FileNotFoundException>(() => missing.WriteAllText("x"));
            Assert.Throws<FileNotFoundException>(() => missing.ReadAllBytes());
            Assert.That(missing.Exists, Is.False);
        }

        /// <summary>Checks that an overwrite truncates longer previous content.</summary>
        [Test]
        public void WriteAllText_TruncatesPreviousContent()
        {
            var file = _temp.WriteText("a.txt", "a much longer original text");

            file.WriteAllText("short");

            Assert.That(file.ReadAllText(), Is.EqualTo("short"));
        }

        /// <summary>Checks that the read stream returned by the handle reads the file content.</summary>
        [Test]
        public void OpenRead_ReturnsReadableStream()
        {
            var file = _temp.WriteText("a.txt", "abc");

            using var stream = file.OpenRead();
            using var reader = new StreamReader(stream);

            Assert.That(stream.CanRead, Is.True);
            Assert.That(reader.ReadToEnd(), Is.EqualTo("abc"));
        }

        /// <summary>Checks that the write stream appends on request.</summary>
        [Test]
        public void OpenWrite_AppendsOnRequest()
        {
            var file = _temp.WriteText("a.txt", "abc");

            using (var stream = file.OpenWrite(append: true))
            {
                stream.WriteByte((byte)'d');
            }

            Assert.That(file.ReadAllText(), Is.EqualTo("abcd"));
        }

        /// <summary>Checks that deleting removes the file and a second delete is a no-op.</summary>
        [Test]
        public void Delete_RemovesFileAndIgnoresMissing()
        {
            var file = _temp.WriteText("a.txt", "x");

            file.Delete();

            Assert.That(file.Exists, Is.False);
            Assert.DoesNotThrow(file.Delete);
        }

        /// <summary>Checks that renaming returns a new handle while the old handle no longer exists.</summary>
        [Test]
        public void Rename_ReturnsNewHandle()
        {
            var file = _temp.WriteText("old.txt", "x");

            var renamed = file.Rename("new.txt");

            Assert.That(renamed.Location, Is.EqualTo(Path.Combine(_temp.Location, "new.txt")));
            Assert.That(renamed.ReadAllText(), Is.EqualTo("x"));
            Assert.That(file.Exists, Is.False);
        }

        /// <summary>Checks that renaming validates the name and requires an existing file, not a directory.</summary>
        [Test]
        public void Rename_ValidatesNameAndExistence()
        {
            var file = _temp.WriteText("a.txt", "x");
            var directoryAsFile = FileSystem.File.Open(_temp.Directory.CreateDirectory("dir").Location);

            Assert.Throws<ArgumentException>(() => file.Rename("a/b"));
            Assert.Throws<FileNotFoundException>(() => FileSystem.File.Open(Path.Combine(_temp.Location, "missing")).Rename("b"));
            Assert.Throws<FileNotFoundException>(() => directoryAsFile.Rename("dir2"));
        }

        /// <summary>Checks that attribute flags written through the handle are reported by its metadata.</summary>
        [Test]
        public void SetAttributes_AppliesNativeFlags()
        {
            var file = _temp.WriteText("a.txt", "x");

            file.SetAttributes(FileAttributes.Hidden);
            var hidden = file.Entry!.IsHidden;
            file.SetAttributes(FileAttributes.Normal);

            Assert.That(hidden, Is.True);
            Assert.That(file.Entry!.IsHidden, Is.False);
        }

        /// <summary>Checks that local, unspecified and UTC timestamps are all stored as the same UTC instant.</summary>
        /// <param name="kind">The kind of the timestamp passed to the handle.</param>
        [TestCase(DateTimeKind.Utc)]
        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        public void SetLastWriteTime_ConvertsToUtc(DateTimeKind kind)
        {
            var file = _temp.WriteText("a.txt", "x");
            var utc = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);
            var time = kind == DateTimeKind.Utc
                ? utc
                : DateTime.SpecifyKind(utc.ToLocalTime(), kind);

            file.SetLastWriteTime(time);

            Assert.That(file.Entry!.LastWriteTimeUtc, Is.EqualTo(utc));
        }
    }
}

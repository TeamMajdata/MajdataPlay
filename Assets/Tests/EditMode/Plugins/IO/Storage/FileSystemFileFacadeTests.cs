#nullable enable
using System;
using System.IO;
using System.Text;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers the location-based file facade over the local backend and a registered content provider.</summary>
    public class FileSystemFileFacadeTests
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

        /// <summary>Adds a provider file below the provider root.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="text">The UTF-8 content.</param>
        /// <returns>The provider location.</returns>
        private string AddContentFile(string name, string text)
        {
            return _provider.AddFile(_provider.RootLocation, name, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Gets the UTF-8 text of a provider document.</summary>
        /// <param name="location">The provider location.</param>
        /// <returns>The decoded content.</returns>
        private string ContentText(string location)
        {
            return Encoding.UTF8.GetString(_provider.GetData(location));
        }

        /// <summary>Checks that creating a local file by location creates it empty and refuses an existing file without overwrite.</summary>
        [Test]
        public void Create_CreatesLocalFile()
        {
            var created = FileSystem.File.Create(PathOf("a.txt"));
            created.WriteAllText("content");

            Assert.That(created.FileSystem, Is.InstanceOf<LocalFileSystem>());
            Assert.Throws<IOException>(() => FileSystem.File.Create(PathOf("a.txt")));
            Assert.That(FileSystem.File.Create(PathOf("a.txt"), overwrite: true).Entry!.Length, Is.EqualTo(0));
        }

        /// <summary>Checks that creating a local file never creates missing parents.</summary>
        [Test]
        public void Create_RequiresExistingLocalParent()
        {
            Assert.Throws<DirectoryNotFoundException>(() => FileSystem.File.Create(Path.Combine(PathOf("missing"), "a.txt")));
        }

        /// <summary>Checks that a provider file can be truncated by location but never created or opened without overwrite.</summary>
        [Test]
        public void Create_TruncatesOnlyExistingProviderFiles()
        {
            var location = AddContentFile("a.txt", "content");

            Assert.Throws<IOException>(() => FileSystem.File.Create(location));
            var truncated = FileSystem.File.Create(location, overwrite: true);

            Assert.That(truncated.Location, Is.EqualTo(location));
            Assert.That(_provider.GetData(location), Is.Empty);
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Create(_provider.RootLocation + "9999", true));
            Assert.Throws<IOException>(() => FileSystem.File.Create(_provider.RootLocation, true));
        }

        /// <summary>Checks that the parent-and-name overload validates the name and content type first.</summary>
        [Test]
        public void CreateChild_ValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => FileSystem.File.Create(_temp.Location, "a/b"));
            var mime = Assert.Throws<ArgumentException>(() => FileSystem.File.Create(_temp.Location, "a.txt", " "));

            Assert.That(mime.ParamName, Is.EqualTo("mimeType"));
        }

        /// <summary>Checks the local parent-and-name overload: creation, conflicts and truncation.</summary>
        [Test]
        public void CreateChild_HandlesLocalConflicts()
        {
            var created = FileSystem.File.Create(_temp.Location, "a.txt");
            created.WriteAllText("content");
            _temp.Directory.CreateDirectory("dir");

            Assert.That(created.Location, Is.EqualTo(PathOf("a.txt")));
            Assert.Throws<IOException>(() => FileSystem.File.Create(_temp.Location, "a.txt"));
            Assert.Throws<IOException>(() => FileSystem.File.Create(_temp.Location, "dir", overwrite: true));
            Assert.That(FileSystem.File.Create(_temp.Location, "a.txt", overwrite: true).Entry!.Length, Is.EqualTo(0));
        }

        /// <summary>Checks the provider parent-and-name overload: new documents, provider names, conflicts and truncation.</summary>
        [Test]
        public void CreateChild_HandlesProviderDocuments()
        {
            _provider.NextCreatedFileName = "adjusted.txt";
            var created = FileSystem.File.Create(_provider.RootLocation, "a.txt", "text/plain");
            var existing = AddContentFile("b.txt", "content");

            var truncated = FileSystem.File.Create(_provider.RootLocation, "b.txt", overwrite: true);

            Assert.That(created.Entry!.Name, Is.EqualTo("adjusted.txt"));
            Assert.That(_provider.LastMimeType, Is.EqualTo("text/plain"));
            Assert.That(truncated.Location, Is.EqualTo(existing));
            Assert.That(_provider.GetData(existing), Is.Empty);
            Assert.Throws<IOException>(() => FileSystem.File.Create(_provider.RootLocation, "b.txt"));
        }

        /// <summary>Checks that reading by location returns the file content.</summary>
        [Test]
        public void OpenRead_ReadsLocalAndProviderFiles()
        {
            var local = _temp.WriteText("a.txt", "local");
            var content = AddContentFile("b.txt", "content");

            using var localReader = new StreamReader(FileSystem.File.OpenRead(local.Location));
            using var contentReader = new StreamReader(FileSystem.File.OpenRead(content));

            Assert.That(localReader.ReadToEnd(), Is.EqualTo("local"));
            Assert.That(contentReader.ReadToEnd(), Is.EqualTo("content"));
        }

        /// <summary>Checks local write modes: creation, exclusive refusal, append and overwrite.</summary>
        [Test]
        public void OpenWrite_HandlesLocalModes()
        {
            var path = PathOf("a.txt");

            using (var stream = FileSystem.File.OpenWrite(path))
            {
                stream.Write(Encoding.ASCII.GetBytes("abc"), 0, 3);
            }
            Assert.Throws<IOException>(() => FileSystem.File.OpenWrite(path).Dispose());
            using (var stream = FileSystem.File.OpenWrite(path, append: true))
            {
                stream.Write(Encoding.ASCII.GetBytes("d"), 0, 1);
            }
            var appended = FileSystem.File.Open(path).ReadAllText();
            using (var stream = FileSystem.File.OpenWrite(path, overwrite: true))
            {
                stream.Write(Encoding.ASCII.GetBytes("z"), 0, 1);
            }

            Assert.That(appended, Is.EqualTo("abcd"));
            Assert.That(FileSystem.File.Open(path).ReadAllText(), Is.EqualTo("z"));
        }

        /// <summary>Checks that a missing local parent is reported by the write facade.</summary>
        [Test]
        public void OpenWrite_RequiresExistingLocalParent()
        {
            Assert.Throws<DirectoryNotFoundException>(() => FileSystem.File.OpenWrite(Path.Combine(PathOf("missing"), "a.txt")));
        }

        /// <summary>Checks provider write modes: existing files only, no directories, and explicit append or overwrite.</summary>
        [Test]
        public void OpenWrite_HandlesProviderModes()
        {
            var location = AddContentFile("a.txt", "abc");

            Assert.Throws<FileNotFoundException>(() => FileSystem.File.OpenWrite(_provider.RootLocation + "9999", overwrite: true));
            Assert.Throws<IOException>(() => FileSystem.File.OpenWrite(_provider.RootLocation, overwrite: true));
            Assert.Throws<IOException>(() => FileSystem.File.OpenWrite(location));
            using (var stream = FileSystem.File.OpenWrite(location, append: true))
            {
                stream.Write(Encoding.ASCII.GetBytes("d"), 0, 1);
            }
            var appended = ContentText(location);
            using (var stream = FileSystem.File.OpenWrite(location, overwrite: true))
            {
                stream.Write(Encoding.ASCII.GetBytes("z"), 0, 1);
            }

            Assert.That(appended, Is.EqualTo("abcd"));
            Assert.That(ContentText(location), Is.EqualTo("z"));
        }

        /// <summary>Checks that local-to-local copies use native copying and honour overwrite.</summary>
        [Test]
        public void Copy_CopiesLocalFilesNatively()
        {
            var source = _temp.WriteText("source.txt", "new");
            var destination = PathOf("destination.txt");

            var copy = FileSystem.File.Copy(source.Location, destination);
            source.WriteAllText("newer");

            Assert.That(copy.ReadAllText(), Is.EqualTo("new"));
            Assert.Throws<IOException>(() => FileSystem.File.Copy(source.Location, destination));
            Assert.That(FileSystem.File.Copy(source.Location, destination, true).ReadAllText(), Is.EqualTo("newer"));
        }

        /// <summary>Checks that a provider source is streamed to a new local destination path.</summary>
        [Test]
        public void Copy_StreamsProviderSourceToLocalPath()
        {
            var source = AddContentFile("remote.txt", "remote");

            var copy = FileSystem.File.Copy(source, PathOf("local.txt"));

            Assert.That(copy.Location, Is.EqualTo(PathOf("local.txt")));
            Assert.That(copy.ReadAllText(), Is.EqualTo("remote"));
        }

        /// <summary>Checks that a filesystem root is refused as a destination file before anything is written.</summary>
        [Test]
        public void Copy_RejectsFilesystemRootDestination()
        {
            var source = AddContentFile("remote.txt", "remote");
            var root = Path.GetPathRoot(_temp.Location)!;

            Assert.Throws<IOException>(() => FileSystem.File.Copy(source, root, true));
        }

        /// <summary>Checks that a local source can overwrite an existing provider document but cannot create one by location.</summary>
        [Test]
        public void Copy_RequiresExistingProviderDestination()
        {
            var source = _temp.WriteText("source.txt", "local");
            var destination = AddContentFile("target.txt", "old");

            var copy = FileSystem.File.Copy(source.Location, destination, true);

            Assert.That(copy.Location, Is.EqualTo(destination));
            Assert.That(ContentText(destination), Is.EqualTo("local"));
            Assert.Throws<FileNotFoundException>(() => FileSystem.File.Copy(source.Location, _provider.RootLocation + "9999", true));
        }

        /// <summary>Checks the parent-and-name copy overload for local directories, including validation.</summary>
        [Test]
        public void CopyChild_CopiesIntoLocalDirectory()
        {
            var source = _temp.WriteText("source.txt", "x");
            var target = _temp.Directory.CreateDirectory("target");

            var copy = FileSystem.File.Copy(source.Location, target.Location, "copy.txt");

            Assert.That(copy.Location, Is.EqualTo(Path.Combine(target.Location, "copy.txt")));
            Assert.That(copy.ReadAllText(), Is.EqualTo("x"));
            Assert.Throws<ArgumentException>(() => FileSystem.File.Copy(source.Location, target.Location, "a/b"));
            Assert.Throws<ArgumentException>(() => FileSystem.File.Copy(source.Location, target.Location, "a:b"));
            Assert.Throws<DirectoryNotFoundException>(() => FileSystem.File.Copy(source.Location, PathOf("missing"), "copy.txt"));
        }

        /// <summary>Checks the parent-and-name copy overload for a provider directory.</summary>
        [Test]
        public void CopyChild_CopiesIntoProviderDirectory()
        {
            var source = _temp.WriteText("source.txt", "to provider");

            var copy = FileSystem.File.Copy(source.Location, _provider.RootLocation, "copy.txt");

            Assert.That(copy.FileSystem, Is.SameAs(_provider));
            Assert.That(ContentText(copy.Location), Is.EqualTo("to provider"));
        }

        /// <summary>Checks that a location-based move renames local files without overwriting.</summary>
        [Test]
        public void Move_MovesLocalFiles()
        {
            var source = _temp.WriteText("a.txt", "x");
            _temp.WriteText("taken.txt", "y");

            var moved = FileSystem.File.Move(source.Location, PathOf("b.txt"));

            Assert.That(moved.ReadAllText(), Is.EqualTo("x"));
            Assert.That(source.Exists, Is.False);
            Assert.Throws(Is.InstanceOf<IOException>(), () => FileSystem.File.Move(moved.Location, PathOf("taken.txt")));
        }

        /// <summary>Checks that a location-based move refuses provider operands, which cannot name a new document.</summary>
        [Test]
        public void Move_RejectsProviderLocations()
        {
            var local = _temp.WriteText("a.txt", "x");
            var content = AddContentFile("b.txt", "y");

            Assert.Throws<NotSupportedException>(() => FileSystem.File.Move(content, PathOf("c.txt")));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Move(local.Location, content));
            Assert.That(local.Exists, Is.True);
        }

        /// <summary>Checks that the parent-and-name move works natively inside each backend.</summary>
        [Test]
        public void MoveChild_MovesInsideOneBackend()
        {
            var local = _temp.WriteText("a.txt", "local");
            var target = _temp.Directory.CreateDirectory("target");
            var content = AddContentFile("b.txt", "content");
            var contentTarget = _provider.AddDirectory(_provider.RootLocation, "target");

            var movedLocal = FileSystem.File.Move(local.Location, target.Location, "moved.txt");
            var movedContent = FileSystem.File.Move(content, contentTarget, "moved.txt");

            Assert.That(movedLocal.Location, Is.EqualTo(Path.Combine(target.Location, "moved.txt")));
            Assert.That(movedLocal.ReadAllText(), Is.EqualTo("local"));
            Assert.That(movedContent.FileSystem, Is.SameAs(_provider));
            Assert.That(ContentText(movedContent.Location), Is.EqualTo("content"));
            Assert.That(_provider.Contains(content), Is.False);
        }

        /// <summary>Checks that the parent-and-name move refuses wrong source kinds, missing sources and cross-backend moves.</summary>
        [Test]
        public void MoveChild_ValidatesOperands()
        {
            var local = _temp.WriteText("a.txt", "x");
            var directory = _temp.Directory.CreateDirectory("dir");

            Assert.Throws<ArgumentException>(() => FileSystem.File.Move(local.Location, _temp.Location, ".."));
            Assert.Throws<FileNotFoundException>(() => FileSystem.File.Move(PathOf("missing"), _temp.Location, "b"));
            Assert.Throws<IOException>(() => FileSystem.File.Move(directory.Location, _temp.Location, "b"));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Move(local.Location, _provider.RootLocation, "b"));
            Assert.That(local.Exists, Is.True);
        }

        /// <summary>Checks native local replacement with a backup of the original destination.</summary>
        [Test]
        public void Replace_ReplacesLocalFileWithBackup()
        {
            var source = _temp.WriteText("source.txt", "replacement");
            var destination = _temp.WriteText("destination.txt", "original");

            var replaced = FileSystem.File.Replace(source.Location, destination.Location, PathOf("backup.txt"));

            Assert.That(replaced.ReadAllText(), Is.EqualTo("replacement"));
            Assert.That(FileSystem.File.Open(PathOf("backup.txt")).ReadAllText(), Is.EqualTo("original"));
            Assert.That(source.Exists, Is.False);
        }

        /// <summary>Checks native local replacement without a backup.</summary>
        [Test]
        public void Replace_ReplacesLocalFileWithoutBackup()
        {
            var source = _temp.WriteText("source.txt", "replacement");
            var destination = _temp.WriteText("destination.txt", "original");

            FileSystem.File.Replace(source.Location, destination.Location);

            Assert.That(destination.ReadAllText(), Is.EqualTo("replacement"));
            Assert.That(source.Exists, Is.False);
        }

        /// <summary>Checks that a provider destination is overwritten in place and the local source is consumed.</summary>
        [Test]
        public void Replace_StreamsIntoProviderDestination()
        {
            var source = _temp.WriteText("source.txt", "replacement");
            var destination = AddContentFile("destination.txt", "original");

            var replaced = FileSystem.File.Replace(source.Location, destination);

            Assert.That(replaced.Location, Is.EqualTo(destination));
            Assert.That(ContentText(destination), Is.EqualTo("replacement"));
            Assert.That(source.Exists, Is.False);
        }

        /// <summary>Checks that a provider source is consumed after replacing a local destination.</summary>
        [Test]
        public void Replace_ConsumesProviderSource()
        {
            var source = AddContentFile("source.txt", "replacement");
            var destination = _temp.WriteText("destination.txt", "original");

            FileSystem.File.Replace(source, destination.Location);

            Assert.That(destination.ReadAllText(), Is.EqualTo("replacement"));
            Assert.That(_provider.Contains(source), Is.False);
        }

        /// <summary>Checks that a backup path is refused for provider replacement before anything changes.</summary>
        [Test]
        public void Replace_RejectsBackupForProviderOperands()
        {
            var source = _temp.WriteText("source.txt", "replacement");
            var destination = AddContentFile("destination.txt", "original");

            Assert.Throws<NotSupportedException>(() => FileSystem.File.Replace(source.Location, destination, PathOf("backup.txt")));

            Assert.That(ContentText(destination), Is.EqualTo("original"));
            Assert.That(source.Exists, Is.True);
        }

        /// <summary>Checks that a failed provider replacement never deletes the source.</summary>
        [Test]
        public void Replace_KeepsSourceWhenProviderWriteFails()
        {
            var source = _temp.WriteText("source.txt", "replacement");
            var destination = AddContentFile("destination.txt", "original");
            _provider.FailWrites = true;

            Assert.Throws<IOException>(() => FileSystem.File.Replace(source.Location, destination));

            Assert.That(source.Exists, Is.True);
        }
    }
}

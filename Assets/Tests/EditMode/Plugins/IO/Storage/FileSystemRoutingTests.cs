#nullable enable
using System;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers backend selection, location validation and local path normalization of the facade.</summary>
    public class FileSystemRoutingTests
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

        /// <summary>Checks that a null or empty location is reported as a missing argument.</summary>
        /// <param name="location">The missing location.</param>
        [TestCase(null)]
        [TestCase("")]
        public void GetEntry_RejectsMissingLocation(string? location)
        {
            Assert.Throws<ArgumentNullException>(() => FileSystem.GetEntry(location!));
        }

        /// <summary>Checks that a whitespace location is rejected as invalid.</summary>
        [Test]
        public void GetEntry_RejectsWhitespaceLocation()
        {
            Assert.Throws<ArgumentException>(() => FileSystem.GetEntry("   "));
        }

        /// <summary>Checks that URIs with schemes other than file, jar and content are not supported.</summary>
        /// <param name="location">A URI with an unsupported scheme.</param>
        [TestCase("http://example.invalid/chart")]
        [TestCase("ftp://example.invalid/chart")]
        public void GetEntry_RejectsUnsupportedSchemes(string location)
        {
            Assert.Throws<NotSupportedException>(() => FileSystem.GetEntry(location));
        }

        /// <summary>Checks that file and content URIs without the authority marker are rejected.</summary>
        /// <param name="location">A file or content URI without <c>://</c>.</param>
        [TestCase("file:relative")]
        [TestCase("content:document")]
        public void GetEntry_RejectsSchemesWithoutAuthorityMarker(string location)
        {
            Assert.Throws<ArgumentException>(() => FileSystem.GetEntry(location));
        }

        /// <summary>Checks that a malformed explicit URI is rejected.</summary>
        [Test]
        public void GetEntry_RejectsMalformedUri()
        {
            Assert.Throws<ArgumentException>(() => FileSystem.GetEntry("file://[malformed"));
        }

        /// <summary>Checks that a content URI without a provider authority is rejected before provider lookup.</summary>
        [Test]
        public void GetEntry_RejectsContentUriWithoutAuthority()
        {
            using var scope = new ContentProviderScope(new MemoryContentProvider());

            Assert.Throws<ArgumentException>(() => FileSystem.GetEntry("content:///document"));
        }

        /// <summary>Checks that content URIs are not supported while no provider is registered.</summary>
        [Test]
        public void GetEntry_RejectsContentUriWithoutProvider()
        {
            using var scope = new ContentProviderScope(null);

            Assert.Throws<NotSupportedException>(() => FileSystem.GetEntry("content://provider/document"));
            Assert.Throws<NotSupportedException>(() => FileSystem.File.Open("content://provider/document"));
        }

        /// <summary>Checks that registering a null provider is rejected and leaves the current provider in place.</summary>
        [Test]
        public void RegisterContentProvider_RejectsNull()
        {
            var provider = new MemoryContentProvider();
            using var scope = new ContentProviderScope(provider);

            Assert.Throws<ArgumentNullException>(() => FileSystem.RegisterContentProvider(null!));
            Assert.That(ContentProviderScope.Current, Is.SameAs(provider));
        }

        /// <summary>Checks that a later registration replaces the provider for new handles while old handles keep theirs.</summary>
        [Test]
        public void RegisterContentProvider_ReplacesProviderForNewHandles()
        {
            var first = new MemoryContentProvider();
            var second = new MemoryContentProvider();
            using var scope = new ContentProviderScope(first);
            var oldHandle = FileSystem.Directory.Open(first.RootLocation);

            FileSystem.RegisterContentProvider(second);
            var newHandle = FileSystem.Directory.Open(second.RootLocation);

            Assert.That(oldHandle.FileSystem, Is.SameAs(first));
            Assert.That(newHandle.FileSystem, Is.SameAs(second));
            Assert.That(oldHandle.Exists, Is.True);
            Assert.That(FileSystem.GetEntry(second.RootLocation)!.IsDirectory, Is.True);
        }

        /// <summary>Checks that a registered provider receives content URIs and that its locations stay unchanged.</summary>
        [Test]
        public void GetEntry_RoutesContentUrisToRegisteredProvider()
        {
            var provider = new MemoryContentProvider();
            using var scope = new ContentProviderScope(provider);
            var file = provider.AddFile(provider.RootLocation, "a.txt", new byte[] { 1, 2, 3 });

            var entry = FileSystem.GetEntry(file);
            var handle = FileSystem.File.Open(file);

            Assert.That(entry!.Name, Is.EqualTo("a.txt"));
            Assert.That(entry.Length, Is.EqualTo(3));
            Assert.That(handle.FileSystem, Is.SameAs(provider));
            Assert.That(handle.Location, Is.EqualTo(file));
        }

        /// <summary>Checks that local paths are routed to the hidden local backend and missing entries return null.</summary>
        [Test]
        public void GetEntry_RoutesNativePathsToLocalBackend()
        {
            var file = _temp.WriteText("a.txt", "abc");

            var entry = FileSystem.GetEntry(file.Location);
            var missing = FileSystem.GetEntry(System.IO.Path.Combine(_temp.Location, "missing.txt"));

            Assert.That(file.FileSystem, Is.InstanceOf<LocalFileSystem>());
            Assert.That(entry!.Location, Is.EqualTo(file.Location));
            Assert.That(entry.Length, Is.EqualTo(3));
            Assert.That(missing, Is.Null);
        }

        /// <summary>Checks that file URIs, including escaped characters, resolve to the same local entry.</summary>
        [Test]
        public void FileUri_ResolvesToNormalizedLocalPath()
        {
            var directory = _temp.Directory.CreateDirectory("with space 中文");
            var uri = new Uri(directory.Location).AbsoluteUri;

            var opened = FileSystem.Directory.Open(uri);

            Assert.That(uri, Does.StartWith("file:///"));
            Assert.That(opened.Location, Is.EqualTo(directory.Location));
            Assert.That(opened.Exists, Is.True);
        }

        /// <summary>Checks that relative local paths are resolved against the current directory when the handle is created.</summary>
        [Test]
        public void RelativePath_IsResolvedWhenTheHandleIsCreated()
        {
            var handle = FileSystem.Directory.Open("relative-storage-test");

            Assert.That(handle.Location, Is.EqualTo(System.IO.Path.GetFullPath("relative-storage-test")));
            Assert.That(System.IO.Path.IsPathRooted(handle.Location), Is.True);
        }

        /// <summary>Checks that trailing separators are trimmed while a filesystem root keeps its separator.</summary>
        [Test]
        public void NormalizeLocalPath_TrimsTrailingSeparatorsExceptRoot()
        {
            var root = System.IO.Path.GetPathRoot(_temp.Location)!;

            var trimmed = FileSystem.NormalizeLocalPath(_temp.Location + "\\\\/");
            var normalizedRoot = FileSystem.NormalizeLocalPath(root);

            Assert.That(trimmed, Is.EqualTo(_temp.Location));
            Assert.That(normalizedRoot, Is.EqualTo(root));
        }

        /// <summary>Checks that file URIs with a query or fragment are rejected because they are not path components.</summary>
        /// <param name="suffix">A query or fragment suffix.</param>
        [TestCase("?q=1")]
        [TestCase("#fragment")]
        public void NormalizeLocalPath_RejectsFileUriQueryAndFragment(string suffix)
        {
            var uri = new Uri(_temp.Location).AbsoluteUri + suffix;

            Assert.Throws<ArgumentException>(() => FileSystem.NormalizeLocalPath(uri));
        }

        /// <summary>Checks that non-file URIs never belong to the local backend.</summary>
        [Test]
        public void NormalizeLocalPath_RejectsNonFileUris()
        {
            Assert.Throws<NotSupportedException>(() => FileSystem.NormalizeLocalPath("content://provider/document"));
            Assert.Throws<NotSupportedException>(() => new StorageFile(new LocalFileSystem(), "http://example.invalid/a"));
        }

        /// <summary>Checks that a null location is rejected by local normalization.</summary>
        [Test]
        public void NormalizeLocalPath_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => FileSystem.NormalizeLocalPath(null!));
        }

        /// <summary>Checks that jar locations are routed to the archive backend regardless of scheme case.</summary>
        [Test]
        public void JarLocations_AreRoutedToArchiveBackend()
        {
            var archive = new Uri(System.IO.Path.Combine(_temp.Location, "missing.zip")).AbsoluteUri;

            var lower = FileSystem.File.Open("jar:" + archive + "!/a.txt");
            var upper = FileSystem.File.Open("JAR:" + archive + "!/a.txt");

            Assert.That(lower.FileSystem, Is.InstanceOf<JarFileSystem>());
            Assert.That(upper.FileSystem, Is.InstanceOf<JarFileSystem>());
            Assert.That(lower.Exists, Is.False);
        }
    }
}

#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers stream copies between file handles of the same and different backends.</summary>
    public class StorageFileCopyTests
    {
        /// <summary>The temporary directory of the current test.</summary>
        private TemporaryDirectory _temp = null!;

        /// <summary>The in-memory provider used as the opaque backend.</summary>
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

        /// <summary>Gets a handle for the provider root directory.</summary>
        private StorageDirectory ContentRoot => new StorageDirectory(_provider, _provider.RootLocation);

        /// <summary>Adds a provider file and returns its handle.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="text">The UTF-8 content.</param>
        /// <returns>The provider file handle.</returns>
        private StorageFile AddContentFile(string name, string text)
        {
            return new StorageFile(_provider, _provider.AddFile(_provider.RootLocation, name, Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>Checks that copying over an existing file replaces its content and returns the destination handle.</summary>
        [Test]
        public void CopyToFile_ReplacesExistingDestination()
        {
            var source = _temp.WriteText("source.txt", "new");
            var destination = _temp.WriteText("destination.txt", "older and longer");

            var result = source.CopyTo(destination, overwrite: true);

            Assert.That(result.Location, Is.EqualTo(destination.Location));
            Assert.That(destination.ReadAllText(), Is.EqualTo("new"));
        }

        /// <summary>Checks that copying to an existing file requires overwrite, an existing destination file and a source.</summary>
        [Test]
        public void CopyToFile_ValidatesOperands()
        {
            var source = _temp.WriteText("source.txt", "new");
            var destination = _temp.WriteText("destination.txt", "old");
            var missing = FileSystem.File.Open(Path.Combine(_temp.Location, "missing.txt"));
            var directory = FileSystem.File.Open(_temp.Directory.CreateDirectory("dir").Location);

            Assert.Throws<ArgumentNullException>(() => source.CopyTo((StorageFile)null!));
            Assert.Throws<IOException>(() => source.CopyTo(destination));
            Assert.Throws<FileNotFoundException>(() => source.CopyTo(missing, overwrite: true));
            Assert.Throws<FileNotFoundException>(() => missing.CopyTo(destination, overwrite: true));
            Assert.Throws<IOException>(() => source.CopyTo(directory, overwrite: true));
            Assert.That(destination.ReadAllText(), Is.EqualTo("old"));
            Assert.That(missing.Exists, Is.False);
        }

        /// <summary>Checks that a file is never copied over itself, including case and file URI aliases.</summary>
        [Test]
        public void CopyToFile_RejectsLocalSelfAliases()
        {
            var source = _temp.WriteText("Self.txt", "keep");
            var sameCase = FileSystem.File.Open(source.Location.ToUpperInvariant());
            var uriAlias = FileSystem.File.Open(new Uri(source.Location).AbsoluteUri);

            Assert.Throws<IOException>(() => source.CopyTo(source, overwrite: true));
            Assert.Throws<IOException>(() => source.CopyTo(sameCase, overwrite: true));
            Assert.Throws<IOException>(() => source.CopyTo(uriAlias, overwrite: true));
            Assert.Throws<IOException>(() => source.CopyTo(_temp.Directory, "Self.txt", overwrite: true));
            Assert.That(source.ReadAllText(), Is.EqualTo("keep"));
        }

        /// <summary>Checks that a pre-cancelled token prevents every side effect of a synchronous copy.</summary>
        [Test]
        public void CopyTo_HonoursPreCancelledToken()
        {
            var source = _temp.WriteText("source.txt", "new");
            var destination = _temp.WriteText("destination.txt", "old");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => source.CopyTo(destination, true, cancellation.Token));
            Assert.Throws<OperationCanceledException>(() => source.CopyTo(_temp.Directory, "copy.txt", false, cancellation.Token));
            Assert.That(destination.ReadAllText(), Is.EqualTo("old"));
            Assert.That(_temp.Directory.FindFile("copy.txt"), Is.Null);
        }

        /// <summary>Checks that copying into a directory validates the destination, name and source before creating anything.</summary>
        [Test]
        public void CopyToDirectory_ValidatesOperands()
        {
            var source = _temp.WriteText("source.txt", "x");
            var missingSource = FileSystem.File.Open(Path.Combine(_temp.Location, "missing.txt"));
            var missingDirectory = FileSystem.Directory.Open(Path.Combine(_temp.Location, "missing-dir"));
            _temp.Directory.CreateDirectory("occupied");

            Assert.Throws<ArgumentNullException>(() => source.CopyTo((StorageDirectory)null!, "a"));
            Assert.Throws<ArgumentException>(() => source.CopyTo(_temp.Directory, ".."));
            Assert.Throws<FileNotFoundException>(() => missingSource.CopyTo(_temp.Directory, "copy.txt"));
            Assert.Throws<DirectoryNotFoundException>(() => source.CopyTo(missingDirectory, "copy.txt"));
            Assert.Throws<IOException>(() => source.CopyTo(_temp.Directory, "occupied", overwrite: true));
            Assert.That(_temp.Directory.FindFile("copy.txt"), Is.Null);
        }

        /// <summary>Checks that a local file streams into a new provider document and keeps the provider-assigned name.</summary>
        [Test]
        public void CopyToDirectory_CreatesProviderDocument()
        {
            var source = _temp.WriteText("source.txt", "to provider");
            _provider.NextCreatedFileName = "source (1).txt";

            var copy = source.CopyTo(ContentRoot, "source.txt");

            Assert.That(copy.FileSystem, Is.SameAs(_provider));
            Assert.That(copy.Entry!.Name, Is.EqualTo("source (1).txt"));
            Assert.That(Encoding.UTF8.GetString(_provider.GetData(copy.Location)), Is.EqualTo("to provider"));
        }

        /// <summary>Checks that a provider document streams into a new local file.</summary>
        [Test]
        public void CopyToDirectory_ExtractsProviderDocumentToLocal()
        {
            var source = AddContentFile("remote.txt", "from provider");

            var copy = source.CopyTo(_temp.Directory, "local.txt");

            Assert.That(copy.FileSystem, Is.InstanceOf<LocalFileSystem>());
            Assert.That(copy.ReadAllText(), Is.EqualTo("from provider"));
        }

        /// <summary>Checks that overwriting an existing provider document truncates it before writing.</summary>
        [Test]
        public void CopyToDirectory_OverwritesProviderDocument()
        {
            var source = _temp.WriteText("source.txt", "new");
            var existing = AddContentFile("target.txt", "older and longer");

            var copy = source.CopyTo(ContentRoot, "target.txt", overwrite: true);

            Assert.That(copy.Location, Is.EqualTo(existing.Location));
            Assert.That(Encoding.UTF8.GetString(_provider.GetData(existing.Location)), Is.EqualTo("new"));
        }

        /// <summary>Checks that a failed write deletes the newly created destination but keeps the source.</summary>
        [Test]
        public void CopyToDirectory_DeletesNewDestinationAfterWriteFailure()
        {
            var source = _temp.WriteText("source.txt", "payload");
            _provider.FailWrites = true;

            Assert.Throws<IOException>(() => source.CopyTo(ContentRoot, "copy.txt"));

            Assert.That(_provider.DeletedLocations, Has.Count.EqualTo(1));
            Assert.That(ContentRoot.FindFile("copy.txt"), Is.Null);
            Assert.That(source.ReadAllText(), Is.EqualTo("payload"));
        }

        /// <summary>Checks that a failed overwrite never deletes the existing destination.</summary>
        [Test]
        public void CopyToDirectory_KeepsExistingDestinationAfterFailure()
        {
            var source = _temp.WriteText("source.txt", "payload");
            var existing = AddContentFile("copy.txt", "old");
            _provider.FailWrites = true;

            Assert.Throws<IOException>(() => source.CopyTo(ContentRoot, "copy.txt", overwrite: true));

            Assert.That(_provider.DeletedLocations, Is.Empty);
            Assert.That(existing.Exists, Is.True);
        }

        /// <summary>Checks that a failed source read deletes the new local destination.</summary>
        [Test]
        public void CopyToDirectory_DeletesNewDestinationAfterReadFailure()
        {
            var source = AddContentFile("remote.txt", "payload");
            _provider.FailReads = true;

            Assert.Throws<IOException>(() => source.CopyTo(_temp.Directory, "local.txt"));

            Assert.That(_temp.Directory.FindFile("local.txt"), Is.Null);
        }

        /// <summary>Checks that cancellation during the transfer deletes the new destination.</summary>
        [Test]
        public void CopyToDirectory_DeletesNewDestinationAfterMidTransferCancellation()
        {
            var source = AddContentFile("remote.txt", "payload");
            using var cancellation = new CancellationTokenSource();
            _provider.AfterRead = cancellation.Cancel;

            Assert.Throws<OperationCanceledException>(() => source.CopyTo(_temp.Directory, "local.txt", false, cancellation.Token));

            Assert.That(_temp.Directory.FindFile("local.txt"), Is.Null);
        }

        /// <summary>Checks that a provider returning the source as the created file never causes the source to be deleted.</summary>
        [Test]
        public void CopyToDirectory_RejectsProviderReturningSource()
        {
            var source = AddContentFile("source.txt", "keep");
            _provider.CreatedFileOverride = source.Location;

            Assert.Throws<IOException>(() => source.CopyTo(ContentRoot, "copy.txt"));

            Assert.That(_provider.DeletedLocations, Is.Empty);
            Assert.That(Encoding.UTF8.GetString(_provider.GetData(source.Location)), Is.EqualTo("keep"));
        }

        /// <summary>Checks that an alias URI sharing the source's resource identity is treated as the source.</summary>
        [Test]
        public void CopyToFile_RejectsResourceIdAlias()
        {
            _provider.ReportResourceIds = true;
            var source = AddContentFile("source.txt", "keep");
            var alias = new StorageFile(_provider, _provider.AddAlias(source.Location));

            Assert.Throws<IOException>(() => source.CopyTo(alias, overwrite: true));
            Assert.That(Encoding.UTF8.GetString(_provider.GetData(source.Location)), Is.EqualTo("keep"));
        }

        /// <summary>Checks that the same provider location is detected without resource identities.</summary>
        [Test]
        public void CopyToFile_RejectsSameProviderLocation()
        {
            var source = AddContentFile("source.txt", "keep");

            Assert.Throws<IOException>(() => source.CopyTo(new StorageFile(_provider, source.Location), overwrite: true));
            Assert.That(_provider.OpenedWriteStreams, Is.Zero);
        }

        /// <summary>Checks that identical content URIs from separate provider instances are still recognized as one resource.</summary>
        [Test]
        public void CopyToFile_RejectsSameContentUriAcrossProviderInstances()
        {
            var first = new MemoryContentProvider("shared-volume");
            var second = new MemoryContentProvider("shared-volume");
            var source = new StorageFile(first, first.AddFile(first.RootLocation, "a.txt", new byte[] { 1 }));
            var destination = new StorageFile(second, second.AddFile(second.RootLocation, "a.txt", new byte[] { 2 }));

            Assert.That(destination.Location, Is.EqualTo(source.Location));
            Assert.Throws<IOException>(() => source.CopyTo(destination, overwrite: true));
            Assert.That(second.GetData(destination.Location), Is.EqualTo(new byte[] { 2 }));
        }

        /// <summary>Checks that different documents of separate provider instances can be copied between each other.</summary>
        [Test]
        public void CopyToFile_CopiesBetweenProviderInstances()
        {
            var other = new MemoryContentProvider();
            var source = AddContentFile("source.txt", "cross");
            var destination = new StorageFile(other, other.AddFile(other.RootLocation, "target.txt", new byte[] { 0 }));

            source.CopyTo(destination, overwrite: true);

            Assert.That(Encoding.UTF8.GetString(other.GetData(destination.Location)), Is.EqualTo("cross"));
        }

        /// <summary>Checks that an asynchronous copy between providers deletes the new destination after mid-transfer cancellation.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CopyToAsync_DeletesNewDestinationAfterMidTransferCancellation() => UniTask.ToCoroutine(async () =>
        {
            var source = AddContentFile("remote.txt", "payload");
            using var cancellation = new CancellationTokenSource();
            _provider.AfterRead = cancellation.Cancel;

            await AsyncAssert.ThrowsAsync<OperationCanceledException>(
                () => source.CopyToAsync(_temp.Directory, "local.txt", cancellationToken: cancellation.Token));

            Assert.That(_temp.Directory.FindFile("local.txt"), Is.Null);
        });

        /// <summary>Checks that an asynchronous copy deletes a new provider document after a write failure.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CopyToAsync_DeletesNewDestinationAfterWriteFailure() => UniTask.ToCoroutine(async () =>
        {
            var source = _temp.WriteText("source.txt", "payload");
            _provider.FailWrites = true;

            await AsyncAssert.ThrowsAsync<IOException>(() => source.CopyToAsync(ContentRoot, "copy.txt"));

            Assert.That(_provider.DeletedLocations, Has.Count.EqualTo(1));
            Assert.That(ContentRoot.FindFile("copy.txt"), Is.Null);
        });

        /// <summary>Checks that an asynchronous copy streams a provider document into a new local file.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CopyToAsync_ExtractsProviderDocument() => UniTask.ToCoroutine(async () =>
        {
            var source = AddContentFile("remote.txt", "async provider");

            var copy = await source.CopyToAsync(_temp.Directory, "local.txt");

            Assert.That(copy.ReadAllText(), Is.EqualTo("async provider"));
        });

        /// <summary>Checks that asynchronous provider reads stop with cancellation once the token is cancelled mid-read.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAllAsync_HonoursMidReadCancellation() => UniTask.ToCoroutine(async () =>
        {
            var source = AddContentFile("remote.txt", new string('x', 10_000));
            using var bytesCancellation = new CancellationTokenSource();
            using var textCancellation = new CancellationTokenSource();

            _provider.AfterRead = bytesCancellation.Cancel;
            await AsyncAssert.ThrowsAsync<OperationCanceledException>(() => source.ReadAllBytesAsync(bytesCancellation.Token));
            _provider.AfterRead = textCancellation.Cancel;
            await AsyncAssert.ThrowsAsync<OperationCanceledException>(() => source.ReadAllTextAsync(cancellationToken: textCancellation.Token));
        });

        /// <summary>Checks that provider documents round-trip through the asynchronous whole-file helpers.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator WriteAllAsync_RoundTripsProviderDocument() => UniTask.ToCoroutine(async () =>
        {
            var file = ContentRoot.CreateFile("doc.txt", "text/plain");

            await file.WriteAllTextAsync("provider text");
            var text = await file.ReadAllTextAsync();
            await file.WriteAllBytesAsync(new byte[] { 1, 2 });
            var bytes = await file.ReadAllBytesAsync();

            Assert.That(text, Is.EqualTo("provider text"));
            Assert.That(bytes, Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(_provider.LastMimeType, Is.EqualTo("text/plain"));
        });
    }
}

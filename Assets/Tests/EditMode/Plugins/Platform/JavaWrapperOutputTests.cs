using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MajdataPlay.IO.Storage;
using MajdataPlay.Platform.Android.Editor;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Platform
{
    /// <summary>Verifies validated publication of generated Java wrapper sources into Unity asset directories.</summary>
    public class JavaWrapperOutputTests
    {
        /// <summary>The generator name accepted by the publisher.</summary>
        private const string Generator = "AndroidJavaWrapperGenerator";
        /// <summary>The root namespace accepted by the publisher.</summary>
        private const string Namespace = "MajdataPlay.Platform.Android.Runtime";
        /// <summary>Owns all fixture directories.</summary>
        private TemporaryDirectory _directory = null!;
        /// <summary>Stores the Unity output directory.</summary>
        private StorageDirectory _output = null!;

        /// <summary>Creates the fixture root and an empty output directory.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = new TemporaryDirectory();
            _output = _directory.Directory.CreateDirectory("output");
        }

        /// <summary>Clears read-only flags and deletes the fixtures.</summary>
        [TearDown]
        public void TearDown()
        {
            ClearReadOnly(_directory.Directory);
            _directory.Dispose();
        }

        /// <summary>Removes read-only attributes recursively so the fixture can be deleted.</summary>
        /// <param name="directory">The directory to clear.</param>
        private static void ClearReadOnly(StorageDirectory directory)
        {
            foreach (var file in directory.EnumerateFiles())
            {
                file.SetAttributes(FileAttributes.Normal);
            }

            foreach (var entry in directory.EnumerateEntries())
            {
                if (entry.IsDirectory)
                {
                    ClearReadOnly(FileSystem.Directory.Open(entry.Location));
                }
            }
        }

        /// <summary>Computes the uppercase SHA-256 fingerprint used by ownership manifests.</summary>
        /// <param name="text">The UTF-8 source text.</param>
        /// <returns>The fingerprint.</returns>
        private static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
        }

        /// <summary>Builds an ownership manifest.</summary>
        /// <param name="files">The relative paths and their fingerprints.</param>
        /// <param name="generator">The generator name.</param>
        /// <param name="rootNamespace">The root namespace.</param>
        /// <returns>The manifest JSON.</returns>
        private static string Manifest(IEnumerable<KeyValuePair<string, string>> files, string generator = Generator, string rootNamespace = Namespace)
        {
            var entries = string.Join(",", files.Select(file => "\"" + file.Key + "\":\"" + file.Value + "\""));
            return "{\"FormatVersion\":1,\"Generator\":\"" + generator + "\",\"RootNamespace\":\"" + rootNamespace + "\",\"Files\":{" + entries + "}}";
        }

        /// <summary>Creates a file at a relative path below a directory, creating intermediate directories.</summary>
        /// <param name="root">The root directory.</param>
        /// <param name="relativePath">The forward-slash relative path.</param>
        /// <param name="text">The file contents.</param>
        private static void Write(StorageDirectory root, string relativePath, string text)
        {
            var parts = relativePath.Split('/');
            var directory = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                directory = directory.CreateDirectory(parts[i]);
            }

            var file = directory.FindFile(parts[^1]) ?? directory.CreateFile(parts[^1]);
            file.WriteAllText(text);
        }

        /// <summary>Reads a file at a relative path, or returns null when it does not exist.</summary>
        /// <param name="root">The root directory.</param>
        /// <param name="relativePath">The forward-slash relative path.</param>
        /// <returns>The file text, or null.</returns>
        private static string? Read(StorageDirectory root, string relativePath)
        {
            return root.FindFileAt(relativePath)?.ReadAllText();
        }

        /// <summary>Creates a staging directory containing sources and their manifest.</summary>
        /// <param name="name">The staging directory name.</param>
        /// <param name="sources">The relative paths and contents.</param>
        /// <returns>The staging directory.</returns>
        private StorageDirectory Stage(string name, IDictionary<string, string> sources)
        {
            var stage = _directory.Directory.CreateDirectory(name);
            foreach (var source in sources)
            {
                Write(stage, source.Key, source.Value);
            }

            Write(stage, "java-wrappers.manifest.json", Manifest(sources.Select(source => new KeyValuePair<string, string>(source.Key, Hash(source.Value)))));
            return stage;
        }

        /// <summary>Checks the complete publication lifecycle: create, unchanged, update and stale-file removal.</summary>
        [Test]
        public void Publish_CreatesUpdatesAndRemovesOwnedSources()
        {
            var first = Stage("stage1", new Dictionary<string, string>
            {
                { "AndroidJni.cs", "// jni" },
                { "Bindings/Java/Lang/Object.g.cs", "// object" },
                { "Stale.cs", "// stale" }
            });

            Assert.That(JavaWrapperOutput.Publish(first.Location, _output.Location, null, true), Is.EqualTo(3));
            Assert.That(Read(_output, "Stale.cs"), Is.Null);
            Assert.That(JavaWrapperOutput.Publish(first.Location, _output.Location), Is.EqualTo(3));
            Assert.That(Read(_output, "AndroidJni.cs"), Is.EqualTo("// jni"));
            Assert.That(Read(_output, "Bindings/Java/Lang/Object.g.cs"), Is.EqualTo("// object"));
            Assert.That(Read(_output, "java-wrappers.manifest.json"), Is.EqualTo(Read(first, "java-wrappers.manifest.json")));
            Assert.That(Read(_output, ".java-wrappers.publication.json"), Is.Null);
            Write(_output, "Stale.cs.meta", "guid");
            Write(_output, "Handwritten.cs", "// handwritten");

            Assert.That(JavaWrapperOutput.Publish(first.Location, _output.Location), Is.EqualTo(3));

            var second = Stage("stage2", new Dictionary<string, string>
            {
                { "AndroidJni.cs", "// jni v2" },
                { "Bindings/Java/Lang/Object.g.cs", "// object" }
            });
            Assert.That(JavaWrapperOutput.Publish(second.Location, _output.Location), Is.EqualTo(2));

            Assert.That(Read(_output, "AndroidJni.cs"), Is.EqualTo("// jni v2"));
            Assert.That(Read(_output, "Stale.cs"), Is.Null);
            Assert.That(Read(_output, "Stale.cs.meta"), Is.Null);
            Assert.That(Read(_output, "Handwritten.cs"), Is.EqualTo("// handwritten"));
        }

        /// <summary>Checks that unowned or locally modified destination sources are never overwritten.</summary>
        [Test]
        public void Publish_RefusesToOverwriteHandwrittenOrModifiedSources()
        {
            Write(_output, "A.cs", "// handwritten");
            var stage = Stage("stage", new Dictionary<string, string> { { "A.cs", "// generated" } });

            Assert.That(Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location))!.Message, Does.Contain("handwritten"));
            Assert.That(Read(_output, "A.cs"), Is.EqualTo("// handwritten"));

            var clean = _directory.Directory.CreateDirectory("clean");
            JavaWrapperOutput.Publish(stage.Location, clean.Location);
            Write(clean, "A.cs", "// edited by hand");
            var update = Stage("update", new Dictionary<string, string> { { "A.cs", "// generated v2" } });

            Assert.That(Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(update.Location, clean.Location))!.Message, Does.Contain("modified generated source"));
        }

        /// <summary>Checks that a read-only destination is refused before any change.</summary>
        [Test]
        public void Publish_RefusesReadOnlyDestination()
        {
            var stage = Stage("stage", new Dictionary<string, string> { { "A.cs", "// v1" } });
            JavaWrapperOutput.Publish(stage.Location, _output.Location);
            _output.FindFile("A.cs")!.SetAttributes(FileAttributes.ReadOnly);
            var update = Stage("update", new Dictionary<string, string> { { "A.cs", "// v2" } });

            var error = Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(update.Location, _output.Location));

            Assert.That(error!.Message, Does.Contain("read-only"));
            Assert.That(Read(_output, "A.cs"), Is.EqualTo("// v1"));
        }

        /// <summary>Checks directory validation and separation between staging, output and former output.</summary>
        [Test]
        public void Publish_ValidatesDirectories()
        {
            var stage = Stage("stage", new Dictionary<string, string> { { "A.cs", "// a" } });

            Assert.Throws<ArgumentException>(() => JavaWrapperOutput.Publish(" ", _output.Location));
            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location + "-missing", _output.Location));
            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, stage.Location));
            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, Path.Combine(stage.Location, "nested")));
            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location, _output.Location));
            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location, stage.Location));
            Write(_output, "A.cs", "// a file, not a directory");
            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, Path.Combine(_output.Location, "A.cs", "x")));
        }

        /// <summary>Checks that malformed manifests are rejected.</summary>
        /// <param name="manifest">The manifest JSON.</param>
        [TestCase("")]
        [TestCase("{")]
        [TestCase("{} {}")]
        [TestCase("[1]")]
        [TestCase("{\"FormatVersion\":2,\"Generator\":\"AndroidJavaWrapperGenerator\",\"RootNamespace\":\"MajdataPlay.Platform.Android.Runtime\",\"Files\":{}}")]
        [TestCase("{\"FormatVersion\":1,\"Generator\":\"Other\",\"RootNamespace\":\"MajdataPlay.Platform.Android.Runtime\",\"Files\":{}}")]
        [TestCase("{\"FormatVersion\":1,\"Generator\":\"AndroidJavaWrapperGenerator\",\"RootNamespace\":\"MajdataPlay.Platform.Android.Runtime.Bindings\",\"Files\":{}}")]
        [TestCase("{\"FormatVersion\":\"x\"}")]
        [TestCase("{\"Generator\":\"\\\"}\"}")]
        public void Publish_RejectsMalformedManifest(string manifest)
        {
            var stage = _directory.Directory.CreateDirectory("stage");
            Write(stage, "java-wrappers.manifest.json", manifest);

            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location));
        }

        /// <summary>Checks that unsafe, duplicate or invalid manifest entries are rejected.</summary>
        /// <param name="path">The manifest entry path.</param>
        /// <param name="hash">The manifest entry fingerprint, or null for a valid one.</param>
        [TestCase("../escape.cs", null)]
        [TestCase("a/./b.cs", null)]
        [TestCase("C:/rooted.cs", null)]
        [TestCase("bad<name.cs", null)]
        [TestCase("trailing./a.cs", null)]
        [TestCase("notes.txt", null)]
        [TestCase("A.cs", "abc")]
        [TestCase("A.cs", "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
        public void Publish_RejectsUnsafeEntries(string path, string? hash)
        {
            var stage = _directory.Directory.CreateDirectory("stage");
            Write(stage, "java-wrappers.manifest.json", Manifest(new[] { new KeyValuePair<string, string>(path, hash ?? Hash("x")) }));

            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location));
        }

        /// <summary>Checks that duplicate case-insensitive paths and files used as directories are rejected.</summary>
        [Test]
        public void Publish_RejectsConflictingEntries()
        {
            var stage = _directory.Directory.CreateDirectory("stage");
            Write(stage, "java-wrappers.manifest.json", Manifest(new[]
            {
                new KeyValuePair<string, string>("A.cs", Hash("a")),
                new KeyValuePair<string, string>("A.cs/B.cs", Hash("b"))
            }));

            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location));

            Write(stage, "java-wrappers.manifest.json", Manifest(new[]
            {
                new KeyValuePair<string, string>("A.cs", Hash("a")),
                new KeyValuePair<string, string>("a.cs", Hash("a"))
            }));

            Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location));
        }

        /// <summary>Checks that staged sources must match their manifest fingerprints.</summary>
        [Test]
        public void Publish_RejectsStagedSourceMismatch()
        {
            var stage = Stage("stage", new Dictionary<string, string> { { "A.cs", "// a" } });
            Write(stage, "A.cs", "// tampered");

            Assert.That(Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location))!.Message, Does.Contain("does not match"));
        }

        /// <summary>Checks migration of owned sources and Unity metadata from a former output directory.</summary>
        [Test]
        public void Publish_MigratesFormerOutputDirectory()
        {
            var former = Stage("Generated", new Dictionary<string, string>
            {
                { "A.cs", "// a" },
                { "Old.cs", "// old" }
            });
            Write(former, "A.cs.meta", "guid-a");
            Write(former, "Old.cs.meta", "guid-old");
            Write(former, "java-wrappers.manifest.json.meta", "guid-manifest");
            var stage = Stage("stage", new Dictionary<string, string>
            {
                { "A.cs", "// a" },
                { "B.cs", "// b" }
            });

            Assert.That(JavaWrapperOutput.Publish(stage.Location, _output.Location, former.Location), Is.EqualTo(2));

            Assert.That(Read(_output, "A.cs"), Is.EqualTo("// a"));
            Assert.That(Read(_output, "A.cs.meta"), Is.EqualTo("guid-a"));
            Assert.That(Read(_output, "B.cs"), Is.EqualTo("// b"));
            Assert.That(Read(_output, "java-wrappers.manifest.json.meta"), Is.EqualTo("guid-manifest"));
            Assert.That(Read(former, "A.cs"), Is.Null);
            Assert.That(Read(former, "Old.cs"), Is.Null);
            Assert.That(Read(former, "Old.cs.meta"), Is.Null);
            Assert.That(Read(former, "java-wrappers.manifest.json"), Is.Null);
        }

        /// <summary>Checks that ownership in both the current and former output directories is refused as ambiguous.</summary>
        [Test]
        public void Publish_RefusesAmbiguousMigration()
        {
            var former = Stage("Generated", new Dictionary<string, string> { { "A.cs", "// a" } });
            var stage = Stage("stage", new Dictionary<string, string> { { "A.cs", "// a" } });
            JavaWrapperOutput.Publish(stage.Location, _output.Location);

            Assert.That(Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location, former.Location))!.Message, Does.Contain("ambiguous"));
        }

        /// <summary>Checks the handling of interrupted publication records.</summary>
        [Test]
        public void Publish_ValidatesInterruptedPublicationRecords()
        {
            var stage = Stage("stage", new Dictionary<string, string> { { "A.cs", "// a" } });
            Write(_output, ".java-wrappers.publication.json", "{}");

            Assert.That(Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location))!.Message, Does.Contain("interrupted publication"));

            var nextManifest = Read(stage, "java-wrappers.manifest.json")!.Replace("\"", "\\\"");
            Write(_output, ".java-wrappers.publication.json", "{\"PreviousManifestHash\":\"" + Hash("other") + "\",\"PreviousFiles\":{},\"NextManifest\":\"" + nextManifest + "\"}");

            Assert.That(Assert.Throws<IOException>(() => JavaWrapperOutput.Publish(stage.Location, _output.Location))!.Message, Does.Contain("does not match"));

            var emptyHash = Hash(string.Empty);
            Write(_output, ".java-wrappers.publication.json", "{\"PreviousManifestHash\":\"" + emptyHash + "\",\"PreviousFiles\":{},\"NextManifest\":\"" + nextManifest + "\"}");
            Write(_output, "A.cs", "// a");

            Assert.That(JavaWrapperOutput.Publish(stage.Location, _output.Location), Is.EqualTo(1));
            Assert.That(Read(_output, ".java-wrappers.publication.json"), Is.Null);
            Assert.That(Read(_output, "A.cs"), Is.EqualTo("// a"));
        }

        /// <summary>Checks the conversion to native Windows paths.</summary>
        [Test]
        public void GetFileSystemPath_UsesExtendedWindowsPaths()
        {
            var absolute = Path.GetFullPath("relative");

            Assert.That(JavaWrapperOutput.GetFileSystemPath("relative"), Is.EqualTo(@"\\?\" + absolute));
            Assert.That(JavaWrapperOutput.GetFileSystemPath(@"\\server\share\x"), Is.EqualTo(@"\\?\UNC\server\share\x"));
        }
    }
}

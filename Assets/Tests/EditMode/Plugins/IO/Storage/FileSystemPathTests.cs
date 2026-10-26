#nullable enable
using System;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers splitting backend-neutral relative paths into validated child names.</summary>
    public class FileSystemPathTests
    {
        /// <summary>Checks that both separators split a path into its names in order.</summary>
        [Test]
        public void Split_AcceptsBothSeparators()
        {
            var names = FileSystem.Path.Split("Skins/default\\notes.png");

            Assert.That(names, Is.EqualTo(new[] { "Skins", "default", "notes.png" }));
        }

        /// <summary>Checks that empty and current-directory segments are dropped.</summary>
        [Test]
        public void Split_DropsEmptyAndCurrentDirectorySegments()
        {
            var names = FileSystem.Path.Split("a//./b/./");

            Assert.That(names, Is.EqualTo(new[] { "a", "b" }));
        }

        /// <summary>Checks that an empty path and a path of only current-directory segments denote the directory itself.</summary>
        /// <param name="path">A path without names.</param>
        [TestCase("")]
        [TestCase(".")]
        [TestCase("./")]
        [TestCase("./.\\")]
        public void Split_ReturnsNoNamesForTheDirectoryItself(string path)
        {
            Assert.That(FileSystem.Path.Split(path), Is.Empty);
        }

        /// <summary>Checks that rooted paths, URIs, traversal and NUL characters are rejected.</summary>
        /// <param name="path">An invalid relative path.</param>
        [TestCase("/rooted")]
        [TestCase("\\rooted")]
        [TestCase("C:\\rooted")]
        [TestCase("http://example.invalid/a")]
        [TestCase("a/../b")]
        [TestCase("..")]
        [TestCase("a/b\0c")]
        public void Split_RejectsInvalidPaths(string path)
        {
            Assert.Throws(Is.InstanceOf<ArgumentException>(), () => FileSystem.Path.Split(path));
        }

        /// <summary>Checks that a null path is reported as a missing argument.</summary>
        [Test]
        public void Split_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => FileSystem.Path.Split(null!));
        }
    }
}

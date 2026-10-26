#nullable enable
using System;
using MajdataPlay.IO.Storage;
using NUnit.Framework;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Covers the backend-neutral single child name validation.</summary>
    public class StorageNameTests
    {
        /// <summary>Checks that ordinary single names, including dot-only names other than traversal, are accepted.</summary>
        /// <param name="name">A valid single child name.</param>
        [TestCase("a")]
        [TestCase("chart.txt")]
        [TestCase("...")]
        [TestCase(".hidden")]
        [TestCase(" leading space")]
        [TestCase("中文 譜面")]
        public void Validate_AcceptsSingleNames(string name)
        {
            Assert.DoesNotThrow(() => StorageName.Validate(name));
        }

        /// <summary>Checks that empty names, traversal segments, separators and NUL are rejected with the parameter name.</summary>
        /// <param name="name">An invalid child name.</param>
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("/")]
        [TestCase("a\0b")]
        public void Validate_RejectsPathLikeNames(string name)
        {
            var exception = Assert.Throws<ArgumentException>(() => StorageName.Validate(name));

            Assert.That(exception.ParamName, Is.EqualTo("name"));
        }

        /// <summary>Checks that a null name is reported as a missing argument.</summary>
        [Test]
        public void Validate_RejectsNull()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => StorageName.Validate(null!));

            Assert.That(exception.ParamName, Is.EqualTo("name"));
        }
    }
}

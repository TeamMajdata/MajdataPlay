using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers the resource manifest helpers used by mobile builds, without generating build manifests.</summary>
    public class ResourceManifestBuildProcessorTests
    {
        /// <summary>The processor type, which is internal to the editor assembly.</summary>
        private static readonly Type s_type = typeof(MajdataPlay.Editor.ResourceManifestBuildProcessor);

        /// <summary>Checks which streaming asset paths are packaged.</summary>
        [Test]
        public void IsPackagedResource_ExcludesMetaAndHiddenPaths()
        {
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsPackagedResource", "SFX/tap.wav"), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsPackagedResource", "SFX/tap.wav.META"), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsPackagedResource", "SFX/.cache/tap.wav"), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsPackagedResource", ".DS_Store"), Is.False);
        }

        /// <summary>Checks relative path normalization and rejection of unsafe paths.</summary>
        [Test]
        public void NormalizeRelativePath_AcceptsOnlySafeRelativePaths()
        {
            Assert.That(PrivateMember.InvokeStatic<string?>(s_type, "NormalizeRelativePath", " a\\b.txt "), Is.EqualTo("a/b.txt"));
            Assert.That(PrivateMember.InvokeStatic(s_type, "NormalizeRelativePath", new object?[] { null }), Is.Null);
            Assert.That(PrivateMember.InvokeStatic(s_type, "NormalizeRelativePath", "  "), Is.Null);
            Assert.That(PrivateMember.InvokeStatic(s_type, "NormalizeRelativePath", "/root/a"), Is.Null);
            Assert.That(PrivateMember.InvokeStatic(s_type, "NormalizeRelativePath", "a//b"), Is.Null);
            Assert.That(PrivateMember.InvokeStatic(s_type, "NormalizeRelativePath", "a/./b"), Is.Null);
            Assert.That(PrivateMember.InvokeStatic(s_type, "NormalizeRelativePath", "a/../b"), Is.Null);
        }

        /// <summary>Checks the lowercase SHA-256 format test.</summary>
        [Test]
        public void IsSha256_RequiresSixtyFourLowercaseHexDigits()
        {
            var valid = new string('a', 32) + new string('0', 32);

            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsSha256", valid), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsSha256", valid.ToUpperInvariant()), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsSha256", valid.Substring(1)), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsSha256", new string('g', 64)), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(s_type, "IsSha256", new object?[] { null }), Is.False);
        }

        /// <summary>Checks that file hashes are lowercase SHA-256 digests.</summary>
        [Test]
        public void ComputeSha256_HashesFileContent()
        {
            using var directory = new TemporaryDirectory();
            var file = directory.WriteText("abc.txt", "abc");

            var hash = PrivateMember.InvokeStatic<string>(s_type, "ComputeSha256", file.Location);

            Assert.That(hash, Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        }

        /// <summary>Checks that asset paths are resolved against the project root.</summary>
        [Test]
        public void ToAbsoluteProjectPath_ResolvesAgainstProjectRoot()
        {
            var path = PrivateMember.InvokeStatic<string>(s_type, "ToAbsoluteProjectPath", "Assets/Resources/x.json");

            Assert.That(Path.GetFullPath(path), Is.EqualTo(Path.GetFullPath(Path.Combine(Application.dataPath, "Resources", "x.json"))));
        }

        /// <summary>Checks that the committed v1 manifest loads as validated lowercase hashes.</summary>
        [Test]
        public void LoadV1HashManifest_LoadsValidatedHashes()
        {
            var hashes = PrivateMember.InvokeStatic<Dictionary<string, string>>(s_type, "LoadV1HashManifest");

            Assert.That(hashes, Is.Not.Empty);
            Assert.That(hashes.Values.All(hash => hash.Length == 64 && hash == hash.ToLowerInvariant()), Is.True);
            Assert.That(hashes.Keys.All(path => !path.Contains('\\')), Is.True);
        }

        /// <summary>Checks that streaming assets are listed relative, sorted and without meta or hidden files.</summary>
        [Test]
        public void GetStreamingAssetPaths_ListsPackagedFilesInOrder()
        {
            var paths = PrivateMember.InvokeStatic<string[]>(s_type, "GetStreamingAssetPaths");

            Assert.That(paths, Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(paths.Any(path => path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(paths.Any(path => path.Contains('\\') || Path.IsPathRooted(path)), Is.False);
        }

        /// <summary>Checks that text assets are written once and rewritten only when the content changes.</summary>
        [Test]
        public void WriteTextAsset_WritesOnlyChangedContent()
        {
            using var folder = new TemporaryAssetFolder();
            var assetPath = folder.Combine("manifest.json");

            PrivateMember.InvokeStatic(s_type, "WriteTextAsset", assetPath, "{}\n");
            var first = folder.ReadText("manifest.json");
            PrivateMember.InvokeStatic(s_type, "WriteTextAsset", assetPath, "{}\n");
            PrivateMember.InvokeStatic(s_type, "WriteTextAsset", assetPath, "[]\n");

            Assert.That(first, Is.EqualTo("{}\n"));
            Assert.That(folder.ReadText("manifest.json"), Is.EqualTo("[]\n"));
        }
    }
}

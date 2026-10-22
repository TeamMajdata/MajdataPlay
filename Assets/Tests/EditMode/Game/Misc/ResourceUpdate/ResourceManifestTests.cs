using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.IO.Storage;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.ResourceUpdate
{
    /// <summary>Covers the bundled resource manifests of <see cref="ResourceManifestLoader"/> and the Windows <see cref="DesktopResourceRestorer"/>.</summary>
    public class ResourceManifestTests
    {
        /// <summary>A chart file listed in the bundled v2 manifest.</summary>
        private const string ChartFile = "MaiCharts/Original/EX-AID/maidata.txt";

        /// <summary>Checks that the bundled v2 manifest lists valid SHA-256 hashes for relative paths.</summary>
        [Test]
        public void TryGetV2Hashes_LoadsBundledManifest()
        {
            Assert.That(ResourceManifestLoader.TryGetV2Hashes(out var hashes), Is.True);

            Assert.That(hashes, Is.Not.Empty);
            Assert.That(hashes.ContainsKey(ChartFile), Is.True);
            Assert.That(hashes.Values.All(hash => hash.Length == 64 && hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))), Is.True);
            Assert.That(hashes.Keys.All(path => !path.StartsWith("/") && !path.Contains("\\")), Is.True);
        }

        /// <summary>Checks that the update manifests load together and the diff only lists files of the v2 manifest.</summary>
        [Test]
        public void TryGetUpdateManifests_LoadsConsistentManifests()
        {
            Assert.That(ResourceManifestLoader.TryGetUpdateManifests(out var v1, out var v2, out var diff), Is.True);

            Assert.That(v1, Is.Not.Empty);
            Assert.That(diff, Is.Not.Empty);
            Assert.That(diff.All(v2.ContainsKey), Is.True);
            Assert.That(diff.Contains(ChartFile), Is.True);
        }

        /// <summary>Checks that manifests with a missing resource or an unexpected version are rejected.</summary>
        [Test]
        public void TryLoadHashManifest_RejectsInvalidManifests()
        {
            var missing = new object?[] { "ResourceUpdate/Missing", 1, null };
            var wrongVersion = new object?[] { "ResourceUpdate/V1ResourceHashes", 2, null };
            var notManifest = new object?[] { "Langs/en-US", 1, null };

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "TryLoadHashManifest", missing), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "TryLoadHashManifest", wrongVersion), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "TryLoadHashManifest", notManifest), Is.False);
            Assert.That((Dictionary<string, string>)wrongVersion[2]!, Is.Empty);
        }

        /// <summary>Checks that a diff manifest naming files missing from the v2 manifest is rejected.</summary>
        [Test]
        public void TryLoadDiffManifest_RequiresListedFiles()
        {
            var arguments = new object?[] { new Dictionary<string, string>(), null };

            var result = PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "TryLoadDiffManifest", arguments);

            Assert.That(result, Is.False);
            Assert.That((string[])arguments[1]!, Is.Empty);
        }

        /// <summary>Checks the normalization of manifest paths.</summary>
        /// <param name="path">The manifest path.</param>
        /// <param name="expected">The normalized path, or null when the path is rejected.</param>
        [TestCase("MaiCharts/a.txt", "MaiCharts/a.txt")]
        [TestCase("  Skins\\default\\a.png ", "Skins/default/a.png")]
        [TestCase("", null)]
        [TestCase("   ", null)]
        [TestCase("/rooted/a.txt", null)]
        [TestCase("C:/rooted/a.txt", null)]
        [TestCase("a/../b", null)]
        [TestCase("a//b", null)]
        [TestCase("./a", null)]
        [TestCase(null, null)]
        public void NormalizeRelativePath_ValidatesPaths(string? path, string? expected)
        {
            Assert.That(PrivateMember.InvokeStatic<string?>(typeof(ResourceManifestLoader), "NormalizeRelativePath", new object?[] { path }), Is.EqualTo(expected));
        }

        /// <summary>Checks the SHA-256 digest format check.</summary>
        [Test]
        public void IsSha256_RequiresLowercaseHex()
        {
            var valid = new string('a', 32) + new string('0', 32);

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "IsSha256", valid), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "IsSha256", valid.ToUpperInvariant()), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "IsSha256", valid.Substring(1)), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "IsSha256", new string('g', 64)), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ResourceManifestLoader), "IsSha256", new object?[] { null }), Is.False);
        }

        /// <summary>Checks that populated chart and skin roots are left untouched.</summary>
        [Test]
        public void RestoreManagedAssets_KeepsPopulatedRoots()
        {
            using var temporary = new TemporaryDirectory();
            using var environment = new MajEnvScope();
            environment.UseTemporaryLayout(temporary);
            MajEnv.ChartDirectory.CreateFile("user.txt");
            MajEnv.SkinDirectory.CreateFile("user.txt");

            DesktopResourceRestorer.RestoreManagedAssetsIfMissing();

            Assert.That(MajEnv.ChartDirectory.EnumerateEntries().Single().Name, Is.EqualTo("user.txt"));
            Assert.That(MajEnv.SkinDirectory.EnumerateEntries().Single().Name, Is.EqualTo("user.txt"));
        }

        /// <summary>Checks that an empty skin root is restored from the packaged resources while a populated chart root is kept.</summary>
        [Test]
        public void RestoreManagedAssets_RestoresEmptySkinRoot()
        {
            using var temporary = new TemporaryDirectory();
            using var environment = new MajEnvScope();
            environment.UseTemporaryLayout(temporary);
            MajEnv.ChartDirectory.CreateFile("user.txt");

            DesktopResourceRestorer.RestoreManagedAssetsIfMissing();

            Assert.That(MajEnv.ChartDirectory.EnumerateEntries().Count(), Is.EqualTo(1));
            Assert.That(MajEnv.SkinDirectory.FindFileAt("default/HoldSkins/hold.png"), Is.Not.Null);
        }

        /// <summary>Checks that restoring a group copies listed files with their nested paths and skips invalid entries.</summary>
        [Test]
        public void RestoreGroup_CopiesListedFiles()
        {
            using var temporary = new TemporaryDirectory();
            var destination = new StorageDirectory(temporary.Directory.FileSystem, System.IO.Path.Combine(temporary.Location, "Charts"));
            var manifest = new Dictionary<string, string>
            {
                { ChartFile, "unused" },
                { "MaiCharts/Missing/maidata.txt", "unused" },
                { "MaiCharts/", "unused" },
                { "Skins/default/HoldSkins/hold.png", "unused" },
            };

            PrivateMember.InvokeStatic(typeof(DesktopResourceRestorer), "RestoreGroup", "MaiCharts/", destination, manifest);

            Assert.That(destination.FindFileAt("Original/EX-AID/maidata.txt"), Is.Not.Null);
            Assert.That(destination.FindDirectory("Missing"), Is.Null);
            Assert.That(destination.FindFileAt("default/HoldSkins/hold.png"), Is.Null);
        }
    }
}

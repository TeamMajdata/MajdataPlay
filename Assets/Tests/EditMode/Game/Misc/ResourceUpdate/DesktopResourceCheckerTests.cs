using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.ResourceUpdate;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.ResourceUpdate
{
    /// <summary>Covers the restore of empty chart and skin roots by <see cref="DesktopResourceChecker"/> from the bundled resources.</summary>
    public class DesktopResourceCheckerTests
    {
        /// <summary>A chart file listed in the bundled v2 manifest.</summary>
        private const string ChartFile = "MaiCharts/Original/EX-AID/maidata.txt";

        /// <summary>Checks that populated chart and skin roots are left untouched and nothing is reported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CheckAsync_KeepsPopulatedRoots()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var temporary = new TemporaryDirectory();
                using var environment = new MajEnvScope();
                environment.UseTemporaryLayout(temporary);
                MajEnv.ChartDirectory.CreateFile("user.txt");
                MajEnv.SkinDirectory.CreateFile("user.txt");
                var reports = new List<ProgressReport>();

                await DesktopResourceChecker.CheckAsync(new ProgressReporter(reports.Add), new FrameBudget(), CancellationToken.None);

                Assert.That(MajEnv.ChartDirectory.EnumerateEntries().Single().Name, Is.EqualTo("user.txt"));
                Assert.That(MajEnv.SkinDirectory.EnumerateEntries().Single().Name, Is.EqualTo("user.txt"));
                Assert.That(reports, Is.Empty);
            });
        }

        /// <summary>Checks that an empty skin root is restored with its progress reported, while a populated chart root is kept.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CheckAsync_RestoresEmptySkinRootAndReportsProgress()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var temporary = new TemporaryDirectory();
                using var environment = new MajEnvScope();
                environment.UseTemporaryLayout(temporary);
                MajEnv.ChartDirectory.CreateFile("user.txt");
                var reports = new List<ProgressReport>();

                await DesktopResourceChecker.CheckAsync(new ProgressReporter(reports.Add), new FrameBudget(), CancellationToken.None);

                Assert.That(MajEnv.ChartDirectory.EnumerateEntries().Count(), Is.EqualTo(1));
                Assert.That(MajEnv.SkinDirectory.FindFileAt("default/HoldSkins/hold.png"), Is.Not.Null);
                Assert.That(ResourceManifestLoader.TryGetV2Hashes(out var hashes), Is.True);
                var skinCount = hashes.Keys.Count(path => path.StartsWith("Skins/"));
                Assert.That(reports.Select(report => report.Message).Distinct().Count(), Is.EqualTo(1));
                Assert.That(reports.Count, Is.EqualTo(skinCount + 1));
                Assert.That(reports[0].Completed, Is.EqualTo(0));
                Assert.That(reports[reports.Count - 1].Completed, Is.EqualTo(skinCount));
                Assert.That(reports.All(report => report.Total == skinCount), Is.True);
            });
        }

        /// <summary>Checks that restoring a group copies listed files with their nested paths and skips invalid entries.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RestoreGroupAsync_CopiesListedFiles()
        {
            return UniTask.ToCoroutine(async () =>
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
                var progress = new ProgressReporter(_ => { });

                var task = PrivateMember.InvokeStatic<System.Threading.Tasks.ValueTask>(typeof(DesktopResourceChecker), "RestoreGroupAsync",
                    "MaiCharts/", destination, manifest, "Restoring", progress, new FrameBudget(), CancellationToken.None);
                await task;

                Assert.That(destination.FindFileAt("Original/EX-AID/maidata.txt"), Is.Not.Null);
                Assert.That(destination.FindDirectory("Missing"), Is.Null);
                Assert.That(destination.FindFileAt("default/HoldSkins/hold.png"), Is.Null);
            });
        }
    }
}

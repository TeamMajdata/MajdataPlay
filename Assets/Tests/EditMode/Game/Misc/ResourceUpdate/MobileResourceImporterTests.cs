using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MajdataPlay.ResourceUpdate;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.ResourceUpdate
{
    /// <summary>
    /// Runs the mobile first-start extraction of <see cref="MobileResourceImporter"/> and the later
    /// <see cref="MobileResourceChecker"/> against the real bundled manifests and streaming assets in a temporary layout.
    /// </summary>
    /// <remarks>
    /// The detailed hash gate, customization, pending-move and cross-backend cases run against controlled manifests in
    /// <c>Tools/Tests/Modules/ResourceStorageValidation</c>.
    /// </remarks>
    public class MobileResourceImporterTests
    {
        /// <summary>A sound effect listed in the bundled v2 manifest, outside the player-managed roots.</summary>
        private const string SoundEffectPrefix = "SFX/";

        /// <summary>Checks that extraction commits every bundled resource, moves the charts and skin, and reports each file; then that the later check restores a deleted resource.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportAsync_ThenCheckAsync_ExtractsAndRestoresBundledResources()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var temporary = new TemporaryDirectory();
                using var environment = new MajEnvScope();
                environment.UseTemporaryLayout(temporary);
                Assert.That(ResourceManifestLoader.TryGetV2Hashes(out var hashes), Is.True);
                var reports = new List<ProgressReport>();
                var mainThreadId = Thread.CurrentThread.ManagedThreadId;
                var progress = new ProgressReporter(report =>
                {
                    Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThreadId), "Loading UI must be updated on the Unity main thread.");
                    reports.Add(report);
                });

                var imported = await MobileResourceImporter.ImportAsync(progress, new FrameBudget(), CancellationToken.None);

                Assert.That(imported, Is.True);
                Assert.That(MajEnv.AssetsDirectory.Exists, Is.True);
                Assert.That(MajEnv.ChartDirectory.FindDirectory("Original"), Is.Not.Null);
                Assert.That(MajEnv.SkinDirectory.FindDirectory("default"), Is.Not.Null);
                Assert.That(MajEnv.AssetsDirectory.FindDirectoryAt(MobileManagedAssetMover.BundledChartsRelativePath), Is.Null);
                Assert.That(MajEnv.AssetsDirectory.FindFile(MobileManagedAssetMover.PendingMovesMarkerName), Is.Null);
                var extraction = reports.Where(report => report.Total == hashes.Count).ToArray();
                Assert.That(extraction.Length, Is.EqualTo(hashes.Count + 1));
                Assert.That(extraction.Select(report => report.Completed), Is.EqualTo(Enumerable.Range(0, hashes.Count + 1)));
                Assert.That(extraction[extraction.Length - 1].Completed, Is.EqualTo(hashes.Count));
                Assert.That(reports[reports.Count - 1].HasTotal, Is.False, "installing the charts and skin is the last step");

                var soundEffect = hashes.Keys.First(path => path.StartsWith(SoundEffectPrefix));
                MajEnv.AssetsDirectory.FindFileAt(soundEffect)!.Delete();
                reports.Clear();

                await MobileResourceChecker.CheckAsync(progress, new FrameBudget(), CancellationToken.None);

                Assert.That(MajEnv.AssetsDirectory.FindFileAt(soundEffect), Is.Not.Null);
                Assert.That(reports[reports.Count - 1].Completed, Is.EqualTo(hashes.Count));
                Assert.That(reports[reports.Count - 1].Total, Is.EqualTo(hashes.Count));
            });
        }

        /// <summary>Checks that cancellation from a progress callback drains workers before an immediate extraction retry.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportAsync_CancelledFromProgress_DoesNotCommitAndCanRetry()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var temporary = new TemporaryDirectory();
                using var environment = new MajEnvScope();
                using var cancellation = new CancellationTokenSource();
                environment.UseTemporaryLayout(temporary);
                var cancelled = false;
                var progress = new ProgressReporter(report =>
                {
                    if (report.HasTotal && report.Completed > 0)
                    {
                        cancellation.Cancel();
                    }
                });

                try
                {
                    await MobileResourceImporter.ImportAsync(progress, new FrameBudget(), cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                Assert.That(cancelled, Is.True);
                Assert.That(MajEnv.AssetsDirectory.Exists, Is.False, "A cancelled extraction must not commit its staging directory.");
                Assert.That(MajEnv.ChartDirectory.FindDirectory("Original"), Is.Null);
                Assert.That(MajEnv.SkinDirectory.FindDirectory("default"), Is.Null);

                var imported = await MobileResourceImporter.ImportAsync(
                    new ProgressReporter(_ =>
                    {
                    }), new FrameBudget(), CancellationToken.None);

                Assert.That(imported, Is.True, "An immediate retry must not race workers from the cancelled extraction.");
                Assert.That(MajEnv.ChartDirectory.FindDirectory("Original"), Is.Not.Null);
                Assert.That(MajEnv.SkinDirectory.FindDirectory("default"), Is.Not.Null);
            });
        }
    }
}

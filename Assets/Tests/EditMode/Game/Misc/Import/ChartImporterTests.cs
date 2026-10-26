using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Import;
using MajdataPlay.IO.Storage;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Misc.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Import
{
    /// <summary>
    /// Covers the archive and directory import layouts of <see cref="ChartImporter"/> with temporary chart roots. The scene
    /// switching driver of the importer is not exercised; the import steps are invoked directly.
    /// </summary>
    public class ChartImporterTests
    {
        /// <summary>The temporary storage for the test.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The scope restoring the application directories.</summary>
        private MajEnvScope _environment = null!;

        /// <summary>The objects created by the test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The scene switcher instance before the test.</summary>
        private SceneSwitcher? _savedSwitcher;

        /// <summary>The import root before the test.</summary>
        private StorageDirectory? _savedImportRoot;

        /// <summary>The loading text shown by the fake scene switcher.</summary>
        private TextMeshProUGUI _loadingText = null!;

        /// <summary>The chart root of the temporary layout.</summary>
        private StorageDirectory _chartRoot = null!;

        /// <summary>The import collection of the temporary layout.</summary>
        private StorageDirectory _importRoot = null!;

        /// <summary>Creates a temporary layout and a scene switcher that only records loading text.</summary>
        [SetUp]
        public void SetUp()
        {
            _temporary = new TemporaryDirectory();
            _environment = new MajEnvScope();
            _environment.UseTemporaryLayout(_temporary);
            _chartRoot = MajEnv.ChartDirectory;
            _importRoot = _chartRoot.CreateDirectory("Import");
            _objects = new TestObjects();
            _savedSwitcher = Majdata<SceneSwitcher>.Instance;
            _savedImportRoot = PrivateMember.GetStaticField<StorageDirectory?>(typeof(ChartImporter), "s_importRoot");
            var switcher = _objects.Add<SceneSwitcher>();
            _loadingText = _objects.Add<TextMeshProUGUI>("Loading");
            PrivateMember.SetField(switcher, "_loadingText", _loadingText);
            Majdata<SceneSwitcher>.Free();
            Majdata<SceneSwitcher>.Instance = switcher;
            PrivateMember.SetStaticField(typeof(ChartImporter), "s_importRoot", _importRoot);
            DrainQueue();
        }

        /// <summary>Restores the importer, scene switcher and directories.</summary>
        [TearDown]
        public void TearDown()
        {
            DrainQueue();
            PrivateMember.SetStaticField(typeof(ChartImporter), "s_importRoot", _savedImportRoot);
            Majdata<SceneSwitcher>.Free();
            if (_savedSwitcher is not null)
            {
                Majdata<SceneSwitcher>.Instance = _savedSwitcher;
            }
            _objects.Dispose();
            _environment.Dispose();
            _temporary.Dispose();
        }

        /// <summary>Gets the number of queued imports.</summary>
        /// <returns>The queue length.</returns>
        private static int QueueCount()
        {
            var queue = PrivateMember.GetStaticField<ICollection>(typeof(ChartImporter), "s_pendingImportTasks");
            return queue.Count;
        }

        /// <summary>Removes every queued import.</summary>
        private static void DrainQueue()
        {
            var queue = PrivateMember.GetStaticField<object>(typeof(ChartImporter), "s_pendingImportTasks");
            var tryDequeue = queue.GetType().GetMethod("TryDequeue")!;
            var arguments = new object?[] { null };
            while ((bool)tryDequeue.Invoke(queue, arguments)!)
            {
            }
        }

        /// <summary>Invokes a private import step and waits for it.</summary>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The method arguments.</param>
        /// <returns>The completed step.</returns>
        private static async UniTask InvokeStepAsync(string name, params object?[] arguments)
        {
            var step = (ValueTask)PrivateMember.InvokeStatic(typeof(ChartImporter), name, arguments)!;
            await step;
        }

        /// <summary>Creates a ZIP archive file from name/content pairs.</summary>
        /// <param name="name">The archive file name.</param>
        /// <param name="entries">The entry names and their text contents; null content creates a directory entry.</param>
        /// <returns>The archive entry.</returns>
        private FileSystemEntry CreateArchive(string name, params (string Name, string? Content)[] entries)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                foreach (var (entryName, content) in entries)
                {
                    var entry = archive.CreateEntry(entryName);
                    if (content is null)
                    {
                        continue;
                    }
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write(content);
                }
            }
            var file = _temporary.Directory.CreateFile(name);
            file.WriteAllBytes(memory.ToArray());
            return FileSystem.GetEntry(file.Location)!;
        }

        /// <summary>Checks that an archive containing one chart at its root is imported into the Import collection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportArchive_RootChart()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var archive = CreateArchive("Song.zip", ("maidata.txt", ChartFixture.CreateMaidata("Song")), ("track.mp3", "audio"), ("bg.png", "cover"));

                await InvokeStepAsync("ImportArchiveAsync", archive, false);

                var chart = _importRoot.FindDirectory("Song");
                Assert.That(chart, Is.Not.Null);
                Assert.That(chart!.FindFile("maidata.txt"), Is.Not.Null);
                Assert.That(chart.FindFile("track.mp3")!.ReadAllText(), Is.EqualTo("audio"));
                Assert.That(chart.FindFile("bg.png"), Is.Not.Null);
                Assert.That(MajEnv.TempDirectory.EnumerateEntries(), Is.Empty);
                Assert.That(_loadingText.text, Does.StartWith("Importing..."));
                Assert.That(_temporary.Directory.FindFile("Song.zip"), Is.Not.Null);
            });
        }

        /// <summary>Checks that chart folders directly inside an archive go to the Import collection and grouped charts to a named collection.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportArchive_ChartAndCollectionLayouts()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var archive = CreateArchive("Pack.adx",
                    ("Single/", null),
                    ("Single/maidata.txt", ChartFixture.CreateMaidata("Single")),
                    ("Single/track.ogg", "a"),
                    ("Pack/C1/maidata.txt", ChartFixture.CreateMaidata("C1")),
                    ("Pack/C1/track.mp3", "b"),
                    ("Pack/C2/maidata.txt", ChartFixture.CreateMaidata("C2")),
                    ("Pack/C2/TRACK.WAV", "c"),
                    ("Pack/NotAChart/readme.txt", "d"),
                    ("Empty/readme.txt", "e"),
                    (".hidden/maidata.txt", ChartFixture.CreateMaidata("Hidden")),
                    (".hidden/track.mp3", "f"));

                await InvokeStepAsync("ImportArchiveAsync", archive, false);

                Assert.That(_importRoot.FindDirectory("Single"), Is.Not.Null);
                var pack = _chartRoot.FindDirectory("Pack");
                Assert.That(pack, Is.Not.Null);
                Assert.That(pack!.FindDirectory("C1"), Is.Not.Null);
                Assert.That(pack.FindDirectory("C2"), Is.Not.Null);
                Assert.That(pack.FindDirectory("NotAChart"), Is.Null);
                Assert.That(_chartRoot.FindDirectory("Empty"), Is.Null);
                Assert.That(_importRoot.FindDirectory(".hidden"), Is.Null);
            });
        }

        /// <summary>Checks that an archive entry escaping the extraction directory aborts the import and leaves no files behind.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportArchive_RejectsTraversal()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var archive = CreateArchive("Evil.zip", ("maidata.txt", ChartFixture.CreateMaidata("Evil")), ("../escaped.txt", "x"));
                var failure = default(Exception);

                try
                {
                    await InvokeStepAsync("ImportArchiveAsync", archive, false);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                Assert.That(failure, Is.InstanceOf<IOException>());
                Assert.That(MajEnv.TempDirectory.EnumerateEntries(), Is.Empty);
                Assert.That(MajEnv.CacheDirectory.FindFile("escaped.txt"), Is.Null);
                Assert.That(_importRoot.EnumerateEntries(), Is.Empty);
            });
        }

        /// <summary>Checks that unsupported or vanished archives are skipped.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportArchive_SkipsUnsupportedAndMissingFiles()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var unsupported = CreateArchive("Song.rar", ("maidata.txt", ChartFixture.CreateMaidata("Song")), ("track.mp3", "a"));
                var vanished = CreateArchive("Gone.zip", ("maidata.txt", ChartFixture.CreateMaidata("Gone")), ("track.mp3", "a"));
                _temporary.Directory.FindFile("Gone.zip")!.Delete();

                await InvokeStepAsync("ImportArchiveAsync", unsupported, false);
                await InvokeStepAsync("ImportArchiveAsync", vanished, false);

                Assert.That(_importRoot.EnumerateEntries(), Is.Empty);
                Assert.That(MajEnv.TempDirectory.EnumerateEntries(), Is.Empty);
            });
        }

        /// <summary>Checks that importing the same chart twice keeps both copies under distinct names.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportDirectory_ChartDirectoryIsCopiedUniquely()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var source = ChartFixture.CreateChart(_temporary.Directory, "Source", "Source");
                var entry = FileSystem.GetEntry(source.Location)!;

                await InvokeStepAsync("ImportDirectoryAsync", entry);
                await InvokeStepAsync("ImportDirectoryAsync", entry);
                await InvokeStepAsync("ImportDirectoryAsync", entry);

                Assert.That(_importRoot.FindDirectory("Source"), Is.Not.Null);
                Assert.That(_importRoot.FindDirectory("Source (1)"), Is.Not.Null);
                Assert.That(_importRoot.FindDirectory("Source (2)")!.FindFile("track.mp3"), Is.Not.Null);
                Assert.That(source.Exists, Is.True);
            });
        }

        /// <summary>Checks that a queued folder holding charts and collections follows the archive layout rules.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ImportDirectory_ContainerLayout()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var container = _temporary.Directory.CreateDirectory("Container");
                ChartFixture.CreateChart(container, "Loose", "Loose");
                var collection = container.CreateDirectory("Collection");
                ChartFixture.CreateChart(collection, "Inner", "Inner");
                ChartFixture.CreateChart(collection, "NoTrack", "NoTrack", trackName: null);
                ChartFixture.CreateChart(container, ".dot", "Dot");
                _chartRoot.CreateDirectory("Collection").CreateDirectory("Existing");

                await InvokeStepAsync("ImportDirectoryAsync", FileSystem.GetEntry(container.Location)!);

                Assert.That(_importRoot.FindDirectory("Loose"), Is.Not.Null);
                var target = _chartRoot.FindDirectory("Collection")!;
                Assert.That(target.FindDirectory("Inner"), Is.Not.Null);
                Assert.That(target.FindDirectory("Existing"), Is.Not.Null);
                Assert.That(target.FindDirectory("NoTrack"), Is.Null);
                Assert.That(_importRoot.FindDirectory(".dot"), Is.Null);
            });
        }

        /// <summary>Checks the chart directory detection rules.</summary>
        [Test]
        public void IsChartDirectory_RequiresChartAndTrack()
        {
            var complete = ChartFixture.CreateChart(_temporary.Directory, "Complete", "Complete", trackName: "Track.Opus");
            var noTrack = ChartFixture.CreateChart(_temporary.Directory, "NoTrack", "NoTrack", trackName: null);
            var noChart = ChartFixture.CreateChart(_temporary.Directory, "NoChart", "NoChart", withMaidata: false);
            noChart.CreateDirectory("maidata.txt");

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsChartDirectory", complete), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsChartDirectory", noTrack), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsChartDirectory", noChart), Is.False);
        }

        /// <summary>Checks that hidden and dot-prefixed entries are excluded.</summary>
        [Test]
        public void IsExcluded_SkipsHiddenEntries()
        {
            var visible = new FileSystemEntry("C:/x/Visible", "Visible", true);
            var dotted = new FileSystemEntry("C:/x/.git", ".git", true);
            var hidden = new FileSystemEntry("C:/x/Hidden", "Hidden", true, isHidden: true);
            var system = new FileSystemEntry("C:/x/System", "System", true, isSystem: true);

            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsExcluded", visible), Is.False);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsExcluded", dotted), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsExcluded", hidden), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(ChartImporter), "IsExcluded", system), Is.True);
        }

        /// <summary>Checks that existing paths are queued and missing paths are ignored.</summary>
        [Test]
        public void AddImportTask_QueuesExistingEntries()
        {
            var file = _temporary.WriteText("queued.zip", "x");

            ChartImporter.AddImportTask(file.Location);
            ChartImporter.AddImportTask(Path.Combine(_temporary.Location, "missing.zip"));

            Assert.That(QueueCount(), Is.EqualTo(1));
        }

        /// <summary>Checks that the update hook does not start an import while the empty scene is active or nothing is queued.</summary>
        [Test]
        public void OnUpdate_WaitsForQueueAndScene()
        {
            var savedScene = SceneSwitcher.CurrentScene;
            try
            {
                ChartImporter.OnUpdate();
                Assert.That(PrivateMember.GetStaticField<ValueTask>(typeof(ChartImporter), "s_importTask").IsCompleted, Is.True);

                PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.CurrentScene), MajScenes.Empty);
                ChartImporter.AddImportTask(_temporary.WriteText("queued.zip", "x").Location);
                ChartImporter.OnUpdate();

                Assert.That(QueueCount(), Is.EqualTo(1));
                Assert.That(PrivateMember.GetStaticField<ValueTask>(typeof(ChartImporter), "s_importTask").IsCompleted, Is.True);
            }
            finally
            {
                PrivateMember.SetStaticProperty(typeof(SceneSwitcher), nameof(SceneSwitcher.CurrentScene), savedScene);
            }
        }

        /// <summary>Checks that startup completes immediately when nothing is queued.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnStartAsync_WithoutQueue_Completes()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var task = ChartImporter.OnStartAsync();

                Assert.That(task.IsCompleted, Is.True);
                await task;
            });
        }

        /// <summary>Checks that cleanup helpers ignore missing targets.</summary>
        [Test]
        public void CleanupHelpers_IgnoreMissingTargets()
        {
            var missingDirectory = new StorageDirectory(_temporary.Directory.FileSystem, Path.Combine(_temporary.Location, "missing"));
            var existing = _temporary.Directory.CreateDirectory("existing");
            existing.CreateFile("file.txt");
            var file = _temporary.WriteText("delete.txt", "x");

            Assert.DoesNotThrow(() => PrivateMember.InvokeStatic(typeof(ChartImporter), "TryDeleteDirectory", missingDirectory));
            PrivateMember.InvokeStatic(typeof(ChartImporter), "TryDeleteDirectory", existing);
            Assert.DoesNotThrow(() => PrivateMember.InvokeStatic(typeof(ChartImporter), "TryDeleteFile", Path.Combine(_temporary.Location, "missing.txt")));
            PrivateMember.InvokeStatic(typeof(ChartImporter), "TryDeleteFile", file.Location);

            Assert.That(existing.Exists, Is.False);
            Assert.That(file.Exists, Is.False);
        }
    }
}

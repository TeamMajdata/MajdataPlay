using System;
using System.Linq;
using MajdataPlay.Buffers;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Settings;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Utils
{
    /// <summary>Checks the predefined judge queues of <see cref="SlideTables"/> and the disposable tables they build.</summary>
    public class SlideTablesTests
    {
        /// <summary>Every slide prefab name that has a predefined judge queue.</summary>
        private static readonly string[] s_tableNames =
        {
            "circle1", "circle2", "circle3", "circle4", "circle5", "circle6", "circle7", "circle8",
            "line3", "line4", "line5", "line6", "line7",
            "v1", "v2", "v3", "v4", "v6", "v7", "v8",
            "ppqq1", "ppqq2", "ppqq3", "ppqq4", "ppqq5", "ppqq6", "ppqq7", "ppqq8",
            "L2", "L3", "L4", "L5", "s",
            "pq1", "pq2", "pq3", "pq4", "pq5", "pq6", "pq7", "pq8",
        };

        /// <summary>Restores the settings changed by the wifi tests.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Installs default settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _settings = new GamePlaySettingsScope();
        }

        /// <summary>Restores the previous settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _settings.Dispose();
        }

        /// <summary>Checks that every predefined table starts at A1, ends with a final area and has positive constants.</summary>
        [Test]
        public void FindTableByName_BuildsEveryPredefinedTable()
        {
            foreach (var name in s_tableNames)
            {
                using var table = SlideTables.FindTableByName(name);
                Assert.That(table, Is.Not.Null, name);
                var queue = table!.JudgeQueue.Span;
                Assert.That(table.Name, Is.EqualTo(name));
                Assert.That(queue.Length, Is.GreaterThanOrEqualTo(2), name);
                Assert.That(queue[0].IncludedAreas[0], Is.EqualTo(SensorArea.A1), name);
                Assert.That(table.Const, Is.GreaterThan(0f), name);
                Assert.That(table.ClassicConst, Is.GreaterThan(0f), name);
                var last = queue[queue.Length - 1];
                last.Check(last.IncludedAreas[0], SwitchStatus.On);
                Assert.That(last.IsFinished, Is.True, $"{name} ends with a final area");
                var previousProgress = -1;
                for (var i = 0; i < queue.Length; i++)
                {
                    Assert.That(queue[i].ArrowProgressWhenFinished, Is.GreaterThanOrEqualTo(queue[i].ArrowProgressWhenOn), $"{name}[{i}]");
                    Assert.That(queue[i].ArrowProgressWhenOn, Is.GreaterThan(previousProgress), $"{name}[{i}] progress increases");
                    previousProgress = queue[i].ArrowProgressWhenOn;
                }
            }
        }

        /// <summary>Checks the exact content of the circle2 table, including its non-skippable head.</summary>
        [Test]
        public void FindTableByName_Circle2HasTheExpectedQueue()
        {
            using var table = SlideTables.FindTableByName("circle2")!;
            var queue = table.JudgeQueue.Span;

            Assert.That(queue.Length, Is.EqualTo(2));
            Assert.That(queue[0].IsSkippable, Is.False);
            Assert.That(queue[0].ArrowProgressWhenOn, Is.EqualTo(0));
            Assert.That(queue[0].ArrowProgressWhenFinished, Is.EqualTo(3));
            Assert.That(queue[1].IncludedAreas[0], Is.EqualTo(SensorArea.A2));
            Assert.That(queue[1].IsSkippable, Is.True);
            Assert.That(table.Const, Is.EqualTo(0.465f));
            Assert.That(table.ClassicConst, Is.EqualTo(0.5293f));
        }

        /// <summary>Checks that unknown names have no table.</summary>
        [Test]
        public void FindTableByName_ReturnsNullForUnknownNames()
        {
            Assert.That(SlideTables.FindTableByName("wifi"), Is.Null);
            Assert.That(SlideTables.FindTableByName("Ex"), Is.Null);
            Assert.That(SlideTables.FindTableByName(string.Empty), Is.Null);
        }

        /// <summary>Checks that every build returns an independent table.</summary>
        [Test]
        public void FindTableByName_BuildsIndependentTables()
        {
            using var first = SlideTables.FindTableByName("line3")!;
            using var second = SlideTables.FindTableByName("line3")!;

            first.Diff(1);

            Assert.That(first.JudgeQueue.Span[0].IncludedAreas[0], Is.EqualTo(SensorArea.A2));
            Assert.That(second.JudgeQueue.Span[0].IncludedAreas[0], Is.EqualTo(SensorArea.A1));
        }

        /// <summary>Checks mirroring, rotation and disposal of a slide table.</summary>
        [Test]
        public void SlideTable_MirrorDiffAndDispose()
        {
            var table = SlideTables.FindTableByName("circle3")!;

            table.Mirror();
            Assert.That(table.JudgeQueue.Span.ToArray().Select(x => x.IncludedAreas[0]), Is.EqualTo(new[] { SensorArea.A1, SensorArea.A8, SensorArea.A7 }));

            table.Diff(3);
            Assert.That(table.JudgeQueue.Span.ToArray().Select(x => x.IncludedAreas[0]), Is.EqualTo(new[] { SensorArea.A4, SensorArea.A3, SensorArea.A2 }));

            table.Dispose();
            table.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = table.JudgeQueue);
            Assert.Throws<ObjectDisposedException>(() => table.Mirror());
            Assert.Throws<ObjectDisposedException>(() => table.Diff(1));
            Assert.Throws<ObjectDisposedException>(() => table.ThrowIfDisposed());
        }

        /// <summary>Checks a table created directly from a rented array.</summary>
        [Test]
        public void SlideTable_WrapsARentedArray()
        {
            var rented = Pool<SlideArea>.Rent(4, true);
            rented[0] = new SlideArea(new[] { (SensorArea.C, true) }, 2);
            var table = new SlideTable(rented, 1)
            {
                Name = "custom",
                Const = 0.5f,
                ClassicConst = 0.25f
            };

            Assert.That(table.JudgeQueue.Length, Is.EqualTo(1));
            Assert.That(table.Name, Is.EqualTo("custom"));
            Assert.That(table.Const, Is.EqualTo(0.5f));
            Assert.That(table.ClassicConst, Is.EqualTo(0.25f));
            table.Dispose();
        }

        /// <summary>Checks the modern wifi queues starting at A1 and rotated for another start key.</summary>
        [Test]
        public void GetWifiTable_ModernQueuesRotateWithTheStartKey()
        {
            _settings.Settings.Judge.Mode = JudgeModeOption.Modern;

            using var atA1 = SlideTables.GetWifiTable(1);
            using var atA3 = SlideTables.GetWifiTable(3);

            Assert.That(atA1.Name, Is.EqualTo("wifi"));
            Assert.That(atA1.Const, Is.EqualTo(0.162870f));
            Assert.That(FirstAreas(atA1.Left), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B8, SensorArea.B7, SensorArea.A6 }));
            Assert.That(FirstAreas(atA1.Center), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B1, SensorArea.C, SensorArea.A5 }));
            Assert.That(FirstAreas(atA1.Right), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B2, SensorArea.B3, SensorArea.A4 }));
            Assert.That(atA1.Left.Span[3].IncludedAreas.ToArray(), Is.EqualTo(new[] { SensorArea.A6, SensorArea.D6 }));
            Assert.That(FirstAreas(atA3.Center), Is.EqualTo(new[] { SensorArea.A3, SensorArea.B3, SensorArea.C, SensorArea.A7 }));
        }

        /// <summary>Checks that the classic wifi centre queue ends at C.</summary>
        [Test]
        public void GetWifiTable_ClassicCenterEndsAtC()
        {
            _settings.Settings.Judge.Mode = JudgeModeOption.Classic;

            using var table = SlideTables.GetWifiTable(1);

            Assert.That(table.Const, Is.EqualTo(0.2106f));
            Assert.That(FirstAreas(table.Center), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B1, SensorArea.C }));
        }

        /// <summary>Checks mirroring, rotation and disposal of a wifi table.</summary>
        [Test]
        public void WifiTable_MirrorDiffAndDispose()
        {
            var table = SlideTables.GetWifiTable(1);

            table.Mirror();
            Assert.That(FirstAreas(table.Left), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B2, SensorArea.B3, SensorArea.A4 }));
            Assert.That(FirstAreas(table.Right), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B8, SensorArea.B7, SensorArea.A6 }));

            table.Diff(1);
            Assert.That(FirstAreas(table.Center), Is.EqualTo(new[] { SensorArea.A2, SensorArea.B2, SensorArea.C, SensorArea.A6 }));

            table.Dispose();
            table.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = table.Left);
            Assert.Throws<ObjectDisposedException>(() => _ = table.Center);
            Assert.Throws<ObjectDisposedException>(() => _ = table.Right);
            Assert.Throws<ObjectDisposedException>(() => table.Mirror());
            Assert.Throws<ObjectDisposedException>(() => table.Diff(1));
        }

        /// <summary>Gets the first sensor of every area in a queue.</summary>
        /// <param name="queue">The queue.</param>
        /// <returns>The first sensors in queue order.</returns>
        private static SensorArea[] FirstAreas(Memory<SlideArea> queue)
        {
            var span = queue.Span;
            var result = new SensorArea[span.Length];
            for (var i = 0; i < span.Length; i++)
            {
                result[i] = span[i].IncludedAreas[0];
            }
            return result;
        }
    }
}

using System;
using System.Collections;
using System.Threading.Tasks;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.List;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the chart density graph and its loading, help and error icons in <see cref="ChartVisualDisplayer"/>.</summary>
    public class ChartVisualDisplayerPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings and the timeline delta.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Restores the registered displayer.</summary>
        private SingletonScope<ChartVisualDisplayer> _slot = null!;

        /// <summary>The loading, help and error icons.</summary>
        private GameObject[] _icons = null!;

        /// <summary>Installs default settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _slot = new SingletonScope<ChartVisualDisplayer>();
        }

        /// <summary>Destroys every object and restores the registration and settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _slot.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that Awake registers the displayer and that a new chart shows the loading icon.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSimaiChart_ShowsLoadingUntilAnalyzed()
        {
            var displayer = CreateDisplayer(out var image);
            Assert.That(Majdata<ChartVisualDisplayer>.Instance, Is.SameAs(displayer));

            displayer.SetSimaiChart(SimaiChart.Empty);

            AssertIcons(loading: true, help: false, error: false);
            Assert.That(image.texture, Is.Not.Null);
            yield return null;
        }

        /// <summary>Checks that an empty chart shows the help icon after analysis.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator EmptyChart_ShowsTheHelpIcon()
        {
            var displayer = CreateDisplayer(out var image);
            displayer.SetSimaiChart(SimaiChart.Empty);
            var empty = image.texture;

            yield return null;
            yield return null;

            AssertIcons(loading: false, help: true, error: false);
            Assert.That(image.texture, Is.SameAs(empty));
        }

        /// <summary>Checks that a chart with notes is drawn as a graph texture.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Chart_IsDrawnAsAGraph()
        {
            var displayer = CreateDisplayer(out var image);
            displayer.SetSimaiChart(SimaiParser.ParseChart("(120){4}1,2,3h[4:1],B1,1-5[8:1],E"), 4f);
            var empty = image.texture;

            yield return null;
            yield return null;

            AssertIcons(loading: false, help: false, error: false);
            Assert.That(image.texture, Is.Not.SameAs(empty));
            Assert.That(image.texture, Is.Not.Null);
            _objects.Track(image.texture);
        }

        /// <summary>Checks that the maidata of a song is loaded after the delay and its selected chart analyzed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_LoadsTheChartAfterTheDelay()
        {
            var displayer = CreateDisplayer(out _);
            var song = new GamePlayFakeSongDetail { Maidata = new SimaiFile("Fake", "Fake", 0, "hash", Array.Empty<SimaiChart>(), null) };

            displayer.SetSongDeatil(song, ChartLevel.Basic, 0, null, 400);
            RemoveBackgroundTasks();
            AssertIcons(loading: true, help: false, error: false);

            _settings.SetDeltaTime(0.1f);
            yield return null;
            AssertIcons(loading: true, help: false, error: false);

            _settings.SetDeltaTime(1f);
            yield return null;
            yield return null;

            Assert.That(song.MaidataRequestCount, Is.EqualTo(1));
            AssertIcons(loading: false, help: true, error: false);
        }

        /// <summary>Checks that a failed maidata load shows the error icon.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetSongDetail_ShowsTheErrorIconWhenLoadingFails()
        {
            var displayer = CreateDisplayer(out _);
            var song = new GamePlayFakeSongDetail();

            displayer.SetSongDeatil(song, ChartLevel.Expert);
            var task = RemoveBackgroundTasks();
            yield return null;
            yield return null;

            AssertIcons(loading: false, help: false, error: true);
            Assert.That(task?.Exception, Is.Not.Null, "The failure is observed");
        }

        /// <summary>Checks that destroying the displayer unregisters it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_UnregistersTheDisplayer()
        {
            var displayer = CreateDisplayer(out _);
            yield return null;

            UnityEngine.Object.Destroy(displayer.gameObject);
            yield return null;

            Assert.That(Majdata<ChartVisualDisplayer>.Instance, Is.Null);
        }

        /// <summary>Removes the maidata tasks the displayer registered as list background tasks.</summary>
        /// <returns>The last removed task, or null when none was registered.</returns>
        private static Task? RemoveBackgroundTasks()
        {
            var tasks = ListManager.AllBackgroundTasks;
            Task? last = null;
            if (tasks.Count != 0)
            {
                last = tasks[tasks.Count - 1];
                tasks.RemoveAt(tasks.Count - 1);
            }
            return last;
        }

        /// <summary>Asserts which status icon is shown.</summary>
        /// <param name="loading">Whether the loading icon is active.</param>
        /// <param name="help">Whether the help icon is active.</param>
        /// <param name="error">Whether the error icon is active.</param>
        private void AssertIcons(bool loading, bool help, bool error)
        {
            Assert.That(_icons[0].activeSelf, Is.EqualTo(loading), "Loading icon");
            Assert.That(_icons[1].activeSelf, Is.EqualTo(help), "Help icon");
            Assert.That(_icons[2].activeSelf, Is.EqualTo(error), "Error icon");
        }

        /// <summary>Creates an active displayer with a sized raw image and its three status icons.</summary>
        /// <param name="image">Receives the raw image that shows the graph.</param>
        /// <returns>The displayer, awakened by Unity.</returns>
        private ChartVisualDisplayer CreateDisplayer(out RawImage image)
        {
            var root = _objects.Create("ChartVisual");
            root.SetActive(false);
            image = root.AddComponent<RawImage>();
            image.rectTransform.sizeDelta = new Vector2(256, 64);
            var icons = _objects.Create("Icons", root.transform);
            _icons = new[]
            {
                _objects.Create("Loading", icons.transform),
                _objects.Create("Help", icons.transform),
                _objects.Create("Error", icons.transform),
            };
            var displayer = root.AddComponent<ChartVisualDisplayer>();
            PrivateMember.SetField(displayer, "_iconPrefab", icons);
            root.SetActive(true);
            return displayer;
        }
    }
}

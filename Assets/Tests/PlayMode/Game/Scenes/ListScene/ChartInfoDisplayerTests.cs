using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using LitMotion;
using MajdataPlay.Scenes.List;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.ListScene
{
    /// <summary>Covers the debounced chart metadata and radar displayers and the online information panels.</summary>
    public class ChartInfoDisplayerTests : ListSceneTestBase
    {
        /// <summary>Gets a text field of a component.</summary>
        /// <param name="component">The component.</param>
        /// <param name="field">The field name.</param>
        /// <returns>The text.</returns>
        private static string TextOf(Component component, string field)
        {
            return PrivateMember.GetField<TextMeshProUGUI>(component, field).text;
        }

        /// <summary>Creates a song whose Master chart has the given fumen.</summary>
        /// <param name="hash">The song hash.</param>
        /// <param name="fumen">The fumen.</param>
        /// <returns>The song.</returns>
        private static FakeSongDetail SongWithChart(string hash, string fumen)
        {
            var song = new FakeSongDetail(hash, "Chart Song", "1", "2", "3", "4", "5");
            song.Artist = "Chart Artist";
            song.SetDesigner((int)ChartLevel.Master, "Master Designer");
            song.Maidata = Maidata(ChartLevel.Master, Chart(fumen));
            return song;
        }

        /// <summary>Checks that metadata shows immediately and the BPM and duration are filled after the debounce.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Metadata_FillsBpmAndDurationAfterDebounce()
        {
            var displayer = CreateMetadataDisplayer(Canvas);
            var song = SongWithChart("majtest-meta-bpm", "(120){4}1,2,3,4,1,2,3,4,E");
            SetDeltaTime(0.25f);

            displayer.SetMetadataFromSongDetail(song, ChartLevel.Master);
            Assert.That(TextOf(displayer, "_title"), Is.EqualTo("Chart Song"));
            Assert.That(TextOf(displayer, "_artist"), Is.EqualTo("Chart Artist"));
            Assert.That(TextOf(displayer, "_charter"), Is.EqualTo("Master Designer"));
            Assert.That(TextOf(displayer, "_onlineIdDisplayer"), Is.Empty);
            Assert.That(TextOf(displayer, "_bpmDisplayer"), Is.EqualTo("--"));
            yield return WaitUntil(() => TextOf(displayer, "_bpmDisplayer") != "--", 3f);

            Assert.That(TextOf(displayer, "_bpmDisplayer"), Is.EqualTo("120"));
            Assert.That(TextOf(displayer, "_durationDisplayer"), Does.Match(@"^\d+:\d\d$"));
            Assert.That(song.MaidataRequestCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a chart with a BPM change shows the BPM range.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Metadata_ShowsBpmRange()
        {
            var displayer = CreateMetadataDisplayer(Canvas);
            var song = SongWithChart("majtest-meta-range", "(120){4}1,2,3,4,(180)1,2,3,4,E");
            SetDeltaTime(0.25f);

            displayer.SetMetadataFromSongDetail(song, ChartLevel.Master, 300);
            yield return WaitUntil(() => TextOf(displayer, "_bpmDisplayer") != "--", 3f);

            Assert.That(TextOf(displayer, "_bpmDisplayer"), Is.EqualTo("120-180"));
        }

        /// <summary>Checks that an empty chart and a failed load keep the placeholders.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Metadata_EmptyOrFailedChartKeepsPlaceholders()
        {
            var displayer = CreateMetadataDisplayer(Canvas);
            var empty = SongWithChart("majtest-meta-empty", "E");
            empty.Maidata = Maidata(ChartLevel.Easy, Chart("(120){4}1,E"));
            var failed = SongWithChart("majtest-meta-failed", "E");
            failed.MaidataException = new InvalidOperationException("broken");
            SetDeltaTime(0.25f);

            displayer.SetMetadataFromSongDetail(empty, ChartLevel.Master);
            yield return WaitUntil(() => empty.MaidataRequestCount > 0 && PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);
            Assert.That(TextOf(displayer, "_bpmDisplayer"), Is.EqualTo("--"));

            displayer.SetMetadataFromSongDetail(failed, ChartLevel.Master);
            yield return WaitUntil(() => failed.MaidataRequestCount > 0 && PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);
            Assert.That(TextOf(displayer, "_durationDisplayer"), Is.EqualTo("--:--"));
        }

        /// <summary>Checks that a cancelled request is dropped without loading maidata.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Metadata_CancelledRequestIsDropped()
        {
            var displayer = CreateMetadataDisplayer(Canvas);
            var song = SongWithChart("majtest-meta-cancel", "(120){4}1,E");
            using var source = new CancellationTokenSource();
            source.Cancel();
            SetDeltaTime(0.25f);

            displayer.SetMetadataFromSongDetail(song, ChartLevel.Master, 0, source.Token);
            yield return WaitUntil(() => PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);

            Assert.That(song.MaidataRequestCount, Is.EqualTo(0));
            Assert.That(TextOf(displayer, "_bpmDisplayer"), Is.EqualTo("--"));
        }

        /// <summary>Checks that online songs show their identifier.</summary>
        [Test]
        public void Metadata_OnlineSongShowsIdentifier()
        {
            var displayer = CreateMetadataDisplayer(Canvas);

            displayer.SetMetadataFromSongDetail(CreateOnlineSong("4242"), ChartLevel.Easy);

            Assert.That(TextOf(displayer, "_onlineIdDisplayer"), Is.EqualTo("ID: 4242"));
            Assert.That(TextOf(displayer, "_charter"), Is.EqualTo("Uploader@Designer"));
        }

        /// <summary>Checks that the radar animates material values and the estimated constant.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Radar_SetRadarAnimatesValuesAndColors()
        {
            var displayer = CreateRadarDisplayer(Canvas);
            var material = PrivateMember.GetField<Material>(displayer, "_radarMaterial");
            var esti = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_estiText");
            var colorComparer = new ColorEqualityComparer(1e-4f);

            displayer.SetRadar(new List<float> { 0.1f, 0.9f, 0.2f, 0.3f, 0.4f, 0.5f, 0f }, 12.5f);
            var handles = PrivateMember.GetField<MotionHandle[]>(displayer, "_scoreHandles");
            yield return null;
            foreach (var handle in handles)
            {
                handle.TryComplete();
            }
            // Color == is approximate, so waiting on it can stop a few microseconds before the color motions end.
            PrivateMember.GetField<MotionHandle>(displayer, "_fillColorHandle").TryComplete();
            PrivateMember.GetField<MotionHandle>(displayer, "_outlineColorHandle").TryComplete();
            yield return WaitUntil(() => esti.text == (12.5f).ToString("F2"), 3f);

            Assert.That(material.GetFloat("_V1"), Is.EqualTo(0.9f).Within(1e-4f));
            Assert.That(esti.text, Is.EqualTo((12.5f).ToString("F2")));
            Assert.That(material.GetColor("_FillColor"), Is.EqualTo(Color.green).Using(colorComparer));
            Assert.That(material.GetColor("_OutlineColor"), Is.EqualTo(Color.gray).Using(colorComparer));
        }

        /// <summary>Checks that destroying the radar stops its value animations.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Radar_Destroy_StopsAnimations()
        {
            var displayer = CreateRadarDisplayer(Canvas);
            displayer.SetRadar(new List<float> { 1, 1, 1, 1, 1, 1, 1 }, 1f);
            yield return null;
            var handle = PrivateMember.GetField<MotionHandle[]>(displayer, "_scoreHandles")[0];

            UnityEngine.Object.DestroyImmediate(displayer.gameObject);

            Assert.That(handle.IsActive(), Is.False);
        }

        /// <summary>Checks that a zero estimate clears the constant text and a missing material is rejected.</summary>
        [Test]
        public void Radar_ZeroEstimateClearsTextAndMissingMaterialIsRejected()
        {
            var displayer = CreateRadarDisplayer(Canvas);
            var esti = PrivateMember.GetField<TextMeshProUGUI>(displayer, "_estiText");
            esti.text = "9.99";

            displayer.SetRadar(new List<float> { 0, 0, 0, 0, 0, 0, 0 }, 0f);
            Assert.That(esti.text, Is.Empty);

            PrivateMember.SetField(displayer, "_radarMaterial", null);
            displayer.SetRadar(new List<float> { 1, 1, 1, 1, 1, 1, 1 }, 5f);
            Assert.That(esti.text, Is.Empty);
        }

        /// <summary>Checks that the radar analyzes the selected chart after the debounce.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Radar_SetRadarFromSongDetail_AnalyzesChart()
        {
            var displayer = CreateRadarDisplayer(Canvas);
            var song = SongWithChart("majtest-radar", "(120){4}1,2,3,4,5,6,7,8,1h[4:1],2-6[4:1],C,E");
            SetDeltaTime(0.25f);

            displayer.SetRadarFromSongDetail(song, ChartLevel.Master);
            yield return WaitUntil(() => song.MaidataRequestCount > 0 && PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);
            yield return WaitUntil(() => ListManager.AllBackgroundTasks.TrueForAll(task => task.IsCompleted), 10f);

            Assert.That(song.MaidataRequestCount, Is.EqualTo(1));
            Assert.That(ListManager.AllBackgroundTasks.TrueForAll(task => task.IsCompleted), Is.True);

            // The estimate text animation is not bound to the displayer's lifetime, so it must finish before teardown.
            yield return new WaitForSecondsRealtime(0.8f);
        }

        /// <summary>Checks that a failed radar load and a cancelled request are dropped.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Radar_FailedAndCancelledLoadsAreDropped()
        {
            var displayer = CreateRadarDisplayer(Canvas);
            var failed = SongWithChart("majtest-radar-failed", "E");
            failed.MaidataException = new InvalidOperationException("broken");
            var cancelled = SongWithChart("majtest-radar-cancelled", "E");
            using var source = new CancellationTokenSource();
            source.Cancel();
            SetDeltaTime(0.25f);

            displayer.SetRadarFromSongDetail(failed, ChartLevel.Master, 100);
            yield return WaitUntil(() => failed.MaidataRequestCount > 0 && PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);
            displayer.SetRadarFromSongDetail(cancelled, ChartLevel.Master, 0, source.Token);
            yield return WaitUntil(() => PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);

            Assert.That(failed.MaidataRequestCount, Is.EqualTo(1));
            Assert.That(cancelled.MaidataRequestCount, Is.EqualTo(0));
        }

        /// <summary>Checks that local songs hide the online panel and online songs reset its counters.</summary>
        [Test]
        public void OnlineInfo_ShowsOnlyForOnlineSongs()
        {
            var displayer = CreateOnlineInfoDisplayer(Canvas);
            using var source = new CancellationTokenSource();
            source.Cancel();

            displayer.SetSongDetail(new FakeSongDetail("majtest-online-info-local"));
            Assert.That(displayer.gameObject.activeSelf, Is.False);

            displayer.SetSongDetail(CreateOnlineSong("info"), 0, source.Token);
            Assert.That(displayer.gameObject.activeSelf, Is.True);
            Assert.That(TextOf(displayer, "_likeCountDisplayer"), Is.EqualTo("--"));
            Assert.That(TextOf(displayer, "_playCountDisplayer"), Is.EqualTo("--"));
            Assert.That(TextOf(displayer, "_commentCountDisplayer"), Is.EqualTo("--"));
        }

        /// <summary>Checks that the ranking hides for local songs and shows a loading state for online songs.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Ranking_ShowsLoadingForOnlineSongs()
        {
            var displayer = CreateRankDisplayer(Canvas);
            var group = displayer.GetComponent<CanvasGroup>();
            using var source = new CancellationTokenSource();
            source.Cancel();
            SetDeltaTime(0.25f);
            Assert.That(group.alpha, Is.EqualTo(0f));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_emptyMessageDisplayer").text, Is.EqualTo("MAJTEXT_LIST_NO_PLAY_RECORD"));

            displayer.SetSongDetail(new FakeSongDetail("majtest-rank-local"), ChartLevel.Master);
            displayer.SetSongDetail(CreateOnlineSong("rank"), ChartLevel.Master, 0, source.Token);
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_loadingIndicator").activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_emptyIndicator").activeSelf, Is.False);
            PrivateMember.GetField<MotionHandle>(displayer, "_displayerAnim").TryComplete();
            Assert.That(group.alpha, Is.EqualTo(1f));
            yield return WaitUntil(() => PrivateMember.GetField<object?>(displayer, "_currentSongDetail") is null, 3f);

            displayer.Hide();
            PrivateMember.GetField<MotionHandle>(displayer, "_displayerAnim").TryComplete();
            Assert.That(group.alpha, Is.EqualTo(0f));
        }

        /// <summary>Creates an online score.</summary>
        /// <param name="name">The player name.</param>
        /// <param name="acc">The achievement.</param>
        /// <param name="combo">The combo state.</param>
        /// <returns>The score.</returns>
        private static MajNetSongScore Score(string name, float acc, MajdataPlay.Scenes.Game.ComboState combo = MajdataPlay.Scenes.Game.ComboState.None)
        {
            return new MajNetSongScore
            {
                Player = new UserProfile { Username = name, Nickname = name + "Nick" },
                Acc = acc,
                ComboState = combo
            };
        }

        /// <summary>Checks that rankers are sorted by achievement and the logged-in player's rank beyond the list is shown.</summary>
        [Test]
        public void Ranking_DisplayRanker_SortsAndShowsSelfRank()
        {
            var displayer = CreateRankDisplayer(Canvas, 2);
            var endpoint = CreateEndpoint();
            endpoint.RuntimeConfig.IsLoggedIn = true;
            endpoint.RuntimeConfig.Username = "me";
            PrivateMember.SetField(displayer, "_currentSongDetail", CreateOnlineSong("ranker", endpoint));
            var scores = new[]
            {
                Score("low", 97.5f),
                Score("top", 100.6543f, MajdataPlay.Scenes.Game.ComboState.APPlus),
                Score("me", 90f),
                Score("mid", 99f, MajdataPlay.Scenes.Game.ComboState.FC),
            };

            PrivateMember.Invoke(displayer, "DisplayRanker", (object)scores);

            var root = PrivateMember.GetField<GameObject>(displayer, "_rankerDisplayerListRoot").transform;
            Assert.That(root.GetChild(0).Find("Username").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("topNick"));
            Assert.That(root.GetChild(1).Find("Username").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("midNick"));
            Assert.That(root.GetChild(0).Find("Accurate").GetComponent<TextMeshProUGUI>().text, Does.Contain("AP<sup>+</sup>").And.Contain("100.<size=70%>65"));
            Assert.That(root.GetChild(0).Find("Rank").GetComponent<TextMeshProUGUI>().color, Is.EqualTo(Color.yellow));
            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_rankDisplayer").text, Is.EqualTo("#4"));
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_loadingIndicator").activeSelf, Is.False);
        }

        /// <summary>Checks that an empty ranking shows the empty state.</summary>
        [Test]
        public void Ranking_DisplayRanker_EmptyShowsEmptyState()
        {
            var displayer = CreateRankDisplayer(Canvas);

            PrivateMember.Invoke(displayer, "DisplayRanker", (object)Array.Empty<MajNetSongScore>());

            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_emptyIndicator").activeSelf, Is.True);
            Assert.That(PrivateMember.GetField<GameObject>(displayer, "_loadingIndicator").activeSelf, Is.False);
        }

        /// <summary>Checks the combo state labels and their colors.</summary>
        [Test]
        public void Ranking_CombostateToStr_LabelsEveryState()
        {
            var displayer = CreateRankDisplayer(Canvas);

            Assert.That(PrivateMember.Invoke<string>(displayer, "CombostateToStr", MajdataPlay.Scenes.Game.ComboState.APPlus), Is.EqualTo("<color=#FF8000>AP<sup>+</sup></color>"));
            Assert.That(PrivateMember.Invoke<string>(displayer, "CombostateToStr", MajdataPlay.Scenes.Game.ComboState.FCPlus), Is.EqualTo("<color=#0080FF>FC<sup>+</sup></color>"));
            Assert.That(PrivateMember.Invoke<string>(displayer, "CombostateToStr", MajdataPlay.Scenes.Game.ComboState.AP), Is.EqualTo("<color=#FF8000>AP</color>"));
            Assert.That(PrivateMember.Invoke<string>(displayer, "CombostateToStr", MajdataPlay.Scenes.Game.ComboState.FC), Is.EqualTo("<color=#0080FF>FC</color>"));
            Assert.That(PrivateMember.Invoke<string>(displayer, "CombostateToStr", MajdataPlay.Scenes.Game.ComboState.None), Is.Empty);
        }

        /// <summary>Checks that the empty message follows language changes.</summary>
        [Test]
        public void Ranking_EmptyMessageFollowsLanguage()
        {
            var displayer = CreateRankDisplayer(Canvas);

            MajdataPlay.i18n.Localization.Current = new MajdataPlay.i18n.Language
            {
                Translations = new Dictionary<string, string> { { "MAJTEXT_LIST_NO_PLAY_RECORD", "No records" } }
            };

            Assert.That(PrivateMember.GetField<TextMeshProUGUI>(displayer, "_emptyMessageDisplayer").text, Is.EqualTo("No records"));
        }
    }
}

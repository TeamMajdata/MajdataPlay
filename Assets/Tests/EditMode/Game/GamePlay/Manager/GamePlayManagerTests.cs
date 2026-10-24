using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Manager
{
    /// <summary>
    /// Checks the parts of <see cref="GamePlayManager"/> that work without the gameplay scene: mode flags, dan mod
    /// overrides, enforced failure thresholds and the guards of the ending flows.
    /// </summary>
    public class GamePlayManagerTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>The manager under test, never awakened.</summary>
        private GamePlayManager _manager = null!;

        /// <summary>Creates a manager with default settings and mods.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _manager = _objects.Add<GamePlayManager>();
            PrivateMember.SetField(_manager, "_gameSettings", _settings.Settings);
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.ModInfo), new GameModInfo(new ModOptions()));
        }

        /// <summary>Destroys the manager and restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks the initial state and the values derived from the mods.</summary>
        [Test]
        public void InitialState_IsWaitingToStart()
        {
            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Start));
            Assert.That(_manager.IsStart, Is.False);
            Assert.That(_manager.AudioStartTime, Is.EqualTo(-114514f));
            Assert.That(_manager.ThisFrameSec, Is.EqualTo(0f));
            Assert.That(_manager.AudioLength, Is.EqualTo(0f));
            Assert.That(_manager.NoteSpeed, Is.EqualTo(7f));
            Assert.That(_manager.TouchSpeed, Is.EqualTo(7f));
            Assert.That(_manager.AutoplayGrade, Is.EqualTo(JudgeGrade.Perfect));
            Assert.That(_manager.IsAutoplay, Is.False);
            Assert.That(_manager.AutoplayMode, Is.EqualTo(AutoplayModeOption.Disable));
            Assert.That(_manager.PlaybackSpeed, Is.EqualTo(1f));
            Assert.That(_manager.HistoryScore, Is.Null);
        }

        /// <summary>Checks that the first note appear timing can be lowered by the note loader.</summary>
        [Test]
        public void FirstNoteAppearTiming_IsWritable()
        {
            _manager.FirstNoteAppearTiming = -7.5f;

            Assert.That(_manager.FirstNoteAppearTiming, Is.EqualTo(-7.5f));
        }

        /// <summary>Checks that autoplay follows the autoplay mod.</summary>
        [Test]
        public void IsAutoplay_FollowsTheMod()
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.ModInfo), new GameModInfo(new ModOptions { AutoPlay = AutoplayModeOption.DJAuto_ButtonRing_First, PlaybackSpeed = 1.5f }));

            Assert.That(_manager.IsAutoplay, Is.True);
            Assert.That(_manager.AutoplayMode, Is.EqualTo(AutoplayModeOption.DJAuto_ButtonRing_First));
            Assert.That(_manager.PlaybackSpeed, Is.EqualTo(1.5f));
        }

        /// <summary>Checks that the classic mode follows the judge mode and defaults to modern without settings.</summary>
        [Test]
        public void IsClassicMode_FollowsTheJudgeMode()
        {
            Assert.That(_manager.IsClassicMode, Is.False);

            _settings.Settings.Judge.Mode = JudgeModeOption.Classic;
            Assert.That(_manager.IsClassicMode, Is.True);

            PrivateMember.SetField(_manager, "_gameSettings", null);
            Assert.That(_manager.IsClassicMode, Is.False);
        }

        /// <summary>Checks the session mode flags read from the game info.</summary>
        [Test]
        public void Mode_IsReadFromTheGameInfo()
        {
            var song = new GamePlayFakeSongDetail();
            PrivateMember.SetField(_manager, "_gameInfo", new GameInfo(GameMode.Practice, new ISongDetail[] { song }, new[] { ChartLevel.Easy }, 2));

            Assert.That(_manager.IsPracticeMode, Is.True);
            Assert.That(_manager.Mode, Is.EqualTo(GameMode.Practice));
        }

        /// <summary>Checks that dan mods with typed values override the player's mods.</summary>
        [Test]
        public void LoadDanModSettings_AppliesTypedOverrides()
        {
            SetDanMods(new Dictionary<string, JToken>
            {
                { "PlaybackSpeed", new JValue(1.5f) },
                { "AllBreak", new JValue(true) },
                { "AllEx", new JValue(true) },
                { "AllTouch", new JValue(true) },
                { "ButtonRingForTouch", new JValue(true) },
                { "IsSlideNoHead", new JValue(true) },
                { "IsSlideNoTrack", new JValue(true) },
                { "SubdivideSlideJudgeGrade", new JValue(true) },
                { "AutoPlay", new JValue(1) },
                { "JudgeStyle", new JValue(2) },
                { "NoteMask", new JValue("Inner") },
                { "Unknown", new JValue(42) },
            });

            PrivateMember.Invoke(_manager, "LoadDanModSettings");

            var mods = _manager.ModInfo;
            Assert.That(mods.PlaybackSpeed, Is.EqualTo(1.5f));
            Assert.That(mods.AllBreak && mods.AllEx && mods.AllTouch && mods.ButtonRingForTouch, Is.True);
            Assert.That(mods.SlideNoHead && mods.SlideNoTrack && mods.SubdivideSlideJudgeGrade, Is.True);
            Assert.That(mods.AutoPlay, Is.EqualTo(AutoplayModeOption.Enable));
            Assert.That(mods.JudgeStyle, Is.EqualTo(JudgeStyleOption.GACHI));
            Assert.That(mods.NoteMask, Is.EqualTo("Inner"));
        }

        /// <summary>Checks that dan mods given as strings are parsed and invalid strings are ignored.</summary>
        [Test]
        public void LoadDanModSettings_ParsesStringOverrides()
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.ModInfo), new GameModInfo(new ModOptions { AllTouch = true, PlaybackSpeed = 0.5f }));
            SetDanMods(new Dictionary<string, JToken>
            {
                { "PlaybackSpeed", new JValue("1.25") },
                { "AllBreak", new JValue("True") },
                { "AllEx", new JValue("false") },
                { "AllTouch", new JValue("maybe") },
                { "ButtonRingForTouch", new JValue("true") },
                { "IsSlideNoHead", new JValue("true") },
                { "IsSlideNoTrack", new JValue("true") },
                { "SubdivideSlideJudgeGrade", new JValue("true") },
                { "AutoPlay", new JValue("DJAuto_TouchPanel_First") },
                { "JudgeStyle", new JValue("MAJI") },
            });

            PrivateMember.Invoke(_manager, "LoadDanModSettings");

            var mods = _manager.ModInfo;
            Assert.That(mods.PlaybackSpeed, Is.EqualTo(1.25f));
            Assert.That(mods.AllBreak, Is.True);
            Assert.That(mods.AllEx, Is.False);
            Assert.That(mods.AllTouch, Is.True, "An unparsable value keeps the player's mod");
            Assert.That(mods.ButtonRingForTouch && mods.SlideNoHead && mods.SlideNoTrack && mods.SubdivideSlideJudgeGrade, Is.True);
            Assert.That(mods.AutoPlay, Is.EqualTo(AutoplayModeOption.DJAuto_TouchPanel_First));
            Assert.That(mods.JudgeStyle, Is.EqualTo(JudgeStyleOption.MAJI));
        }

        /// <summary>Checks that unparsable speed, autoplay and judge style strings keep the player's mods.</summary>
        [Test]
        public void LoadDanModSettings_IgnoresInvalidStrings()
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.ModInfo), new GameModInfo(new ModOptions { PlaybackSpeed = 0.75f, JudgeStyle = JudgeStyleOption.GORI }));
            SetDanMods(new Dictionary<string, JToken>
            {
                { "PlaybackSpeed", new JValue("fast") },
                { "AutoPlay", new JValue("sometimes") },
                { "JudgeStyle", new JValue("strict") },
                { "PlaybackSpeed2", new JValue(3) },
            });

            PrivateMember.Invoke(_manager, "LoadDanModSettings");

            Assert.That(_manager.ModInfo.PlaybackSpeed, Is.EqualTo(0.75f));
            Assert.That(_manager.ModInfo.AutoPlay, Is.EqualTo(AutoplayModeOption.Disable));
            Assert.That(_manager.ModInfo.JudgeStyle, Is.EqualTo(JudgeStyleOption.GORI));
        }

        /// <summary>Checks that an integer playback speed is accepted.</summary>
        [Test]
        public void LoadDanModSettings_AcceptsIntegerSpeeds()
        {
            SetDanMods(new Dictionary<string, JToken> { { "PlaybackSpeed", new JValue(2) } });

            PrivateMember.Invoke(_manager, "LoadDanModSettings");

            Assert.That(_manager.ModInfo.PlaybackSpeed, Is.EqualTo(2f));
        }

        /// <summary>Checks that enforced failure does nothing when disabled, not running or in dan and practice sessions.</summary>
        [Test]
        public void EnforceGameFailure_SkipsWhenNotApplicable()
        {
            var counter = CreateCounter();
            PrivateMember.SetField(_manager, "_objectCounter", counter);
            PrivateMember.SetField(_manager, "_gameInfo", new GameInfo(GameMode.Normal, null!, null!));
            PrivateMember.SetField(_manager, "_enforceGameFailureCondition", EnforceGameFailureCondition.Disabled);
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Running);
            counter.ReportResult(_objects.Add<TapDrop>(), new NoteJudgeResult { Grade = JudgeGrade.Miss });

            PrivateMember.Invoke(_manager, "EnforceGameFailureLateUpdate");
            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Running), "Disabled");

            PrivateMember.SetField(_manager, "_enforceGameFailureCondition", EnforceGameFailureCondition.Retry_FC);
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.WaitForEnd);
            PrivateMember.Invoke(_manager, "EnforceGameFailureLateUpdate");
            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.WaitForEnd), "Not running");

            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Running);
            PrivateMember.SetField(_manager, "_gameInfo", new GameInfo(GameMode.Practice, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Easy }, 1));
            PrivateMember.Invoke(_manager, "EnforceGameFailureLateUpdate");
            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Running), "Practice");
        }

        /// <summary>Checks that a play that can still reach the target never triggers the enforced failure.</summary>
        /// <param name="condition">The enforced failure condition.</param>
        [TestCase(EnforceGameFailureCondition.TrackSkip_S)]
        [TestCase(EnforceGameFailureCondition.Retry_S)]
        [TestCase(EnforceGameFailureCondition.TrackSkip_SS)]
        [TestCase(EnforceGameFailureCondition.Retry_SS)]
        [TestCase(EnforceGameFailureCondition.TrackSkip_SSS)]
        [TestCase(EnforceGameFailureCondition.Retry_SSS)]
        [TestCase(EnforceGameFailureCondition.TrackSkip_SSSPlus)]
        [TestCase(EnforceGameFailureCondition.Retry_SSSPlus)]
        [TestCase(EnforceGameFailureCondition.TrackSkip_Best)]
        [TestCase(EnforceGameFailureCondition.Retry_Best)]
        [TestCase(EnforceGameFailureCondition.TrackSkip_FC)]
        [TestCase(EnforceGameFailureCondition.Retry_FC)]
        [TestCase(EnforceGameFailureCondition.TrackSkip_AP)]
        [TestCase(EnforceGameFailureCondition.Retry_AP)]
        public void EnforceGameFailure_KeepsPlayingWhileTheTargetIsReachable(EnforceGameFailureCondition condition)
        {
            PrivateMember.SetField(_manager, "_objectCounter", CreateCounter());
            PrivateMember.SetField(_manager, "_gameInfo", new GameInfo(GameMode.Normal, null!, null!));
            PrivateMember.SetField(_manager, "_enforceGameFailureCondition", condition);
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Blocking);

            PrivateMember.Invoke(_manager, "EnforceGameFailureLateUpdate");

            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Blocking));
        }

        /// <summary>Checks that classic mode compares the classic achievement with the target.</summary>
        [Test]
        public void EnforceGameFailure_UsesTheClassicAchievementInClassicMode()
        {
            _settings.Settings.Judge.Mode = JudgeModeOption.Classic;
            PrivateMember.SetField(_manager, "_objectCounter", CreateCounter());
            PrivateMember.SetField(_manager, "_gameInfo", new GameInfo(GameMode.Normal, null!, null!));
            PrivateMember.SetField(_manager, "_enforceGameFailureCondition", EnforceGameFailureCondition.Retry_SSS);
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Running);

            PrivateMember.Invoke(_manager, "EnforceGameFailureLateUpdate");

            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Running), "The classic maximum of 100% still reaches SSS");
        }

        /// <summary>Checks that an unknown enforced failure condition is rejected.</summary>
        [Test]
        public void EnforceGameFailure_RejectsUnknownConditions()
        {
            PrivateMember.SetField(_manager, "_objectCounter", CreateCounter());
            PrivateMember.SetField(_manager, "_gameInfo", new GameInfo(GameMode.Normal, null!, null!));
            PrivateMember.SetField(_manager, "_enforceGameFailureCondition", (EnforceGameFailureCondition)99);
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Running);

            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_manager, "EnforceGameFailureLateUpdate"));

            Assert.That(exception!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>Checks that the audio clock is not advanced before an audio track is loaded and playback started.</summary>
        [Test]
        public void AudioTimeUpdate_WaitsForAudioAndStart()
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Running);

            PrivateMember.Invoke(_manager, "AudioTimeUpdate");
            Assert.That(_manager.ThisFrameSec, Is.EqualTo(0f), "No audio track");

            PrivateMember.SetField(_manager, "_audioSample", AudioSampleWrap.Empty);
            PrivateMember.Invoke(_manager, "AudioTimeUpdate");
            Assert.That(_manager.ThisFrameSec, Is.EqualTo(0f), "Playback has not started");
        }

        /// <summary>Checks that the end-of-chart check waits for audio, a running chart and every note.</summary>
        [Test]
        public void GameControlLateUpdate_WaitsUntilEveryNoteIsJudged()
        {
            var counter = CreateCounter();
            PrivateMember.SetField(_manager, "_objectCounter", counter);

            PrivateMember.Invoke(_manager, "GameControlLateUpdate");
            Assert.That(PrivateMember.GetField<float?>(_manager, "_allNotesFinishedTiming"), Is.Null, "No audio track");

            PrivateMember.SetField(_manager, "_audioSample", AudioSampleWrap.Empty);
            PrivateMember.Invoke(_manager, "GameControlLateUpdate");
            Assert.That(PrivateMember.GetField<float?>(_manager, "_allNotesFinishedTiming"), Is.Null, "Not running");

            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Running);
            PrivateMember.SetProperty(counter, nameof(ObjectCounter.TapSum), 1);
            PrivateMember.Invoke(_manager, "GameControlLateUpdate");
            Assert.That(PrivateMember.GetField<float?>(_manager, "_allNotesFinishedTiming"), Is.Null, "Notes remain");

            PrivateMember.SetProperty(counter, nameof(ObjectCounter.TapSum), 0);
            PrivateMember.SetField(_manager, "_thisFrameSec", 12f);
            PrivateMember.Invoke(_manager, "GameControlLateUpdate");
            Assert.That(PrivateMember.GetField<float?>(_manager, "_allNotesFinishedTiming"), Is.EqualTo(12f));

            PrivateMember.SetField(_manager, "_thisFrameSec", 12.05f);
            PrivateMember.Invoke(_manager, "GameControlLateUpdate");
            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Running), "The end is delayed by 0.1 s");
        }

        /// <summary>Checks that a score is calculated and recorded without effects.</summary>
        [Test]
        public void CalculateScore_RecordsTheResult()
        {
            var song = new GamePlayFakeSongDetail();
            var info = new GameInfo(GameMode.Normal, new ISongDetail[] { song }, new[] { ChartLevel.Expert });
            var counter = CreateCounter();
            PrivateMember.SetField(_manager, "_objectCounter", counter);
            PrivateMember.SetField(_manager, "_gameInfo", info);
            PrivateMember.SetField(_manager, "_songDetail", song);
            counter.ReportResult(_objects.Add<TapDrop>(), new NoteJudgeResult { Grade = JudgeGrade.Miss });

            var silent = (GameResult)PrivateMember.Invoke(_manager, "CalculateScore", false)!;
            var withEffect = (GameResult)PrivateMember.Invoke(_manager, "CalculateScore", true)!;

            Assert.That(silent.SongDetail, Is.SameAs(song));
            Assert.That(silent.ComboState, Is.EqualTo(ComboState.None));
            Assert.That(withEffect.ComboState, Is.EqualTo(ComboState.None));
            Assert.That(info.GetLastResult().SongDetail, Is.SameAs(song));
        }

        /// <summary>Checks that the ending flows do nothing once the game has ended.</summary>
        [Test]
        public void EndingFlows_AreIgnoredAfterTheEnd()
        {
            PrivateMember.SetProperty(_manager, nameof(GamePlayManager.State), GamePlayStatus.Ended);

            _manager.GameOver();
            _manager.EndGame();
            _manager.FastRetry().Forget();
            _manager.SkipTrack().Forget();
            _manager.SkipTrack(quickReturn: true).Forget();
            _manager.FastPractice();
            PrivateMember.Invoke(_manager, "NextRound4Practice", 0);
            PrivateMember.Invoke(_manager, "TrackSkipTo", "List", 0);
            PrivateMember.Invoke(_manager, "EnforceTrackSkipTo", "List");
            PrivateMember.Invoke(_manager, "EnforceFastRetry");

            Assert.That(_manager.State, Is.EqualTo(GamePlayStatus.Ended));
            Assert.That(PrivateMember.GetField<System.Threading.CancellationTokenSource>(_manager, "_cts").IsCancellationRequested, Is.False);
        }

        /// <summary>Checks that disposing the audio track stops and releases it.</summary>
        [Test]
        public void DisposeAudioTrack_ReleasesTheSample()
        {
            PrivateMember.SetField(_manager, "_audioSample", AudioSampleWrap.Empty);

            PrivateMember.Invoke(_manager, "DisposeAudioTrack");
            PrivateMember.Invoke(_manager, "DisposeAudioTrack");

            Assert.That(PrivateMember.GetField<AudioSampleWrap?>(_manager, "_audioSample"), Is.Null);
        }

        /// <summary>Checks that the game over effect is skipped when no animation is assigned.</summary>
        [Test]
        public void PlayGameOverEffect_WithoutAnimationDoesNothing()
        {
            Assert.DoesNotThrow(() => PrivateMember.Invoke(_manager, "PlayGameOverEffect"));
            Assert.That(PrivateMember.GetField<UnityEngine.GameObject?>(_manager, "_gameOverAnimation"), Is.Null);
        }

        /// <summary>Creates a cleared object counter in a normal session.</summary>
        /// <returns>The counter.</returns>
        private ObjectCounter CreateCounter()
        {
            var counter = _objects.Add<ObjectCounter>();
            PrivateMember.SetField(counter, "_gameInfo", new GameInfo(GameMode.Normal, null!, null!));
            counter.Clear();
            return counter;
        }

        /// <summary>Installs a dan session with mod overrides.</summary>
        /// <param name="mods">The dan mod overrides.</param>
        private void SetDanMods(Dictionary<string, JToken> mods)
        {
            var info = new GameInfo(GameMode.Dan, new ISongDetail[] { new GamePlayFakeSongDetail() }, new[] { ChartLevel.Master })
            {
                DanInfo = new DanInfo { Mods = mods },
            };
            PrivateMember.SetField(_manager, "_gameInfo", info);
        }
    }
}

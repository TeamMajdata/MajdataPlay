using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.Game.Notes;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Loading
{
    /// <summary>
    /// Checks the conversion of simai notes into pooling infos by <see cref="NoteLoader"/>, driving its private factory
    /// methods directly because the full loading pipeline needs the gameplay scene, skins and note pools.
    /// </summary>
    public class NoteLoaderTests
    {
        /// <summary>The default note appear rate of the settings.</summary>
        private const float AppearRate = 0.265f;

        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Restores the registered note loader.</summary>
        private SingletonScope<NoteLoader> _loaderSlot = null!;

        /// <summary>Restores the registered note controller.</summary>
        private SingletonScope<INoteController> _controllerSlot = null!;

        /// <summary>The loader under test.</summary>
        private NoteLoader _loader = null!;

        /// <summary>Creates a loader with default settings and empty note indexes.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _loaderSlot = new SingletonScope<NoteLoader>();
            _controllerSlot = new SingletonScope<INoteController>();
            _loader = _objects.Add<NoteLoader>();
            ResetStaticState();
            var noteIndex = PrivateMember.GetStaticField<Dictionary<int, int>>(typeof(NoteLoader), "_noteIndex");
            for (var key = 1; key <= 8; key++)
            {
                noteIndex[key] = 0;
            }
            var touchIndex = PrivateMember.GetStaticField<Dictionary<SensorArea, int>>(typeof(NoteLoader), "_touchIndex");
            for (var i = 0; i < 33; i++)
            {
                touchIndex[(SensorArea)i] = 0;
            }
        }

        /// <summary>Destroys the loader and restores every static it touched.</summary>
        [TearDown]
        public void TearDown()
        {
            ResetStaticState();
            _objects.Dispose();
            _controllerSlot.Dispose();
            _loaderSlot.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks the default speeds and that the touch speed is stored as a magnitude.</summary>
        [Test]
        public void Properties_HaveDefaultsAndTouchSpeedIsAbsolute()
        {
            Assert.That(_loader.NoteSpeed, Is.EqualTo(7f));
            Assert.That(_loader.TouchSpeed, Is.EqualTo(7.5f));
            Assert.That(_loader.ChartRotation, Is.EqualTo(0));
            Assert.That(_loader.NoteCount, Is.EqualTo(0));
            Assert.That(_loader.Progress, Is.EqualTo(0));
            Assert.That(_loader.IsHasTap.Length, Is.EqualTo(8));
            Assert.That(_loader.IsHasHold.Length, Is.EqualTo(8));
            Assert.That(_loader.IsHasTouch.Length, Is.EqualTo(33));
            Assert.That(_loader.IsHasTouchHold.Length, Is.EqualTo(33));

            _loader.TouchSpeed = -6f;
            _loader.Progress = 0.5;

            Assert.That(_loader.TouchSpeed, Is.EqualTo(6f));
            Assert.That(_loader.Progress, Is.EqualTo(0.5));
        }

        /// <summary>Checks that Awake registers the loader, builds the slide lookup and clears the note flags, and OnDestroy releases it.</summary>
        [Test]
        public void AwakeAndOnDestroy_RegisterAndReleaseTheLoader()
        {
            HasTap()[0] = true;
            SlideDataBuilder.SlideAreaLookup.Clear();

            UnityMessages.Send(_loader, "Awake");

            Assert.That(Majdata<NoteLoader>.Instance, Is.SameAs(_loader));
            Assert.That(SlideDataBuilder.SlideAreaLookup, Has.Count.EqualTo(176));
            Assert.That(_loader.IsHasTap[0], Is.False);

            UnityMessages.Send(_loader, "OnDestroy");

            Assert.That(Majdata<NoteLoader>.Instance, Is.Null);
            Assert.That(PrivateMember.GetStaticField<Dictionary<int, int>>(typeof(NoteLoader), "_noteIndex"), Is.Empty);
        }

        /// <summary>Checks that Start reads the slide mods of the note controller.</summary>
        [Test]
        public void Start_ReadsTheSlideModsOfTheController()
        {
            _controllerSlot.Set(new GamePlayFakeNoteController
            {
                ModInfo = new GameModInfo(new MajdataPlay.Settings.ModOptions { SlideNoHead = true, SlideNoTrack = true }),
            });

            UnityMessages.Send(_loader, "Start");

            Assert.That(PrivateMember.GetField<bool>(_loader, "_isSlideNoHead"), Is.True);
            Assert.That(PrivateMember.GetField<bool>(_loader, "_isSlideNoTrack"), Is.True);
        }

        /// <summary>Checks that Start without a controller disables both slide mods.</summary>
        [Test]
        public void Start_WithoutControllerDisablesTheSlideMods()
        {
            UnityMessages.Send(_loader, "Start");

            Assert.That(PrivateMember.GetField<bool>(_loader, "_isSlideNoHead"), Is.False);
            Assert.That(PrivateMember.GetField<bool>(_loader, "_isSlideNoTrack"), Is.False);
        }

        /// <summary>Checks that Clear resets the note manager counters, the indexes and the note flags.</summary>
        [Test]
        public void Clear_ResetsIndexesAndFlags()
        {
            var noteManager = _objects.Add<NoteManager>();
            PrivateMember.SetField(_loader, "_noteManager", noteManager);
            HasTap()[3] = true;

            _loader.Clear();

            Assert.That(_loader.IsHasTap[3], Is.False);
            Assert.That(PrivateMember.GetStaticField<Dictionary<int, int>>(typeof(NoteLoader), "_noteIndex"), Is.Empty);
            Assert.That(PrivateMember.GetStaticField<Dictionary<SensorArea, int>>(typeof(NoteLoader), "_touchIndex"), Is.Empty);
        }

        /// <summary>Checks the tap info: rotation, queue index, sort order, appear timing and flags.</summary>
        [Test]
        public void CreateTap_BuildsATapInfo()
        {
            _loader.ChartRotation = 2;
            var tap = new SimaiNote { Type = SimaiNoteType.Tap, StartPosition = 1, IsBreak = true, IsEx = true, IsForceStar = true, IsFakeRotate = true };
            var other = new SimaiNote { Type = SimaiNoteType.Tap, StartPosition = 5 };
            var timing = Timing(2.0, tap, other);

            var first = Invoke<TapPoolingInfo>("CreateTap", tap, timing);
            var second = Invoke<TapPoolingInfo>("CreateTap", tap, timing);

            Assert.That(first.StartPos, Is.EqualTo(3));
            Assert.That(first.Timing, Is.EqualTo(2f));
            Assert.That(first.AppearTiming, Is.EqualTo(2f + ExpectedAppearDiff(7f)).Within(1e-4f));
            Assert.That(first.Speed, Is.EqualTo(7f));
            Assert.That(first.IsEach, Is.True);
            Assert.That(first.IsBreak && first.IsEX && first.IsStar, Is.True);
            Assert.That(first.IsMine, Is.False);
            Assert.That(first.RotateSpeed, Is.EqualTo(-440f));
            Assert.That(first.QueueInfo.KeyIndex, Is.EqualTo(3));
            Assert.That(first.QueueInfo.Index, Is.EqualTo(0));
            Assert.That(second.QueueInfo.Index, Is.EqualTo(1));
            Assert.That(first.NoteSortOrder - second.NoteSortOrder, Is.EqualTo(2));
            Assert.That(_loader.IsHasTap[2], Is.True);
        }

        /// <summary>Checks that a note close to its appear time still appears at least 0.15 s before the judge time.</summary>
        [Test]
        public void CreateTap_AppearsAtLeastAShortLeadBeforeTheNote()
        {
            _loader.NoteSpeed = 1000f;
            var tap = new SimaiNote { Type = SimaiNoteType.Tap, StartPosition = 4 };

            var info = Invoke<TapPoolingInfo>("CreateTap", tap, Timing(1.0, tap));

            Assert.That(info.AppearTiming, Is.EqualTo(0.85f).Within(1e-5f));
            Assert.That(info.IsEach, Is.False);
            Assert.That(info.RotateSpeed, Is.EqualTo(0f));
        }

        /// <summary>Checks that a tap on an invalid key is reported as a simai syntax error with its position.</summary>
        [Test]
        public void CreateTap_InvalidKeyThrowsASyntaxError()
        {
            var tap = new SimaiNote { Type = SimaiNoteType.Tap, StartPosition = 9, RawContent = "9" };
            var timing = new SimaiTimingPoint(1.0, new[] { tap }, "9", textPosX: 4, textPosY: 7);

            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_loader, "CreateTap", tap, timing));
            var syntaxError = exception!.InnerException as InvalidSimaiSyntaxException;

            Assert.That(syntaxError, Is.Not.Null);
            Assert.That(syntaxError!.Line, Is.EqualTo(7));
            Assert.That(syntaxError.Column, Is.EqualTo(4));
            Assert.That(syntaxError.Content, Is.EqualTo("9"));
            Assert.That(syntaxError.Message, Does.Contain("(at L7:C4)"));
        }

        /// <summary>Checks the hold info: duration, magnitude of a negative speed and both key flags.</summary>
        [Test]
        public void CreateHold_BuildsAHoldInfo()
        {
            var hold = new SimaiNote { Type = SimaiNoteType.Hold, StartPosition = 6, HoldTime = 1.5, IsMine = true };
            var timing = new SimaiTimingPoint(3.0, new[] { hold }, "6h", hspeed: -2f);

            var info = Invoke<HoldPoolingInfo>("CreateHold", hold, timing);

            Assert.That(info.StartPos, Is.EqualTo(6));
            Assert.That(info.LastFor, Is.EqualTo(1.5f));
            Assert.That(info.Speed, Is.EqualTo(14f));
            Assert.That(info.IsMine, Is.True);
            Assert.That(info.IsEach, Is.False);
            Assert.That(info.QueueInfo.KeyIndex, Is.EqualTo(6));
            Assert.That(_loader.IsHasHold[5], Is.True);
            Assert.That(_loader.IsHasTap[5], Is.True);
        }

        /// <summary>Checks that an invalid hold is reported as a simai syntax error.</summary>
        [Test]
        public void CreateHold_InvalidKeyThrowsASyntaxError()
        {
            var hold = new SimaiNote { Type = SimaiNoteType.Hold, StartPosition = 0, RawContent = "0h" };

            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_loader, "CreateHold", hold, Timing(1.0, hold)));

            Assert.That(exception!.InnerException, Is.TypeOf<InvalidSimaiSyntaxException>());
        }

        /// <summary>Checks the touch info: rotated sensor, touch speed, appear timing and each group membership.</summary>
        [Test]
        public void CreateTouch_BuildsATouchInfoAndCollectsEachMembers()
        {
            _loader.ChartRotation = 1;
            _loader.TouchSpeed = 7.5f;
            var touch = new SimaiNote { Type = SimaiNoteType.Touch, StartPosition = 2, TouchArea = 'B', IsHanabi = true, IsBreak = true };
            var center = new SimaiNote { Type = SimaiNoteType.Touch, StartPosition = 1, TouchArea = 'C' };
            var timing = Timing(4.0, touch, center);
            var members = new List<ITouchGroupInfoProvider>();

            var info = Invoke<TouchPoolingInfo>("CreateTouch", touch, timing, members);
            var centerInfo = Invoke<TouchPoolingInfo>("CreateTouch", center, timing, members);

            Assert.That(info.SensorPos, Is.EqualTo(SensorArea.B3));
            Assert.That(info.StartPos, Is.EqualTo(3));
            Assert.That(info.AreaPos, Is.EqualTo('B'));
            Assert.That(info.Speed, Is.EqualTo(7.5f));
            Assert.That(info.AppearTiming, Is.EqualTo(4f - (3.209385682f * Mathf.Pow(7.5f, -0.9549621752f))).Within(1e-4f));
            Assert.That(info.IsFirework && info.IsBreak && info.IsEach, Is.True);
            Assert.That(info.IsEX, Is.False);
            Assert.That(info.QueueInfo.SensorPos, Is.EqualTo(SensorArea.B3));
            Assert.That(centerInfo.SensorPos, Is.EqualTo(SensorArea.C));
            Assert.That(members, Is.EqualTo(new ITouchGroupInfoProvider[] { info, centerInfo }));
            Assert.That(_loader.IsHasTouch[(int)SensorArea.B3], Is.True);
            Assert.That(info.NoteSortOrder - centerInfo.NoteSortOrder, Is.EqualTo(6));
        }

        /// <summary>Checks the touch hold info and that it joins both the touch and touch hold groups.</summary>
        [Test]
        public void CreateTouchHold_BuildsATouchHoldInfo()
        {
            var touchHold = new SimaiNote { Type = SimaiNoteType.TouchHold, StartPosition = 1, TouchArea = 'C', HoldTime = 2.0 };
            var touch = new SimaiNote { Type = SimaiNoteType.Touch, StartPosition = 4, TouchArea = 'E' };
            var timing = Timing(1.0, touchHold, touch);
            var touchMembers = new List<ITouchGroupInfoProvider>();
            var holdMembers = new List<ITouchHoldGroupInfoProvider>();

            var info = Invoke<TouchHoldPoolingInfo>("CreateTouchHold", touchHold, timing, touchMembers, holdMembers);

            Assert.That(info.SensorPos, Is.EqualTo(SensorArea.C));
            Assert.That(info.LastFor, Is.EqualTo(2f));
            Assert.That(info.IsEach, Is.True);
            Assert.That(touchMembers, Has.Member(info));
            Assert.That(holdMembers, Has.Member(info));
            Assert.That(_loader.IsHasTouchHold[(int)SensorArea.C], Is.True);
        }

        /// <summary>Checks that a lone touch hold joins no group.</summary>
        [Test]
        public void CreateTouchHold_LoneNoteJoinsNoGroup()
        {
            var touchHold = new SimaiNote { Type = SimaiNoteType.TouchHold, StartPosition = 1, TouchArea = 'C', HoldTime = 2.0 };
            var touchMembers = new List<ITouchGroupInfoProvider>();
            var holdMembers = new List<ITouchHoldGroupInfoProvider>();

            Invoke<TouchHoldPoolingInfo>("CreateTouchHold", touchHold, Timing(1.0, touchHold), touchMembers, holdMembers);

            Assert.That(touchMembers, Is.Empty);
            Assert.That(holdMembers, Is.Empty);
        }

        /// <summary>Checks that invalid touches are reported as simai syntax errors.</summary>
        [Test]
        public void CreateTouch_InvalidKeyThrowsASyntaxError()
        {
            var touch = new SimaiNote { Type = SimaiNoteType.Touch, StartPosition = 12, TouchArea = 'B' };
            var touchHold = new SimaiNote { Type = SimaiNoteType.TouchHold, StartPosition = 12, TouchArea = 'C' };

            var touchError = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_loader, "CreateTouch", touch, Timing(1.0, touch), new List<ITouchGroupInfoProvider>()));
            var holdError = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_loader, "CreateTouchHold", touchHold, Timing(1.0, touchHold), new List<ITouchGroupInfoProvider>(), new List<ITouchHoldGroupInfoProvider>()));

            Assert.That(touchError!.InnerException, Is.TypeOf<InvalidSimaiSyntaxException>());
            Assert.That(holdError!.InnerException, Is.TypeOf<InvalidSimaiSyntaxException>());
        }

        /// <summary>Checks the each line between two notes: the shorter arc is chosen and a shared binding is installed.</summary>
        [Test]
        public void CreateEachLine_LinksTwoNotesAlongTheShorterArc()
        {
            var timing = Timing(2.0, new SimaiNote());
            var tap = new TapPoolingInfo { StartPos = 1 };
            var hold = new HoldPoolingInfo { StartPos = 3 };

            var line = Invoke<EachLinePoolingInfo>("CreateEachLine", timing, tap, hold);

            Assert.That(line.StartPos, Is.EqualTo(1));
            Assert.That(line.CurvLength, Is.EqualTo(2));
            Assert.That(line.Timing, Is.EqualTo(2f));
            Assert.That(line.Speed, Is.EqualTo(7f));
            Assert.That(line.AppearTiming, Is.EqualTo(2f + ExpectedAppearDiff(7f)).Within(1e-4f));
            Assert.That(line.MemberA, Is.SameAs(tap));
            Assert.That(line.MemberB, Is.SameAs(hold));
            Assert.That(tap.EachLineBinding, Is.Not.Null);
            Assert.That(hold.EachLineBinding, Is.SameAs(tap.EachLineBinding));
            Assert.That(line.DistanceProvider, Is.SameAs(tap.EachLineBinding));
        }

        /// <summary>Checks that a line going more than half way round starts from the second note instead.</summary>
        [Test]
        public void CreateEachLine_WrapsAroundFromTheSecondNote()
        {
            var timing = Timing(1.0, new SimaiNote());
            var existing = new EachLineBinding();
            var first = new TapPoolingInfo { StartPos = 1 };
            var second = new TapPoolingInfo { StartPos = 7, EachLineBinding = existing };

            var line = Invoke<EachLinePoolingInfo>("CreateEachLine", timing, first, second);

            Assert.That(line.StartPos, Is.EqualTo(7));
            Assert.That(line.CurvLength, Is.EqualTo(2));
            Assert.That(first.EachLineBinding, Is.SameAs(existing), "An existing binding is shared");
            Assert.That(line.DistanceProvider, Is.SameAs(existing));
        }

        /// <summary>Checks that notes on the same key get no each line and that touches get no binding.</summary>
        [Test]
        public void CreateEachLine_SkipsSameKeyAndIgnoresTouches()
        {
            var timing = Timing(1.0, new SimaiNote());
            var tap = new TapPoolingInfo { StartPos = 4 };
            var sameKey = new HoldPoolingInfo { StartPos = 4 };
            var touch = new TouchPoolingInfo { StartPos = 6 };

            Assert.That(PrivateMember.Invoke(_loader, "CreateEachLine", timing, tap, sameKey), Is.Null);

            var line = Invoke<EachLinePoolingInfo>("CreateEachLine", timing, touch, tap);

            Assert.That(line.StartPos, Is.EqualTo(4));
            Assert.That(line.CurvLength, Is.EqualTo(2));
            Assert.That(tap.EachLineBinding, Is.Not.Null);
        }

        /// <summary>Checks that adjacent touches share a group and distant touches get their own group.</summary>
        [Test]
        public void AllocTouchGroup_GroupsAdjacentSensors()
        {
            var a1 = new TouchPoolingInfo { SensorPos = SensorArea.A1 };
            var b1 = new TouchPoolingInfo { SensorPos = SensorArea.B1 };
            var c = new TouchPoolingInfo { SensorPos = SensorArea.C };
            var a5 = new TouchPoolingInfo { SensorPos = SensorArea.A5 };
            var e5 = new TouchPoolingInfo { SensorPos = SensorArea.E5 };
            var members = new List<ITouchGroupInfoProvider> { a1, b1, c, a5, e5 };

            var task = (Task)PrivateMember.Invoke(_loader, "AllocTouchGroup", members, CancellationToken.None)!;
            Assert.That(task.Wait(TimeSpan.FromSeconds(10)), Is.True);

            Assert.That(a1.GroupInfo, Is.Not.Null);
            Assert.That(b1.GroupInfo, Is.SameAs(a1.GroupInfo));
            Assert.That(c.GroupInfo, Is.SameAs(a1.GroupInfo), "C is adjacent to B1");
            Assert.That(a1.GroupInfo!.Members, Has.Length.EqualTo(3));
            Assert.That(a5.GroupInfo, Is.Not.SameAs(a1.GroupInfo));
            Assert.That(e5.GroupInfo, Is.SameAs(a5.GroupInfo));
            Assert.That(a5.GroupInfo!.Members, Has.Length.EqualTo(2));
        }

        /// <summary>Checks that touch holds on the same sensor share one hold group.</summary>
        [Test]
        public void AllocTouchHoldGroup_GroupsTouchHolds()
        {
            var first = new TouchHoldPoolingInfo { SensorPos = SensorArea.D1 };
            var second = new TouchHoldPoolingInfo { SensorPos = SensorArea.A1 };
            var lone = new TouchHoldPoolingInfo { SensorPos = SensorArea.D5 };
            var members = new List<ITouchHoldGroupInfoProvider> { first, second, lone };

            var task = (Task)PrivateMember.Invoke(_loader, "AllocTouchHoldGroup", members, CancellationToken.None)!;
            Assert.That(task.Wait(TimeSpan.FromSeconds(10)), Is.True);

            Assert.That(first.TouchHoldGroupInfo, Is.Not.Null);
            Assert.That(second.TouchHoldGroupInfo, Is.SameAs(first.TouchHoldGroupInfo));
            Assert.That(lone.TouchHoldGroupInfo, Is.Not.SameAs(first.TouchHoldGroupInfo));
            Assert.That(first.TouchHoldGroupInfo!.Members, Has.Length.EqualTo(2));
        }

        /// <summary>Checks that a cancelled grouping task does not assign groups.</summary>
        [Test]
        public void AllocTouchGroup_HonoursCancellation()
        {
            var a1 = new TouchPoolingInfo { SensorPos = SensorArea.A1 };
            using var source = new CancellationTokenSource();
            source.Cancel();

            var task = (Task)PrivateMember.Invoke(_loader, "AllocTouchGroup", new List<ITouchGroupInfoProvider> { a1 }, source.Token)!;

            Assert.Throws<AggregateException>(() => task.Wait(TimeSpan.FromSeconds(10)));
            Assert.That(a1.GroupInfo, Is.Null);
        }

        /// <summary>Checks the syntax error messages with and without note content.</summary>
        [Test]
        public void BuildSyntaxErrorMessage_FormatsThePosition()
        {
            Assert.That(PrivateMember.Invoke<string>(_loader, "BuildSyntaxErrorMessage", 3, 9, "1-x"), Is.EqualTo("(at L3:C9) \"1-x\" is not a valid note syntax"));
            Assert.That(PrivateMember.Invoke<string>(_loader, "BuildSyntaxErrorMessage", 3, 9), Is.EqualTo("(at L3:C9)"));
        }

        /// <summary>Checks that invalid slide chains are rejected as simai syntax errors before any slide is created.</summary>
        /// <param name="content">The invalid slide chain.</param>
        [TestCase("13-5[8:1]")]
        [TestCase("1-3[8:1]-5")]
        [TestCase("1-3-5")]
        [TestCase("1-3[8:1]-5[8:1]-7")]
        public void CreateSlideGroup_RejectsInvalidChains(string content)
        {
            _loader.slidePrefab = CreateSlidePrefabs();
            var note = CreateFoldedSlide(content);

            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_loader, "CreateSlideGroup", Timing(1.0, note), note, new List<NotePoolingInfo?>()));
            var syntaxError = exception!.InnerException as InvalidSimaiSyntaxException;

            Assert.That(syntaxError, Is.Not.Null);
            Assert.That(syntaxError!.Content, Is.EqualTo(content));
        }

        /// <summary>Checks that malformed extended slides are rejected before any geometry is built.</summary>
        /// <param name="content">The malformed extended slide.</param>
        [TestCase("1K")]
        [TestCase("xK3")]
        [TestCase("1Kx")]
        public void CreateExtendSlide_RejectsMalformedCodes(string content)
        {
            var note = CreateFoldedSlide(content);

            var exception = Assert.Throws<TargetInvocationException>(() => PrivateMember.Invoke(_loader, "CreateExtendSlide", Timing(1.0, note), note, new List<NotePoolingInfo?>()));

            Assert.That(exception!.InnerException, Is.TypeOf<InvalidSimaiSyntaxException>());
        }

        /// <summary>Computes the note appear offset used for taps, holds and each lines at the default appear rate.</summary>
        /// <param name="speed">The note speed.</param>
        /// <returns>The appear offset in seconds, negative because notes appear early.</returns>
        private static float ExpectedAppearDiff(float speed)
        {
            return (-(1 - (AppearRate * 1.225f)) - (4.8f * AppearRate)) / (Math.Abs(speed) * AppearRate);
        }

        /// <summary>Creates a timing point holding notes.</summary>
        /// <param name="timing">The timing in seconds.</param>
        /// <param name="notes">The notes of the timing point.</param>
        /// <returns>The timing point.</returns>
        private static SimaiTimingPoint Timing(double timing, params SimaiNote[] notes)
        {
            return new SimaiTimingPoint(timing, notes, "test");
        }

        /// <summary>Invokes a private factory method of the loader.</summary>
        /// <typeparam name="T">The expected result type.</typeparam>
        /// <param name="name">The method name.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The result.</returns>
        private T Invoke<T>(string name, params object?[] arguments)
        {
            return PrivateMember.Invoke<T>(_loader, name, arguments);
        }

        /// <summary>Creates the folded slide note type used by the slide factories.</summary>
        /// <param name="content">The slide text.</param>
        /// <returns>The note.</returns>
        private static SimaiNote CreateFoldedSlide(string content)
        {
            var type = typeof(NoteLoader).GetNestedType("FoldedSimaiNote", BindingFlags.NonPublic)!;
            var note = (SimaiNote)Activator.CreateInstance(type, true)!;
            note.Type = SimaiNoteType.Slide;
            note.StartPosition = 1;
            note.RawContent = content;
            note.SlideTime = 1;
            note.SlideStartTime = 1;
            PrivateMember.SetProperty(note, "Count", 1);
            return note;
        }

        /// <summary>Creates placeholder slide prefabs whose child count stands for the number of slide bars.</summary>
        /// <returns>The prefabs indexed like the loader's slide prefab table.</returns>
        private GameObject[] CreateSlidePrefabs()
        {
            var prefabs = new GameObject[43];
            for (var i = 0; i < prefabs.Length; i++)
            {
                prefabs[i] = _objects.Create($"Slide{i}");
                for (var bar = 0; bar < 3; bar++)
                {
                    _objects.Create($"Bar{bar}", prefabs[i].transform);
                }
            }
            return prefabs;
        }

        /// <summary>Gets the static tap flags of the loader.</summary>
        /// <returns>The flags indexed by key.</returns>
        private static bool[] HasTap()
        {
            return PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), "_isHasTap");
        }

        /// <summary>Clears every static collection the loader keeps between instances.</summary>
        private static void ResetStaticState()
        {
            PrivateMember.GetStaticField<Dictionary<int, int>>(typeof(NoteLoader), "_noteIndex").Clear();
            PrivateMember.GetStaticField<Dictionary<SensorArea, int>>(typeof(NoteLoader), "_touchIndex").Clear();
            PrivateMember.GetStaticField<List<SlideQueueInfo>>(typeof(NoteLoader), "_slideQueueInfos").Clear();
            foreach (var name in new[] { "_isHasTap", "_isHasHold", "_isHasTouch", "_isHasTouchHold" })
            {
                var flags = PrivateMember.GetStaticField<bool[]>(typeof(NoteLoader), name);
                Array.Clear(flags, 0, flags.Length);
            }
        }
    }
}

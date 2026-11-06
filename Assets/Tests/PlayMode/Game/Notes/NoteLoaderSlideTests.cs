using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MajdataPlay.Buffers;
using MajdataPlay.Game.Notes;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.Notes.PlayMode.Support;
using MajSimai;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode
{
    /// <summary>
    /// Drives the slide factories of <see cref="NoteLoader"/>: simai slides, connection slides included, are translated
    /// into slide code and built as <see cref="ExtendSlideDrop"/>, which uses the predefined table of its shape when
    /// there is one, or built natively as <see cref="SlideDrop"/> parts when translation is off; wifi slides keep
    /// <see cref="WifiDrop"/>.
    /// </summary>
    public class NoteLoaderSlideTests
    {
        /// <summary>The index of the wifi slide prefab in the loader's prefab table.</summary>
        private const int WifiPrefabIndex = 36;

        /// <summary>The index of the extend slide prefab in the loader's prefab table.</summary>
        private const int ExtendSlidePrefabIndex = 42;

        /// <summary>The gameplay objects of the current test.</summary>
        private GameplayRig _rig = null!;

        /// <summary>The loader under test, whose lifecycle messages never run.</summary>
        private NoteLoader _loader = null!;

        /// <summary>Builds the gameplay objects and gives the loader its slide prefabs, note layers and note pools.</summary>
        /// <returns>The setup coroutine.</returns>
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = new GameplayRig();
            yield return null;
            Assume.That(_rig.Runtime, Is.Not.Null, "The runtime database with the slide arrow prefab is required.");

            SlideDataBuilder.InitializeSlideAreaLookup();
            _loader = _rig.Loader;
            _loader.notes = _rig.NoteManager.gameObject;
            var prefabs = new GameObject[ExtendSlidePrefabIndex + 1];
            prefabs[WifiPrefabIndex] = NoteTemplates.Wifi(_rig.Templates, _rig.StarTemplate);
            prefabs[ExtendSlidePrefabIndex] = NoteTemplates.ExtendSlide(_rig.Templates, _rig.StarTemplate);
            _loader.slidePrefab = prefabs;
            PrivateMember.SetField(_loader, "_poolManager", _rig.PoolManager);
            ResetLoaderState();
        }

        /// <summary>Destroys the gameplay objects and clears the static queues of the loader.</summary>
        [TearDown]
        public void TearDown()
        {
            ResetLoaderState();
            _rig.Dispose();
        }

        /// <summary>Checks that a simai slide becomes one extend slide that uses the predefined table of its shape.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_UsesThePredefinedTableOfTheShape()
        {
            var eachNotes = CreateSlideGroup("1-4[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.PredefinedTable, Is.Not.Null);
            Assert.That(slide.PredefinedTable!.Name, Is.EqualTo("line4"));
            Assert.That(FirstAreas(slide), Is.EqualTo(new[] { SensorArea.A1, SensorArea.B2, SensorArea.B3, SensorArea.A4 }));
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(4));
            Assert.That(slide.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - 0.19f)).Within(1e-4f));
            Assert.That(slide.ConnectInfo.IsConnSlide, Is.False);
            Assert.That(eachNotes.OfType<TapPoolingInfo>().Single().StartPos, Is.EqualTo(1));
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>Checks that chart rotation turns the slide code, the judge table and the star together.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_RotatesTheSlideWithTheChart()
        {
            _loader.ChartRotation = 2;
            var eachNotes = CreateSlideGroup("1-4[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.PredefinedTable!.Name, Is.EqualTo("line4"));
            Assert.That(FirstAreas(slide), Is.EqualTo(new[] { SensorArea.A3, SensorArea.B4, SensorArea.B5, SensorArea.A6 }));
            Assert.That(slide.StartPos, Is.EqualTo(3));
            Assert.That(slide.EndPos, Is.EqualTo(6));
            Assert.That(eachNotes.OfType<TapPoolingInfo>().Single().StartPos, Is.EqualTo(3));
        }

        /// <summary>
        /// Checks that a connection slide is translated into one slide code and built as one extend slide over its whole
        /// duration, judged with the table generated for the whole path.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_TranslatesAConnectionSlideIntoOneSlide()
        {
            CreateSlideGroup("1-4[8:1]q7[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            var expected = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse("1A4Q0K7")).Select(x => (SensorArea)x.SensorA);
            Assert.That(slide.PredefinedTable, Is.Null);
            Assert.That(FirstAreas(slide), Is.EqualTo(expected));
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(7));
            Assert.That(slide.StartTiming, Is.EqualTo(1f));
            Assert.That(slide.Length, Is.EqualTo(1f));
            Assert.That(slide.ConnectInfo.IsConnSlide, Is.False);
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>Checks that the loader places the arrows of connection slides in the classic style by default.</summary>
        [Test]
        public void SlidePathStyle_IsClassicByDefault()
        {
            Assert.That(_loader.SlidePathStyle, Is.EqualTo(SlidePathStyle.Classic));
        }

        /// <summary>
        /// Checks that the loader places the arrows of a connection slide in its slide path style: 1^2^3 shows 14 arrows
        /// with a gap at key 2 in the classic style and 15 in the medium and perfect styles, and the star always passes
        /// the 17 points from key 1 to key 3, key 2 included.
        /// </summary>
        /// <param name="style">The slide path style of the loader.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_PlacesConnectionArrowsInTheSlidePathStyle([Values] SlidePathStyle style)
        {
            _loader.SlidePathStyle = style;
            CreateSlideGroup("1^2[8:1]^3[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            var arrowCount = style == SlidePathStyle.Classic ? 14 : 15;
            Assert.That(slide.transform.childCount, Is.EqualTo(arrowCount));
            var starPositions = PrivateMember.GetField<PooledList<Vector3>>(slide, "StarPositions");
            Assert.That(starPositions, Has.Count.EqualTo(17));
            var joint = NoteHelper.GetTapPosition(2, 4.8f);
            Assert.That(starPositions.Count(x => Vector2.Distance(x, joint) < 1e-3f), Is.EqualTo(1), "The star passes key 2");
            var arrowsAtJoint = Enumerable.Range(0, slide.transform.childCount)
                                          .Count(i => Vector2.Distance(slide.transform.GetChild(i).position, joint) < 1e-3f);
            Assert.That(arrowsAtJoint, Is.EqualTo(arrowCount == 14 ? 0 : 1));
        }

        /// <summary>
        /// Checks that a connection slide of simai segments followed by a slide code segment, 1^2^3Q9K4, is built as one
        /// extend slide over the whole chain, with every segment, the slide code one included, placed in the slide path
        /// style: three 45° arcs show 21 arrows in the classic style and 23 in the others, and the star passes 25 points.
        /// </summary>
        /// <param name="style">The slide path style of the loader.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_BuildsAConnectionSlideEndingWithSlideCode([Values] SlidePathStyle style)
        {
            _loader.SlidePathStyle = style;
            CreateSlideGroup("1^2^3Q9K4[1:100]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.SlideCode, Is.EqualTo("1Q9A2Q9A3Q9K4"));
            var expected = SlideDataBuilder.BuildSlideAreas(SlideCodeParser.Parse("1Q9A2Q9A3Q9K4")).Select(x => (SensorArea)x.SensorA);
            Assert.That(FirstAreas(slide), Is.EqualTo(expected));
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(4));
            Assert.That(slide.StartTiming, Is.EqualTo(1f));
            Assert.That(slide.Length, Is.EqualTo(1f));
            Assert.That(slide.transform.childCount, Is.EqualTo(style == SlidePathStyle.Classic ? 21 : 23));
            Assert.That(PrivateMember.GetField<PooledList<Vector3>>(slide, "StarPositions"), Has.Count.EqualTo(25));
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Checks that a slide code segment may start a connection slide and that each segment may have its own duration.
        /// </summary>
        /// <param name="content">The slide text.</param>
        /// <param name="expectedCode">The slide code of the whole chain.</param>
        /// <param name="endPos">The end key of the chain.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        [TestCase("1Q9K3-7[1:1]", "1Q9A3K7", 7, ExpectedResult = null)]
        [TestCase("1^2[8:1]^3[8:1]Q9K4[8:1]", "1Q9A2Q9A3Q9K4", 4, ExpectedResult = null)]
        [TestCase("1Q9K3Q9K5[1:1]", "1Q9A3Q9K5", 5, ExpectedResult = null)]
        public IEnumerator CreateSlideGroup_ChainsSlideCodeSegments(string content, string expectedCode, int endPos)
        {
            CreateSlideGroup(content);
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.SlideCode, Is.EqualTo(expectedCode));
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(endPos));
        }

        /// <summary>
        /// Checks that a connection slide with a slide code segment is translated even when simai slides are built
        /// natively, because slide code has no slide prefab.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_TranslatesSlideCodeChainsWhenTranslationIsOff()
        {
            UseNativeLine3Prefab();
            CreateSlideGroup("1Q9K3-7[1:1]");
            yield return null;

            Assert.That(_rig.SlideParent.GetComponentsInChildren<SlideDrop>(), Is.Empty);
            Assert.That(SingleExtendSlide().SlideCode, Is.EqualTo("1Q9A3K7"));
        }

        /// <summary>Checks that wifi slides still create a wifi slide.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_KeepsWifiSlides()
        {
            CreateSlideGroup("1w5[8:1]");
            yield return null;

            Assert.That(_rig.SlideParent.GetComponentsInChildren<WifiDrop>(), Has.Length.EqualTo(1));
            Assert.That(_rig.SlideParent.GetComponentsInChildren<ExtendSlideDrop>(), Is.Empty);
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>Checks that the native path builds a simai slide from its slide prefab instead of translating it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_BuildsANativeSlideWhenTranslationIsOff()
        {
            UseNativeLine3Prefab();
            var eachNotes = CreateSlideGroup("1-3[8:1]");
            yield return null;

            var slides = _rig.SlideParent.GetComponentsInChildren<SlideDrop>();
            Assert.That(slides, Has.Length.EqualTo(1));
            Assert.That(_rig.SlideParent.GetComponentsInChildren<ExtendSlideDrop>(), Is.Empty);
            var slide = slides[0];
            Assert.That(slide.SlideType, Is.EqualTo("line3"));
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(3));
            Assert.That(slide.StartTiming, Is.EqualTo(1f));
            Assert.That(slide.Length, Is.EqualTo(1f));
            Assert.That(slide.ConnectInfo.IsConnSlide, Is.False);
            Assert.That(eachNotes.OfType<TapPoolingInfo>().Single().StartPos, Is.EqualTo(1));
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Checks that the native path chains the segments of a connection slide: they share the duration by their arrow
        /// counts, link to the previous part and count the judge areas of the whole chain once.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateSlideGroup_ChainsNativeConnectionSlides()
        {
            UseNativeLine3Prefab();
            CreateSlideGroup("1-3[8:1]-5[8:1]");
            yield return null;

            var slides = _rig.SlideParent.GetComponentsInChildren<SlideDrop>();
            Assert.That(slides, Has.Length.EqualTo(2));
            var head = slides.Single(x => x.ConnectInfo.IsGroupPartHead);
            var tail = slides.Single(x => x.ConnectInfo.IsGroupPartEnd);
            Assert.That(tail.Parent, Is.SameAs(head));
            Assert.That(head.StartPos, Is.EqualTo(1));
            Assert.That(tail.StartPos, Is.EqualTo(3));
            Assert.That(tail.EndPos, Is.EqualTo(5));
            Assert.That(head.Length, Is.EqualTo(0.5f).Within(1e-5f), "Equal arrow counts share the duration equally");
            Assert.That(tail.StartTiming, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(head.ConnectInfo.TotalJudgeQueueLen, Is.EqualTo(5), "Two line3 tables of three areas share one area");
            Assert.That(SlideQueue(), Has.Count.EqualTo(2));
        }

        /// <summary>Checks that a slide code that draws an official shape uses the predefined table of that shape.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateExtendSlide_UsesThePredefinedTableOfAnOfficialShape()
        {
            CreateExtendSlide("1P3K6[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.PredefinedTable!.Name, Is.EqualTo("ppqq6"));
            Assert.That(slide.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - 0.067f)).Within(1e-4f));
        }

        /// <summary>Checks that a slide code without a predefined shape uses the table generated by the slide code algorithm.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateExtendSlide_GeneratesTheTableOfOtherShapes()
        {
            CreateExtendSlide("1B46K1[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            var metadata = slide.Metadata!.Value;
            Assert.That(slide.PredefinedTable, Is.Null);
            Assert.That(FirstAreas(slide), Is.EqualTo(metadata.JudgeAreaQueue.Select(x => x.SensorA)));
            Assert.That(slide.JudgeTimingWithOffset, Is.EqualTo(1f + (1f - metadata.SlideConst)).Within(1e-4f));
        }

        /// <summary>Checks that chart rotation also turns slides written as slide code.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CreateExtendSlide_RotatesTheSlideWithTheChart()
        {
            _loader.ChartRotation = 1;
            var eachNotes = CreateExtendSlide("1P3K6[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.PredefinedTable!.Name, Is.EqualTo("ppqq6"));
            Assert.That(FirstAreas(slide)[0], Is.EqualTo(SensorArea.A2));
            Assert.That(slide.StartPos, Is.EqualTo(2));
            Assert.That(slide.EndPos, Is.EqualTo(7));
            Assert.That(eachNotes.OfType<TapPoolingInfo>().Single().StartPos, Is.EqualTo(2));
        }

        /// <summary>
        /// Checks that a parsed slide with a head, a head tap followed by its slide since MajSimai 3.0, builds one slide and
        /// exactly one star, and no extra tap for the head.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSlides_HeadedSlideSpawnsOneStarAndNoTap()
        {
            var eachNotes = LoadSlides("1-4[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(4));
            Assert.That(slide.Multiple, Is.EqualTo(1));
            var star = eachNotes.OfType<TapPoolingInfo>().Single();
            Assert.That(star.IsStar, Is.True);
            Assert.That(star.IsDouble, Is.False);
            Assert.That(star.StartPos, Is.EqualTo(1));
            Assert.That(eachNotes, Has.Count.EqualTo(1));
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Checks that a same-head group builds both slides but only one double star, drawn by its first slide; the later
        /// slide has no head, as in the 2.x model.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSlides_SameHeadGroupSpawnsOneDoubleStar()
        {
            var eachNotes = LoadSlides("1-3[8:1]*-5[8:1]");
            yield return null;

            var slides = ExtendSlides();
            Assert.That(slides, Has.Length.EqualTo(2));
            Assert.That(slides.Select(x => x.EndPos), Is.EquivalentTo(new[] { 3, 5 }));
            Assert.That(slides.Select(x => x.IsEach), Is.All.True, "Two slides of one timing point are each slides");
            var star = eachNotes.OfType<TapPoolingInfo>().Single();
            Assert.That(star.IsDouble, Is.True);
            Assert.That(star.IsEach, Is.False, "The head is the only note that is not a slide");
            Assert.That(SlideQueue(), Has.Count.EqualTo(2));
        }

        /// <summary>Checks that a slide written without a head builds its slide and no star.</summary>
        /// <param name="fumen">The slide.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        [TestCase("1?-4[8:1]", ExpectedResult = null)]
        [TestCase("1!-4[8:1]", ExpectedResult = null)]
        public IEnumerator LoadSlides_HeadlessSlideSpawnsNoStar(string fumen)
        {
            var eachNotes = LoadSlides(fumen);
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.StartPos, Is.EqualTo(1));
            Assert.That(slide.EndPos, Is.EqualTo(4));
            Assert.That(eachNotes, Is.Empty);
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>Checks that a tap-head slide (<c>@</c>) still spawns one star, as the loader drew it before MajSimai 3.0.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSlides_TapHeadSlideSpawnsOneStar()
        {
            var eachNotes = LoadSlides("1@-4[8:1]");
            yield return null;

            SingleExtendSlide();
            var star = eachNotes.OfType<TapPoolingInfo>().Single();
            Assert.That(star.IsStar, Is.True);
            Assert.That(eachNotes, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Checks that the break and mine flags written before the slide path mark the star and those written after it mark
        /// the slide.
        /// </summary>
        /// <param name="fumen">The slide.</param>
        /// <param name="starBreak">Whether the star is a break.</param>
        /// <param name="starMine">Whether the star is a mine.</param>
        /// <param name="slideBreak">Whether the slide is a break.</param>
        /// <param name="slideMine">Whether the slide is a mine.</param>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        [TestCase("1b-4[8:1]", true, false, false, false, ExpectedResult = null)]
        [TestCase("1-4b[8:1]", false, false, true, false, ExpectedResult = null)]
        [TestCase("1m-4[8:1]", false, true, false, false, ExpectedResult = null)]
        [TestCase("1-4m[8:1]", false, false, false, true, ExpectedResult = null)]
        public IEnumerator LoadSlides_SplitsBreakAndMineBetweenHeadAndPath(string fumen, bool starBreak, bool starMine, bool slideBreak, bool slideMine)
        {
            var eachNotes = LoadSlides(fumen);
            yield return null;

            var slide = SingleExtendSlide();
            var star = eachNotes.OfType<TapPoolingInfo>().Single();
            Assert.That(star.IsBreak, Is.EqualTo(starBreak));
            Assert.That(star.IsMine, Is.EqualTo(starMine));
            Assert.That(slide.IsBreak, Is.EqualTo(slideBreak));
            Assert.That(slide.IsMine, Is.EqualTo(slideMine));
        }

        /// <summary>
        /// Checks that two identical slides with heads, <c>1-4[8:1]/1-4[8:1]</c>, fold into one slide with a multiple of 2
        /// and two stars, and that the heads are not built as taps.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LoadSlides_FoldsIdenticalHeadedSlides()
        {
            Assume.That(PrivateMember.GetStaticField<bool>(typeof(NoteLoader), "USERSETTING_NOTE_FOLDING"), Is.True);
            var eachNotes = LoadSlides("1-4[8:1]/1-4[8:1]");
            yield return null;

            var slide = SingleExtendSlide();
            Assert.That(slide.Multiple, Is.EqualTo(2));
            Assert.That(slide.IsEach, Is.True, "A folded slide is an each slide");
            var stars = eachNotes.OfType<TapPoolingInfo>().ToArray();
            Assert.That(stars, Has.Length.EqualTo(2));
            Assert.That(stars.Select(x => x.IsDouble), Is.All.True);
            Assert.That(stars.Select(x => x.IsEach), Is.All.True);
            Assert.That(eachNotes, Has.Count.EqualTo(2));
            Assert.That(SlideQueue(), Has.Count.EqualTo(1));
        }

        /// <summary>Turns translation off and gives the loader a line3 prefab, the only native shape these tests build.</summary>
        private void UseNativeLine3Prefab()
        {
            _loader.IsSimaiSlideTranslated = false;
            _loader.slidePrefab[0] = _rig.CreateSlideTemplate("line3");
        }

        /// <summary>Invokes the simai slide factory of the loader.</summary>
        /// <param name="content">The slide text without note flags.</param>
        /// <returns>The notes created for the slide heads.</returns>
        private List<NotePoolingInfo?> CreateSlideGroup(string content)
        {
            var (timing, note) = CreateHeadedSlide(content);
            var eachNotes = new List<NotePoolingInfo?>();
            PrivateMember.Invoke(_loader, "CreateSlideGroup", timing, note, eachNotes);
            return eachNotes;
        }

        /// <summary>Invokes the slide code factory of the loader.</summary>
        /// <param name="content">The slide code with its duration.</param>
        /// <returns>The notes created for the slide heads.</returns>
        private List<NotePoolingInfo?> CreateExtendSlide(string content)
        {
            var (timing, note) = CreateHeadedSlide(content);
            var eachNotes = new List<NotePoolingInfo?>();
            PrivateMember.Invoke(_loader, "CreateExtendSlide", timing, note, eachNotes);
            return eachNotes;
        }

        /// <summary>
        /// Creates a slide with a star head, starting at 1 s and lasting 1 s, in the shape MajSimai 3.x gives it: a head
        /// tap whose first related note is the slide, followed by the slide.
        /// </summary>
        /// <param name="content">The slide text.</param>
        /// <returns>The timing point that holds the head and the slide, and the boxed folded note the slide factories take.</returns>
        private static (SimaiTimingPoint Timing, object Note) CreateHeadedSlide(string content)
        {
            var slide = new SimaiNote
            {
                Type = SimaiNoteType.Slide,
                StartPosition = content[0] - '0',
                RawContent = content,
                SlideTime = 1,
                SlideStartTime = 1,
            };
            var head = new SimaiNote
            {
                Type = SimaiNoteType.Tap,
                StartPosition = slide.StartPosition,
                RawContent = content.Substring(0, 1),
                RelatedNotes = new[] { slide },
            };
            var timing = new SimaiTimingPoint(0.5, new[] { head, slide }, "test");
            var type = typeof(NoteLoader).GetNestedType("FoldedSimaiNote", BindingFlags.NonPublic)!;
            return (timing, Activator.CreateInstance(type, slide, head, 1)!);
        }

        /// <summary>
        /// Parses one timing point and builds its slides the way the loader does: its notes are folded, which leaves the
        /// slide heads out, and every slide is built with the head it carries.
        /// </summary>
        /// <param name="fumen">The notes of the timing point, only simai slides, without the comma.</param>
        /// <returns>The notes created for the slide heads.</returns>
        private List<NotePoolingInfo?> LoadSlides(string fumen)
        {
            var chart = SimaiParser.ParseChart(("(120){4}" + fumen + ",E").AsSpan());
            var timing = chart.NoteTimings[0];
            var helper = typeof(NoteLoader).GetNestedType("NoteCreateHelper", BindingFlags.NonPublic)!;
            var folded = PrivateMember.InvokeStatic<Array>(helper, "NoteFolding", new object?[] { timing.Notes });
            var eachNotes = new List<NotePoolingInfo?>();
            foreach (var entry in folded)
            {
                var note = PrivateMember.GetProperty<SimaiNote>(entry, "Note");
                Assert.That(note.Type, Is.EqualTo(SimaiNoteType.Slide), "A slide head is not built as a note of its own");
                PrivateMember.Invoke(_loader, "CreateSlideGroup", timing, entry, eachNotes);
            }
            return eachNotes;
        }

        /// <summary>Gets the extend slides created under the slide layer.</summary>
        /// <returns>The slides.</returns>
        private ExtendSlideDrop[] ExtendSlides()
        {
            return _rig.SlideParent.GetComponentsInChildren<ExtendSlideDrop>();
        }

        /// <summary>Gets the only extend slide created under the slide layer.</summary>
        /// <returns>The slide.</returns>
        private ExtendSlideDrop SingleExtendSlide()
        {
            var slides = _rig.SlideParent.GetComponentsInChildren<ExtendSlideDrop>();
            Assert.That(slides, Has.Length.EqualTo(1));
            return slides[0];
        }

        /// <summary>Gets the first sensor of every area in the judge queue of a slide.</summary>
        /// <param name="slide">The slide.</param>
        /// <returns>The first sensors in queue order.</returns>
        private static SensorArea[] FirstAreas(ExtendSlideDrop slide)
        {
            var queue = PrivateMember.GetField<Memory<SlideArea>[]>(slide, "JudgeQueues")[0].ToArray();
            return queue.Select(x => x.IncludedAreas[0]).ToArray();
        }

        /// <summary>Gets the static slide queue the loader fills.</summary>
        /// <returns>The queue.</returns>
        private static List<SlideQueueInfo> SlideQueue()
        {
            return PrivateMember.GetStaticField<List<SlideQueueInfo>>(typeof(NoteLoader), "_slideQueueInfos");
        }

        /// <summary>Clears the static queues of the loader and gives every key an empty note index.</summary>
        private static void ResetLoaderState()
        {
            SlideQueue().Clear();
            var noteIndex = PrivateMember.GetStaticField<Dictionary<int, int>>(typeof(NoteLoader), "_noteIndex");
            noteIndex.Clear();
            for (var key = 1; key <= 8; key++)
            {
                noteIndex[key] = 0;
            }
        }
    }
}

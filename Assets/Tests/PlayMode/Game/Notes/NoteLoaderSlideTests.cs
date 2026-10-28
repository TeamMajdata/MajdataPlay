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
            var note = CreateSlideNote(content);
            var eachNotes = new List<NotePoolingInfo?>();
            PrivateMember.Invoke(_loader, "CreateSlideGroup", new SimaiTimingPoint(0.5, new[] { note }, "test"), note, eachNotes);
            return eachNotes;
        }

        /// <summary>Invokes the slide code factory of the loader.</summary>
        /// <param name="content">The slide code with its duration.</param>
        /// <returns>The notes created for the slide heads.</returns>
        private List<NotePoolingInfo?> CreateExtendSlide(string content)
        {
            var note = CreateSlideNote(content);
            var eachNotes = new List<NotePoolingInfo?>();
            PrivateMember.Invoke(_loader, "CreateExtendSlide", new SimaiTimingPoint(0.5, new[] { note }, "test"), note, eachNotes);
            return eachNotes;
        }

        /// <summary>Creates the folded slide note type used by the slide factories, starting at 1 s and lasting 1 s.</summary>
        /// <param name="content">The slide text.</param>
        /// <returns>The note.</returns>
        private static SimaiNote CreateSlideNote(string content)
        {
            var type = typeof(NoteLoader).GetNestedType("FoldedSimaiNote", BindingFlags.NonPublic)!;
            var note = (SimaiNote)Activator.CreateInstance(type, true)!;
            note.Type = SimaiNoteType.Slide;
            note.StartPosition = content[0] - '0';
            note.RawContent = content;
            note.SlideTime = 1;
            note.SlideStartTime = 1;
            PrivateMember.SetProperty(note, "Count", 1);
            return note;
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

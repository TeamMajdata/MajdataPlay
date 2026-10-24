using System;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.SlideTest;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.SlideTest
{
    /// <summary>
    /// Checks the chart text <see cref="SlideTestManager"/> builds from each slide source. The manager is added in Edit
    /// Mode, where its lifecycle messages never run, so it builds charts without the global managers.
    /// </summary>
    public class SlideTestManagerTests
    {
        /// <summary>The object that holds the manager under test.</summary>
        private GameObject _gameObject = null!;

        /// <summary>The manager under test.</summary>
        private SlideTestManager _manager = null!;

        /// <summary>Creates the manager.</summary>
        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(SlideTestManagerTests));
            _manager = _gameObject.AddComponent<SlideTestManager>();
        }

        /// <summary>Destroys the manager.</summary>
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        /// <summary>Checks that simai notes are wrapped in a chart with the tempo and an end mark.</summary>
        [Test]
        public void BuildFumen_WrapsSimaiNotes()
        {
            PrivateMember.SetField(_manager, "_source", SlideTestSource.Simai);
            PrivateMember.SetField(_manager, "_simai", " 1-5[4:1]/2V75[8:1]\n");
            PrivateMember.SetField(_manager, "_bpm", 150.5f);

            Assert.That(_manager.BuildFumen(), Is.EqualTo("(150.5){4}1-5[4:1]/2V75[8:1],E"));
        }

        /// <summary>Checks that a slide code gets the duration appended.</summary>
        [Test]
        public void BuildFumen_AppendsTheDurationToSlideCode()
        {
            PrivateMember.SetField(_manager, "_source", SlideTestSource.SlideCode);
            PrivateMember.SetField(_manager, "_slideCode", "1P6K7");
            PrivateMember.SetField(_manager, "_duration", "[8:1]");

            Assert.That(_manager.BuildFumen(), Is.EqualTo("(120){4}1P6K7[8:1],E"));
        }

        /// <summary>
        /// Checks that every predefined table is rotated to the start key, so that the loader finds the same table again
        /// for the generated slide code.
        /// </summary>
        /// <param name="startKey">The start key.</param>
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(8)]
        public void BuildFumen_RotatesEveryPredefinedTable(int startKey)
        {
            PrivateMember.SetField(_manager, "_source", SlideTestSource.PredefinedTable);
            PrivateMember.SetField(_manager, "_startKey", startKey);
            foreach (var (name, _) in SlideTables.PredefinedShapes)
            {
                PrivateMember.SetField(_manager, "_tableName", name);

                var slideCode = ExtractSlideCode(_manager.BuildFumen());

                Assert.That(slideCode, Does.StartWith(startKey.ToString()), name);
                Assert.That(SlideTables.TryMatchSlideCode(slideCode, out var tableName, out var isMirrored, out var rotation), Is.True, slideCode);
                Assert.That(tableName, Is.EqualTo(name), slideCode);
                Assert.That(isMirrored, Is.False, slideCode);
                Assert.That(rotation, Is.EqualTo(startKey - 1), slideCode);
            }
        }

        /// <summary>
        /// Checks that a mirrored table draws the mirror image of its shape at the start key: the loader finds the table
        /// of the mirrored shape, for example line7 for line3, at the same rotation.
        /// </summary>
        [Test]
        public void BuildFumen_MirrorsEveryPredefinedTable()
        {
            const int StartKey = 8;
            PrivateMember.SetField(_manager, "_source", SlideTestSource.PredefinedTable);
            PrivateMember.SetField(_manager, "_startKey", StartKey);
            PrivateMember.SetField(_manager, "_isMirrored", true);
            foreach (var (name, shapeCode) in SlideTables.PredefinedShapes)
            {
                PrivateMember.SetField(_manager, "_tableName", name);

                var slideCode = ExtractSlideCode(_manager.BuildFumen());

                Assert.That(SlideTables.TryMatchSlideCode(SlideCodeShape.Mirror(shapeCode), out var expectedTable, out _, out _), Is.True, name);
                Assert.That(SlideTables.TryMatchSlideCode(slideCode, out var tableName, out _, out var rotation), Is.True, slideCode);
                Assert.That(tableName, Is.EqualTo(expectedTable), slideCode);
                Assert.That(rotation, Is.EqualTo(StartKey - 1), slideCode);
            }
        }

        /// <summary>Checks that the wifi table becomes a simai wifi slide to the opposite key.</summary>
        /// <param name="startKey">The start key.</param>
        /// <param name="endKey">The opposite key.</param>
        [TestCase(1, 5)]
        [TestCase(6, 2)]
        public void BuildFumen_BuildsAWifiSlideToTheOppositeKey(int startKey, int endKey)
        {
            PrivateMember.SetField(_manager, "_source", SlideTestSource.PredefinedTable);
            PrivateMember.SetField(_manager, "_tableName", SlideTestManager.WifiTableName);
            PrivateMember.SetField(_manager, "_startKey", startKey);

            Assert.That(_manager.BuildFumen(), Is.EqualTo($"(120){{4}}{startKey}w{endKey}[4:1],E"));
        }

        /// <summary>Checks that an unknown table name is reported.</summary>
        [Test]
        public void BuildFumen_RejectsAnUnknownTable()
        {
            PrivateMember.SetField(_manager, "_source", SlideTestSource.PredefinedTable);
            PrivateMember.SetField(_manager, "_tableName", "line9");

            Assert.Throws<ArgumentException>(() => _manager.BuildFumen());
        }

        /// <summary>Gets the slide code of a chart built from a predefined table with the default tempo and duration.</summary>
        /// <param name="fumen">The chart, for example <c>(120){4}3K5[4:1],E</c>.</param>
        /// <returns>The slide code, for example <c>3K5</c>.</returns>
        private static string ExtractSlideCode(string fumen)
        {
            const string Prefix = "(120){4}";
            const string Suffix = "[4:1],E";
            Assert.That(fumen, Does.StartWith(Prefix).And.EndWith(Suffix));
            return fumen.Substring(Prefix.Length, fumen.Length - Prefix.Length - Suffix.Length);
        }
    }
}

using MajdataPlay.Editor;
using MajdataPlay.Scenes.Game;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// Covers the binding of the object counter inspector and the formatting and statistics of its timing section,
    /// which do not need an inspector GUI event.
    /// </summary>
    public class ObjectCounterInspectorTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the test's objects, including inspectors.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that the object counter gets its inspector, which only repaints constantly in Play Mode.</summary>
        [Test]
        public void CreateEditor_UsesTheObjectCounterInspector()
        {
            var counter = _objects.Add<ObjectCounter>();

            var inspector = _objects.Track(UnityEditor.Editor.CreateEditor(counter));

            Assert.That(inspector, Is.InstanceOf<ObjectCounterInspector>());
            Assert.That(inspector.RequiresConstantRepaint(), Is.False);
        }

        /// <summary>Checks that a judge difference is signed and named fast or late.</summary>
        /// <param name="diffMSec">The difference in milliseconds.</param>
        /// <param name="expected">The expected text.</param>
        [TestCase(-12.345f, "-12.35 ms fast")]
        [TestCase(7f, "+7.00 ms late")]
        [TestCase(0f, "0.00 ms")]
        public void FormatDiff_SignsAndNamesTheDirection(float diffMSec, string expected)
        {
            Assert.That(ObjectCounterInspector.FormatDiff(diffMSec), Is.EqualTo(expected));
        }

        /// <summary>Checks that only the latest differences are listed, the newest last.</summary>
        [Test]
        public void FormatLatestDiffs_ListsTheLatestEight()
        {
            var diffs = new[] { 99f, 98f, 1f, -2f, 3f, -4f, 5f, -6f, 7f, 0f };

            Assert.That(ObjectCounterInspector.FormatLatestDiffs(diffs), Is.EqualTo("+1.0, -2.0, +3.0, -4.0, +5.0, -6.0, +7.0, 0.0"));
            Assert.That(ObjectCounterInspector.FormatLatestDiffs(new[] { -1.24f }), Is.EqualTo("-1.2"));
        }

        /// <summary>Checks the mean, the population standard deviation and the range of the differences.</summary>
        [Test]
        public void GetDiffStatistics_ComputesTheMeanDeviationAndRange()
        {
            ObjectCounterInspector.GetDiffStatistics(new[] { -10f, 0f, 10f, 20f }, out var mean, out var standardDeviation, out var min, out var max);

            Assert.That(mean, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(standardDeviation, Is.EqualTo(11.18034f).Within(1e-4f));
            Assert.That((min, max), Is.EqualTo((-10f, 20f)));
        }
    }
}

using System.Collections;
using MajdataPlay.Scenes.SlideGen;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SlideGenScene
{
    /// <summary>Covers the slide path sampling and code generation helpers of the slide generator tool scene.</summary>
    public class SlideGeneratorTests : SceneTestBase
    {
        /// <summary>The tolerance for geometric comparisons.</summary>
        private const float Tolerance = 1e-3f;

        /// <summary>The generator under test.</summary>
        private SlideGenerator _generator = null!;

        /// <summary>Creates the generator.</summary>
        [SetUp]
        public void SetUp()
        {
            _generator = Objects.Add<SlideGenerator>();
            _generator.rad = 2f;
        }

        /// <summary>Samples a slide path.</summary>
        /// <param name="type">The slide type.</param>
        /// <param name="position">The normalized position.</param>
        /// <returns>The position with the rotation in z.</returns>
        private Vector3 Point(string type, float position)
        {
            return PrivateMember.Invoke<Vector3>(_generator, "GetPointAtPosition", type, position);
        }

        /// <summary>Gets a point on a circle around the judge ring.</summary>
        /// <param name="distance">The radius.</param>
        /// <param name="position">The button position.</param>
        /// <returns>The point.</returns>
        private Vector3 Ring(float distance, float position)
        {
            return PrivateMember.Invoke<Vector3>(_generator, "GetPositionFromDistance", distance, position);
        }

        /// <summary>Checks that button positions lie on the requested radius at the expected angles.</summary>
        [Test]
        public void GetPositionFromDistance_PlacesButtonsOnTheRing()
        {
            var first = Ring(4.8f, 1f);
            var fifth = Ring(4.8f, 5f);

            Assert.That(first.x, Is.EqualTo(4.8f * Mathf.Cos(0.375f * Mathf.PI)).Within(Tolerance));
            Assert.That(first.y, Is.EqualTo(4.8f * Mathf.Sin(0.375f * Mathf.PI)).Within(Tolerance));
            Assert.That(((Vector2)fifth).magnitude, Is.EqualTo(4.8f).Within(Tolerance));
            Assert.That(fifth.x, Is.EqualTo(-first.x).Within(Tolerance));
            Assert.That(fifth.y, Is.EqualTo(-first.y).Within(Tolerance));
        }

        /// <summary>Checks that a straight slide interpolates between buttons seven and five with a constant rotation.</summary>
        [Test]
        public void StraightSlide_InterpolatesWithConstantRotation()
        {
            var start = Point("-", 0f);
            var middle = Point("-", 0.5f);
            var end = Point("-", 1f);

            Assert.That(Vector2.Distance(start, Ring(4.8f, 7f)), Is.LessThan(Tolerance));
            Assert.That(Vector2.Distance(end, Ring(4.8f, 5f)), Is.LessThan(Tolerance));
            Assert.That(Vector2.Distance(middle, (Ring(4.8f, 7f) + Ring(4.8f, 5f)) / 2f), Is.LessThan(Tolerance));
            Assert.That(start.z, Is.EqualTo(middle.z).Within(Tolerance));
            Assert.That(end.z, Is.EqualTo(middle.z).Within(Tolerance));
        }

        /// <summary>Checks that a clockwise arc stays on the outer ring and faces along its tangent.</summary>
        [Test]
        public void ArcSlide_StaysOnOuterRing()
        {
            var start = Point(">", 0f);
            var end = Point(">", 1f);

            Assert.That(((Vector2)start).magnitude, Is.EqualTo(4.8f).Within(Tolerance));
            Assert.That(((Vector2)end).magnitude, Is.EqualTo(4.8f).Within(Tolerance));
            Assert.That(start.z, Is.EqualTo(157.5f).Within(Tolerance));
            Assert.That(end.z, Is.EqualTo(112.5f).Within(Tolerance));
        }

        /// <summary>Checks that a full circle slide uses the configured radius and horizontal offset.</summary>
        [Test]
        public void CircleSlide_UsesRadiusAndOffset()
        {
            _generator.xoffset = 1f;

            var top = Point("qq", 0f);
            var right = Point("qq", 0.25f);

            Assert.That(top.x, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(top.y, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(top.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(right.x, Is.EqualTo(3f).Within(Tolerance));
            Assert.That(right.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(right.z, Is.EqualTo(-90f).Within(Tolerance));
        }

        /// <summary>Checks the three segments of a q slide: entry line, inner curve and exit line.</summary>
        [Test]
        public void QSlide_HasLineCurveAndLineSegments()
        {
            var entry = Point("q", 0f);
            var exit = Point("q", 1f);

            Assert.That(Vector2.Distance(entry, Ring(4.8f, 1f)), Is.LessThan(Tolerance));
            Assert.That(Vector2.Distance(exit, Ring(4.8f, 8f)), Is.LessThan(Tolerance));

            var lineLength = (Ring(2f, 7.5f) - Ring(4.8f, 1f)).magnitude;
            var curveLength = Mathf.PI * 2f * 2f * 0.75f;
            var curveCenter = (lineLength + (lineLength + curveLength)) / 2f / (lineLength + curveLength + lineLength);
            var curve = Point("q", curveCenter);

            Assert.That(((Vector2)curve).magnitude, Is.EqualTo(2f).Within(Tolerance));
        }

        /// <summary>Checks that positions outside a q slide and unknown slide types produce the origin.</summary>
        [Test]
        public void UnknownTypeOrOutOfRange_ReturnsOrigin()
        {
            Assert.That(Point("w", 0.5f), Is.EqualTo(Vector3.zero));
            Assert.That(Point("q", 1.5f), Is.EqualTo(Vector3.zero));
        }

        /// <summary>Checks that generating slides creates one sprite child per step strictly inside the path.</summary>
        [Test]
        public void GenerateSlides_CreatesOneSpritePerStep()
        {
            _generator.slide = CreateSprite("Slide");

            PrivateMember.Invoke(_generator, "GenerateSlides", ">", 0.25f);

            Assert.That(_generator.transform.childCount, Is.EqualTo(3));
            var renderer = _generator.transform.GetChild(0).GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.SameAs(_generator.slide));
            Assert.That(_generator.transform.GetChild(0).name, Is.EqualTo("Slide_0.75"));
            Assert.That(((Vector2)_generator.transform.GetChild(1).position).magnitude, Is.EqualTo(4.8f).Within(Tolerance));
        }

        /// <summary>Checks that array code generation emits a jagged Vector3 initializer.</summary>
        [Test]
        public void ArrayCodeGen_EmitsJaggedInitializer()
        {
            var rows = new[]
            {
                new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) },
                new[] { new Vector3(7, 8, 9) },
            };

            var code = PrivateMember.Invoke<string>(_generator, "ArrayCodeGen", "Star", rows);

            Assert.That(code, Does.StartWith("public static readonly Vector3[][] Star ="));
            Assert.That(code, Does.Contain("    new Vector3[] {new Vector3 (1f,2f,3f),new Vector3 (4f,5f,6f)},"));
            Assert.That(code, Does.Contain("    new Vector3[] {new Vector3 (7f,8f,9f)}"));
            Assert.That(code.TrimEnd(), Does.EndWith("};"));
        }

        /// <summary>Checks that starting the generator has no side effects.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_HasNoSideEffects()
        {
            yield return null;

            Assert.That(_generator.transform.childCount, Is.EqualTo(0));
        }
    }
}

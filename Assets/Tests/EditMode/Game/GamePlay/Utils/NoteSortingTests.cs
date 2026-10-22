using System;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using Sorting = MajdataPlay.Scenes.Game.Utils.NoteHelper.Sorting;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Utils
{
    /// <summary>
    /// Checks the gameplay depth sorting of <see cref="NoteHelper.Sorting"/>, porting the exact-grid expectations of the
    /// standalone note sorting validation.
    /// </summary>
    public class NoteSortingTests
    {
        /// <summary>The depth between two gameplay layers.</summary>
        private const float LayerSpacing = 0.5f;

        /// <summary>The depth between two adjacent sort orders.</summary>
        private const float OrderStep = 1f / 131072f;

        /// <summary>The number of gameplay layers.</summary>
        private const int LayerCount = 7;

        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys every object created by the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks the back boundary and centre depth of every layer.</summary>
        [Test]
        public void GetZIndex_LayerBoundariesAndCentres()
        {
            for (var layer = 0; layer < LayerCount; layer++)
            {
                var back = -layer * LayerSpacing;
                Assert.That(Sorting.GetZIndex((Sorting.NoteLayer)layer, short.MinValue), Is.EqualTo(back), $"Back of layer {layer}");
                Assert.That(Sorting.GetZIndex((Sorting.NoteLayer)layer), Is.EqualTo(back - (LayerSpacing / 2)), $"Centre of layer {layer}");
            }
        }

        /// <summary>Checks that every sampled order lies on the exact grid and that larger orders are in front.</summary>
        [Test]
        public void GetZIndex_OrdersLieOnAnExactGrid()
        {
            for (var layer = 0; layer < LayerCount; layer++)
            {
                var back = -layer * LayerSpacing;
                var front = back - LayerSpacing;
                var previous = float.PositiveInfinity;
                for (var order = (int)short.MinValue; order <= short.MaxValue; order += 257)
                {
                    var z = Sorting.GetZIndex((Sorting.NoteLayer)layer, order);
                    Assert.That(z, Is.EqualTo(back - ((order - short.MinValue) * OrderStep)), $"Exact depth {layer}/{order}");
                    Assert.That(z <= back && z > front, Is.True, $"Bounded depth {layer}/{order}");
                    Assert.That(z, Is.LessThan(previous), $"Priority {layer}/{order}");
                    previous = z;
                }
                Assert.That(Sorting.GetZIndex((Sorting.NoteLayer)layer, short.MaxValue), Is.LessThan(Sorting.GetZIndex((Sorting.NoteLayer)layer, short.MaxValue - 1)));
            }
        }

        /// <summary>Checks that orders outside the signed 16-bit range are clamped to the boundary depths.</summary>
        [Test]
        public void GetZIndex_ClampsOutOfRangeOrders()
        {
            foreach (Sorting.NoteLayer layer in Enum.GetValues(typeof(Sorting.NoteLayer)))
            {
                Assert.That(Sorting.GetZIndex(layer, int.MinValue), Is.EqualTo(Sorting.GetZIndex(layer, short.MinValue)));
                Assert.That(Sorting.GetZIndex(layer, short.MinValue - 1), Is.EqualTo(Sorting.GetZIndex(layer, short.MinValue)));
                Assert.That(Sorting.GetZIndex(layer, int.MaxValue), Is.EqualTo(Sorting.GetZIndex(layer, short.MaxValue)));
                Assert.That(Sorting.GetZIndex(layer, short.MaxValue + 1), Is.EqualTo(Sorting.GetZIndex(layer, short.MaxValue)));
            }
        }

        /// <summary>Checks that the depth ranges of consecutive layers do not overlap.</summary>
        [Test]
        public void GetZIndex_LayersAreDisjoint()
        {
            for (var layer = 1; layer < LayerCount; layer++)
            {
                var backOfThis = Sorting.GetZIndex((Sorting.NoteLayer)layer, int.MinValue);
                var frontOfPrevious = Sorting.GetZIndex((Sorting.NoteLayer)(layer - 1), int.MaxValue);
                Assert.That(backOfThis, Is.LessThan(frontOfPrevious), $"Layer {layer}");
            }
        }

        /// <summary>Checks that applying a layer resets the renderer sorting and sets the local depth relative to the parent.</summary>
        [Test]
        public void Apply_ResetsSortingAndSetsTheLocalDepth()
        {
            var renderer = _objects.Add<SpriteRenderer>();
            renderer.sortingOrder = 42;
            renderer.spriteSortPoint = SpriteSortPoint.Center;
            renderer.transform.localPosition = new Vector3(1, 2, 3);

            Sorting.Apply(renderer, Sorting.NoteLayer.Touch, 100, -0.25f);

            Assert.That(renderer.sortingLayerID, Is.EqualTo(0));
            Assert.That(renderer.sortingOrder, Is.EqualTo(0));
            Assert.That(renderer.spriteSortPoint, Is.EqualTo(SpriteSortPoint.Pivot));
            Assert.That(renderer.transform.localPosition.x, Is.EqualTo(1f));
            Assert.That(renderer.transform.localPosition.y, Is.EqualTo(2f));
            Assert.That(renderer.transform.localPosition.z, Is.EqualTo(Sorting.GetZIndex(Sorting.NoteLayer.Touch, 100) + 0.25f).Within(1e-6f));
        }

        /// <summary>Checks that moving in world space keeps the world depth.</summary>
        [Test]
        public void SetPosition_KeepsTheWorldDepth()
        {
            var transform = _objects.Create().transform;
            transform.position = new Vector3(0, 0, 7);

            Sorting.SetPosition(transform, new Vector3(4, 5, 99));

            Assert.That(transform.position, Is.EqualTo(new Vector3(4, 5, 7)));
        }

        /// <summary>Checks that moving in the parent plane keeps the local depth under a translated parent.</summary>
        [Test]
        public void SetLocalPosition_KeepsTheLocalDepth()
        {
            var parent = _objects.Create("Parent").transform;
            parent.position = new Vector3(10, 0, 100);
            var child = _objects.Create("Child", parent).transform;
            child.localPosition = new Vector3(0, 0, -0.5f);

            Sorting.SetLocalPosition(child, new Vector3(1, 2, 3));

            Assert.That(child.localPosition, Is.EqualTo(new Vector3(1, 2, -0.5f)));
            Assert.That(child.position.z, Is.EqualTo(99.5f).Within(1e-5f));
        }

        /// <summary>Checks that only the local depth changes.</summary>
        [Test]
        public void SetLocalZPosition_ChangesOnlyTheDepth()
        {
            var transform = _objects.Create().transform;
            transform.localPosition = new Vector3(3, 4, 5);

            Sorting.SetLocalZPosition(transform, -1.5f);

            Assert.That(transform.localPosition, Is.EqualTo(new Vector3(3, 4, -1.5f)));
        }
    }
}

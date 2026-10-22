using System;
using System.Linq;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Base
{
    /// <summary>Covers the general purpose extension methods in the Base folder.</summary>
    public class ExtensionMethodTests
    {
        /// <summary>A class with nested references, arrays and private state used to observe deep copies.</summary>
        private sealed class CloneProbe
        {
            /// <summary>A value field copied by value.</summary>
            public int Number;

            /// <summary>A string field shared by reference.</summary>
            public string Text = string.Empty;

            /// <summary>A nested object that must be copied.</summary>
            public CloneProbe? Child;

            /// <summary>A nested array that must be copied element by element.</summary>
            public CloneProbe?[]? Items;

            /// <summary>A private field that must also be copied.</summary>
            private int _secret;

            /// <summary>Gets or sets the private field.</summary>
            public int Secret
            {
                get => _secret;
                set => _secret = value;
            }
        }

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

        /// <summary>Checks that cloning null returns null and value types are returned unchanged.</summary>
        [Test]
        public void Clone_NullAndValueTypes()
        {
            CloneProbe? none = null;

            Assert.That(none.Clone(), Is.Null);
            Assert.That(42.Clone(), Is.EqualTo(42));
        }

        /// <summary>Checks that cloning copies nested objects, arrays and private fields instead of sharing them.</summary>
        [Test]
        public void Clone_CopiesObjectGraph()
        {
            var source = new CloneProbe
            {
                Number = 1,
                Text = "root",
                Child = new CloneProbe { Number = 2 },
                Items = new CloneProbe?[] { new CloneProbe { Number = 3 }, null },
                Secret = 7
            };

            var copy = source.Clone()!;

            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.Number, Is.EqualTo(1));
            Assert.That(copy.Text, Is.EqualTo("root"));
            Assert.That(copy.Secret, Is.EqualTo(7));
            Assert.That(copy.Child, Is.Not.SameAs(source.Child));
            Assert.That(copy.Child!.Number, Is.EqualTo(2));
            Assert.That(copy.Items, Is.Not.SameAs(source.Items));
            Assert.That(copy.Items![0], Is.Not.SameAs(source.Items[0]));
            Assert.That(copy.Items[0]!.Number, Is.EqualTo(3));
            Assert.That(copy.Items[1], Is.Null);

            copy.Child.Number = 99;
            Assert.That(source.Child.Number, Is.EqualTo(2));
        }

        /// <summary>Checks the integer type classification.</summary>
        [Test]
        public void IsIntType_RecognizesEveryIntegerType()
        {
            var integers = new[] { typeof(int), typeof(long), typeof(short), typeof(byte), typeof(uint), typeof(ulong), typeof(ushort), typeof(sbyte) };

            foreach (var type in integers)
            {
                Assert.That(type.IsIntType(), Is.True, type.Name);
                Assert.That(type.IsFloatType(), Is.False, type.Name);
            }
            Assert.That(typeof(string).IsIntType(), Is.False);
        }

        /// <summary>Checks the floating point type classification.</summary>
        [Test]
        public void IsFloatType_RecognizesEveryFloatingType()
        {
            var floats = new[] { typeof(float), typeof(double), typeof(decimal) };

            foreach (var type in floats)
            {
                Assert.That(type.IsFloatType(), Is.True, type.Name);
                Assert.That(type.IsIntType(), Is.False, type.Name);
            }
            Assert.That(typeof(bool).IsFloatType(), Is.False);
        }

        /// <summary>Checks that the direct children of a transform are enumerated in order.</summary>
        [Test]
        public void ToEnumerable_ListsDirectChildren()
        {
            var root = _objects.Create("Root").transform;
            var first = _objects.Create("First", root).transform;
            var second = _objects.Create("Second", root).transform;
            _objects.Create("Grandchild", first);

            var children = root.ToEnumerable().ToArray();

            Assert.That(children, Is.EqualTo(new[] { first, second }));
        }

        /// <summary>Checks that the indexed child component lookup returns the component and rejects invalid indices.</summary>
        [Test]
        public void GetComponentInChildren_ByIndex()
        {
            var root = _objects.Create("Root").transform;
            _objects.Create("First", root);
            var camera = _objects.Add<Camera>("Second", root);

            Assert.That(TransformExtensions.GetComponentInChildren<Camera>(root, 1), Is.SameAs(camera));
            Assert.That(TransformExtensions.GetComponentInChildren<Camera>(root, 0) == null, Is.True);
            Assert.Throws<IndexOutOfRangeException>(() => TransformExtensions.GetComponentInChildren<Camera>(root, 2));
        }

        /// <summary>Checks that every descendant is returned depth first.</summary>
        [Test]
        public void GetChildren_ListsDescendantsDepthFirst()
        {
            var root = _objects.Create("Root").transform;
            var first = _objects.Create("First", root).transform;
            var grandchild = _objects.Create("Grandchild", first).transform;
            var second = _objects.Create("Second", root).transform;

            var children = root.GetChildren();

            Assert.That(children, Is.EqualTo(new[] { first, grandchild, second }));
        }
    }
}

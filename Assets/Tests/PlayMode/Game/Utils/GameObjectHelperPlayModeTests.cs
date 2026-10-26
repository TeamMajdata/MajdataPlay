using System.Collections;
using MajdataPlay.Utils;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils.PlayMode
{
    /// <summary>Verifies that <see cref="GameObjectHelper"/> destroys objects and clears the caller's references in Play Mode.</summary>
    public class GameObjectHelperPlayModeTests
    {
        /// <summary>Checks that destroying through a reference clears the reference and destroys the object at the end of the frame.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Destroy_ClearsReferenceAndDestroysObject()
        {
            var gameObject = new GameObject("GameObjectHelperTarget");
            var reference = gameObject;

            GameObjectHelper.Destroy(ref reference);
            Assert.That(reference, Is.Null);

            yield return null;

            Assert.That(gameObject == null, Is.True);
        }

        /// <summary>Checks that a null reference is left untouched.</summary>
        [Test]
        public void Destroy_NullReference_DoesNothing()
        {
            GameObject? reference = null;

            Assert.DoesNotThrow(() => GameObjectHelper.Destroy(ref reference));
            Assert.That(reference, Is.Null);
        }

        /// <summary>Checks that every element of an array is destroyed and cleared, skipping null slots.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DestroyArray_ClearsEveryElement()
        {
            var first = new GameObject("First");
            var second = new GameObject("Second");
            var objects = new GameObject?[] { first, null, second };

            GameObjectHelper.Destroy(ref objects);

            Assert.That(objects, Is.All.Null);
            yield return null;
            Assert.That(first == null && second == null, Is.True);
        }
    }
}

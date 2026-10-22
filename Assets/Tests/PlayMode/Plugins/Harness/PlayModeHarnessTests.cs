using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Harness
{
    /// <summary>
    /// Verifies that PlayMode test assemblies run inside Play Mode, where Unity drives the player loop and calls the
    /// lifecycle messages of behaviours itself.
    /// </summary>
    public class PlayModeHarnessTests
    {
        /// <summary>Checks that Play Mode is active and that frames advance between yields.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayMode_AdvancesFrames()
        {
            Assert.That(Application.isPlaying, Is.True);
            var frame = Time.frameCount;

            yield return null;

            Assert.That(Time.frameCount, Is.GreaterThan(frame));
        }

        /// <summary>Checks that Unity calls Awake and Update on a behaviour created in Play Mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayMode_CallsLifecycleMessages()
        {
            using var objects = new TestObjects();
            var probe = objects.Add<PlayModeProbe>();

            Assert.That(probe.Awakened, Is.True);

            yield return null;
            yield return null;

            Assert.That(probe.UpdateCount, Is.GreaterThan(0));
        }
    }
}

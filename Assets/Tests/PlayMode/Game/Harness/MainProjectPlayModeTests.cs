using System.Collections;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Harness.PlayMode
{
    /// <summary>
    /// Verifies that the Game PlayMode tests compile into MajdataPlay.Tests.Game.PlayMode, separate from the main
    /// project's MajdataPlay.Core, and run in Play Mode, where Unity drives the lifecycle of behaviours itself.
    /// </summary>
    public class MainProjectPlayModeTests
    {
        /// <summary>Checks that the test is compiled into its own test assembly rather than into MajdataPlay.Core.</summary>
        [Test]
        public void Tests_CompileIntoPlayModeTestAssembly()
        {
            var testAssembly = typeof(MainProjectPlayModeTests).Assembly;

            Assert.That(testAssembly.GetName().Name, Is.EqualTo("MajdataPlay.Tests.Game.PlayMode"));
            Assert.That(testAssembly, Is.Not.SameAs(typeof(MajEnv).Assembly));
        }

        /// <summary>Checks that Unity calls Awake, Start and Update on a main project behaviour in Play Mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PlayMode_DrivesMainProjectBehaviours()
        {
            Assert.That(Application.isPlaying, Is.True);
            using var objects = new TestObjects();
            var probe = objects.Add<MainProjectPlayModeProbe>();

            Assert.That(probe.Awakened, Is.True);

            yield return null;
            yield return null;

            Assert.That(probe.Started, Is.True);
            Assert.That(probe.UpdateCount, Is.GreaterThan(0));
        }
    }
}

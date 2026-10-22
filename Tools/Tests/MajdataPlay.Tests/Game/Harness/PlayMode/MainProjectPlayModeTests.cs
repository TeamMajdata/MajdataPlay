using System.Collections;
using MajdataPlay.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Harness.PlayMode
{
    /// <summary>
    /// Verifies that PlayMode tests staged from Game/&lt;Area&gt;/PlayMode compile into Assembly-CSharp and run in Play
    /// Mode, where Unity drives the lifecycle of main project behaviours itself.
    /// </summary>
    public class MainProjectPlayModeTests
    {
        /// <summary>Checks that the test is compiled into Assembly-CSharp next to the main project types.</summary>
        [Test]
        public void Tests_CompileIntoAssemblyCSharp()
        {
            Assert.That(typeof(MainProjectPlayModeTests).Assembly, Is.SameAs(typeof(MajEnv).Assembly));
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

using System.Collections;
using MajdataPlay.FFmpeg.Interop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Plugins.FFmpeg
{
    /// <summary>Verifies the hidden collector that keeps polling native texture retirements after players close.</summary>
    public class HardwareTextureRetirementPumpPlayModeTests
    {
        /// <summary>Counts the live collectors, including hidden ones.</summary>
        /// <returns>The number of collectors that have not been destroyed.</returns>
        private static int CountPumps()
        {
            var count = 0;
            foreach (var pump in Resources.FindObjectsOfTypeAll<HardwareTextureRetirementPump>())
            {
                if (pump != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Resets the shutdown flag and removes any remaining collector.</summary>
        [TearDown]
        public void TearDown()
        {
            PrivateMember.SetStaticField(typeof(HardwareTextureRetirementPump), "s_quitting", false);
            foreach (var pump in Resources.FindObjectsOfTypeAll<HardwareTextureRetirementPump>())
            {
                if (pump != null)
                {
                    Object.DestroyImmediate(pump.gameObject);
                }
            }

            PrivateMember.SetStaticField(typeof(HardwareTextureRetirementPump), "s_instance", null);
        }

        /// <summary>Checks that Ensure creates one hidden persistent collector that destroys itself when nothing is pending.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Ensure_CreatesSingleCollectorThatRetiresItself()
        {
            Assert.That(HardwareVideoPresenter.CollectRetiredTextures(), Is.True);

            HardwareTextureRetirementPump.Ensure();
            HardwareTextureRetirementPump.Ensure();

            Assert.That(CountPumps(), Is.EqualTo(1));
            var pump = PrivateMember.GetStaticField<HardwareTextureRetirementPump>(typeof(HardwareTextureRetirementPump), "s_instance");
            Assert.That(pump.gameObject.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            yield return PlayModeWait.Until(() => CountPumps() == 0, 2);

            Assert.That(CountPumps(), Is.EqualTo(0));
            Assert.That(PrivateMember.GetStaticField<HardwareTextureRetirementPump?>(typeof(HardwareTextureRetirementPump), "s_instance"), Is.Null);
        }

        /// <summary>Checks that no collector is created after the application starts quitting, and that the flag resets.</summary>
        [Test]
        public void Ensure_AfterQuit_DoesNothing()
        {
            HardwareTextureRetirementPump.Ensure();
            var pump = PrivateMember.GetStaticField<HardwareTextureRetirementPump>(typeof(HardwareTextureRetirementPump), "s_instance");
            PrivateMember.Invoke(pump, "OnApplicationQuit");
            Object.DestroyImmediate(pump.gameObject);

            Assert.That(PrivateMember.GetStaticField<HardwareTextureRetirementPump?>(typeof(HardwareTextureRetirementPump), "s_instance"), Is.Null);
            HardwareTextureRetirementPump.Ensure();
            Assert.That(CountPumps(), Is.EqualTo(0));

            PrivateMember.InvokeStatic(typeof(HardwareTextureRetirementPump), "ResetQuitting");
            HardwareTextureRetirementPump.Ensure();
            Assert.That(CountPumps(), Is.EqualTo(1));
        }
    }
}

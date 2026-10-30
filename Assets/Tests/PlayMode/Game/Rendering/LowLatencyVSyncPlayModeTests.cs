using System;
using System.Collections;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering.PlayMode
{
    /// <summary>Verifies that <see cref="LowLatencyVSync"/> runs first in the player loop and only paces Vulkan with VSync.</summary>
    public class LowLatencyVSyncPlayModeTests
    {
        /// <summary>Counts the pacing steps at the start of the TimeUpdate phase.</summary>
        /// <returns>The number of leading TimeUpdate systems whose delegate belongs to <see cref="LowLatencyVSync"/>.</returns>
        private static int CountPacingStepsAtStartOfTimeUpdate()
        {
            foreach (var phase in PlayerLoop.GetCurrentPlayerLoop().subSystemList)
            {
                if (phase.type != typeof(TimeUpdate))
                {
                    continue;
                }

                var count = 0;
                foreach (var system in phase.subSystemList)
                {
                    if (system.updateDelegate?.Method.DeclaringType != typeof(LowLatencyVSync))
                    {
                        break;
                    }
                    count++;
                }
                return count;
            }

            throw new InvalidOperationException("The player loop has no TimeUpdate phase.");
        }

        /// <summary>Checks that installing twice inserts one pacing step as the first TimeUpdate system.</summary>
        [Test]
        public void Install_InsertsOneStepAtTheStartOfTimeUpdate()
        {
            LowLatencyVSync.Install();
            LowLatencyVSync.Install();

            Assert.That(CountPacingStepsAtStartOfTimeUpdate(), Is.EqualTo(1));
        }

        /// <summary>Checks that installing twice inserts one frame end step as the last PostLateUpdate system.</summary>
        [Test]
        public void Install_InsertsOneFrameEndStepAtTheEndOfPostLateUpdate()
        {
            LowLatencyVSync.Install();
            LowLatencyVSync.Install();

            foreach (var phase in PlayerLoop.GetCurrentPlayerLoop().subSystemList)
            {
                if (phase.type != typeof(PostLateUpdate))
                {
                    continue;
                }

                var steps = 0;
                foreach (var system in phase.subSystemList)
                {
                    if (system.updateDelegate?.Method.DeclaringType == typeof(LowLatencyVSync))
                    {
                        steps++;
                    }
                }
                var last = phase.subSystemList[phase.subSystemList.Length - 1];
                Assert.That(steps, Is.EqualTo(1));
                Assert.That(last.updateDelegate?.Method.DeclaringType, Is.EqualTo(typeof(LowLatencyVSync)));
                return;
            }

            Assert.Fail("The player loop has no PostLateUpdate phase.");
        }

        /// <summary>Checks that frames are not paced when the backend is not Vulkan or VSync is off.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator NonVulkanOrNoVSync_DoesNotPace()
        {
            var previousVSync = QualitySettings.vSyncCount;
            try
            {
                LowLatencyVSync.Install();
                QualitySettings.vSyncCount = 0;
                yield return null;
                yield return null;

                Assert.That(LowLatencyVSync.IsActive, Is.False, "VSync off");
                Assert.That(LowLatencyVSync.LastWaitMilliseconds, Is.EqualTo(0));

                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                {
                    QualitySettings.vSyncCount = 1;
                    yield return null;
                    yield return null;

                    Assert.That(LowLatencyVSync.IsActive, Is.False, "not Vulkan");
                }
            }
            finally
            {
                QualitySettings.vSyncCount = previousVSync;
            }
        }
    }
}

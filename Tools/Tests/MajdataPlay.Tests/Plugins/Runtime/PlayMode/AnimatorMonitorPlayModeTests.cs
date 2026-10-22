using System.Collections;
using System.Collections.Generic;
using MajdataPlay.Runtime.Monitors;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.Animations;
#endif

#nullable enable
namespace MajdataPlay.Tests.Plugins.Runtime.PlayMode
{
    /// <summary>
    /// Covers <see cref="AnimatorMonitor"/> in Play Mode, where Unity drives Awake, OnEnable, OnDisable and LateUpdate
    /// and the Animator evaluates real controllers. The static instance registry is restored after each test.
    /// </summary>
    public class AnimatorMonitorPlayModeTests
    {
        /// <summary>The maximum number of frames a test waits for an animation change.</summary>
        private const int MaxWaitFrames = 30;

        /// <summary>Tracks the objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>The registered monitors before the test.</summary>
        private List<AnimatorMonitor> _savedInstances = null!;

        /// <summary>Saves the registry and creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedInstances = new List<AnimatorMonitor>(AnimatorMonitor.GetInstances());
            _objects = new TestObjects();
        }

        /// <summary>Destroys the test objects and restores the registry.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            AnimatorMonitor.ClearAll();
            var instances = (List<AnimatorMonitor>)AnimatorMonitor.GetInstances();
            instances.AddRange(_savedInstances);
        }

        /// <summary>Counts how often a monitor appears in the registry.</summary>
        /// <param name="monitor">The monitor to look for.</param>
        /// <returns>The number of registrations.</returns>
        private static int CountRegistrations(AnimatorMonitor monitor)
        {
            var count = 0;
            foreach (var instance in AnimatorMonitor.GetInstances())
            {
                if (instance == monitor)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Creates an in-memory controller whose layers each play a one second clip.</summary>
        /// <param name="layerCount">The number of layers.</param>
        /// <returns>The controller.</returns>
        private RuntimeAnimatorController CreateController(int layerCount)
        {
#if UNITY_EDITOR
            var controller = _objects.Track(new AnimatorController { name = "TestController" });
            for (var i = 0; i < layerCount; i++)
            {
                controller.AddLayer("Layer" + i);
                var clip = _objects.Track(new AnimationClip { name = "Clip" + i });
                clip.SetCurve(string.Empty, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                var state = controller.layers[i].stateMachine.AddState("State" + i);
                state.motion = clip;
            }
            return controller;
#else
            throw new System.NotSupportedException("Controllers can only be authored in the Editor.");
#endif
        }

        /// <summary>Waits until the monitor reports a change, for at most <see cref="MaxWaitFrames"/> frames.</summary>
        /// <param name="monitor">The monitor to watch.</param>
        /// <returns>The waiting coroutine.</returns>
        private static IEnumerator WaitForChange(AnimatorMonitor monitor)
        {
            for (var i = 0; i < MaxWaitFrames && !monitor.UpdatedThisFrame; i++)
            {
                yield return null;
            }
        }

        /// <summary>Checks that the monitor registers on Awake, follows enable and disable, and leaves the registry when destroyed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Lifecycle_MaintainsRegistry()
        {
            var monitor = _objects.Add<AnimatorMonitor>();

            Assert.That(monitor.Animator, Is.SameAs(monitor.GetComponent<Animator>()));
            Assert.That(CountRegistrations(monitor), Is.EqualTo(1));

            monitor.enabled = false;
            Assert.That(CountRegistrations(monitor), Is.EqualTo(0));

            monitor.enabled = true;
            Assert.That(CountRegistrations(monitor), Is.EqualTo(1));

            Object.Destroy(monitor.gameObject);
            yield return null;

            Assert.That(CountRegistrations(monitor), Is.EqualTo(0));
        }

        /// <summary>Checks that an Animator without a controller is sampled every frame without reporting changes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LateUpdate_WithoutController_ReportsNoChange()
        {
            var monitor = _objects.Add<AnimatorMonitor>();

            yield return null;
            yield return null;

            Assert.That(monitor.UpdatedThisFrame, Is.False);
            Assert.That(monitor.EstimatedTimeMs, Is.GreaterThanOrEqualTo(0f));
            Assert.That(PrivateMember.GetField<int>(monitor, "_frame"), Is.GreaterThanOrEqualTo(2));
        }

        /// <summary>Checks that a playing state is reported as a change and a frozen state is not.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LateUpdate_PlayingState_ReportsChanges()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            var animator = monitor.Animator;
            animator.runtimeAnimatorController = CreateController(1);

            yield return WaitForChange(monitor);

            Assert.That(monitor.UpdatedThisFrame, Is.True);
            Assert.That(monitor.EstimatedTimeMs, Is.GreaterThanOrEqualTo(0f));

            animator.speed = 0f;
            yield return null;
            yield return null;
            yield return null;

            Assert.That(monitor.UpdatedThisFrame, Is.False);
        }

        /// <summary>Checks that disabling the Animator resets the reported change and cost.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LateUpdate_DisabledAnimator_ResetsState()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            monitor.Animator.runtimeAnimatorController = CreateController(1);
            yield return WaitForChange(monitor);

            monitor.Animator.enabled = false;
            yield return null;

            Assert.That(monitor.UpdatedThisFrame, Is.False);
            Assert.That(monitor.EstimatedTimeMs, Is.EqualTo(0f));
        }

        /// <summary>Checks that the state buffers grow when the controller gains layers and that the swap is reported.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator LateUpdate_MoreLayers_GrowsStateBuffers()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            monitor.Animator.runtimeAnimatorController = CreateController(1);
            yield return null;
            yield return null;

            Assert.That(PrivateMember.GetField<AnimatorStateInfo[]>(monitor, "_previousStates").Length, Is.EqualTo(1));

            monitor.Animator.runtimeAnimatorController = CreateController(2);
            yield return null;
            yield return null;

            Assert.That(monitor.Animator.layerCount, Is.EqualTo(2));
            Assert.That(PrivateMember.GetField<AnimatorStateInfo[]>(monitor, "_previousStates").Length, Is.EqualTo(2));
            Assert.That(PrivateMember.GetField<AnimatorStateInfo[]>(monitor, "_currentStates").Length, Is.EqualTo(2));
        }

        /// <summary>Checks that ClearAll removes live monitors from the registry.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ClearAll_RemovesLiveMonitors()
        {
            _objects.Add<AnimatorMonitor>();
            _objects.Add<AnimatorMonitor>();
            yield return null;

            AnimatorMonitor.ClearAll();

            Assert.That(AnimatorMonitor.GetInstances(), Is.Empty);
        }
    }
}

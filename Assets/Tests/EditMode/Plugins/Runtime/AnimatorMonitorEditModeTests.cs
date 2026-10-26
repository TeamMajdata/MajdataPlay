using System.Collections.Generic;
using MajdataPlay.Runtime.Monitors;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Runtime
{
    /// <summary>
    /// Covers the branches of <see cref="AnimatorMonitor"/> that Play Mode cannot reach, by delivering its messages
    /// manually in Edit Mode, where Unity does not call Awake. The static instance registry is restored after each test.
    /// </summary>
    public class AnimatorMonitorEditModeTests
    {
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

        /// <summary>Checks that the required Animator is added alongside the monitor.</summary>
        [Test]
        public void AddComponent_AddsRequiredAnimator()
        {
            var monitor = _objects.Add<AnimatorMonitor>();

            Assert.That(monitor.GetComponent<Animator>(), Is.Not.Null);
        }

        /// <summary>Checks that LateUpdate before Awake, without a cached Animator, reports no update and no cost.</summary>
        [Test]
        public void LateUpdate_BeforeAwake_ReportsNothing()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            PrivateMember.SetField(monitor, "_updated", true);
            PrivateMember.SetField(monitor, "_estimatedTimeMs", 5f);

            UnityMessages.Send(monitor, "LateUpdate");

            Assert.That(monitor.Animator, Is.Null);
            Assert.That(monitor.UpdatedThisFrame, Is.False);
            Assert.That(monitor.EstimatedTimeMs, Is.EqualTo(0f));
        }

        /// <summary>Checks that Awake caches the Animator and registers the monitor exactly once.</summary>
        [Test]
        public void Awake_CachesAnimatorAndRegistersOnce()
        {
            var monitor = _objects.Add<AnimatorMonitor>();

            UnityMessages.Send(monitor, "Awake");
            UnityMessages.Send(monitor, "OnEnable");

            Assert.That(monitor.Animator, Is.SameAs(monitor.GetComponent<Animator>()));
            Assert.That(CountRegistrations(monitor), Is.EqualTo(1));
        }

        /// <summary>Checks that OnDisable unregisters the monitor and OnEnable registers it again.</summary>
        [Test]
        public void OnDisableAndOnEnable_UpdateRegistry()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            UnityMessages.Send(monitor, "Awake");

            UnityMessages.Send(monitor, "OnDisable");
            Assert.That(CountRegistrations(monitor), Is.EqualTo(0));

            UnityMessages.Send(monitor, "OnEnable");
            Assert.That(CountRegistrations(monitor), Is.EqualTo(1));
        }

        /// <summary>Checks that ClearAll empties the registry.</summary>
        [Test]
        public void ClearAll_EmptiesRegistry()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            UnityMessages.Send(monitor, "Awake");

            AnimatorMonitor.ClearAll();

            Assert.That(AnimatorMonitor.GetInstances(), Is.Empty);
        }

        /// <summary>Checks that LateUpdate on an Animator without a controller samples no layers and reports no change.</summary>
        [Test]
        public void LateUpdate_WithoutController_ReportsNoChange()
        {
            var monitor = _objects.Add<AnimatorMonitor>();
            UnityMessages.Send(monitor, "Awake");

            UnityMessages.Send(monitor, "LateUpdate");
            UnityMessages.Send(monitor, "LateUpdate");

            Assert.That(monitor.UpdatedThisFrame, Is.False);
            Assert.That(monitor.EstimatedTimeMs, Is.GreaterThanOrEqualTo(0f));
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
    }
}

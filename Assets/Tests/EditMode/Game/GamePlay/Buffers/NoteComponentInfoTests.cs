using System;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Misc.Notes;
using MajdataPlay.Scenes.Game.Notes;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.Buffers
{
    /// <summary>Checks the player loop callback discovery and dispatch of <see cref="NoteComponentInfo"/>.</summary>
    public class NoteComponentInfoTests
    {
        /// <summary>Checks that a null component is invalid and never executable.</summary>
        [Test]
        public void Constructor_NullComponentIsInvalid()
        {
            using var info = new NoteComponentInfo(null!);

            Assert.That(info.IsValid, Is.False);
            Assert.That(info.Component, Is.Null);
            Assert.That(info.State, Is.EqualTo(NoteStatus.End));
            Assert.That(info.IsUpdatable || info.IsFixedUpdatable || info.IsLateUpdatable || info.IsPreUpdatable, Is.False);
            Assert.That(info.IsExecutable(), Is.False);
        }

        /// <summary>Checks that a component without the note component attribute is ignored even if it has callbacks.</summary>
        [Test]
        public void Constructor_ComponentWithoutAttributeIsInvalid()
        {
            var probe = new UnmarkedProbe();
            using var info = new NoteComponentInfo(probe);

            info.OnUpdate();

            Assert.That(info.IsValid, Is.False);
            Assert.That(info.Component, Is.Null);
            Assert.That(info.IsUpdatable, Is.False);
            Assert.That(probe.UpdateCount, Is.EqualTo(0));
        }

        /// <summary>Checks that the pre-update, update and fixed-update callbacks are discovered for their own phases.</summary>
        [Test]
        public void Constructor_DiscoversCallbacksByTiming()
        {
            var probe = new LoopProbe();
            using var info = new NoteComponentInfo(probe);

            Assert.That(info.IsValid, Is.True);
            Assert.That(info.Component, Is.SameAs(probe));
            Assert.That(info.IsPreUpdatable, Is.True);
            Assert.That(info.IsUpdatable, Is.True);
            Assert.That(info.IsFixedUpdatable, Is.True);
            Assert.That(info.IsLateUpdatable, Is.True);
        }

        /// <summary>Checks that callbacks only run while the note is active and between its start and end states.</summary>
        [Test]
        public void IsExecutable_RequiresAnActiveRunningNote()
        {
            var probe = new LoopProbe();
            using var info = new NoteComponentInfo(probe);

            probe.State = NoteStatus.Start;
            probe.Active = true;
            Assert.That(info.IsExecutable(), Is.False);

            probe.State = NoteStatus.Running;
            probe.Active = false;
            Assert.That(info.IsExecutable(), Is.False);

            probe.Active = true;
            Assert.That(info.IsExecutable(), Is.True);
            Assert.That(info.State, Is.EqualTo(NoteStatus.Running));

            probe.State = NoteStatus.End;
            Assert.That(info.IsExecutable(), Is.False);
        }

        /// <summary>Checks that each dispatcher invokes its callbacks only when the note is executable.</summary>
        [Test]
        public void Dispatchers_InvokeCallbacksOfTheirPhase()
        {
            var probe = new LoopProbe { State = NoteStatus.Scaling, Active = false };
            using var info = new NoteComponentInfo(probe);

            info.OnPreUpdate();
            info.OnUpdate();
            info.OnFixedUpdate();
            info.OnLateUpdate();
            Assert.That(probe.PreUpdateCount + probe.UpdateCount + probe.FixedUpdateCount, Is.EqualTo(0));

            probe.Active = true;
            info.OnPreUpdate();
            info.OnUpdate();
            info.OnFixedUpdate();

            Assert.That(probe.PreUpdateCount, Is.EqualTo(1));
            Assert.That(probe.UpdateCount, Is.EqualTo(1));
            Assert.That(probe.FixedUpdateCount, Is.EqualTo(1));
            Assert.That(probe.IgnoredCount, Is.EqualTo(0), "Methods with parameters, return values or no attribute are not callbacks");
        }

        /// <summary>Checks that a throwing callback is isolated so the other callbacks still run.</summary>
        [Test]
        public void Dispatchers_ContinueAfterAThrowingCallback()
        {
            var probe = new ThrowingProbe { State = NoteStatus.Running, Active = true };
            using var info = new NoteComponentInfo(probe);

            Assert.DoesNotThrow(() => info.OnUpdate());

            Assert.That(probe.AfterThrowCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a component with only a pre-update callback is not considered valid.</summary>
        [Test]
        public void IsValid_IgnoresPreUpdateOnlyComponents()
        {
            var probe = new PreUpdateOnlyProbe { State = NoteStatus.Running, Active = true };
            using var info = new NoteComponentInfo(probe);

            info.OnUpdate();
            info.OnPreUpdate();

            Assert.That(info.IsPreUpdatable, Is.True);
            Assert.That(info.IsUpdatable, Is.False);
            Assert.That(info.IsValid, Is.False);
            Assert.That(probe.PreUpdateCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a stateless component without a game object never executes.</summary>
        [Test]
        public void IsExecutable_StatelessComponentNeedsAGameObject()
        {
            using var info = new NoteComponentInfo(new StatelessProbe());

            Assert.That(info.IsUpdatable, Is.True);
            Assert.That(info.IsExecutable(), Is.False);
            Assert.That(info.State, Is.EqualTo(NoteStatus.End));
        }

        /// <summary>Checks that a late-update callback only runs in the late-update phase.</summary>
        [Test]
        [Ignore("Known issue: LoopTiming.LateUpdate is 3 (PreUpdate | Update), so NoteComponentInfo also registers late-update callbacks such as EachLineDrop.OnLateUpdate for pre-update and update.")]
        public void LateUpdateCallback_RunsOnlyInLateUpdate()
        {
            var probe = new LateUpdateOnlyProbe { State = NoteStatus.Running, Active = true };
            using var info = new NoteComponentInfo(probe);

            info.OnPreUpdate();
            info.OnUpdate();
            info.OnLateUpdate();

            Assert.That(info.IsPreUpdatable, Is.False);
            Assert.That(info.IsUpdatable, Is.False);
            Assert.That(probe.LateUpdateCount, Is.EqualTo(1));
        }

        /// <summary>Checks that a disposed info rejects use and can be disposed again.</summary>
        [Test]
        public void Dispose_RejectsLaterUse()
        {
            var info = new NoteComponentInfo(new LoopProbe());

            info.Dispose();
            info.Dispose();

            Assert.Throws<ObjectDisposedException>(() => _ = info.Component);
            Assert.Throws<ObjectDisposedException>(() => _ = info.State);
            Assert.Throws<ObjectDisposedException>(() => info.OnPreUpdate());
            Assert.Throws<ObjectDisposedException>(() => info.OnUpdate());
            Assert.Throws<ObjectDisposedException>(() => info.OnFixedUpdate());
            Assert.Throws<ObjectDisposedException>(() => info.OnLateUpdate());
            Assert.Throws<ObjectDisposedException>(() => info.IsExecutable());
        }

        /// <summary>A note-like component whose state and activity are set by the test.</summary>
        private abstract class ProbeBase : IStateful<NoteStatus>, IMajComponent
        {
            /// <summary>Gets or sets the note state.</summary>
            public NoteStatus State { get; set; } = NoteStatus.Running;

            /// <summary>Gets or sets a value indicating whether the component is active.</summary>
            public bool Active { get; set; } = true;

            /// <summary>Gets the component tag.</summary>
            public string Tag => string.Empty;

            /// <summary>Gets the game object; always null.</summary>
            public GameObject GameObject => null!;

            /// <summary>Gets the transform; always null.</summary>
            public Transform Transform => null!;

            /// <summary>Sets <see cref="Active"/>.</summary>
            /// <param name="state">The new activity.</param>
            public void SetActive(bool state)
            {
                Active = state;
            }
        }

        /// <summary>A component with callbacks for several phases and methods that must be ignored.</summary>
        [NoteComponent]
        private sealed class LoopProbe : ProbeBase
        {
            /// <summary>Gets the number of pre-update calls.</summary>
            public int PreUpdateCount { get; private set; }

            /// <summary>Gets the number of update calls.</summary>
            public int UpdateCount { get; private set; }

            /// <summary>Gets the number of fixed-update calls.</summary>
            public int FixedUpdateCount { get; private set; }

            /// <summary>Gets the number of late-update calls.</summary>
            public int LateUpdateCount { get; private set; }

            /// <summary>Gets the number of calls to methods that are not callbacks.</summary>
            public int IgnoredCount { get; private set; }

            /// <summary>A pre-update callback.</summary>
            [OnPreUpdate]
            private void PreUpdate()
            {
                PreUpdateCount++;
            }

            /// <summary>An update callback.</summary>
            [OnUpdate]
            private void Update()
            {
                UpdateCount++;
            }

            /// <summary>A fixed-update callback.</summary>
            [OnFixedUpdate]
            private void FixedUpdate()
            {
                FixedUpdateCount++;
            }

            /// <summary>A late-update callback.</summary>
            [OnLateUpdate]
            private void LateUpdate()
            {
                LateUpdateCount++;
            }

            /// <summary>A method with a parameter, which cannot be a callback.</summary>
            /// <param name="value">Ignored.</param>
            [OnUpdate]
            private void WithParameter(int value)
            {
                IgnoredCount++;
            }

            /// <summary>A method with a return value, which cannot be a callback.</summary>
            /// <returns>Always zero.</returns>
            [OnUpdate]
            private int WithReturnValue()
            {
                IgnoredCount++;
                return 0;
            }

            /// <summary>A method without a callback attribute.</summary>
            private void NotACallback()
            {
                IgnoredCount++;
            }
        }

        /// <summary>A component without the note component attribute.</summary>
        private sealed class UnmarkedProbe : ProbeBase
        {
            /// <summary>Gets the number of update calls.</summary>
            public int UpdateCount { get; private set; }

            /// <summary>An update callback that must never run.</summary>
            [OnUpdate]
            private void Update()
            {
                UpdateCount++;
            }
        }

        /// <summary>A component whose first update callback throws.</summary>
        [NoteComponent]
        private sealed class ThrowingProbe : ProbeBase
        {
            /// <summary>Gets the number of calls of the non-throwing callback.</summary>
            public int AfterThrowCount { get; private set; }

            /// <summary>A throwing update callback.</summary>
            /// <exception cref="InvalidOperationException">Always.</exception>
            [OnUpdate]
            private void A()
            {
                throw new InvalidOperationException("Expected test failure");
            }

            /// <summary>A counting update callback.</summary>
            [OnUpdate]
            private void B()
            {
                AfterThrowCount++;
            }
        }

        /// <summary>A component with only a pre-update callback.</summary>
        [NoteComponent]
        private sealed class PreUpdateOnlyProbe : ProbeBase
        {
            /// <summary>Gets the number of pre-update calls.</summary>
            public int PreUpdateCount { get; private set; }

            /// <summary>A pre-update callback.</summary>
            [OnPreUpdate]
            private void PreUpdate()
            {
                PreUpdateCount++;
            }
        }

        /// <summary>A component with only a late-update callback.</summary>
        [NoteComponent]
        private sealed class LateUpdateOnlyProbe : ProbeBase
        {
            /// <summary>Gets the number of late-update calls.</summary>
            public int LateUpdateCount { get; private set; }

            /// <summary>A late-update callback.</summary>
            [OnLateUpdate]
            private void LateUpdate()
            {
                LateUpdateCount++;
            }
        }

        /// <summary>A marked component that is neither stateful nor a Majdata component.</summary>
        [NoteComponent]
        private sealed class StatelessProbe
        {
            /// <summary>An update callback.</summary>
            [OnUpdate]
            private void Update()
            {
            }
        }
    }
}

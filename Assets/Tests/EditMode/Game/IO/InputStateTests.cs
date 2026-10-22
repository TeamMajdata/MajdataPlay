using MajdataPlay.IO;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the per-frame UI input state machines <see cref="InputControlState"/> and <see cref="InputRepeatState"/>.</summary>
    public class InputStateTests
    {
        /// <summary>The click animation duration used by the state machine.</summary>
        private const float Click = InputManager.UI_CLICK_ANIMATION_DURATION_SEC;

        /// <summary>Checks that a press is reported in its frame and the click completes after twice the animation time.</summary>
        [Test]
        public void ControlState_Press_CompletesClickAfterTwoAnimationDurations()
        {
            var state = default(InputControlState);

            state.Update(true, true, false, 0.01f);
            Assert.That(state.IsPressed, Is.True);
            Assert.That(state.PressedThisFrame, Is.True);
            Assert.That(state.ReleasedThisFrame, Is.False);
            Assert.That(state.ClickCompletedThisFrame, Is.False);

            state.Update(true, false, false, Click);
            Assert.That(state.PressedThisFrame, Is.False);
            Assert.That(state.ClickCompletedThisFrame, Is.False);

            state.Update(true, false, false, Click);
            Assert.That(state.ClickCompletedThisFrame, Is.False, "completion is published on the next frame");

            state.Update(true, false, false, 0.01f);
            Assert.That(state.ClickCompletedThisFrame, Is.True);

            state.Update(true, false, false, 0.01f);
            Assert.That(state.ClickCompletedThisFrame, Is.False, "completion lasts a single frame");
        }

        /// <summary>Checks that a release completes after one animation duration and reports how long the control was held.</summary>
        [Test]
        public void ControlState_Release_ReportsHeldDuration()
        {
            var state = default(InputControlState);
            state.Update(true, true, false, 0.25f);
            state.Update(true, false, false, 0.5f);

            state.Update(false, false, true, 0.01f);
            Assert.That(state.IsPressed, Is.False);
            Assert.That(state.ReleasedThisFrame, Is.True);
            Assert.That(state.ReleaseCompletedThisFrame, Is.False);

            state.Update(false, false, false, Click);
            Assert.That(state.ReleaseCompletedThisFrame, Is.False);
            Assert.That(state.ReleaseHeldDuration, Is.EqualTo(0f));

            state.Update(false, false, false, 0.01f);
            Assert.That(state.ReleaseCompletedThisFrame, Is.True);
            Assert.That(state.ReleaseHeldDuration, Is.EqualTo(0.75f).Within(1e-5f));

            state.Update(false, false, false, 0.01f);
            Assert.That(state.ReleaseCompletedThisFrame, Is.False);
            Assert.That(state.ReleaseHeldDuration, Is.EqualTo(0f));
        }

        /// <summary>Checks that a second press while a click is pending does not restart the click.</summary>
        [Test]
        public void ControlState_RepeatedPressWhilePending_KeepsTheFirstClick()
        {
            var state = default(InputControlState);
            state.Update(true, true, false, 0f);
            state.Update(false, false, true, Click);
            state.Update(true, true, false, Click);
            Assert.That(state.ClickCompletedThisFrame, Is.False);

            state.Update(true, false, false, 0f);

            Assert.That(state.ClickCompletedThisFrame, Is.True);
        }

        /// <summary>Checks that negative frame times do not advance the timers.</summary>
        [Test]
        public void ControlState_NegativeDeltaTime_IsIgnored()
        {
            var state = default(InputControlState);
            state.Update(true, true, false, 0f);

            for (var i = 0; i < 5; i++)
            {
                state.Update(true, false, false, -1f);
                Assert.That(state.ClickCompletedThisFrame, Is.False);
            }
        }

        /// <summary>Checks that a scene change suppresses a held control until it is released.</summary>
        [Test]
        public void ControlState_SceneChangeWhileHeld_SuppressesUntilRelease()
        {
            var state = default(InputControlState);
            state.Update(true, true, false, 0.1f);

            state.ResetForSceneChange(true);
            Assert.That(state.IsPressed, Is.False);

            state.Update(true, false, false, 1f);
            Assert.That(state.IsPressed, Is.False, "held input stays suppressed");

            state.Update(false, false, true, 0.1f);
            Assert.That(state.ReleasedThisFrame, Is.False, "the release ending suppression is swallowed");

            state.Update(true, true, false, 0.1f);
            Assert.That(state.PressedThisFrame, Is.True);
        }

        /// <summary>Checks that a scene change while released clears the state without suppression.</summary>
        [Test]
        public void ControlState_SceneChangeWhileReleased_ClearsPendingCompletions()
        {
            var state = default(InputControlState);
            state.Update(true, true, false, 0f);

            state.ResetForSceneChange(false);
            state.Update(false, false, false, 1f);
            state.Update(false, false, false, 1f);

            Assert.That(state.ClickCompletedThisFrame, Is.False);
            Assert.That(state.IsPressed, Is.False);
        }

        /// <summary>Checks that the first press fires immediately and repeats after the delay and interval.</summary>
        [Test]
        public void RepeatState_HoldingFiresImmediatelyThenRepeats()
        {
            var state = default(InputRepeatState);

            Assert.That(state.Update(true, false, 0.016f, 0.5f, 0.1f, out var direction), Is.True);
            Assert.That(direction, Is.EqualTo(1));
            Assert.That(state.Update(true, false, 0.5f, 0.5f, 0.1f, out _), Is.False, "the delay has not passed the first interval");
            Assert.That(state.Update(true, false, 0.11f, 0.5f, 0.1f, out _), Is.True, "first repeat after delay + interval");
            Assert.That(state.Update(true, false, 0.05f, 0.5f, 0.1f, out _), Is.False);
            Assert.That(state.Update(true, false, 0.06f, 0.5f, 0.1f, out _), Is.True, "second repeat after another interval");
        }

        /// <summary>Checks that a distinct first repeat interval is honoured before the regular interval.</summary>
        [Test]
        public void RepeatState_FirstRepeatIntervalDiffersFromLaterIntervals()
        {
            var state = default(InputRepeatState);
            state.Update(false, true, 0f, 0.2f, 0.3f, 0.05f, out var direction);
            Assert.That(direction, Is.EqualTo(-1));

            Assert.That(state.Update(false, true, 0.45f, 0.2f, 0.3f, 0.05f, out _), Is.False);
            Assert.That(state.Update(false, true, 0.06f, 0.2f, 0.3f, 0.05f, out _), Is.True);
            Assert.That(state.Update(false, true, 0.05f, 0.2f, 0.3f, 0.05f, out _), Is.True);
        }

        /// <summary>Checks that releasing both directions resets the state and reports no direction.</summary>
        [Test]
        public void RepeatState_Release_ResetsAndFiresAgainOnNextPress()
        {
            var state = default(InputRepeatState);
            state.Update(true, false, 0f, 0.5f, 0.1f, out _);

            Assert.That(state.Update(false, false, 0.1f, 0.5f, 0.1f, out var direction), Is.False);
            Assert.That(direction, Is.EqualTo(0));
            Assert.That(state.Update(true, false, 0.1f, 0.5f, 0.1f, out _), Is.True);
        }

        /// <summary>Checks that the positive direction wins when both directions are pressed.</summary>
        [Test]
        public void RepeatState_BothPressed_PrefersPositive()
        {
            var state = default(InputRepeatState);

            state.Update(true, true, 0f, 0.5f, 0.1f, out var direction);

            Assert.That(direction, Is.EqualTo(1));
        }

        /// <summary>Checks that switching direction while held does not fire until the new direction repeats.</summary>
        [Test]
        public void RepeatState_SwitchingDirection_DoesNotFireImmediately()
        {
            var state = default(InputRepeatState);
            state.Update(true, false, 0f, 0.5f, 0.1f, out _);

            Assert.That(state.Update(false, true, 0.01f, 0.5f, 0.1f, out var direction), Is.False);
            Assert.That(direction, Is.EqualTo(-1));
            Assert.That(state.Update(false, true, 0.7f, 0.5f, 0.1f, out _), Is.True, "the held time keeps accumulating");
        }

        /// <summary>Checks that suppression swallows input until both directions are released.</summary>
        [Test]
        public void RepeatState_SuppressUntilRelease_SwallowsHeldInput()
        {
            var state = default(InputRepeatState);
            state.SuppressUntilRelease();

            Assert.That(state.Update(true, false, 1f, 0.1f, 0.1f, out _), Is.False);
            Assert.That(state.Update(true, false, 1f, 0.1f, 0.1f, out _), Is.False);
            Assert.That(state.Update(false, false, 0f, 0.1f, 0.1f, out _), Is.False);
            Assert.That(state.Update(true, false, 0f, 0.1f, 0.1f, out _), Is.True);
        }

        /// <summary>Checks that zero or negative intervals and frame times are clamped to a minimum.</summary>
        [Test]
        public void RepeatState_NonPositiveIntervals_UseTheMinimum()
        {
            var state = default(InputRepeatState);
            state.Update(true, false, 0f, 0f, 0f, out _);

            Assert.That(state.Update(true, false, -1f, 0f, 0f, out _), Is.False, "negative time does not advance");
            Assert.That(state.Update(true, false, 0.0015f, 0f, 0f, out _), Is.True);
            Assert.That(state.Update(true, false, 0.0015f, 0f, 0f, out _), Is.True);
        }
    }
}

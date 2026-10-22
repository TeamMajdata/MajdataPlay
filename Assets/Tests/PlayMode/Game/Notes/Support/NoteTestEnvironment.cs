using System;
using System.Collections.Generic;
using MajdataPlay.Databases;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Settings;
using MajdataPlay.Tests;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Isolates a note test from the rest of the game: it installs fresh default settings, a fixed frame length and empty
    /// singleton holders, owns the Unity objects created by the test and restores every piece of static state it touched
    /// when disposed.
    /// </summary>
    internal sealed class NoteTestEnvironment : IDisposable
    {
        /// <summary>The frame length used for <see cref="MajTimeline.DeltaTime"/> during tests.</summary>
        public const float FrameLength = 1f / 60f;

        /// <summary>The settings that were active before the test.</summary>
        private readonly GameSetting _savedSettings;

        /// <summary>The running mode that was active before the test.</summary>
        private readonly RunningMode _savedMode;

        /// <summary>The unscaled frame delta that was active before the test.</summary>
        private readonly float _savedDeltaTime;

        /// <summary>The time scale that was active before the test.</summary>
        private readonly float _savedTimeScale;

        /// <summary>The singleton holders cleared for the test.</summary>
        private readonly List<SingletonSlot> _slots = new();

        /// <summary>The static arrays whose elements are restored after the test.</summary>
        private readonly List<StaticArraySnapshot> _arrays = new();

        /// <summary>The static scalar fields restored after the test, with their saved values.</summary>
        private readonly List<(Type Owner, string Name, object? Value)> _fields = new();

        /// <summary>Whether the environment has already been disposed.</summary>
        private bool _isDisposed;

        /// <summary>Captures the global state and installs the test defaults.</summary>
        public NoteTestEnvironment()
        {
            _savedSettings = MajEnv.Settings;
            _savedMode = MajEnv.Mode;
            _savedDeltaTime = MajTimeline.UnscaledDeltaTime;
            _savedTimeScale = MajTimeline.TimeScale;

            CaptureSingletons();
            CaptureStaticArrays();
            CaptureField(typeof(NoteManager), "_isUseButtonRingForTouch");
            CaptureField(typeof(EachLineDrop), "_curvSprites");
            CaptureField(typeof(EachLineDrop), "_isCurvSpritesInited");
            CaptureField(typeof(SlideOK), "s_GlobalSortingOrder");

            Settings = new GameSetting();
            Settings.Debug.TapPoolCapacity = 0;
            Settings.Debug.HoldPoolCapacity = 0;
            Settings.Debug.TouchPoolCapacity = 0;
            Settings.Debug.TouchHoldPoolCapacity = 0;
            Settings.Debug.EachLinePoolCapacity = 0;
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), Settings);
            MajEnv.Mode = RunningMode.View;
            MajTimeline.TimeScale = 1f;
            SetDeltaTime(FrameLength);
        }

        /// <summary>Gets the fresh settings object installed for the test.</summary>
        public GameSetting Settings { get; }

        /// <summary>Gets the tracker that destroys every Unity object created by the test.</summary>
        public TestObjects Objects { get; } = new();

        /// <summary>Sets the unscaled frame delta reported by <see cref="MajTimeline"/>.</summary>
        /// <param name="seconds">The frame delta in seconds.</param>
        public static void SetDeltaTime(float seconds)
        {
            PrivateMember.SetStaticProperty(typeof(MajTimeline), nameof(MajTimeline.UnscaledDeltaTime), seconds);
        }

        /// <summary>Destroys the test objects and restores every captured static value.</summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            Objects.Dispose();
            foreach (var slot in _slots)
            {
                slot.Restore();
            }
            foreach (var array in _arrays)
            {
                array.Restore();
            }
            foreach (var (owner, name, value) in _fields)
            {
                PrivateMember.SetStaticField(owner, name, value);
            }
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), _savedSettings);
            MajEnv.Mode = _savedMode;
            MajTimeline.TimeScale = _savedTimeScale;
            SetDeltaTime(_savedDeltaTime);
        }

        /// <summary>Captures and clears every singleton holder used by the gameplay note components.</summary>
        private void CaptureSingletons()
        {
            _slots.Add(SingletonSlot.Capture<INoteController>());
            _slots.Add(SingletonSlot.Capture<NoteManager>());
            _slots.Add(SingletonSlot.Capture<NoteEffectManager>());
            _slots.Add(SingletonSlot.Capture<NoteEffectPool>());
            _slots.Add(SingletonSlot.Capture<NoteAudioManager>());
            _slots.Add(SingletonSlot.Capture<NotePoolManager>());
            _slots.Add(SingletonSlot.Capture<MultTouchHandler>());
            _slots.Add(SingletonSlot.Capture<ObjectCounter>());
            _slots.Add(SingletonSlot.Capture<NoteLoader>());
            _slots.Add(SingletonSlot.Capture<FireworkDisplayer>());
            _slots.Add(SingletonSlot.Capture<GameInfo>());
            _slots.Add(SingletonSlot.Capture<GamePlayManager>());
            _slots.Add(SingletonSlot.Capture<XxlbAnimationController>());
            _slots.Add(SingletonSlot.Capture<SkinManager>());
            _slots.Add(SingletonSlot.Capture<AudioManager>());
            _slots.Add(SingletonSlot.Capture<GameRuntime>());
            _slots.Add(SingletonSlot.Capture<TapUpdater>());
            _slots.Add(SingletonSlot.Capture<HoldUpdater>());
            _slots.Add(SingletonSlot.Capture<SlideUpdater>());
            _slots.Add(SingletonSlot.Capture<TouchUpdater>());
            _slots.Add(SingletonSlot.Capture<TouchHoldUpdater>());
            _slots.Add(SingletonSlot.Capture<EachLineUpdater>());
            _slots.Add(SingletonSlot.Capture<SlideOKUpdater>());
        }

        /// <summary>Captures the static arrays that the note components and their collaborators write into.</summary>
        private void CaptureStaticArrays()
        {
            foreach (var name in new[] { "_tapLines", "_starLines", "_holdEnds", "_touchHoldFans", "_touchHoldBreakFans", "_touchHoldMineFans", "_touchHoldBreakMineFans" })
            {
                _arrays.Add(StaticArraySnapshot.Capture(typeof(SkinManager), name));
            }
            foreach (var name in new[] { "_isHasTap", "_isHasHold", "_isHasTouch", "_isHasTouchHold" })
            {
                _arrays.Add(StaticArraySnapshot.Capture(typeof(NoteLoader), name));
            }
            foreach (var name in new[] { "_noteSFXPlaybackRequests", "_noteSFXs" })
            {
                _arrays.Add(StaticArraySnapshot.Capture(typeof(NoteAudioManager), name));
            }
            foreach (var name in new[] { "_judgedTapCount", "_judgedHoldCount", "_judgedTouchCount", "_judgedTouchHoldCount", "_judgedSlideCount", "_judgedBreakCount", "_totalJudgedCount" })
            {
                _arrays.Add(StaticArraySnapshot.Capture(typeof(ObjectCounter), name));
            }
            foreach (var name in new[] { "_btnStatusInThisFrame", "_btnStatusInPreviousFrame", "_sensorStatusInThisFrame", "_sensorStatusInPreviousFrame" })
            {
                _arrays.Add(StaticArraySnapshot.Capture(typeof(InputManager), name));
            }
        }

        /// <summary>Saves the value of a static field so it can be restored.</summary>
        /// <param name="owner">The declaring type.</param>
        /// <param name="name">The field name.</param>
        private void CaptureField(Type owner, string name)
        {
            _fields.Add((owner, name, PrivateMember.GetStaticField<object?>(owner, name)));
        }
    }
}

using System;
using MajdataPlay.Settings;
using MajdataPlay.Timer;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Global.PlayMode
{
    /// <summary>Replaces <see cref="MajEnv.Settings"/> with fresh defaults in Play Mode and restores the original when disposed.</summary>
    internal sealed class PlayModeSettingsScope : IDisposable
    {
        /// <summary>The settings object that was active before the scope.</summary>
        private readonly GameSetting _original;

        /// <summary>Installs a new default settings object.</summary>
        public PlayModeSettingsScope()
        {
            _original = MajEnv.Settings;
            Settings = new GameSetting();
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), Settings);
        }

        /// <summary>Gets the settings object installed for the scope.</summary>
        public GameSetting Settings { get; }

        /// <summary>Restores the original settings object.</summary>
        public void Dispose()
        {
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), _original);
        }
    }

    /// <summary>A manually advanced clock installed as <see cref="MajTimeline.TimeProvider"/> until disposed.</summary>
    internal sealed class PlayModeClock : ITimeProvider, IDisposable
    {
        /// <summary>The provider active before the clock was installed.</summary>
        private readonly ITimeProvider _original;

        /// <summary>Installs the clock.</summary>
        public PlayModeClock()
        {
            _original = MajTimeline.TimeProvider;
            Ticks = TimeSpan.FromSeconds(20).Ticks;
            MajTimeline.TimeProvider = this;
        }

        /// <summary>Gets the provider kind reported to the timeline.</summary>
        public BuiltInTimeProvider Type => BuiltInTimeProvider.Stopwatch;

        /// <summary>Gets or sets the current time in ticks.</summary>
        public long Ticks { get; set; }

        /// <summary>Does nothing; the test advances the clock itself.</summary>
        public void OnPreUpdate()
        {
        }

        /// <summary>Moves the clock forward.</summary>
        /// <param name="time">The amount to add.</param>
        public void Advance(TimeSpan time)
        {
            Ticks += time.Ticks;
        }

        /// <summary>Restores the previous time provider.</summary>
        public void Dispose()
        {
            MajTimeline.TimeProvider = _original;
        }
    }

    /// <summary>Creates inactive game objects so serialized fields can be assigned before Awake runs.</summary>
    internal static class InactiveObjects
    {
        /// <summary>Creates an inactive game object tracked by the given tracker.</summary>
        /// <param name="objects">The tracker that destroys the object.</param>
        /// <param name="name">The object name.</param>
        /// <param name="parent">An optional parent.</param>
        /// <returns>The inactive object.</returns>
        public static GameObject Create(TestObjects objects, string name, Transform? parent = null)
        {
            var gameObject = objects.Create(name, parent);
            gameObject.SetActive(false);
            return gameObject;
        }
    }
}

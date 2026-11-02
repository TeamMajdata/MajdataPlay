using System;
using MajdataPlay.Settings;
using MajdataPlay.Timer;

#nullable enable
namespace MajdataPlay.Tests.Game.Global
{
    /// <summary>Replaces <see cref="MajEnv.UserSettings"/> and <see cref="MajEnv.SystemSettings"/> with fresh defaults and restores the originals when disposed.</summary>
    internal sealed class GlobalSettingsScope : IDisposable
    {
        /// <summary>The user settings object that was active before the scope.</summary>
        private readonly UserSetting _originalUser;

        /// <summary>The system settings object that was active before the scope.</summary>
        private readonly SystemSetting _originalSystem;

        /// <summary>Installs new default settings objects.</summary>
        public GlobalSettingsScope()
        {
            _originalUser = MajEnv.UserSettings;
            _originalSystem = MajEnv.SystemSettings;
            UserSettings = new UserSetting();
            SystemSettings = new SystemSetting();
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.UserSettings), UserSettings);
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.SystemSettings), SystemSettings);
        }

        /// <summary>Gets the user settings object installed for the scope.</summary>
        public UserSetting UserSettings { get; }

        /// <summary>Gets the system settings object installed for the scope.</summary>
        public SystemSetting SystemSettings { get; }

        /// <summary>Restores the original settings objects.</summary>
        public void Dispose()
        {
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.UserSettings), _originalUser);
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.SystemSettings), _originalSystem);
        }
    }

    /// <summary>A manually advanced clock installed as <see cref="MajTimeline.TimeProvider"/> until disposed.</summary>
    internal sealed class GlobalTestClock : ITimeProvider, IDisposable
    {
        /// <summary>The provider active before the clock was installed.</summary>
        private readonly ITimeProvider _original;

        /// <summary>Installs the clock at a fixed start time.</summary>
        public GlobalTestClock()
        {
            _original = MajTimeline.TimeProvider;
            Ticks = TimeSpan.FromSeconds(50).Ticks;
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
}

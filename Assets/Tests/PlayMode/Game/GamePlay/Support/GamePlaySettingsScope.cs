using System;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Tests;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// Installs fresh default game settings, runtime configuration, running mode and timeline delta for the lifetime of
    /// a test and restores the previous static state of <see cref="MajEnv"/> and <see cref="MajTimeline"/> when disposed.
    /// </summary>
    internal sealed class GamePlaySettingsScope : IDisposable
    {
        /// <summary>The user settings that were installed before the scope was created.</summary>
        private readonly UserSetting _previousUserSettings;

        /// <summary>The system settings that were installed before the scope was created.</summary>
        private readonly SystemSetting _previousSystemSettings;

        /// <summary>The runtime configuration that was installed before the scope was created.</summary>
        private readonly RuntimeConfig? _previousRuntimeConfig;

        /// <summary>The running mode that was active before the scope was created.</summary>
        private readonly RunningMode _previousMode;

        /// <summary>The unscaled timeline delta that was active before the scope was created.</summary>
        private readonly float _previousDeltaTime;

        /// <summary>The timeline scale that was active before the scope was created.</summary>
        private readonly float _previousTimeScale;

        /// <summary>Captures the current static state and installs default settings and runtime configuration.</summary>
        public GamePlaySettingsScope()
        {
            _previousUserSettings = MajEnv.UserSettings;
            _previousSystemSettings = MajEnv.SystemSettings;
            _previousRuntimeConfig = MajEnv.RuntimeConfig;
            _previousMode = MajEnv.Mode;
            _previousDeltaTime = MajTimeline.UnscaledDeltaTime;
            _previousTimeScale = MajTimeline.TimeScale;
            UserSettings = new UserSetting();
            SystemSettings = new SystemSetting();
            RuntimeConfig = new RuntimeConfig();
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.UserSettings), UserSettings);
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.SystemSettings), SystemSettings);
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.RuntimeConfig), RuntimeConfig);
            MajEnv.Mode = RunningMode.Play;
            MajTimeline.TimeScale = 1f;
        }

        /// <summary>Gets the user settings installed as <see cref="MajEnv.UserSettings"/> for the scope.</summary>
        public UserSetting UserSettings { get; }

        /// <summary>Gets the system settings installed as <see cref="MajEnv.SystemSettings"/> for the scope.</summary>
        public SystemSetting SystemSettings { get; }

        /// <summary>Gets the runtime configuration installed as <see cref="MajEnv.RuntimeConfig"/> for the scope.</summary>
        public RuntimeConfig RuntimeConfig { get; }

        /// <summary>Sets the unscaled delta time reported by <see cref="MajTimeline.DeltaTime"/>.</summary>
        /// <param name="seconds">The frame duration in seconds.</param>
        public void SetDeltaTime(float seconds)
        {
            PrivateMember.SetStaticProperty(typeof(MajTimeline), nameof(MajTimeline.UnscaledDeltaTime), seconds);
        }

        /// <summary>Restores the captured settings, runtime configuration, running mode and timeline values.</summary>
        public void Dispose()
        {
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.UserSettings), _previousUserSettings);
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.SystemSettings), _previousSystemSettings);
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.RuntimeConfig), _previousRuntimeConfig);
            PrivateMember.SetStaticProperty(typeof(MajTimeline), nameof(MajTimeline.UnscaledDeltaTime), _previousDeltaTime);
            MajEnv.Mode = _previousMode;
            MajTimeline.TimeScale = _previousTimeScale;
        }
    }
}

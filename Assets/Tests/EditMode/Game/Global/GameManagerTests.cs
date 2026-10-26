using System;
using System.Collections.Generic;
using MajdataPlay.Timer;
using NUnit.Framework;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Global
{
    /// <summary>Verifies the application event hub of <see cref="GameManager"/> without running its startup sequence.</summary>
    public class GameManagerTests
    {
        /// <summary>The static event fields restored after each test.</summary>
        private static readonly string[] s_eventFields = { "OnAppQuit", "OnSave", "OnAppFocus", "OnAppPause" };

        /// <summary>The event delegates before the test.</summary>
        private readonly Dictionary<string, Delegate?> _savedEvents = new();

        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The installed default settings.</summary>
        private GlobalSettingsScope _settings = null!;

        /// <summary>The manager under test, created without its lifecycle.</summary>
        private GameManager _manager = null!;

        /// <summary>The focus state before the test.</summary>
        private bool _savedFocus;

        /// <summary>The detected hardware encoder before the test.</summary>
        private HardwareEncoder _savedEncoder;

        /// <summary>The sleep timeout before the test.</summary>
        private int _savedSleepTimeout;

        /// <summary>Saves the static state and creates the manager.</summary>
        [SetUp]
        public void SetUp()
        {
            foreach (var name in s_eventFields)
            {
                _savedEvents[name] = PrivateMember.GetStaticField<Delegate?>(typeof(GameManager), name);
                PrivateMember.SetStaticField(typeof(GameManager), name, null);
            }
            _savedFocus = GameManager.IsAppOnFocus;
            _savedEncoder = MajEnv.HWEncoder;
            _savedSleepTimeout = Screen.sleepTimeout;
            _objects = new TestObjects();
            _settings = new GlobalSettingsScope();
            _manager = _objects.Add<GameManager>();
        }

        /// <summary>Restores the static state.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var pair in _savedEvents)
            {
                PrivateMember.SetStaticField(typeof(GameManager), pair.Key, pair.Value);
            }
            PrivateMember.SetStaticProperty(typeof(GameManager), nameof(GameManager.IsAppOnFocus), _savedFocus);
            MajEnv.HWEncoder = _savedEncoder;
            Screen.sleepTimeout = _savedSleepTimeout;
            _objects.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that the settings property exposes the active settings.</summary>
        [Test]
        public void Settings_ExposesTheActiveSettings()
        {
            Assert.That(_manager.Settings, Is.SameAs(MajEnv.Settings));
        }

        /// <summary>Checks that save requests reach every subscriber and a failing subscriber is contained.</summary>
        [Test]
        public void RequestSave_NotifiesSubscribersAndContainsFailures()
        {
            object? sender = null;
            GameManager.OnSave += (s, _) => sender = s;

            GameManager.RequestSave(this);
            Assert.That(sender, Is.SameAs(this));

            GameManager.OnSave += static (_, _) => throw new InvalidOperationException("save failed");
            Assert.DoesNotThrow(() => GameManager.RequestSave(this));
            Assert.DoesNotThrow(() =>
            {
                PrivateMember.SetStaticField(typeof(GameManager), "OnSave", null);
                GameManager.RequestSave(this);
            });
        }

        /// <summary>Checks that quitting saves, notifies, contains failures and removes every subscriber.</summary>
        [Test]
        public void OnApplicationQuit_SavesNotifiesAndClearsSubscribers()
        {
            var saved = 0;
            var quit = 0;
            GameManager.OnSave += (_, _) => saved++;
            GameManager.OnAppQuit += (_, _) => quit++;
            GameManager.OnAppQuit += static (_, _) => throw new InvalidOperationException("quit failed");

            UnityMessages.Send(_manager, "OnApplicationQuit");

            Assert.That(saved, Is.EqualTo(1));
            Assert.That(quit, Is.EqualTo(1));
            Assert.That(Screen.sleepTimeout, Is.EqualTo(SleepTimeout.SystemSetting));
            Assert.That(PrivateMember.GetStaticField<Delegate?>(typeof(GameManager), "OnAppQuit"), Is.Null);
            Assert.That(PrivateMember.GetStaticField<Delegate?>(typeof(GameManager), "OnSave"), Is.Null);
            Assert.DoesNotThrow(() => UnityMessages.Send(_manager, "OnApplicationQuit"));
        }

        /// <summary>Checks that focus changes update the focus flag and notify subscribers.</summary>
        [Test]
        public void OnApplicationFocus_UpdatesFlagAndNotifies()
        {
            var received = new List<bool>();
            PrivateMember.Invoke(_manager, "OnApplicationFocus", false);
            Assert.That(GameManager.IsAppOnFocus, Is.False);

            GameManager.OnAppFocus += (_, focus) => received.Add(focus);
            PrivateMember.Invoke(_manager, "OnApplicationFocus", true);

            Assert.That(GameManager.IsAppOnFocus, Is.True);
            Assert.That(received, Is.EqualTo(new[] { true }));
        }

        /// <summary>Checks that pause changes notify subscribers.</summary>
        [Test]
        public void OnApplicationPause_NotifiesSubscribers()
        {
            var received = new List<bool>();
            PrivateMember.Invoke(_manager, "OnApplicationPause", true);

            GameManager.OnAppPause += (_, paused) => received.Add(paused);
            PrivateMember.Invoke(_manager, "OnApplicationPause", true);
            PrivateMember.Invoke(_manager, "OnApplicationPause", false);

            Assert.That(received, Is.EqualTo(new[] { true, false }));
        }

        /// <summary>Checks that the hardware encoder is chosen from the graphics device vendor.</summary>
        [Test]
        public void DetectHWEncoder_FollowsTheGraphicsVendor()
        {
            PrivateMember.Invoke(_manager, "DetectHWEncoder");

            var name = SystemInfo.graphicsDeviceName.ToLower();
            var expected = name.Contains("nvidia") ? HardwareEncoder.NVENC
                : name.Contains("amd") ? HardwareEncoder.AMF
                : name.Contains("intel") ? HardwareEncoder.QSV
                : HardwareEncoder.None;
            Assert.That(MajEnv.HWEncoder, Is.EqualTo(expected));
        }

        /// <summary>Checks that the garbage collector helpers can be called on desktop platforms.</summary>
        [Test]
        public void GarbageCollectorHelpers_DoNotThrow()
        {
            Assert.DoesNotThrow(() => _manager.DisableGC());
            Assert.DoesNotThrow(() => _manager.EnableGC());
        }

        /// <summary>Checks that the timer check restores the configured time provider when another one is active.</summary>
        [Test]
        public void ChangeTimerIfRequested_RestoresTheConfiguredTimer()
        {
            var original = MajTimeline.TimeProvider;
            try
            {
                var timers = MajTimeline.BuiltInTimeProviders.Span;
                MajTimeline.TimeProvider = timers[(int)BuiltInTimeProvider.Unity];
                PrivateMember.SetField(_manager, "_timer", BuiltInTimeProvider.Stopwatch);

                PrivateMember.Invoke(_manager, "ChangeTimerIfRequested");

                Assert.That(MajTimeline.TimeProvider.Type, Is.EqualTo(BuiltInTimeProvider.Stopwatch));
                PrivateMember.Invoke(_manager, "ChangeTimerIfRequested");
                Assert.That(MajTimeline.TimeProvider.Type, Is.EqualTo(BuiltInTimeProvider.Stopwatch));
            }
            finally
            {
                MajTimeline.TimeProvider = original;
            }
        }
    }
}

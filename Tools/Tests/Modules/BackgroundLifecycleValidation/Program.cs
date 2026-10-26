#nullable enable
using MajdataPlay.Scenes.Game;
using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;
using UnityEngine.UI;

namespace MajdataPlay.Tests.BackgroundLifecycleValidation
{
    /// <summary>Exercises the production background manager cleanup with owned and borrowed materials.</summary>
    internal static class Program
    {
        /// <summary>Counts completed ownership and lifecycle assertions.</summary>
        private static int s_assertions;

        /// <summary>Runs independent managed lifecycle cases.</summary>
        /// <returns>Zero if every case passes, or one after a regression.</returns>
        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("normal destruction releases only the owned copy", TestNormal),
                ("Close failure still releases material and singleton", TestCloseFailure),
                ("uninitialized and repeated destruction are safe", TestUninitialized),
                ("repeated destruction does not repeat cleanup", TestRepeated),
                ("old cleanup preserves a successor singleton", TestSuccessor),
                ("material destruction failure still frees singleton", TestDestroyFailure),
                ("partial Awake failure retains cleanup ownership", TestPartialAwake)
            };
            var failed = 0;
            foreach (var test in tests)
            {
                Majdata<BGManager>.Free();
                UnityEngine.Object.DestroyFailure = null;
                try
                {
                    test.Run();
                    Console.WriteLine($"PASS {test.Name}");
                }
                catch (Exception exception)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL {test.Name}: {exception}");
                }
            }
            Majdata<BGManager>.Free();
            UnityEngine.Object.DestroyFailure = null;
            var marker = failed == 0 ? "BACKGROUND_LIFECYCLE_VALIDATION_PASSED" : "BACKGROUND_LIFECYCLE_VALIDATION_FAILED";
            Console.WriteLine($"{marker} ({tests.Length - failed} passed, {failed} failed, {s_assertions} assertions)");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Checks shared private ownership and normal decoder shutdown.</summary>
        private static void TestNormal()
        {
            var fixture = CreateFixture();
            fixture.Manager.Initialize();
            var owned = fixture.Cover.material;
            Assert(!ReferenceEquals(owned, fixture.Original), "Awake must clone the borrowed material.");
            Assert(ReferenceEquals(owned, fixture.Video.material), "Both graphics must share one owned copy.");
            DestroyManager(fixture.Manager);
            Assert(owned.DestroyCalls == 1, "The private material must be destroyed once.");
            Assert(fixture.Original.DestroyCalls == 0, "The original cover material must survive.");
            Assert(fixture.OriginalVideo.DestroyCalls == 0, "The original video material must survive.");
            Assert(fixture.Player.CloseCalls == 2, "Awake closes idle playback and destruction closes it once.");
            Assert(fixture.Player.TextureSubscribers == 0 && fixture.Player.ErrorSubscribers == 0, "Decoder events must be detached.");
            Assert(Majdata<BGManager>.Instance == null, "The manager singleton must be cleared.");
        }

        /// <summary>Checks cleanup when the decoder Close method fails.</summary>
        private static void TestCloseFailure()
        {
            var fixture = CreateFixture();
            fixture.Manager.Initialize();
            var owned = fixture.Cover.material;
            var error = new InvalidOperationException("simulated decoder Close failure");
            fixture.Player.CloseFailure = error;
            ExpectSameException(() => DestroyManager(fixture.Manager), error);
            Assert(owned.DestroyCalls == 1, "Close failure must not leak the private material.");
            Assert(Majdata<BGManager>.Instance == null, "Close failure must not retain the singleton.");
            Assert(fixture.Player.TextureSubscribers == 0 && fixture.Player.ErrorSubscribers == 0, "Close failure must not retain event subscriptions.");
            DestroyManager(fixture.Manager);
            Assert(owned.DestroyCalls == 1 && fixture.Player.CloseCalls == 2, "Failed destruction must still become idempotent.");
            Assert(fixture.Original.DestroyCalls == 0, "Close failure must never destroy a borrowed material.");
        }

        /// <summary>Checks cleanup before any Awake initialization and preserves another manager.</summary>
        private static void TestUninitialized()
        {
            var manager = new TestManager();
            var other = new TestManager();
            Majdata<BGManager>.Instance = other;
            DestroyManager(manager);
            DestroyManager(manager);
            Assert(ReferenceEquals(Majdata<BGManager>.Instance, other), "An uninitialized manager must not clear another singleton.");
        }

        /// <summary>Checks that a second destroy neither closes playback nor destroys the material again.</summary>
        private static void TestRepeated()
        {
            var fixture = CreateFixture();
            fixture.Manager.Initialize();
            var owned = fixture.Cover.material;
            DestroyManager(fixture.Manager);
            var other = new TestManager();
            Majdata<BGManager>.Instance = other;
            DestroyManager(fixture.Manager);
            Assert(owned.DestroyCalls == 1, "The same private material must never be destroyed twice.");
            Assert(fixture.Player.CloseCalls == 2, "Playback shutdown must not be repeated.");
            Assert(ReferenceEquals(Majdata<BGManager>.Instance, other), "Repeated cleanup must preserve a successor.");
        }

        /// <summary>Checks cleanup after a new manager has already registered itself.</summary>
        private static void TestSuccessor()
        {
            var fixture = CreateFixture();
            fixture.Manager.Initialize();
            var owned = fixture.Cover.material;
            var successor = new TestManager();
            Majdata<BGManager>.Instance = successor;
            DestroyManager(fixture.Manager);
            Assert(owned.DestroyCalls == 1, "An old manager still owns its private copy.");
            Assert(ReferenceEquals(Majdata<BGManager>.Instance, successor), "Old cleanup must not unregister the successor.");
        }

        /// <summary>Checks the nested cleanup guarantee when native material destruction rejects a request.</summary>
        private static void TestDestroyFailure()
        {
            var fixture = CreateFixture();
            fixture.Manager.Initialize();
            var owned = fixture.Cover.material;
            var error = new InvalidOperationException("simulated material destruction failure");
            UnityEngine.Object.DestroyFailure = error;
            ExpectSameException(() => DestroyManager(fixture.Manager), error);
            Assert(owned.DestroyCalls == 1, "The owned material must receive the destruction request.");
            Assert(Majdata<BGManager>.Instance == null, "Singleton cleanup must survive material destruction failure.");
            UnityEngine.Object.DestroyFailure = null;
            DestroyManager(fixture.Manager);
            Assert(owned.DestroyCalls == 1, "Repeated destruction must not repeat a failed request.");
        }

        /// <summary>Checks cleanup after initialization created the material but idle decoder shutdown failed.</summary>
        private static void TestPartialAwake()
        {
            var fixture = CreateFixture();
            var error = new InvalidOperationException("simulated Awake decoder Close failure");
            fixture.Player.CloseFailure = error;
            ExpectSameException(fixture.Manager.Initialize, error);
            var owned = fixture.Cover.material;
            Assert(!ReferenceEquals(owned, fixture.Original), "The failure must occur after private material creation.");
            ExpectSameException(() => DestroyManager(fixture.Manager), error);
            Assert(owned.DestroyCalls == 1 && fixture.Original.DestroyCalls == 0, "Partial initialization must release only the owned material.");
            Assert(Majdata<BGManager>.Instance == null, "Partial initialization must release its singleton.");
            Assert(fixture.Player.TextureSubscribers == 0 && fixture.Player.ErrorSubscribers == 0, "Partial initialization must detach events.");
        }

        /// <summary>Creates borrowed UI resources and injects serialized references without running Unity.</summary>
        /// <returns>A fully wired fixture whose Awake method has not run.</returns>
        private static Fixture CreateFixture()
        {
            var original = new Material();
            var originalVideo = new Material();
            var cover = new Image { material = original };
            var video = new RawImage { material = originalVideo };
            var player = new MajdataPlay.FFmpeg.FFmpegVideoPlayer();
            var manager = new TestManager { TestComponent = player };
            SetField(manager, "_coverRenderer", cover);
            SetField(manager, "_videoRenderer", video);
            SetField(manager, "_defaultSprite", new Sprite());
            return new Fixture(manager, cover, video, player, original, originalVideo);
        }

        /// <summary>Invokes the actual private Unity destroy message and unwraps reflection exceptions.</summary>
        /// <param name="manager">The production manager whose cleanup is under test.</param>
        private static void DestroyManager(BGManager manager)
        {
            var method = typeof(BGManager).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(BGManager), "OnDestroy");
            try
            {
                method.Invoke(manager, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        /// <summary>Assigns one serialized production field for the fixture.</summary>
        /// <param name="manager">The manager being configured.</param>
        /// <param name="name">The exact private serialized field name.</param>
        /// <param name="value">The borrowed test resource assigned to the field.</param>
        private static void SetField(BGManager manager, string name, object value)
        {
            var field = typeof(BGManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(BGManager), name);
            field.SetValue(manager, value);
        }

        /// <summary>Checks that cleanup preserves the original failure for Unity to report.</summary>
        /// <param name="action">The failing operation.</param>
        /// <param name="expected">The original exception instance.</param>
        private static void ExpectSameException(Action action, Exception expected)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Assert(ReferenceEquals(exception, expected), "Cleanup must propagate the original failure.");
                return;
            }
            throw new InvalidOperationException("The expected failure did not occur.");
        }

        /// <summary>Counts and enforces one lifecycle condition.</summary>
        /// <param name="condition">Whether the condition passed.</param>
        /// <param name="message">The failure description.</param>
        private static void Assert(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Exposes the production Awake method without changing its implementation.</summary>
        private sealed class TestManager : BGManager
        {
            /// <summary>Runs production initialization on the managed fixture.</summary>
            public void Initialize()
            {
                base.Awake();
            }
        }

        /// <summary>Retains borrowed resources and the actual production manager for one case.</summary>
        /// <param name="Manager">The production manager.</param>
        /// <param name="Cover">The cover graphic.</param>
        /// <param name="Video">The video graphic.</param>
        /// <param name="Player">The observable decoder substitute.</param>
        /// <param name="Original">The borrowed cover material.</param>
        /// <param name="OriginalVideo">The borrowed video material.</param>
        private sealed record Fixture(TestManager Manager, Image Cover, RawImage Video,
            MajdataPlay.FFmpeg.FFmpegVideoPlayer Player, Material Original, Material OriginalVideo);
    }
}

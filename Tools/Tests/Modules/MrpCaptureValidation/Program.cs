#nullable enable
using MajdataPlay.Rendering;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace MajdataPlay.Tests.MrpCaptureValidation
{
    /// <summary>Checks the production capture registry against narrow managed Unity test doubles.</summary>
    internal static class Program
    {
        /// <summary>Counts individual assertions made by this executable.</summary>
        private static int s_assertions;

        /// <summary>Stores callback work without allocating in the measured render loop.</summary>
        private static int s_callbackCount;

        /// <summary>Runs isolated registry cases and reports a machine-readable completion marker.</summary>
        /// <returns>Zero when every case passes, or one when any case fails.</returns>
        private static int Main()
        {
            var tests = new (string Name, Action Run)[]
            {
                ("empty lookup uses the shared empty array", TestEmpty),
                ("duplicate delegates retain a single cached registration", TestDuplicate),
                ("registration changes preserve in-flight snapshots", TestSnapshots),
                ("an action can remove itself without skipping another action", TestSelfRemoval),
                ("camera registrations are isolated", TestDifferentCameras),
                ("destroyed camera references can still be removed", TestDestroyedCamera),
                ("the final removal releases its camera key", TestFinalRemoval),
                ("missing removal is a no-op", TestMissingRemoval),
                ("null and destroyed registrations are rejected", TestInvalidRegistration),
                ("runtime reset releases all registrations", TestReset),
                ("lookups and callback dispatch allocate no per-frame managed memory", TestAllocation)
            };
            var failed = 0;
            foreach (var test in tests)
            {
                MajCameraCaptureBridge.Reset();
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
            MajCameraCaptureBridge.Reset();
            var marker = failed == 0 ? "MRP_CAPTURE_VALIDATION_PASSED" : "MRP_CAPTURE_VALIDATION_FAILED";
            Console.WriteLine($"{marker} ({tests.Length - failed} passed, {failed} failed, {s_assertions} assertions)");
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Checks that absent listeners never require a new array.</summary>
        private static void TestEmpty()
        {
            var camera = new Camera();
            var empty = MajCameraCaptureBridge.GetCaptureActions(camera);
            Assert(empty.Length == 0, "An unregistered camera must have no callbacks.");
            Assert(ReferenceEquals(empty, Array.Empty<Action<RenderTargetIdentifier, CommandBuffer>>()), "The empty array must be shared.");
            Assert(ReferenceEquals(empty, MajCameraCaptureBridge.GetCaptureActions(camera)), "Repeated empty lookups must reuse the array.");
        }

        /// <summary>Checks delegate equality and unchanged snapshot identity for repeated registration.</summary>
        private static void TestDuplicate()
        {
            var camera = new Camera();
            Action<RenderTargetIdentifier, CommandBuffer> first = CountCallback;
            Action<RenderTargetIdentifier, CommandBuffer> same = CountCallback;
            MajCameraCaptureBridge.AddCaptureAction(camera, first);
            var snapshot = MajCameraCaptureBridge.GetCaptureActions(camera);
            MajCameraCaptureBridge.AddCaptureAction(camera, same);
            Assert(snapshot.Length == 1, "The first callback must be registered.");
            Assert(ReferenceEquals(snapshot, MajCameraCaptureBridge.GetCaptureActions(camera)), "Duplicate delegates must not replace the snapshot.");
        }

        /// <summary>Checks that registration and removal replace arrays instead of mutating a render's snapshot.</summary>
        private static void TestSnapshots()
        {
            var camera = new Camera();
            Action<RenderTargetIdentifier, CommandBuffer> first = CountCallback;
            Action<RenderTargetIdentifier, CommandBuffer> second = IgnoreCallback;
            MajCameraCaptureBridge.AddCaptureAction(camera, first);
            var one = MajCameraCaptureBridge.GetCaptureActions(camera);
            MajCameraCaptureBridge.AddCaptureAction(camera, second);
            var two = MajCameraCaptureBridge.GetCaptureActions(camera);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, first);
            var remaining = MajCameraCaptureBridge.GetCaptureActions(camera);
            Assert(one.Length == 1 && one[0] == first, "An old one-action snapshot must remain intact.");
            Assert(two.Length == 2 && two[0] == first && two[1] == second, "An old two-action snapshot must remain intact after removal.");
            Assert(remaining.Length == 1 && remaining[0] == second, "The next render must see only the retained action.");
        }

        /// <summary>Checks safe mutation from within a callback while dispatching a captured array.</summary>
        private static void TestSelfRemoval()
        {
            var camera = new Camera();
            var commands = new CommandBuffer();
            var calls = 0;
            Action<RenderTargetIdentifier, CommandBuffer> self = null!;
            self = (source, buffer) =>
            {
                Assert(source.Value == 7 && ReferenceEquals(buffer, commands), "Dispatch must preserve renderer arguments.");
                calls++;
                MajCameraCaptureBridge.RemoveCaptureAction(camera, self);
            };
            Action<RenderTargetIdentifier, CommandBuffer> other = (source, buffer) =>
            {
                calls++;
            };
            MajCameraCaptureBridge.AddCaptureAction(camera, self);
            MajCameraCaptureBridge.AddCaptureAction(camera, other);
            var snapshot = MajCameraCaptureBridge.GetCaptureActions(camera);
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i](new RenderTargetIdentifier(7), commands);
            }
            Assert(calls == 2, "Removing the current callback must not skip its neighbor.");
            Assert(snapshot.Length == 2, "In-flight dispatch must retain its original snapshot.");
            Assert(MajCameraCaptureBridge.GetCaptureActions(camera).Length == 1, "The next lookup must observe removal.");
        }

        /// <summary>Checks that one camera's registration cannot affect another camera.</summary>
        private static void TestDifferentCameras()
        {
            var first = new Camera();
            var second = new Camera();
            MajCameraCaptureBridge.AddCaptureAction(first, CountCallback);
            MajCameraCaptureBridge.AddCaptureAction(second, IgnoreCallback);
            MajCameraCaptureBridge.RemoveCaptureAction(first, CountCallback);
            Assert(MajCameraCaptureBridge.GetCaptureActions(first).Length == 0, "Removing the first camera must clear its callbacks.");
            Assert(MajCameraCaptureBridge.GetCaptureActions(second).Length == 1, "The other camera's callbacks must survive.");
        }

        /// <summary>Checks the difference between an actual null reference and Unity's destroyed-camera null.</summary>
        private static void TestDestroyedCamera()
        {
            var camera = new Camera();
            MajCameraCaptureBridge.AddCaptureAction(camera, CountCallback);
            camera.Destroyed = true;
            Assert(camera == null, "The stub must reproduce Unity's destroyed-object comparison.");
            Assert(GetRegistrationCount() == 1, "The destroyed key remains until its owner unhooks.");
            Assert(MajCameraCaptureBridge.GetCaptureActions(camera!).Length == 0, "Destroyed cameras must not start capture.");
            MajCameraCaptureBridge.RemoveCaptureAction(camera!, CountCallback);
            Assert(GetRegistrationCount() == 0, "Removal must release a destroyed camera's dictionary entry.");
        }

        /// <summary>Checks that the last listener removal releases the camera key rather than retaining an empty entry.</summary>
        private static void TestFinalRemoval()
        {
            var camera = new Camera();
            MajCameraCaptureBridge.AddCaptureAction(camera, CountCallback);
            Assert(GetRegistrationCount() == 1, "Adding a listener must retain one camera key.");
            MajCameraCaptureBridge.RemoveCaptureAction(camera, CountCallback);
            Assert(GetRegistrationCount() == 0, "The last listener removal must release the camera key.");
        }

        /// <summary>Checks that null or unregistered removals do not disturb an existing snapshot.</summary>
        private static void TestMissingRemoval()
        {
            var camera = new Camera();
            MajCameraCaptureBridge.AddCaptureAction(camera, CountCallback);
            var snapshot = MajCameraCaptureBridge.GetCaptureActions(camera);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, IgnoreCallback);
            MajCameraCaptureBridge.RemoveCaptureAction(new Camera(), CountCallback);
            MajCameraCaptureBridge.RemoveCaptureAction(null!, CountCallback);
            MajCameraCaptureBridge.RemoveCaptureAction(camera, null!);
            Assert(ReferenceEquals(snapshot, MajCameraCaptureBridge.GetCaptureActions(camera)), "Missing removals must retain snapshot identity.");
        }

        /// <summary>Checks that no registrations are accepted for invalid cameras or callbacks.</summary>
        private static void TestInvalidRegistration()
        {
            AssertThrows<ArgumentNullException>(() => MajCameraCaptureBridge.AddCaptureAction(null!, CountCallback));
            AssertThrows<ArgumentNullException>(() => MajCameraCaptureBridge.AddCaptureAction(new Camera { Destroyed = true }, CountCallback));
            AssertThrows<ArgumentNullException>(() => MajCameraCaptureBridge.AddCaptureAction(new Camera(), null!));
            Assert(GetRegistrationCount() == 0, "Invalid additions must leave no camera entries.");
        }

        /// <summary>Checks that runtime initialization clears cameras retained by an earlier session.</summary>
        private static void TestReset()
        {
            var camera = new Camera();
            MajCameraCaptureBridge.AddCaptureAction(camera, CountCallback);
            MajCameraCaptureBridge.Reset();
            Assert(GetRegistrationCount() == 0, "Subsystem reset must release every camera key.");
            Assert(MajCameraCaptureBridge.GetCaptureActions(camera).Length == 0, "Reset must remove all callbacks.");
            var method = typeof(MajCameraCaptureBridge).GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic);
            var marker = method?.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert(marker?.LoadType == RuntimeInitializeLoadType.SubsystemRegistration, "Reset must be scheduled for runtime subsystem registration.");
        }

        /// <summary>Measures steady-state absent and active callback dispatch without allocating inside a rendered-frame loop.</summary>
        private static void TestAllocation()
        {
            var camera = new Camera();
            var emptyCamera = new Camera();
            var commands = new CommandBuffer();
            MajCameraCaptureBridge.AddCaptureAction(camera, CountCallback);
            for (var i = 0; i < 1024; i++)
            {
                Dispatch(camera, commands);
                Dispatch(emptyCamera, commands);
            }
            s_callbackCount = 0;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
            {
                Dispatch(camera, commands);
                Dispatch(emptyCamera, commands);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert(bytes == 0, $"Steady-state lookup and dispatch allocated {bytes} bytes.");
            Assert(s_callbackCount == 10000, "Every active frame must invoke exactly one callback.");
        }

        /// <summary>Models the renderer's immutable-array capture dispatch without issuing GPU commands.</summary>
        /// <param name="camera">The camera whose registered snapshot is queried.</param>
        /// <param name="commands">The cached command-buffer token supplied to callbacks.</param>
        private static void Dispatch(Camera camera, CommandBuffer commands)
        {
            var snapshot = MajCameraCaptureBridge.GetCaptureActions(camera);
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i](new RenderTargetIdentifier(1), commands);
            }
        }

        /// <summary>Counts one callback without producing managed allocations.</summary>
        /// <param name="source">The source identity supplied by the renderer.</param>
        /// <param name="commands">The command-buffer identity supplied by the renderer.</param>
        private static void CountCallback(RenderTargetIdentifier source, CommandBuffer commands)
        {
            s_callbackCount++;
        }

        /// <summary>Provides a different callback identity with no observable work.</summary>
        /// <param name="source">The source identity supplied by the renderer.</param>
        /// <param name="commands">The command-buffer identity supplied by the renderer.</param>
        private static void IgnoreCallback(RenderTargetIdentifier source, CommandBuffer commands)
        {
        }

        /// <summary>Observes retained keys without widening the production API for testing.</summary>
        /// <returns>The number of camera entries currently owned by the production registry.</returns>
        private static int GetRegistrationCount()
        {
            var field = typeof(MajCameraCaptureBridge).GetField("s_actions", BindingFlags.Static | BindingFlags.NonPublic);
            var entries = (IDictionary?)field?.GetValue(null);
            return entries?.Count ?? throw new InvalidOperationException("The capture registry was not found.");
        }

        /// <summary>Records one required invariant and throws when the invariant is false.</summary>
        /// <param name="condition">Whether the expected behavior was observed.</param>
        /// <param name="message">The failure context to report.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Assert(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>Requires the supplied action to reject an invalid operation with the expected exception.</summary>
        /// <typeparam name="TException">The expected exception type.</typeparam>
        /// <param name="action">The invalid operation under test.</param>
        /// <exception cref="InvalidOperationException">The action completes without the expected exception.</exception>
        private static void AssertThrows<TException>(Action action) where TException : Exception
        {
            s_assertions++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
        }
    }
}

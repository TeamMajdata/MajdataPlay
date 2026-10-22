using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MajdataPlay.IO;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Verifies the daemon lifetime and reconnect loop of <see cref="IODevice"/> with a scripted device.</summary>
    public class IODeviceTests
    {
        /// <summary>The longest time a test waits for the daemon thread.</summary>
        private const double TimeoutSeconds = 5;

        /// <summary>The device created by the test, disposed after it.</summary>
        private FakeIODevice? _device;

        /// <summary>Disposes the device so no daemon thread outlives the test.</summary>
        [TearDown]
        public void TearDown()
        {
            _device?.Dispose();
            _device = null;
        }

        /// <summary>Checks that a started device connects, updates and disconnects cleanly when disposed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ConnectsAndDisposeDisconnects()
        {
            var device = _device = new FakeIODevice();
            Assert.That(device.ThreadName, Is.EqualTo("IO/FakeIODevice Thread"));

            device.Start();
            yield return WaitUntil(() => device.IsConnected && device.UpdateCount > 0);
            Assert.That(device.IsConnected, Is.True);

            device.Dispose();

            Assert.That(device.IsConnected, Is.False);
            Assert.That(device.ConnectCount, Is.EqualTo(1));
            Assert.That(device.DisconnectCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(device.DisconnectedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that starting twice keeps one daemon and that a disposed device cannot be restarted.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_IsIdempotentAndRejectedAfterDispose()
        {
            var device = _device = new FakeIODevice();
            device.Start();
            device.Start();
            yield return WaitUntil(() => device.IsConnected);

            device.Dispose();
            device.Dispose();

            Assert.That(device.ConnectCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => device.Start());
        }

        /// <summary>Checks that disposing a device that never started has no effect.</summary>
        [Test]
        public void Dispose_WithoutStart_IsHarmless()
        {
            var device = _device = new FakeIODevice();

            device.Dispose();

            Assert.That(device.IsConnected, Is.False);
            Assert.That(device.ConnectCount, Is.EqualTo(0));
            Assert.That(device.DisconnectedCount, Is.EqualTo(0));
        }

        /// <summary>Checks that a failed connection notifies the disconnect and waits for a retry until disposed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FailedConnection_NotifiesAndWaitsForRetry()
        {
            var device = _device = new FakeIODevice
            {
                ConnectBehaviour = static _ => false,
            };

            device.Start();
            yield return WaitUntil(() => device.DisconnectedCount >= 1);
            var stopwatch = Stopwatch.StartNew();
            device.Dispose();

            Assert.That(device.ConnectCount, Is.EqualTo(1));
            Assert.That(device.UpdateCount, Is.EqualTo(0));
            Assert.That(device.IsConnected, Is.False);
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(1.5), "disposal interrupts the reconnect delay");
        }

        /// <summary>Checks that an update failure ends the session and notifies the disconnect.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator UpdateFailure_EndsTheSession()
        {
            var device = _device = new FakeIODevice
            {
                UpdateBehaviour = static _ => throw new IOException("lost"),
            };

            device.Start();
            yield return WaitUntil(() => device.DisconnectedCount >= 1);

            Assert.That(device.IsConnected, Is.False);
            Assert.That(device.UpdateCount, Is.EqualTo(1));
            Assert.That(device.DisconnectCount, Is.GreaterThanOrEqualTo(1));
        }

        /// <summary>Checks that failures while disconnecting or notifying are contained by the daemon.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator DisconnectFailures_AreContained()
        {
            var device = _device = new FakeIODevice
            {
                ConnectBehaviour = static _ => false,
                ThrowOnDisconnect = true,
                ThrowOnDisconnected = true,
            };

            device.Start();
            yield return WaitUntil(() => device.DisconnectedCount >= 1);
            device.Dispose();

            Assert.That(device.DisconnectCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(device.IsConnected, Is.False);
        }

        /// <summary>Checks that a polling interval keeps updating until the device is disposed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator PollingInterval_RepeatsUpdatesUntilDisposed()
        {
            var device = _device = new FakeIODevice
            {
                Interval = TimeSpan.FromMilliseconds(1),
                UpdateBehaviour = static _ => { },
            };

            device.Start();
            yield return WaitUntil(() => device.UpdateCount >= 3);
            device.Dispose();
            var updates = device.UpdateCount;

            Assert.That(updates, Is.GreaterThanOrEqualTo(3));
            Assert.That(device.ConnectCount, Is.EqualTo(1));
            Assert.That(device.DisconnectedCount, Is.EqualTo(1));
        }

        /// <summary>Checks that cancellation observed while connecting stops the daemon without a retry.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CancellationWhileConnecting_StopsTheDaemon()
        {
            using var entered = new ManualResetEventSlim();
            var device = _device = new FakeIODevice
            {
                ConnectBehaviour = token =>
                {
                    entered.Set();
                    token.WaitHandle.WaitOne();
                    token.ThrowIfCancellationRequested();
                    return true;
                },
            };

            device.Start();
            yield return WaitUntil(() => entered.IsSet);
            device.Dispose();

            Assert.That(device.ConnectCount, Is.EqualTo(1));
            Assert.That(device.UpdateCount, Is.EqualTo(0));
            Assert.That(device.DisconnectedCount, Is.EqualTo(1));
        }

        /// <summary>Checks the daemon loop on the calling thread: a failed connection notifies and stops once cancelled.</summary>
        [Test]
        public void Run_FailedConnection_StopsWhenCancelled()
        {
            using var cancellation = new CancellationTokenSource();
            var device = _device = new FakeIODevice
            {
                ConnectBehaviour = _ =>
                {
                    cancellation.Cancel();
                    return false;
                },
            };
            var completion = new TaskCompletionSource<bool>();

            PrivateMember.Invoke(device, "Run", cancellation.Token, completion);

            Assert.That(completion.Task.IsCompleted, Is.True);
            Assert.That(device.ConnectCount, Is.EqualTo(1));
            Assert.That(device.DisconnectedCount, Is.EqualTo(1));
            Assert.That(device.IsConnected, Is.False);
        }

        /// <summary>Checks the daemon loop on the calling thread: a connected device updates until cancellation interrupts its polling wait.</summary>
        [Test]
        public void Run_ConnectedDevice_PollsUntilCancelled()
        {
            using var cancellation = new CancellationTokenSource();
            var device = _device = new FakeIODevice
            {
                Interval = TimeSpan.FromMilliseconds(50),
            };
            device.UpdateBehaviour = _ =>
            {
                Assert.That(device.IsConnected, Is.True);
                if (device.UpdateCount >= 2)
                {
                    cancellation.Cancel();
                }
            };
            var completion = new TaskCompletionSource<bool>();

            PrivateMember.Invoke(device, "Run", cancellation.Token, completion);

            Assert.That(device.UpdateCount, Is.EqualTo(2));
            Assert.That(device.IsConnected, Is.False);
            Assert.That(device.DisconnectedCount, Is.EqualTo(1));
            Assert.That(completion.Task.IsCompleted, Is.True);
        }

        /// <summary>Checks the daemon loop on the calling thread: update, disconnect and notification failures are contained.</summary>
        [Test]
        public void Run_Failures_AreContained()
        {
            using var cancellation = new CancellationTokenSource();
            var device = _device = new FakeIODevice
            {
                ThrowOnDisconnect = true,
                ThrowOnDisconnected = true,
            };
            device.UpdateBehaviour = _ =>
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(1));
                throw new IOException("lost");
            };
            var completion = new TaskCompletionSource<bool>();

            PrivateMember.Invoke(device, "Run", cancellation.Token, completion);

            Assert.That(device.UpdateCount, Is.EqualTo(1));
            Assert.That(device.DisconnectCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(completion.Task.IsCompleted, Is.True);
        }

        /// <summary>Checks the daemon loop on the calling thread: cancellation thrown while connecting ends the loop.</summary>
        [Test]
        public void Run_CancellationWhileConnecting_EndsTheLoop()
        {
            using var cancellation = new CancellationTokenSource();
            var device = _device = new FakeIODevice
            {
                ConnectBehaviour = token =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return true;
                },
            };
            var completion = new TaskCompletionSource<bool>();

            PrivateMember.Invoke(device, "Run", cancellation.Token, completion);

            Assert.That(device.UpdateCount, Is.EqualTo(0));
            Assert.That(device.DisconnectedCount, Is.EqualTo(1));
        }

        /// <summary>Waits for a condition while letting the editor run frames.</summary>
        /// <param name="condition">The condition to wait for.</param>
        /// <returns>A coroutine that ends when the condition holds or the timeout passes.</returns>
        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            var stopwatch = Stopwatch.StartNew();
            while (!condition() && stopwatch.Elapsed.TotalSeconds < TimeoutSeconds)
            {
                yield return null;
            }
            Assert.That(condition(), Is.True, "timed out waiting for the daemon thread");
        }
    }
}

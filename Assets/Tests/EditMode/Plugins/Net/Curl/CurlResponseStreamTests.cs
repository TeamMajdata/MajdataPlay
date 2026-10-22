#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MajdataPlay.Net.Curl.Core;
using MajdataPlay.Net.Curl.Utils;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MajdataPlay.Tests.Plugins.Net.Curl
{
    /// <summary>Covers the response body hand-off stream between the libcurl worker and the reader, without native code.</summary>
    public class CurlResponseStreamTests
    {
        /// <summary>The number of resume requests raised by the stream under test.</summary>
        private int _resumeCount;

        /// <summary>The number of dispose notifications raised by the stream under test.</summary>
        private int _disposeCount;

        /// <summary>Resets the callback counters.</summary>
        [SetUp]
        public void SetUp()
        {
            _resumeCount = 0;
            _disposeCount = 0;
        }

        /// <summary>Creates a stream whose pause threshold is 80 bytes and resume threshold 40 bytes.</summary>
        /// <returns>The stream.</returns>
        private CurlResponseStream CreateStream()
        {
            return new CurlResponseStream(100, () => Interlocked.Increment(ref _resumeCount), () => Interlocked.Increment(ref _disposeCount));
        }

        /// <summary>Creates a byte array filled with one value.</summary>
        /// <param name="length">The array length.</param>
        /// <param name="value">The fill value.</param>
        /// <returns>The filled array.</returns>
        private static byte[] Bytes(int length, byte value)
        {
            var bytes = new byte[length];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = value;
            }
            return bytes;
        }

        /// <summary>Checks the fixed capabilities of the read-only, non-seekable stream.</summary>
        [Test]
        public void Capabilities_AreReadOnlySequential()
        {
            using var stream = CreateStream();

            Assert.That(stream.CanRead, Is.True);
            Assert.That(stream.CanSeek, Is.False);
            Assert.That(stream.CanWrite, Is.False);
            Assert.Throws<NotSupportedException>(() => _ = stream.Length);
            Assert.Throws<NotSupportedException>(() => _ = stream.Position);
            Assert.Throws<NotSupportedException>(() => stream.Position = 0);
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
            Assert.DoesNotThrow(stream.Flush);
        }

        /// <summary>Checks that written chunks are read back in order across chunk boundaries until the body completes.</summary>
        [Test]
        public void WriteThenRead_ReturnsBytesInOrder()
        {
            using var stream = CreateStream();

            Assert.That(stream.WriteChunk(new byte[] { 1, 2, 3 }), Is.EqualTo((UIntPtr)3));
            Assert.That(stream.WriteChunk(new byte[] { 4, 5 }), Is.EqualTo((UIntPtr)2));
            stream.CompleteWriting();
            var buffer = new byte[10];
            var first = stream.Read(buffer, 0, 2);
            var second = stream.Read(buffer, 2, 10 - 2);
            var third = stream.Read(buffer, 3, 7);
            var end = stream.Read(buffer, 0, 10);

            Assert.That(first, Is.EqualTo(2));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(third, Is.EqualTo(2));
            Assert.That(end, Is.Zero);
            Assert.That(buffer.AsSpan(0, 5).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        /// <summary>Checks that an empty chunk is consumed without buffering anything.</summary>
        [Test]
        public void WriteChunk_EmptyChunkReturnsZero()
        {
            using var stream = CreateStream();

            Assert.That(stream.WriteChunk(ReadOnlySpan<byte>.Empty), Is.EqualTo(UIntPtr.Zero));
            stream.CompleteWriting();
            Assert.That(stream.Read(new byte[1], 0, 1), Is.Zero);
        }

        /// <summary>Checks that a zero-length read returns at once even while no data is buffered.</summary>
        [Test]
        public void Read_ZeroCountReturnsImmediately()
        {
            using var stream = CreateStream();

            Assert.That(stream.Read(new byte[1], 0, 0), Is.Zero);
        }

        /// <summary>Checks that writes after completion or disposal are reported as errors to libcurl.</summary>
        [Test]
        public void WriteChunk_FailsAfterCompletionOrDisposal()
        {
            var completed = CreateStream();
            var disposed = CreateStream();
            completed.CompleteWriting();
            disposed.Dispose();

            Assert.That(completed.WriteChunk(new byte[] { 1 }), Is.EqualTo(CurlCallbackReturn.Write.Error));
            Assert.That(disposed.WriteChunk(new byte[] { 1 }), Is.EqualTo(CurlCallbackReturn.Write.Error));
            completed.Dispose();
        }

        /// <summary>Checks that the first chunk is accepted even above the pause threshold, because no read could resume it otherwise.</summary>
        [Test]
        public void WriteChunk_NeverPausesAnEmptyBuffer()
        {
            using var stream = CreateStream();

            Assert.That(stream.WriteChunk(Bytes(500, 1)), Is.EqualTo((UIntPtr)500));
        }

        /// <summary>Checks that the transfer pauses above the high-water mark and resumes once at the low-water mark.</summary>
        [Test]
        public void Backpressure_PausesAndResumesOnce()
        {
            using var stream = CreateStream();
            var buffer = new byte[100];

            Assert.That(stream.WriteChunk(Bytes(60, 1)), Is.EqualTo((UIntPtr)60));
            Assert.That(stream.WriteChunk(Bytes(30, 2)), Is.EqualTo(CurlCallbackReturn.Write.Pause));
            stream.Read(buffer, 0, 10);
            var afterFirstRead = _resumeCount;
            stream.Read(buffer, 0, 15);
            var afterSecondRead = _resumeCount;
            stream.Read(buffer, 0, 10);

            Assert.That(afterFirstRead, Is.Zero, "50 buffered bytes are still above the resume threshold.");
            Assert.That(afterSecondRead, Is.EqualTo(1));
            Assert.That(_resumeCount, Is.EqualTo(1));
            Assert.That(stream.WriteChunk(Bytes(30, 3)), Is.EqualTo((UIntPtr)30));
        }

        /// <summary>Checks that an abort releases buffered data and makes every read rethrow the first abort reason.</summary>
        [Test]
        public void Abort_RethrowsFirstReason()
        {
            using var stream = CreateStream();
            var first = new IOException("first");
            stream.WriteChunk(new byte[] { 1, 2 });

            stream.Abort(first);
            stream.Abort(new InvalidOperationException("second"));

            var thrown = Assert.Throws<IOException>(() => stream.Read(new byte[2], 0, 2));
            Assert.That(thrown, Is.SameAs(first));
            Assert.Throws<IOException>(() => stream.Read(new byte[2], 0, 2));
            Assert.That(stream.WriteChunk(new byte[] { 3 }), Is.EqualTo(CurlCallbackReturn.Write.Error));
        }

        /// <summary>Checks that disposal notifies the owner once, makes reads fail and ignores later aborts.</summary>
        [Test]
        public void Dispose_NotifiesOnceAndRejectsReads()
        {
            var stream = CreateStream();
            stream.WriteChunk(new byte[] { 1 });

            stream.Dispose();
            stream.Dispose();
            stream.Abort(new IOException("late"));

            Assert.That(_disposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[1], 0, 1));
        }

        /// <summary>Checks that a reader blocked on an empty buffer wakes up when a chunk arrives from another thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Read_WakesWhenChunkArrives() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            var read = Task.Run(() =>
            {
                var buffer = new byte[4];
                var count = stream.Read(buffer, 0, 4);
                return buffer.AsSpan(0, count).ToArray();
            });

            await Task.Delay(50);
            var blocked = !read.IsCompleted;
            stream.WriteChunk(new byte[] { 7, 8 });
            await NetAsyncAssert.CompletesWithin(read, 5000, "The blocked read");

            Assert.That(blocked, Is.True);
            Assert.That(read.Result, Is.EqualTo(new byte[] { 7, 8 }));
        });

        /// <summary>Checks that a blocked reader returns end-of-body when writing completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Read_WakesAtEndOfBody() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            var read = Task.Run(() => stream.Read(new byte[4], 0, 4));

            await Task.Delay(50);
            stream.CompleteWriting();
            await NetAsyncAssert.CompletesWithin(read, 5000, "The blocked read");

            Assert.That(read.Result, Is.Zero);
        });

        /// <summary>Checks that disposing the stream wakes a blocked reader with an object disposed failure.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Dispose_WakesBlockedReader() => UniTask.ToCoroutine(async () =>
        {
            var stream = CreateStream();
            var read = Task.Run(() => stream.Read(new byte[4], 0, 4));

            await Task.Delay(50);
            stream.Dispose();
            await NetAsyncAssert.CompletesWithin(read.ContinueWith(_ => { }), 5000, "The blocked read");

            Assert.That(read.IsFaulted, Is.True);
            Assert.That(read.Exception!.InnerException, Is.InstanceOf<ObjectDisposedException>());
            Assert.That(_disposeCount, Is.EqualTo(1));
        });

        /// <summary>Checks that aborting the stream wakes a blocked reader with the abort reason.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Abort_WakesBlockedReader() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            var read = Task.Run(() => stream.Read(new byte[4], 0, 4));

            await Task.Delay(50);
            stream.Abort(new OperationCanceledException("aborted"));
            await NetAsyncAssert.CompletesWithin(read.ContinueWith(_ => { }), 5000, "The blocked read");

            Assert.That(read.Exception!.InnerException, Is.InstanceOf<OperationCanceledException>());
        });
    }
}

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
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

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

        /// <summary>Checks that span reads through the base stream type return bytes in order across chunk boundaries.</summary>
        [Test]
        public void ReadSpan_ReturnsBytesInOrder()
        {
            using var stream = CreateStream();
            Stream baseStream = stream;

            stream.WriteChunk(new byte[] { 1, 2, 3 });
            stream.WriteChunk(new byte[] { 4, 5 });
            stream.CompleteWriting();
            var buffer = new byte[10];
            var first = baseStream.Read(buffer.AsSpan(0, 2));
            var second = baseStream.Read(buffer.AsSpan(2));
            var third = baseStream.Read(buffer.AsSpan(3));
            var end = baseStream.Read(buffer.AsSpan());

            Assert.That(first, Is.EqualTo(2));
            Assert.That(second, Is.EqualTo(1));
            Assert.That(third, Is.EqualTo(2));
            Assert.That(end, Is.Zero);
            Assert.That(buffer.AsSpan(0, 5).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        /// <summary>
        /// Regression test: a span read larger than the shared array pool limit (as BASS requests from
        /// BassHttpFileSource) must copy straight from the chunk instead of renting a temporary array.
        /// </summary>
        [Test]
        public void ReadSpan_LargeDestination_DoesNotAllocateGCMemory()
        {
            using var stream = CreateStream();
            Stream baseStream = stream;
            var destination = new byte[2 * 1024 * 1024];
            var bytesRead = 0;
            TestDelegate read = () =>
            {
                bytesRead = baseStream.Read(destination.AsSpan());
            };
            stream.WriteChunk(Bytes(64, 9));
            Assert.That(baseStream.Read(destination.AsSpan(0, 1)), Is.EqualTo(1));

            Assert.That(() =>
            {
                GC.KeepAlive(new byte[128]);
            }, Is.AllocatingGCMemory(), "the profiler must detect a known managed allocation");
            Assert.That(read, Is.Not.AllocatingGCMemory());
            Assert.That(bytesRead, Is.EqualTo(63));
            Assert.That(destination.AsSpan(0, 63).ToArray(), Is.EqualTo(Bytes(63, 9)));
        }

        /// <summary>Checks that the array overload validates its arguments before touching the body.</summary>
        [Test]
        public void ReadArray_InvalidArgumentsThrow()
        {
            using var stream = CreateStream();
            stream.WriteChunk(new byte[] { 1, 2 });

            Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[2], 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(new byte[2], -1, 1));
            Assert.That(stream.Read(new byte[2], 0, 2), Is.EqualTo(2), "Rejected reads must not consume body bytes.");
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

        /// <summary>Checks that an asynchronous read of buffered bytes completes synchronously, in order, across chunks.</summary>
        [Test]
        public void ReadAsync_BufferedData_CompletesSynchronously()
        {
            using var stream = CreateStream();
            Stream baseStream = stream;
            stream.WriteChunk(new byte[] { 1, 2, 3 });
            stream.WriteChunk(new byte[] { 4, 5 });
            stream.CompleteWriting();
            var buffer = new byte[10];

            var first = baseStream.ReadAsync(buffer.AsMemory(0, 2));
            var second = baseStream.ReadAsync(buffer.AsMemory(2));
            var third = baseStream.ReadAsync(buffer.AsMemory(3));
            var end = baseStream.ReadAsync(buffer.AsMemory());

            Assert.That(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully && third.IsCompletedSuccessfully && end.IsCompletedSuccessfully, Is.True);
            Assert.That(first.Result, Is.EqualTo(2));
            Assert.That(second.Result, Is.EqualTo(1));
            Assert.That(third.Result, Is.EqualTo(2));
            Assert.That(end.Result, Is.Zero);
            Assert.That(buffer.AsSpan(0, 5).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }

        /// <summary>Checks that an asynchronous read of buffered bytes allocates nothing, even into a large destination.</summary>
        [Test]
        public void ReadAsync_BufferedData_DoesNotAllocateGCMemory()
        {
            using var stream = CreateStream();
            Stream baseStream = stream;
            var destination = new byte[2 * 1024 * 1024];
            var bytesRead = 0;
            TestDelegate read = () =>
            {
                bytesRead = baseStream.ReadAsync(destination.AsMemory(), CancellationToken.None).Result;
            };
            stream.WriteChunk(Bytes(64, 9));
            Assert.That(baseStream.ReadAsync(destination.AsMemory(0, 1), CancellationToken.None).Result, Is.EqualTo(1));

            Assert.That(() =>
            {
                GC.KeepAlive(new byte[128]);
            }, Is.AllocatingGCMemory(), "the profiler must detect a known managed allocation");
            Assert.That(read, Is.Not.AllocatingGCMemory());
            Assert.That(bytesRead, Is.EqualTo(63));
        }

        /// <summary>Checks that the array overload completes buffered reads synchronously and validates its arguments.</summary>
        [Test]
        public void ReadAsyncArray_ForwardsAndValidatesArguments()
        {
            using var stream = CreateStream();
            stream.WriteChunk(new byte[] { 1, 2 });
            var buffer = new byte[4];

            Assert.Throws<ArgumentNullException>(() => stream.ReadAsync(null!, 0, 1, CancellationToken.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.ReadAsync(buffer, 3, 2, CancellationToken.None));
            var read = stream.ReadAsync(buffer, 1, 3, CancellationToken.None);

            Assert.That(read.IsCompleted, Is.True);
            Assert.That(read.Result, Is.EqualTo(2));
            Assert.That(buffer, Is.EqualTo(new byte[] { 0, 1, 2, 0 }));
        }

        /// <summary>Checks that an asynchronous read without data returns at once and completes when a chunk arrives.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_WaitsWithoutBlockingUntilChunkArrives() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            var buffer = new byte[4];

            var read = stream.ReadAsync(buffer.AsMemory()).AsTask();
            await Task.Delay(50);
            var pending = !read.IsCompleted;
            stream.WriteChunk(new byte[] { 7, 8 });
            await NetAsyncAssert.CompletesWithin(read, 5000, "The pending read");

            Assert.That(pending, Is.True, "The read must wait for data instead of completing early.");
            Assert.That(read.Result, Is.EqualTo(2));
            Assert.That(buffer.AsSpan(0, 2).ToArray(), Is.EqualTo(new byte[] { 7, 8 }));
        });

        /// <summary>
        /// Regression test: a pending asynchronous read must wait for the writer instead of running the blocking
        /// synchronous read on a thread-pool thread, as the base <see cref="Stream"/> implementation does.
        /// </summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_DoesNotFallBackToBlockingRead() => UniTask.ToCoroutine(async () =>
        {
            using var stream = new SyncReadCountingStream(() => Interlocked.Increment(ref _resumeCount), () => Interlocked.Increment(ref _disposeCount));

            var read = stream.ReadAsync(new byte[4].AsMemory()).AsTask();
            await Task.Delay(50);
            stream.WriteChunk(new byte[] { 1 });
            await NetAsyncAssert.CompletesWithin(read, 5000, "The pending read");

            Assert.That(read.Result, Is.EqualTo(1));
            Assert.That(stream.SyncReadCount, Is.Zero);
        });

        /// <summary>Checks that a pending asynchronous read returns end-of-body when writing completes.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_ReturnsZeroWhenWritingCompletes() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();

            var read = stream.ReadAsync(new byte[4].AsMemory()).AsTask();
            stream.CompleteWriting();
            await NetAsyncAssert.CompletesWithin(read, 5000, "The pending read");

            Assert.That(read.Result, Is.Zero);
        });

        /// <summary>Checks that canceling a pending read ends it without consuming the bytes written afterwards.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_CancellationStopsWaitingWithoutConsumingData() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            using var cts = new CancellationTokenSource();

            var read = stream.ReadAsync(new byte[4].AsMemory(), cts.Token).AsTask();
            cts.Cancel();
            await NetAsyncAssert.CompletesWithin(read.ContinueWith(_ => { }), 5000, "The canceled read");
            stream.WriteChunk(new byte[] { 5, 6 });
            var buffer = new byte[4];

            Assert.That(read.IsCanceled, Is.True);
            Assert.That(stream.Read(buffer, 0, 4), Is.EqualTo(2));
            Assert.That(buffer.AsSpan(0, 2).ToArray(), Is.EqualTo(new byte[] { 5, 6 }));
        });

        /// <summary>Checks that an already canceled token cancels the read without consuming buffered bytes.</summary>
        [Test]
        public void ReadAsync_PreCanceledTokenLeavesBufferedData()
        {
            using var stream = CreateStream();
            stream.WriteChunk(new byte[] { 3 });

            var read = stream.ReadAsync(new byte[1].AsMemory(), new CancellationToken(true));

            Assert.That(read.IsCanceled, Is.True);
            Assert.That(stream.Read(new byte[1], 0, 1), Is.EqualTo(1));
        }

        /// <summary>Checks that aborting the stream faults a pending asynchronous read with the abort reason.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_AbortFaultsPendingRead() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            var reason = new IOException("aborted");

            var read = stream.ReadAsync(new byte[4].AsMemory()).AsTask();
            stream.Abort(reason);
            await NetAsyncAssert.CompletesWithin(read.ContinueWith(_ => { }), 5000, "The pending read");

            Assert.That(read.IsFaulted, Is.True);
            Assert.That(read.Exception!.InnerException, Is.SameAs(reason));
        });

        /// <summary>Checks that disposing the stream faults a pending asynchronous read and later reads fail at once.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator ReadAsync_DisposeFaultsPendingRead() => UniTask.ToCoroutine(async () =>
        {
            var stream = CreateStream();

            var read = stream.ReadAsync(new byte[4].AsMemory()).AsTask();
            stream.Dispose();
            await NetAsyncAssert.CompletesWithin(read.ContinueWith(_ => { }), 5000, "The pending read");
            var late = stream.ReadAsync(new byte[4].AsMemory());

            Assert.That(read.Exception!.InnerException, Is.InstanceOf<ObjectDisposedException>());
            Assert.That(late.IsFaulted, Is.True, "A read after disposal must fault the task instead of throwing.");
            Assert.That(_disposeCount, Is.EqualTo(1));
        });

        /// <summary>Checks that an asynchronous read resumes a paused transfer once the low-water mark is reached.</summary>
        [Test]
        public void ReadAsync_ResumesPausedTransfer()
        {
            using var stream = CreateStream();
            stream.WriteChunk(Bytes(60, 1));
            Assert.That(stream.WriteChunk(Bytes(30, 2)), Is.EqualTo(CurlCallbackReturn.Write.Pause));

            var read = stream.ReadAsync(new byte[25].AsMemory());

            Assert.That(read.Result, Is.EqualTo(25));
            Assert.That(_resumeCount, Is.EqualTo(1));
        }

        /// <summary>Checks that CopyToAsync, which reads through ReadAsync, copies a body written from another thread.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator CopyToAsync_CopiesBodyWrittenConcurrently() => UniTask.ToCoroutine(async () =>
        {
            using var stream = CreateStream();
            using var destination = new MemoryStream();

            var copy = stream.CopyToAsync(destination);
            var writer = Task.Run(async () =>
            {
                for (byte i = 1; i <= 5; i++)
                {
                    stream.WriteChunk(new[] { i, i });
                    await Task.Delay(5);
                }
                stream.CompleteWriting();
            });
            await NetAsyncAssert.CompletesWithin(Task.WhenAll(copy, writer), 5000, "The copy");

            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 }));
        });

        /// <summary>Counts calls to the blocking synchronous read overloads.</summary>
        private sealed class SyncReadCountingStream : CurlResponseStream
        {
            /// <summary>The number of synchronous reads, from any thread.</summary>
            private int _syncReadCount;

            /// <summary>Creates a stream with the same thresholds as <see cref="CreateStream"/>.</summary>
            /// <param name="onResume">Called when the transfer is resumed.</param>
            /// <param name="onDispose">Called when the stream is disposed.</param>
            public SyncReadCountingStream(Action onResume, Action onDispose) : base(100, onResume, onDispose)
            {
            }

            /// <summary>Gets the number of synchronous reads.</summary>
            public int SyncReadCount => Volatile.Read(ref _syncReadCount);

            /// <summary>Counts the read and forwards it.</summary>
            /// <param name="buffer">The destination array.</param>
            /// <param name="offset">The destination offset.</param>
            /// <param name="count">The maximum number of bytes to read.</param>
            /// <returns>The number of bytes read.</returns>
            public override int Read(byte[] buffer, int offset, int count)
            {
                Interlocked.Increment(ref _syncReadCount);
                return base.Read(buffer, offset, count);
            }

            /// <summary>Counts the read and forwards it.</summary>
            /// <param name="buffer">The destination span.</param>
            /// <returns>The number of bytes read.</returns>
            public override int Read(Span<byte> buffer)
            {
                Interlocked.Increment(ref _syncReadCount);
                return base.Read(buffer);
            }
        }
    }
}

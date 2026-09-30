using MajdataPlay.Buffers;
using MajdataPlay.Net.Curl.Utils;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Net.Curl.Core
{
    internal class CurlResponseStream : Stream
    {
        MemoryChunk _currentChunk;
        MemoryChunk _writingChunk;
        Exception? _abortException;

        volatile bool _isPaused = false;

        volatile int _bufferedBytes;

        SpinLock _pauseOrResumeLock = new(false);

        volatile int _isDisposed = 0;

        readonly int _lwmBytes;
        readonly int _hwmBytes;

        readonly int _chunkSize;
        readonly long _maxBufferSize;
        readonly Action _onResume;
        readonly Action _onDispose;
        readonly BlockingCollection<MemoryChunk> _bufferQueue;

        public CurlResponseStream(long maxBufferLength, Action onResume, Action onDispose) : this(1024, maxBufferLength, onResume, onDispose) { }
        public CurlResponseStream(int chunkSize, long maxBufferLength, Action onResume, Action onDispose)
        {
            _onResume = onResume;
            _onDispose = onDispose;
            _chunkSize = chunkSize;
            _maxBufferSize = maxBufferLength;
            _lwmBytes = (int)(_maxBufferSize * 0.4);
            _hwmBytes = (int)(_maxBufferSize * 0.8);
            _bufferQueue = new();
        }

        public UIntPtr WriteChunk(ReadOnlySpan<byte> chunk)
        {
            if (_isDisposed == 1)
            {
                return CurlCallbackReturn.Write.Error;
            }
            if (chunk.IsEmpty)
            {
                return UIntPtr.Zero;
            }
            var bytesToWrite = chunk.Length;
            var isLocked = false;
            try
            {
                _pauseOrResumeLock.Enter(ref isLocked);
                if (!_isPaused && _bufferedBytes + chunk.Length > _hwmBytes)
                {
                    _isPaused = true;
                    return CurlCallbackReturn.Write.Pause;
                }
            }
            finally
            {
                if(isLocked)
                {
                    _pauseOrResumeLock.Exit();
                }
            }

            try
            {
                while (bytesToWrite > 0)
                {
                    ref var chunkInfo = ref _writingChunk;
                    var buffer = _writingChunk.Buffer;
                    if (!_writingChunk.IsValid)
                    {
                        buffer = Pool<byte>.Rent(_chunkSize);
                        chunkInfo = new MemoryChunk()
                        {
                            Buffer = buffer,
                            Length = 0,
                            Offset = 0
                        };
                    }
                    var bufferRemaining = buffer!.Length - chunkInfo.Length;
                    var bytesToWriteBuffer = Math.Min(bufferRemaining, bytesToWrite);
                    var sourceOffset = chunk.Length - bytesToWrite;
                    Interlocked.Add(ref _bufferedBytes, bytesToWriteBuffer);
                    chunk.Slice(sourceOffset, bytesToWriteBuffer).CopyTo(buffer.AsSpan(chunkInfo.Length));
                    chunkInfo.Length += bytesToWriteBuffer;
                    if (chunkInfo.Length == buffer.Length)
                    {
                        _bufferQueue.Add(chunkInfo);
                        chunkInfo = default;
                    }
                    bytesToWrite -= bytesToWriteBuffer;
                }
            }
            catch
            {
                return CurlCallbackReturn.Write.Error;
            }

            return (UIntPtr)chunk.Length;
        }

        public void CompleteWriting()
        {
            ThrowIfDisposed();
            ref var writingChunk = ref _writingChunk;
            if(writingChunk.IsValid)
            {
                _bufferQueue.Add(writingChunk);
            }
            _bufferQueue.CompleteAdding();
            writingChunk = default;
        }
        public void Abort(Exception ex)
        {
            if (_isDisposed == 1)
            {
                return;
            }
            _abortException = ex;
            _bufferQueue.CompleteAdding();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            if (_abortException != null)
            {
                ExceptionDispatchInfo.Capture(_abortException).Throw();
            }
            var totalBytesRead = 0;

            while (count > 0)
            {
                if (_currentChunk.IsCompleted)
                {
                    _currentChunk.Buffer.Dispose();
                    _currentChunk = default;
                    if (_bufferQueue.IsCompleted)
                    {
                        break;
                    }

                    try
                    {
                        _currentChunk = _bufferQueue.Take();
                    }
                    catch (InvalidOperationException)
                    {
                        if (_abortException != null)
                        {
                            ExceptionDispatchInfo.Capture(_abortException).Throw();
                        }
                        break;
                    }
                }
                ref var chunk = ref _currentChunk;
                var bytesToCopy = Math.Min(count, chunk.Length - chunk.Offset);
                var currentBuffered = Interlocked.Add(ref _bufferedBytes, -bytesToCopy);
                Buffer.BlockCopy(chunk.Buffer, chunk.Offset, buffer, offset, bytesToCopy);

                offset += bytesToCopy;
                count -= bytesToCopy;
                totalBytesRead += bytesToCopy;
                chunk.Offset += bytesToCopy;

                var isLocked = false;

                try
                {
                    _pauseOrResumeLock.Enter(ref isLocked);
                    if (_isPaused && currentBuffered <= _lwmBytes)
                    {
                        _isPaused = false;

                        ThreadPool.QueueUserWorkItem(_ => _onResume());
                    }
                }
                finally
                {
                    if(isLocked)
                    {
                        _pauseOrResumeLock.Exit();
                    }
                }

                if (totalBytesRead > 0)
                {
                    break;
                }
            }

            return totalBytesRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        internal void CleanUp()
        {
            _isDisposed = 1;
            if (!_bufferQueue.IsAddingCompleted)
            {
                _bufferQueue.CompleteAdding();
            }
            while (_bufferQueue.TryTake(out var chunk))
            {
                chunk.Buffer.Dispose();
            }
            _currentChunk.Buffer.Dispose();
            _writingChunk.Buffer.Dispose();
            _currentChunk = default;
            _writingChunk = default;
            _bufferQueue.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _onDispose();                
            }
        }
        void ThrowIfDisposed()
        {
            if(_isDisposed == 1)
            {
                throw new ObjectDisposedException(nameof(CurlResponseStream));
            }
        }

        struct MemoryChunk
        {
            public PooledArray<byte> Buffer { get; init; }
            public int Length { get; set; }
            public int Offset { get; set; }
            public bool IsCompleted
            {
                get => Offset >= Length;
            }
            public bool IsValid
            {
                get => !Buffer.IsEmpty;
            }
        }
    }
}

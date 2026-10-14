#nullable enable
using System;
using System.IO;
using StorageResult = MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay.StorageAccess.Result;
using DocumentStream = MajdataPlay.Platform.Android.Runtime.Bindings.Net.Majdata.Majdataplay.StorageAccess.StreamHandle;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>Owns a Java provider stream with optional descriptor-backed seeking and length queries.</summary>
    /// <remarks>
    /// Dispose deterministically, including after cancellation. Synchronous operations and disposal are serialized.
    /// Inherited asynchronous Stream methods use these scoped JNI operations; provider calls can still block.
    /// Java byte arrays are signed sbyte arrays at the JNI boundary, then copied without changing their bit patterns.
    /// </remarks>
    public sealed class AndroidDocumentStream : Stream
    {
        /// <summary>Serializes stream operations against disposal and reuse of the write buffer.</summary>
        private readonly object _gate = new object();

        /// <summary>Records whether this stream was opened for reading rather than writing.</summary>
        private readonly bool _readable;

        /// <summary>Records whether the opened provider supports complete seek, length, and position semantics.</summary>
        private readonly bool _seekable;

        /// <summary>Reuses a bounded signed-byte write buffer, or null for read-only streams.</summary>
        private readonly sbyte[]? _writeBuffer;

        /// <summary>Owns one global Java stream handle until deterministic disposal.</summary>
        private DocumentStream? _handle;

        /// <summary>Adopts an already-open Java stream; no native descriptor is detached or borrowed.</summary>
        /// <param name="handle">The caller-owned Java stream handle whose ownership is transferred on success.</param>
        /// <param name="readable">Whether the handle is read-only rather than write-only.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentNullException">The handle is null.</exception>
        internal AndroidDocumentStream(DocumentStream handle, bool readable)
        {
            AndroidDocumentBridge.EnsureAndroid();
            if (handle is null)
            {
                throw new ArgumentNullException(nameof(handle));
            }
            _readable = readable;
            _seekable = handle.Seekable;
            _writeBuffer = readable ? null : new sbyte[AndroidDocumentBridge.MaxTransferSize];
            _handle = handle;
        }

        /// <summary>Gets whether the open stream supports sequential reading.</summary>
        public override bool CanRead
        {
            get
            {
                lock (_gate)
                {
                    return _handle is not null && _readable;
                }
            }
        }

        /// <summary>Gets whether the live opened resource supports reliable seek, length, and position operations.</summary>
        public override bool CanSeek
        {
            get
            {
                lock (_gate)
                {
                    return _handle is not null && _seekable;
                }
            }
        }

        /// <summary>Gets whether the open stream supports sequential writing.</summary>
        public override bool CanWrite
        {
            get
            {
                lock (_gate)
                {
                    return _handle is not null && !_readable;
                }
            }
        }

        /// <summary>Gets the logical length of the opened asset, independently of document-query metadata.</summary>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="NotSupportedException">The provider or append mode does not expose seekable length semantics.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied access.</exception>
        /// <exception cref="IOException">The opened resource no longer exposes a reliable length.</exception>
        public override long Length
        {
            get
            {
                lock (_gate)
                {
                    var handle = RequireSeekableHandle();
                    return InvokeValue(handle.GetLength);
                }
            }
        }

        /// <summary>Gets or sets the position relative to the opened asset's logical start.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The requested position is negative.</exception>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="NotSupportedException">The provider or append mode does not support seeking.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied access.</exception>
        /// <exception cref="IOException">The provider failed or the physical position would overflow.</exception>
        public override long Position
        {
            get
            {
                lock (_gate)
                {
                    var handle = RequireSeekableHandle();
                    return InvokeValue(handle.GetPosition);
                }
            }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                lock (_gate)
                {
                    var handle = RequireSeekableHandle();
                    InvokeValue(() => handle.SetPosition(value));
                }
            }
        }

        /// <summary>Reads at most one bounded JNI chunk, preserving all Java byte bit patterns.</summary>
        /// <param name="buffer">The destination byte array.</param>
        /// <param name="offset">The first destination index to fill.</param>
        /// <param name="count">The maximum requested number of bytes.</param>
        /// <returns>The number of bytes copied, or zero only for a zero-count request or provider EOF.</returns>
        /// <exception cref="ArgumentNullException">The buffer is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The offset or count is negative.</exception>
        /// <exception cref="ArgumentException">The requested slice is outside the buffer.</exception>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="NotSupportedException">This stream was opened for writing.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider revoked or denied read access.</exception>
        /// <exception cref="FileNotFoundException">The provider removed the document during a read.</exception>
        /// <exception cref="IOException">Reading failed or the bridge returned an invalid byte count.</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);
            lock (_gate)
            {
                var handle = RequireHandle();
                if (!_readable)
                {
                    throw new NotSupportedException("This SAF stream is write-only.");
                }
                if (count == 0)
                {
                    return 0;
                }
                var requested = Math.Min(count, AndroidDocumentBridge.MaxTransferSize);
                return AndroidDocumentBridge.Invoke(() =>
                {
                    using var result = AndroidDocumentBridge.RequireResult(handle.Read(requested));
                    AndroidDocumentBridge.CheckResult(result);
                    var read = result.Count;
                    if (read == -1)
                    {
                        return 0;
                    }
                    if (read <= 0 || read > requested)
                    {
                        throw new IOException("The SAF provider returned an invalid read count.");
                    }
                    var bytes = result.Data;
                    if (bytes is null || bytes.Length != read)
                    {
                        throw new IOException("The SAF bridge returned inconsistent signed byte data.");
                    }
                    Buffer.BlockCopy(bytes, 0, buffer, offset, read);
                    return read;
                });
            }
        }

        /// <summary>Writes the requested slice in bounded signed-byte chunks without emulating native descriptor semantics.</summary>
        /// <param name="buffer">The source byte array.</param>
        /// <param name="offset">The first source index to write.</param>
        /// <param name="count">The number of bytes to write.</param>
        /// <exception cref="ArgumentNullException">The buffer is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The offset or count is negative.</exception>
        /// <exception cref="ArgumentException">The requested slice is outside the buffer.</exception>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="NotSupportedException">This stream was opened for reading.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider revoked or denied write access.</exception>
        /// <exception cref="FileNotFoundException">The provider removed the document during a write.</exception>
        /// <exception cref="IOException">The provider failed, possibly after writing part of the requested data.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);
            lock (_gate)
            {
                var handle = RequireHandle();
                if (_readable)
                {
                    throw new NotSupportedException("This SAF stream is read-only.");
                }
                if (count == 0)
                {
                    return;
                }
                var bytes = _writeBuffer!;
                AndroidDocumentBridge.Invoke(() =>
                {
                    var remaining = count;
                    var sourceOffset = offset;
                    while (remaining != 0)
                    {
                        var chunk = Math.Min(remaining, bytes.Length);
                        Buffer.BlockCopy(buffer, sourceOffset, bytes, 0, chunk);
                        using var result = AndroidDocumentBridge.RequireResult(handle.Write(bytes, chunk));
                        AndroidDocumentBridge.CheckResult(result);
                        sourceOffset += chunk;
                        remaining -= chunk;
                    }
                    return true;
                });
            }
        }

        /// <summary>Flushes the provider output stream; flushing a live read-only stream is a no-op.</summary>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied access.</exception>
        /// <exception cref="IOException">The provider failed to flush pending writes.</exception>
        public override void Flush()
        {
            lock (_gate)
            {
                var handle = RequireHandle();
                AndroidDocumentBridge.Invoke(() =>
                {
                    using var result = AndroidDocumentBridge.RequireResult(handle.Flush());
                    AndroidDocumentBridge.CheckResult(result);
                    return true;
                });
            }
        }

        /// <summary>Moves within the opened asset using a signed 64-bit logical offset.</summary>
        /// <param name="offset">The displacement from the requested logical origin.</param>
        /// <param name="origin">The beginning, current position, or logical end of the opened asset.</param>
        /// <returns>The new position relative to the asset's start.</returns>
        /// <exception cref="ArgumentException">The seek origin is invalid.</exception>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="NotSupportedException">The provider or append mode does not support seeking.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied access.</exception>
        /// <exception cref="IOException">The requested position is negative, overflows, or seeking failed.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin != SeekOrigin.Begin && origin != SeekOrigin.Current && origin != SeekOrigin.End)
            {
                throw new ArgumentException("The seek origin is invalid.", nameof(origin));
            }
            lock (_gate)
            {
                var handle = RequireSeekableHandle();
                return InvokeValue(() => handle.Seek(offset, (int)origin));
            }
        }

        /// <summary>Rejects changing length independently of the explicit mode used when opening.</summary>
        /// <param name="value">The unused requested stream length.</param>
        /// <exception cref="NotSupportedException">SAF streams do not expose SetLength.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException("Use OpenWrite's explicit truncate or append mode instead.");
        }

        /// <summary>Closes the Java stream exactly once and releases its JNI reference even when closing fails.</summary>
        /// <param name="disposing">Whether deterministic disposal should release the caller-owned Java resource.</param>
        /// <exception cref="UnauthorizedAccessException">The provider denied closing the stream.</exception>
        /// <exception cref="IOException">The provider failed to close or flush the stream.</exception>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    lock (_gate)
                    {
                        var handle = _handle;
                        _handle = null;
                        if (handle is not null)
                        {
                            AndroidDocumentBridge.CloseHandle(handle);
                        }
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>Gets the live owned handle while the caller holds the operation lock.</summary>
        /// <returns>The Java stream handle, without transferring ownership.</returns>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        private DocumentStream RequireHandle()
        {
            return _handle ?? throw new ObjectDisposedException(nameof(AndroidDocumentStream));
        }

        /// <summary>Gets the live handle only when all seek-related operations are supported.</summary>
        /// <returns>The borrowed seekable Java handle while the operation lock is held.</returns>
        /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
        /// <exception cref="NotSupportedException">The opened provider resource does not support seeking.</exception>
        private DocumentStream RequireSeekableHandle()
        {
            var handle = RequireHandle();
            if (!_seekable)
            {
                throw new NotSupportedException("The SAF stream does not support seeking and length queries.");
            }
            return handle;
        }

        /// <summary>Reads a signed 64-bit value through the existing typed, thread-attached JNI protocol.</summary>
        /// <param name="operation">The Java operation returning a length or logical position.</param>
        /// <returns>The checked result value without taking ownership of the stream handle.</returns>
        /// <exception cref="ArgumentException">The Java operation rejected the requested argument.</exception>
        /// <exception cref="FileNotFoundException">The provider removed the resource.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied access.</exception>
        /// <exception cref="NotSupportedException">The operation is unavailable for this resource.</exception>
        /// <exception cref="IOException">The provider or JNI bridge failed.</exception>
        private static long InvokeValue(Func<StorageResult?> operation)
        {
            return AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.RequireResult(operation());
                AndroidDocumentBridge.CheckResult(result);
                return result.Value;
            });
        }

        /// <summary>Validates array slices without offset-plus-count overflow.</summary>
        /// <param name="buffer">The source or destination buffer.</param>
        /// <param name="offset">The first requested array index.</param>
        /// <param name="count">The requested number of bytes.</param>
        /// <exception cref="ArgumentNullException">The buffer is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The offset or count is negative.</exception>
        /// <exception cref="ArgumentException">The requested range exceeds the buffer.</exception>
        private static void ValidateBuffer(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (offset > buffer.Length || count > buffer.Length - offset)
            {
                throw new ArgumentException("The buffer range is outside the array.");
            }
        }
    }
}

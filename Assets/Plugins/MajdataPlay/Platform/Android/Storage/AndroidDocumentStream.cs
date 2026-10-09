#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>Owns a sequential Java provider stream without exposing a native descriptor or assuming seekability.</summary>
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

        /// <summary>Reuses a bounded signed-byte write buffer, or null for read-only streams.</summary>
        private readonly sbyte[]? _writeBuffer;

        /// <summary>Owns one global Java stream handle until deterministic disposal.</summary>
        private AndroidJavaObject? _handle;

        /// <summary>Adopts an already-open Java stream; no native descriptor is detached or borrowed.</summary>
        /// <param name="handle">The caller-owned Java stream handle whose ownership is transferred on success.</param>
        /// <param name="readable">Whether the handle is read-only rather than write-only.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentNullException">The handle is null.</exception>
        internal AndroidDocumentStream(AndroidJavaObject handle, bool readable)
        {
            AndroidDocumentBridge.EnsureAndroid();
            if (handle is null)
            {
                throw new ArgumentNullException(nameof(handle));
            }
            _readable = readable;
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

        /// <summary>Gets false because cloud documents and provider pipes need not support seeking.</summary>
        public override bool CanSeek
        {
            get
            {
                return false;
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

        /// <summary>Rejects stream length queries; nullable metadata does not establish a seekable stream length.</summary>
        /// <exception cref="NotSupportedException">Length queries are unsupported.</exception>
        public override long Length
        {
            get
            {
                throw new NotSupportedException("SAF streams do not expose a seekable length.");
            }
        }

        /// <summary>Rejects getting or setting a seek position for sequential provider streams.</summary>
        /// <exception cref="NotSupportedException">Position queries and seeking are unsupported.</exception>
        public override long Position
        {
            get
            {
                throw new NotSupportedException("SAF streams do not expose a seek position.");
            }
            set
            {
                throw new NotSupportedException("SAF streams do not support seeking.");
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
                    using var result = AndroidDocumentBridge.Call(handle, "read", "I", requested);
                    AndroidDocumentBridge.CheckResult(result);
                    var read = AndroidDocumentBridge.Field<int>(result, AndroidDocumentBridge.ResultClass, "count", "I");
                    if (read == -1)
                    {
                        return 0;
                    }
                    if (read <= 0 || read > requested)
                    {
                        throw new IOException("The SAF provider returned an invalid read count.");
                    }
                    var bytes = AndroidDocumentBridge.Field<sbyte[]?>(result, AndroidDocumentBridge.ResultClass, "data", "[B");
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
                        using var result = AndroidDocumentBridge.Call(handle, "write", "[BI", bytes, chunk);
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
                    using var result = AndroidDocumentBridge.Call(handle, "flush", "");
                    AndroidDocumentBridge.CheckResult(result);
                    return true;
                });
            }
        }

        /// <summary>Rejects seeking, including on providers which happen to use a local file.</summary>
        /// <param name="offset">The unused requested seek displacement.</param>
        /// <param name="origin">The unused requested seek origin.</param>
        /// <returns>No value; this operation always throws.</returns>
        /// <exception cref="NotSupportedException">SAF streams are sequential.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("SAF streams do not support seeking.");
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
        private AndroidJavaObject RequireHandle()
        {
            return _handle ?? throw new ObjectDisposedException(nameof(AndroidDocumentStream));
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

#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MajdataPlay.Tests
{
    /// <summary>Exposes only sequential fixture reads, like a provider stream with no seekable representation.</summary>
    internal sealed class ValidationSequentialReadStream : Stream
    {
        /// <summary>Stores the caller-owned underlying stream closed when this wrapper is disposed.</summary>
        private readonly Stream _inner;

        /// <summary>Gets whether the underlying fixture remains readable.</summary>
        public override bool CanRead => _inner.CanRead;
        /// <summary>Gets false because seeking is deliberately unsupported.</summary>
        public override bool CanSeek => false;
        /// <summary>Gets false because fixture streams never support writes.</summary>
        public override bool CanWrite => false;
        /// <summary>Rejects size queries to ensure production buffers original bytes before parsing.</summary>
        /// <exception cref="NotSupportedException">Length was queried on a sequential-only stream.</exception>
        public override long Length => throw new NotSupportedException("The fixture stream has no seekable length.");
        /// <summary>Rejects position queries and updates on sequential-only streams.</summary>
        /// <exception cref="NotSupportedException">Position was queried or set.</exception>
        public override long Position
        {
            get
            {
                throw new NotSupportedException("The fixture stream has no seekable position.");
            }
            set
            {
                throw new NotSupportedException("The fixture stream has no seekable position.");
            }
        }

        /// <summary>Owns a readable fixture stream without exposing its seek functionality.</summary>
        /// <param name="inner">The readable stream disposed together with this wrapper.</param>
        public ValidationSequentialReadStream(Stream inner)
        {
            _inner = inner;
        }

        /// <summary>Reads bytes in order without consulting Length or Position.</summary>
        /// <param name="buffer">The caller's destination array.</param>
        /// <param name="offset">The first destination index.</param>
        /// <param name="count">The maximum requested byte count.</param>
        /// <returns>The number of bytes read, or zero at the end.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        /// <summary>Asynchronously reads original bytes while propagating cancellation.</summary>
        /// <param name="buffer">The caller's destination array.</param>
        /// <param name="offset">The first destination index.</param>
        /// <param name="count">The maximum requested byte count.</param>
        /// <param name="cancellationToken">The token canceling the read.</param>
        /// <returns>A task containing the byte count, or zero at the end.</returns>
        /// <exception cref="OperationCanceledException">The read is canceled.</exception>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        /// <summary>Performs no flush because read-only fixtures have no pending writes.</summary>
        public override void Flush()
        {
        }

        /// <summary>Rejects seeking rather than emulating provider positioning.</summary>
        /// <param name="offset">The unused seek offset.</param>
        /// <param name="origin">The unused seek origin.</param>
        /// <returns>No position; seeking is unsupported.</returns>
        /// <exception cref="NotSupportedException">Seeking was attempted.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seeking is outside the sequential fixture contract.");
        }

        /// <summary>Rejects length changes on a read-only fixture.</summary>
        /// <param name="value">The unused requested length.</param>
        /// <exception cref="NotSupportedException">A length change was attempted.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException("The fixture stream is read-only.");
        }

        /// <summary>Rejects writes rather than storing downloaded media.</summary>
        /// <param name="buffer">The unused source array.</param>
        /// <param name="offset">The unused source index.</param>
        /// <param name="count">The unused byte count.</param>
        /// <exception cref="NotSupportedException">Writing was attempted.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The fixture stream is read-only.");
        }

        /// <summary>Closes the original fixture stream when explicitly disposed.</summary>
        /// <param name="disposing">Whether to release managed stream resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

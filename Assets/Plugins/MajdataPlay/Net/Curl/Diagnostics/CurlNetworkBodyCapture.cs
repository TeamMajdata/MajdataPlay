#nullable enable
using System;

namespace MajdataPlay.Net.Curl.Diagnostics
{
    /// <summary>
    /// Keeps the leading bytes of a request or response body for <see cref="CurlNetworkRecord"/>, up to a per-body limit
    /// and the budget shared through <see cref="CurlNetworkMonitor"/>, while counting every byte.
    /// </summary>
    /// <remarks>Not thread-safe; the owning record guards every call with its lock.</remarks>
    internal sealed class CurlNetworkBodyCapture
    {
        /// <summary>
        /// The smallest buffer allocated once the first byte arrives.
        /// </summary>
        private const int MinCapacity = 4096;

        /// <summary>
        /// The maximum number of bytes this capture keeps.
        /// </summary>
        private readonly int _limit;

        /// <summary>
        /// The captured bytes; only the first <see cref="_length"/> are valid.
        /// </summary>
        private byte[] _buffer = Array.Empty<byte>();

        /// <summary>
        /// The number of captured bytes, all of which are reserved from the shared budget.
        /// </summary>
        private int _length;

        /// <summary>
        /// Whether capturing stopped because the shared budget ran out, an allocation failed or the capture was released.
        /// </summary>
        private bool _isStopped;

        /// <summary>
        /// Creates an empty capture.
        /// </summary>
        /// <param name="limit">The maximum number of bytes to keep; 0 only counts bytes.</param>
        public CurlNetworkBodyCapture(int limit)
        {
            _limit = Math.Max(0, limit);
        }

        /// <summary>
        /// Gets the number of body bytes seen, captured or not.
        /// </summary>
        public long TotalBytes { get; private set; }

        /// <summary>
        /// Gets the number of bytes kept.
        /// </summary>
        public int CapturedBytes
        {
            get => _length;
        }

        /// <summary>
        /// Gets whether some of the bytes seen were not kept.
        /// </summary>
        public bool IsTruncated
        {
            get => TotalBytes > _length;
        }

        /// <summary>
        /// Counts <paramref name="data"/> and keeps as much of it as the limit and the shared budget allow. Never throws.
        /// </summary>
        /// <param name="data">The next body bytes.</param>
        public void Append(ReadOnlySpan<byte> data)
        {
            TotalBytes += data.Length;
            var count = Math.Min(data.Length, _limit - _length);
            if (_isStopped || count <= 0)
            {
                return;
            }
            if (!CurlNetworkMonitor.TryReserveBodyBytes(count))
            {
                _isStopped = true;
                return;
            }
            try
            {
                EnsureCapacity(_length + count);
            }
            catch (OutOfMemoryException)
            {
                // A debugging aid must not take down the transfer that libcurl is calling back from.
                CurlNetworkMonitor.ReleaseBodyBytes(count);
                _isStopped = true;
                return;
            }
            data.Slice(0, count).CopyTo(_buffer.AsSpan(_length));
            _length += count;
        }

        /// <summary>
        /// Drops everything after <paramref name="position"/>, because libcurl rewound the upload to send it again
        /// (after a redirect or an authentication challenge).
        /// </summary>
        /// <param name="position">The stream position libcurl rewound to.</param>
        public void Rewind(long position)
        {
            position = Math.Max(0, position);
            if (position < _length)
            {
                CurlNetworkMonitor.ReleaseBodyBytes(_length - position);
                _length = (int)position;
            }
            TotalBytes = Math.Min(TotalBytes, position);
        }

        /// <summary>
        /// Copies the captured bytes.
        /// </summary>
        /// <returns>A new array holding the captured bytes.</returns>
        public byte[] ToArray()
        {
            return _buffer.AsSpan(0, _length).ToArray();
        }

        /// <summary>
        /// Frees the captured bytes, returns them to the shared budget and stops capturing. Later appends are only counted.
        /// </summary>
        public void Release()
        {
            if (_length > 0)
            {
                CurlNetworkMonitor.ReleaseBodyBytes(_length);
            }
            _buffer = Array.Empty<byte>();
            _length = 0;
            _isStopped = true;
        }

        /// <summary>
        /// Grows the buffer geometrically, without exceeding the limit.
        /// </summary>
        /// <param name="required">The number of bytes the buffer must hold.</param>
        /// <exception cref="OutOfMemoryException">The larger buffer could not be allocated.</exception>
        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
            {
                return;
            }
            var capacity = Math.Max(MinCapacity, _buffer.Length * 2L);
            capacity = Math.Min(Math.Max(capacity, required), _limit);
            Array.Resize(ref _buffer, (int)capacity);
        }
    }
}

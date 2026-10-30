using System;

#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>
    /// Holds the digests of one online chart's files as recorded in <see cref="OnlineChartCache"/>, so an
    /// <see cref="OnlineSongDetail"/> can find its files in the <see cref="ObjectStore"/> and download only missing ones.
    /// </summary>
    /// <remarks>
    /// A record is resolved when its digests came from the <c>maichart/info</c> API: a file without a digest is then
    /// known to be missing on the server. In an unresolved record a missing digest is only unknown, and the file is
    /// requested from the chart endpoint, whose digest is recorded once the download completes.
    /// </remarks>
    internal sealed class OnlineChartRecord
    {
        /// <summary>Gets the server-assigned identity of the chart.</summary>
        public string ChartId { get; }
        /// <summary>Gets the endpoint key, the absolute URI of the endpoint.</summary>
        public string Endpoint { get; }
        /// <summary>Gets whether the digests came from the <c>maichart/info</c> API, so a missing digest means a missing file.</summary>
        public bool IsResolved { get; }
        /// <summary>Gets the cache owning the record, which also owns the object store.</summary>
        public OnlineChartCache Cache { get; }

        /// <summary>The number of resource kinds.</summary>
        private const int KindCount = (int)OnlineChartResourceKind.Thumbnail + 1;

        /// <summary>Records whether digest changes are written to the runtime database.</summary>
        private readonly bool _isPersistent;
        /// <summary>Stores the digests by resource kind, guarded by itself.</summary>
        private readonly ChartResourceDigest?[] _digests = new ChartResourceDigest?[KindCount];
        /// <summary>Stores the direct download URIs reported by the info API in this session, by resource kind.</summary>
        private readonly Uri?[] _urls = new Uri?[KindCount];

        /// <summary>Creates a record.</summary>
        /// <param name="cache">The cache owning the record.</param>
        /// <param name="endpoint">The endpoint key.</param>
        /// <param name="chartId">The chart identity.</param>
        /// <param name="isResolved">Whether the digests came from the info API.</param>
        /// <param name="isPersistent">Whether digest changes are written to the runtime database.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal OnlineChartRecord(OnlineChartCache cache, string endpoint, string chartId, bool isResolved, bool isPersistent)
        {
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            ChartId = chartId ?? throw new ArgumentNullException(nameof(chartId));
            IsResolved = isResolved;
            _isPersistent = isPersistent;
        }

        /// <summary>Gets the recorded digest of a file.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <param name="digest">The digest, when recorded.</param>
        /// <returns>True when a digest is recorded; otherwise false.</returns>
        public bool TryGetDigest(OnlineChartResourceKind kind, out ChartResourceDigest digest)
        {
            lock (_digests)
            {
                var recorded = _digests[(int)kind];
                digest = recorded.GetValueOrDefault();
                return recorded.HasValue;
            }
        }

        /// <summary>Checks whether the server is known not to store a file.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <returns>True when the record is resolved and has no digest for the file; otherwise false.</returns>
        public bool IsMissing(OnlineChartResourceKind kind)
        {
            return IsResolved && !TryGetDigest(kind, out _);
        }

        /// <summary>Gets the direct download URI the info API reported for a file in this session.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <returns>The URI, or null when the file was not resolved in this session.</returns>
        public Uri? GetUrl(OnlineChartResourceKind kind)
        {
            lock (_digests)
            {
                return _urls[(int)kind];
            }
        }

        /// <summary>Records the digest of a downloaded file and writes it to the runtime database.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <param name="digest">The digest of the downloaded file.</param>
        /// <remarks>The write is queued; a failure is logged, because the record stays correct for this session.</remarks>
        public void SetDigest(OnlineChartResourceKind kind, ChartResourceDigest digest)
        {
            lock (_digests)
            {
                if (_digests[(int)kind] == digest)
                {
                    return;
                }
                _digests[(int)kind] = digest;
            }
            if (_isPersistent)
            {
                Cache.QueueDigestWrite(this, kind, digest);
            }
        }

        /// <summary>Loads a digest without writing it to the database, while the cache builds the record.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <param name="digest">The digest, or null when none is recorded.</param>
        /// <param name="url">The direct download URI reported by the info API, or null.</param>
        internal void Load(OnlineChartResourceKind kind, ChartResourceDigest? digest, Uri? url)
        {
            lock (_digests)
            {
                _digests[(int)kind] = digest;
                _urls[(int)kind] = url;
            }
        }
    }

    /// <summary>The digests of one stored file.</summary>
    internal readonly struct ChartResourceDigest : IEquatable<ChartResourceDigest>
    {
        /// <summary>Gets the lowercase hexadecimal MD5 digest.</summary>
        public string Md5 { get; }
        /// <summary>Gets the lowercase hexadecimal SHA-256 digest, which names the file in the <see cref="ObjectStore"/>.</summary>
        public string Sha256 { get; }

        /// <summary>Creates the digests of a file.</summary>
        /// <param name="md5">The lowercase hexadecimal MD5 digest.</param>
        /// <param name="sha256">The lowercase hexadecimal SHA-256 digest.</param>
        public ChartResourceDigest(string md5, string sha256)
        {
            Md5 = md5;
            Sha256 = sha256;
        }

        /// <summary>Compares two digests.</summary>
        /// <param name="other">The other digest.</param>
        /// <returns>True when both digests are equal; otherwise false.</returns>
        public bool Equals(ChartResourceDigest other)
        {
            return Md5 == other.Md5 && Sha256 == other.Sha256;
        }

        /// <summary>Compares the digest with an object.</summary>
        /// <param name="obj">The object.</param>
        /// <returns>True when the object is an equal digest; otherwise false.</returns>
        public override bool Equals(object? obj)
        {
            return obj is ChartResourceDigest other && Equals(other);
        }

        /// <summary>Gets the hash code of the digest.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Md5, Sha256);
        }

        /// <summary>Compares two digests.</summary>
        /// <param name="left">The first digest.</param>
        /// <param name="right">The second digest.</param>
        /// <returns>True when both digests are equal; otherwise false.</returns>
        public static bool operator ==(ChartResourceDigest left, ChartResourceDigest right)
        {
            return left.Equals(right);
        }

        /// <summary>Compares two digests.</summary>
        /// <param name="left">The first digest.</param>
        /// <param name="right">The second digest.</param>
        /// <returns>True when the digests differ; otherwise false.</returns>
        public static bool operator !=(ChartResourceDigest left, ChartResourceDigest right)
        {
            return !left.Equals(right);
        }
    }
}

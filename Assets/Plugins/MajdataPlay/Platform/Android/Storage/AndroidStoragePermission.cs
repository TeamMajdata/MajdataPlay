#nullable enable
using System;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>A persisted URI permission, not a guarantee of continued document availability.</summary>
    public sealed class AndroidStoragePermission
    {
        /// <summary>Gets the original grant URI to save, reopen, or release.</summary>
        public string Location { get; }

        /// <summary>Gets whether read access is granted.</summary>
        public bool CanRead { get; }

        /// <summary>Gets whether write access is granted.</summary>
        public bool CanWrite { get; }

        /// <summary>Gets when Android persisted the grant, in UTC.</summary>
        public DateTime PersistedAtUtc { get; }

        /// <summary>Creates a snapshot of an Android URI grant.</summary>
        /// <param name="location">The original content URI.</param>
        /// <param name="canRead">Whether read access is granted.</param>
        /// <param name="canWrite">Whether write access is granted.</param>
        /// <param name="persistedAtUtc">The UTC grant timestamp.</param>
        internal AndroidStoragePermission(string location, bool canRead, bool canWrite, DateTime persistedAtUtc)
        {
            Location = location;
            CanRead = canRead;
            CanWrite = canWrite;
            PersistedAtUtc = persistedAtUtc;
        }
    }
}

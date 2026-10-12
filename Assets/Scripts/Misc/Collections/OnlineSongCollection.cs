using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
#nullable enable
namespace MajdataPlay.Collections
{
    /// <summary>Represents a remote song list whose refresh operations never scan its optional local cache.</summary>
    public class OnlineSongCollection : SongCollection
    {
        /// <summary>Gets the online endpoint supplying this collection's songs.</summary>
        public ApiEndpoint Source { get; init; }

        /// <summary>Gets the complete requested course or playlist definition, including hashes not currently available.</summary>
        /// <remarks>Local-only refreshes use this definition to restore temporarily missing songs without a new network request.</remarks>
        internal DanInfo? Definition { get; init; }

        /// <summary>Creates an online collection without a backing cache directory.</summary>
        /// <param name="source">The endpoint supplying the remote song list.</param>
        /// <param name="name">The collection's display name.</param>
        /// <param name="pArray">The remote songs copied into the initial snapshot.</param>
        /// <exception cref="System.ArgumentNullException">The name or song array is null.</exception>
        public OnlineSongCollection(ApiEndpoint source, string name, ISongDetail[] pArray) : base(name, pArray)
        {
            IsOnline = true;
            Source = source;
        }

        /// <summary>Creates an online collection while retaining an optional cache directory's original backend.</summary>
        /// <param name="source">The endpoint supplying the remote song list.</param>
        /// <param name="directory">The backing cache directory, or null for a virtual remote collection.</param>
        /// <param name="name">The collection's display name.</param>
        /// <param name="pArray">The remote songs copied into the initial snapshot.</param>
        /// <remarks>Refresh and RefreshAsync leave this remote list unchanged, even when the cache directory is present.</remarks>
        /// <exception cref="System.ArgumentNullException">The name or song array is null.</exception>
        /// <exception cref="System.ArgumentException">The cache directory does not exist.</exception>
        /// <exception cref="System.IO.IOException">The collection identifier cannot be read or persisted.</exception>
        /// <exception cref="System.UnauthorizedAccessException">The backend denies access to the directory or identifier.</exception>
        /// <exception cref="System.NotSupportedException">The backend does not support identifier operations.</exception>
        public OnlineSongCollection(ApiEndpoint source, StorageDirectory? directory, string name, ISongDetail[] pArray)
            : base(directory, name, pArray)
        {
            IsOnline = true;
            Source = source;
        }
    }
}

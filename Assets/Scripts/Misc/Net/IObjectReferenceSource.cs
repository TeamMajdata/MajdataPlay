using System.Collections.Generic;
using System.IO;

#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>A component that keeps files in an <see cref="ObjectStore"/> and reports which of them it still needs.</summary>
    internal interface IObjectReferenceSource
    {
        /// <summary>Gets the digests of every object the component still references.</summary>
        /// <returns>The lowercase hexadecimal SHA-256 digests; values that are not digests are ignored.</returns>
        /// <remarks>Called on the thread pool while the store is pruned at startup.</remarks>
        /// <exception cref="IOException">The references could not be read; the store is then not pruned.</exception>
        IEnumerable<string> GetReferencedDigests();
    }
}

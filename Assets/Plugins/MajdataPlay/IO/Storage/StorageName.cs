#nullable enable
using System;

namespace MajdataPlay.IO.Storage
{
    /// <summary>Validates names used for immediate children across storage backends.</summary>
    public static class StorageName
    {
        /// <summary>Rejects empty names, traversal segments, and path separators.</summary>
        /// <param name="name">One child display name, not a path or URI.</param>
        /// <exception cref="ArgumentNullException">The name is null.</exception>
        /// <exception cref="ArgumentException">The name is empty, a traversal segment, or contains a separator or null character.</exception>
        public static void Validate(string name)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (name.Length == 0 || name == "." || name == ".." ||
                name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("A single child name without path separators is required.", nameof(name));
            }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;

namespace MajdataPlay.IO.Storage
{
    public static partial class FileSystem
    {
        /// <summary>Splits backend-neutral relative paths into validated child names.</summary>
        /// <remarks>
        /// A relative path is a sequence of child names joined by <c>/</c> or <c>\</c>. It is never combined with a storage
        /// location through <see cref="System.IO.Path"/>; callers resolve each name through the owning directory's backend
        /// instead, so the same relative path works for native paths, jar archives, and opaque content URIs.
        /// </remarks>
        public static class Path
        {
            /// <summary>The separators accepted between child names.</summary>
            private static readonly char[] s_separators = { '/', '\\' };

            /// <summary>Splits a relative path into child names, dropping empty and current-directory segments.</summary>
            /// <param name="relativePath">Child names joined by <c>/</c> or <c>\</c>; an empty path denotes the directory itself.</param>
            /// <returns>The validated child names in order, or an empty array for an empty path.</returns>
            /// <exception cref="ArgumentNullException">The path is null.</exception>
            /// <exception cref="ArgumentException">The path is rooted, is a URI, or contains a traversal segment or null character.</exception>
            public static string[] Split(string relativePath)
            {
                if (relativePath is null)
                {
                    throw new ArgumentNullException(nameof(relativePath));
                }
                if (relativePath.Length == 0)
                {
                    return Array.Empty<string>();
                }
                if (relativePath[0] == '/' || relativePath[0] == '\\' || System.IO.Path.IsPathRooted(relativePath) ||
                    relativePath.IndexOf("://", StringComparison.Ordinal) >= 0)
                {
                    throw new ArgumentException("A relative path without a root or URI scheme is required.", nameof(relativePath));
                }
                var parts = relativePath.Split(s_separators);
                var names = new List<string>(parts.Length);
                foreach (var part in parts)
                {
                    if (part.Length == 0 || part == ".")
                    {
                        continue;
                    }
                    StorageName.Validate(part);
                    names.Add(part);
                }
                return names.ToArray();
            }
        }
    }
}

using System;
using System.IO;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using UnityEngine;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Reads the resources packaged with the player in its streaming assets, which an Android APK keeps inside the archive.</summary>
    internal static class PackagedResources
    {
        /// <summary>Opens the packaged streaming assets root as a directory handle.</summary>
        /// <returns>A handle to the packaged root; it may not exist when nothing was packaged.</returns>
        /// <exception cref="ArgumentException">The packaged root location is invalid.</exception>
        public static StorageDirectory OpenRoot()
        {
            return FileSystem.Directory.Open(Application.streamingAssetsPath);
        }

        /// <summary>Reads the complete bytes of one packaged resource.</summary>
        /// <param name="relativePath">The portable packaged resource path from a validated manifest.</param>
        /// <param name="data">Receives the packaged bytes, or an empty array after failure.</param>
        /// <param name="rootLocation">The packaged root captured on the main thread, or null to read Unity's current path.</param>
        /// <returns>Whether the packaged bytes were read; failures are logged.</returns>
        public static bool TryReadAllBytes(string relativePath, out byte[] data, string? rootLocation = null)
        {
            try
            {
                data = FileSystem.File.Open(GetLocation(relativePath, rootLocation)).ReadAllBytes();
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(PackagedResources), $"Failed to read packaged resource: {relativePath}\n{exception}");
                data = Array.Empty<byte>();
                return false;
            }
        }

        /// <summary>Resolves one validated manifest path against the platform's packaged resource root.</summary>
        /// <param name="relativePath">The portable packaged resource path whose segments contain literal file names.</param>
        /// <param name="rootLocation">The packaged root captured on the main thread, or null to read Unity's current path.</param>
        /// <returns>A local path or a jar URI with each archive entry segment escaped exactly once.</returns>
        /// <remarks>The packaged root is a Unity application path, so it is combined directly rather than through a handle.</remarks>
        /// <exception cref="ArgumentException">The local path is invalid.</exception>
        /// <exception cref="UriFormatException">An archive entry segment cannot be escaped.</exception>
        public static string GetLocation(string relativePath, string? rootLocation = null)
        {
            var root = rootLocation ?? Application.streamingAssetsPath;
            if (root.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
            {
                var segments = relativePath.Split('/');
                for (var i = 0; i < segments.Length; i++)
                {
                    segments[i] = Uri.EscapeDataString(segments[i]);
                }
                return root.TrimEnd('/') + "/" + string.Join("/", segments);
            }
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}

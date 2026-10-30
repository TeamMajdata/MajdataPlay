using System;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Reads packaged resources below one packaged root, reusing a single open archive for an Android APK.</summary>
    /// <remarks>
    /// Opening a jar location reads and indexes the entire APK, so reading files one by one repeats that work for every
    /// file. This reader opens the APK on its first read and keeps it until disposal. Native packaged roots are read file
    /// by file. A reader is not thread-safe; each extraction worker owns its own.
    /// </remarks>
    internal sealed class PackagedResourceReader : IDisposable
    {
        /// <summary>The packaged root captured on the Unity main thread.</summary>
        private readonly string _rootLocation;

        /// <summary>Whether the packaged root is a jar location inside an APK.</summary>
        private readonly bool _isArchive;

        /// <summary>The open APK directory, or null until the first archive read succeeds in opening it.</summary>
        private ArchiveDirectoryReader? _archive;

        /// <summary>Initializes a reader without touching storage.</summary>
        /// <param name="rootLocation">The packaged root captured on the Unity main thread.</param>
        public PackagedResourceReader(string rootLocation)
        {
            _rootLocation = rootLocation;
            _isArchive = rootLocation.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reads the complete bytes of one packaged resource.</summary>
        /// <param name="relativePath">The portable packaged resource path from a validated manifest.</param>
        /// <param name="data">Receives the packaged bytes, or an empty array after failure.</param>
        /// <returns>Whether the packaged bytes were read; failures are logged.</returns>
        public bool TryReadAllBytes(string relativePath, out byte[] data)
        {
            try
            {
                if (_isArchive)
                {
                    _archive ??= FileSystem.Directory.OpenArchiveReader(_rootLocation);
                    data = _archive.ReadAllBytes(relativePath);
                }
                else
                {
                    data = FileSystem.File.Open(PackagedResources.GetLocation(relativePath, _rootLocation)).ReadAllBytes();
                }
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(PackagedResourceReader),$"Failed to read packaged resource: {relativePath}\n{exception}");
                data = Array.Empty<byte>();
                return false;
            }
        }

        /// <summary>Closes the APK when it was opened.</summary>
        public void Dispose()
        {
            _archive?.Dispose();
            _archive = null;
        }
    }
}

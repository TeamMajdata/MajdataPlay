using System;
using MajdataPlay.IO.Storage;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests
{
    /// <summary>
    /// Creates a unique directory under Unity's temporary cache path and deletes it with its contents when disposed.
    /// </summary>
    public sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>Creates a new, empty temporary directory.</summary>
        /// <exception cref="System.IO.IOException">The directory could not be created.</exception>
        public TemporaryDirectory()
        {
            var root = FileSystem.Directory.Create(Application.temporaryCachePath).CreateDirectory("MajdataPlay.Tests");
            Directory = root.CreateDirectory(Guid.NewGuid().ToString("N"));
        }

        /// <summary>Gets the handle of the temporary directory.</summary>
        public StorageDirectory Directory { get; }

        /// <summary>Gets the location of the temporary directory.</summary>
        public string Location => Directory.Location;

        /// <summary>Creates a text file directly inside the temporary directory.</summary>
        /// <param name="name">The file name.</param>
        /// <param name="text">The UTF-8 file contents.</param>
        /// <returns>The created file.</returns>
        /// <exception cref="System.IO.IOException">The file could not be written.</exception>
        public StorageFile WriteText(string name, string text)
        {
            var file = Directory.CreateFile(name);
            file.WriteAllText(text);
            return file;
        }

        /// <summary>Deletes the directory and everything in it, ignoring a directory that no longer exists.</summary>
        public void Dispose()
        {
            if (Directory.Exists)
            {
                Directory.Delete(true);
            }
        }
    }
}

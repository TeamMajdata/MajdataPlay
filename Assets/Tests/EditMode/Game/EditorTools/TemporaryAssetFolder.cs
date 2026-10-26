using System;
using UnityEditor;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// A uniquely named folder under the project's Assets for editor tools that must write and import assets;
    /// it is deleted with its contents and meta files when disposed.
    /// </summary>
    internal sealed class TemporaryAssetFolder : IDisposable
    {
        /// <summary>Creates the folder path; the folder itself is created by the tool under test.</summary>
        public TemporaryAssetFolder()
        {
            Path = $"Assets/MajdataPlayTestsTemp_{Guid.NewGuid():N}";
        }

        /// <summary>Gets the project-relative folder path.</summary>
        public string Path { get; }

        /// <summary>Gets a project-relative path inside the folder.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The asset path.</returns>
        public string Combine(string name)
        {
            return $"{Path}/{name}";
        }

        /// <summary>Reads a text file written into the folder.</summary>
        /// <param name="name">The file name.</param>
        /// <returns>The file text.</returns>
        /// <exception cref="System.IO.FileNotFoundException">The file does not exist.</exception>
        public string ReadText(string name)
        {
            var folder = MajdataPlay.IO.Storage.FileSystem.Directory.Open(System.IO.Path.GetFullPath(Path));
            var file = folder.FindFile(name) ?? throw new System.IO.FileNotFoundException(name);
            return file.ReadAllText();
        }

        /// <summary>Deletes the folder through the asset database, removing imported assets and meta files.</summary>
        public void Dispose()
        {
            AssetDatabase.DeleteAsset(Path);
            var absolute = System.IO.Path.GetFullPath(Path);
            var folder = MajdataPlay.IO.Storage.FileSystem.Directory.Open(absolute);
            if (folder.Exists)
            {
                folder.Delete(true);
            }
            var meta = MajdataPlay.IO.Storage.FileSystem.Directory.Open(System.IO.Path.GetDirectoryName(absolute)!).FindFile(System.IO.Path.GetFileName(absolute) + ".meta");
            meta?.Delete();
        }
    }
}

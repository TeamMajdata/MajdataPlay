#nullable enable
using System;
using System.IO.Compression;
using System.Text;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>Writes ZIP archives through the storage facade and builds jar locations for them.</summary>
    internal static class ZipFixture
    {
        /// <summary>Creates an archive with the given entries; a name ending in a slash creates an explicit directory record.</summary>
        /// <param name="directory">The directory receiving the archive.</param>
        /// <param name="archiveName">The archive file name.</param>
        /// <param name="entries">Pairs of raw ZIP entry names and UTF-8 contents; the content of directory records is ignored.</param>
        /// <returns>The archive file.</returns>
        public static StorageFile Create(StorageDirectory directory, string archiveName, params (string Name, string Content)[] entries)
        {
            var file = directory.CreateFile(archiveName);
            using var stream = file.OpenWrite();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2022, 3, 4, 5, 6, 8, TimeSpan.Zero);
                if (name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }
                using var entryStream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(bytes, 0, bytes.Length);
            }
            return file;
        }

        /// <summary>Builds the jar location of an entry inside a local archive.</summary>
        /// <param name="archive">The local archive file.</param>
        /// <param name="escapedEntryPath">The already escaped entry path after <c>!/</c>; empty for the archive root.</param>
        /// <returns>The jar URI.</returns>
        public static string JarLocation(StorageFile archive, string escapedEntryPath = "")
        {
            return "jar:" + new Uri(archive.Location).AbsoluteUri + "!/" + escapedEntryPath;
        }
    }
}

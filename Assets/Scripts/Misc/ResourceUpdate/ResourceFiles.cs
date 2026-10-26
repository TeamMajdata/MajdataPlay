using System;
using System.IO;
using System.Security.Cryptography;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Hashes resources and commits verified packaged bytes, shared by the resource importers and checkers.</summary>
    internal static class ResourceFiles
    {
        /// <summary>Verifies packaged bytes and commits one resource atomically below a destination root.</summary>
        /// <param name="relativePath">The portable path inside packaged resources, reused below the destination root.</param>
        /// <param name="destinationRoot">The existing directory receiving the verified bytes at the same relative path.</param>
        /// <param name="expectedHash">The official SHA-256 hash expected for the packaged bytes.</param>
        /// <param name="operation">The operation label used in resource logs.</param>
        /// <param name="packagedRoot">The packaged root captured on the main thread, or null to read Unity's current path.</param>
        /// <returns>Whether the packaged bytes were verified and committed; failures are logged.</returns>
        public static bool TryCopyVerified(
            string relativePath,
            StorageDirectory destinationRoot,
            string expectedHash,
            string operation,
            string? packagedRoot = null)
        {
            if (!TryReadVerified(relativePath, expectedHash, operation, out var data, packagedRoot))
            {
                return false;
            }

            try
            {
                WriteAllBytesAtomically(destinationRoot, relativePath, data);
                MajDebug.LogInfo(nameof(ResourceFiles), $"{operation}: {relativePath} -> {destinationRoot.Location}");
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(ResourceFiles), $"{operation} failed while writing: {relativePath}\n{exception}");
                return false;
            }
        }

        /// <summary>Reads packaged bytes and checks them against the official hash.</summary>
        /// <param name="relativePath">The portable packaged resource path.</param>
        /// <param name="expectedHash">The official SHA-256 hash expected for the packaged bytes.</param>
        /// <param name="operation">The operation label used in resource logs.</param>
        /// <param name="data">Receives the verified bytes, or an empty array after failure.</param>
        /// <param name="packagedRoot">The packaged root captured on the main thread, or null to read Unity's current path.</param>
        /// <returns>Whether the bytes were read and match the hash; failures are logged.</returns>
        public static bool TryReadVerified(string relativePath, string expectedHash, string operation, out byte[] data, string? packagedRoot = null)
        {
            if (!PackagedResources.TryReadAllBytes(relativePath, out data, packagedRoot))
            {
                return false;
            }
            if (!string.Equals(ComputeSha256(data), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                MajDebug.LogError(nameof(ResourceFiles), $"{operation} failed (packaged hash mismatch): {relativePath}");
                data = Array.Empty<byte>();
                return false;
            }
            return true;
        }

        /// <summary>Stages resource bytes beside the destination before a move or replacement inside the destination's backend.</summary>
        /// <param name="destinationRoot">The existing directory below which the resource is committed.</param>
        /// <param name="relativePath">The resource path below the root; missing directories are created.</param>
        /// <param name="data">The complete verified bytes to commit.</param>
        /// <exception cref="ArgumentException">The relative path is rooted, traverses upwards, or names no file.</exception>
        /// <exception cref="UnauthorizedAccessException">Creating, writing, or committing the file was denied.</exception>
        /// <exception cref="IOException">Staging or committing the file failed.</exception>
        /// <exception cref="NotSupportedException">The backend does not support creation or replacement.</exception>
        public static void WriteAllBytesAtomically(StorageDirectory destinationRoot, string relativePath, byte[] data)
        {
            var names = FileSystem.Path.Split(relativePath);
            if (names.Length == 0)
            {
                throw new ArgumentException("A resource path ending with a file name is required.", nameof(relativePath));
            }
            var parent = destinationRoot.CreateDirectoryAt(names.AsSpan(0, names.Length - 1));
            var name = names[names.Length - 1];
            var temporaryName = $"{name}.{Guid.NewGuid():N}.tmp";
            var temporaryFile = parent.CreateFile(temporaryName);
            try
            {
                temporaryFile.WriteAllBytes(data);
                var destinationFile = parent.FindFile(name);
                if (destinationFile is not null)
                {
                    FileSystem.File.Replace(temporaryFile.Location, destinationFile.Location);
                }
                else
                {
                    FileSystem.File.Move(temporaryFile.Location, parent.Location, name);
                }
            }
            finally
            {
                var leftover = parent.FindFile(temporaryName);
                if (leftover is not null)
                {
                    try
                    {
                        leftover.Delete();
                    }
                    catch (Exception exception)
                    {
                        MajDebug.LogError(nameof(ResourceFiles), $"Failed to clean temporary resource file: {leftover.Location}\n{exception}");
                    }
                }
            }
        }

        /// <summary>Hashes a resource through its storage stream without buffering the whole file.</summary>
        /// <param name="file">The existing resource file to hash.</param>
        /// <param name="hash">Receives the lowercase SHA-256 hash, or an empty string after failure.</param>
        /// <returns>Whether the resource was opened and hashed; failures are logged.</returns>
        public static bool TryComputeSha256(StorageFile file, out string hash)
        {
            try
            {
                using var stream = file.OpenRead();
                using var sha256 = SHA256.Create();
                hash = ToHexString(sha256.ComputeHash(stream));
                return true;
            }
            catch (Exception exception)
            {
                MajDebug.LogError(nameof(ResourceFiles), $"Failed to hash local resource: {file.Location}\n{exception}");
                hash = string.Empty;
                return false;
            }
        }

        /// <summary>Hashes already buffered resource bytes.</summary>
        /// <param name="data">The bytes to hash.</param>
        /// <returns>The lowercase SHA-256 hash.</returns>
        public static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return ToHexString(sha256.ComputeHash(data));
        }

        /// <summary>Formats hash bytes for comparison with validated resource manifest entries.</summary>
        /// <param name="hash">The hash bytes to format.</param>
        /// <returns>The lowercase hexadecimal hash without separators.</returns>
        private static string ToHexString(byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}

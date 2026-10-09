#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.IO.Storage;
using UnityEngine;

namespace MajdataPlay.Platform.Android.Storage
{
    /// <summary>
    /// Implements synchronous SAF storage over independently authorized document and tree URIs.
    /// </summary>
    /// <remarks>
    /// Construction and registration do not initialize Java. URI permissions belong to the caller.
    /// Locations are opaque, names are compared ordinally, and provider I/O should run off the game loop.
    /// A provider can change document IDs and display names during creation or rename; retain returned locations.
    /// </remarks>
    public sealed class AndroidDocumentFileSystem : IFileSystem
    {
        /// <summary>Creates an unbound backend without consulting Java or acquiring URI permissions.</summary>
        public AndroidDocumentFileSystem()
        {
        }

        /// <summary>Registers the stateless content backend without loading Java or depending on picker initialization.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            MajdataPlay.IO.Storage.FileSystem.RegisterContentProvider(new AndroidDocumentFileSystem());
#endif
        }

        /// <summary>Queries authoritative metadata without treating access failures as absence.</summary>
        /// <param name="location">An authorized content document or tree URI.</param>
        /// <returns>The entry, or null only when the provider reports that it does not exist.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The URI is invalid or not a SAF document.</exception>
        /// <exception cref="UnauthorizedAccessException">The URI grant was denied or revoked.</exception>
        /// <exception cref="IOException">The provider failed or returned invalid metadata.</exception>
        public FileSystemEntry? GetEntry(string location)
        {
            return AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "getEntry", "Ljava/lang/String;", location);
                if (AndroidDocumentBridge.ErrorCode(result) == 2)
                {
                    return null;
                }
                AndroidDocumentBridge.CheckResult(result, location);
                return AndroidDocumentBridge.ReadEntry(result);
            });
        }

        /// <summary>Finds one immediate child by exact display name and rejects ambiguous duplicates.</summary>
        /// <param name="directoryLocation">An authorized existing parent document or tree URI.</param>
        /// <param name="name">One child display name, not a relative path.</param>
        /// <returns>The child's metadata, or null if that name is absent.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The location or name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">The parent query was denied.</exception>
        /// <exception cref="NotSupportedException">The provider cannot enumerate this parent.</exception>
        /// <exception cref="IOException">The parent is not a directory, names are ambiguous, or the query failed.</exception>
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            AndroidDocumentBridge.EnsureAndroid();
            StorageName.Validate(name);
            return AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "getChildEntry", "Ljava/lang/String;Ljava/lang/String;",
                    directoryLocation, name);
                AndroidDocumentBridge.CheckResult(result, directoryLocation, true);
                return AndroidDocumentBridge.ReadEntry(result);
            });
        }

        /// <summary>Streams immediate child metadata and closes the provider cursor when enumeration ends.</summary>
        /// <param name="directoryLocation">An authorized existing parent document or tree URI.</param>
        /// <returns>A repeatable enumerable which owns a fresh cursor for each enumeration.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">The parent query was denied.</exception>
        /// <exception cref="NotSupportedException">The provider cannot enumerate this parent.</exception>
        /// <exception cref="IOException">The parent is not a directory, is still loading, or the query failed.</exception>
        /// <remarks>Names are not assumed unique; name-based resolution separately rejects duplicates.</remarks>
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            AndroidDocumentBridge.EnsureAndroid();
            return EnumerateCore(directoryLocation);
        }

        /// <summary>Enumerates on the caller's thread while bounding the lifetime of each JNI row result.</summary>
        /// <param name="location">The directory URI whose immediate children are requested.</param>
        /// <returns>Managed metadata copied from each provider row.</returns>
        /// <exception cref="ArgumentException">The location is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied a query.</exception>
        /// <exception cref="NotSupportedException">The provider cannot enumerate the URI.</exception>
        /// <exception cref="IOException">Opening, advancing, reading, or closing the cursor failed.</exception>
        private static IEnumerable<FileSystemEntry> EnumerateCore(string location)
        {
            var cursor = AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "enumerateEntries", "Ljava/lang/String;", location);
                AndroidDocumentBridge.CheckResult(result, location, true);
                return AndroidDocumentBridge.Field<AndroidJavaObject?>(result, AndroidDocumentBridge.ResultClass, "cursor",
                    "Lnet/majdata/majdataplay/StorageAccess$CursorHandle;")
                    ?? throw new IOException("The SAF bridge returned no cursor handle.");
            });
            try
            {
                while (true)
                {
                    var entry = AndroidDocumentBridge.Invoke(() =>
                    {
                        using var result = AndroidDocumentBridge.Call(cursor, "next", "");
                        AndroidDocumentBridge.CheckResult(result, location, true);
                        return AndroidDocumentBridge.ReadEntry(result);
                    });
                    if (entry is null)
                    {
                        yield break;
                    }
                    yield return entry;
                }
            }
            finally
            {
                AndroidDocumentBridge.CloseHandle(cursor);
            }
        }

        /// <summary>Opens an existing binary file without assuming a seekable descriptor or known length.</summary>
        /// <param name="location">The authorized existing document URI.</param>
        /// <returns>A caller-owned, nonseekable sequential read stream.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The URI is invalid.</exception>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Reading was denied.</exception>
        /// <exception cref="NotSupportedException">The document is virtual or has no binary representation.</exception>
        /// <exception cref="IOException">The entry is a directory or opening failed.</exception>
        public Stream OpenRead(string location)
        {
            return OpenStream(location, true, false);
        }

        /// <summary>Opens an existing binary file with explicit provider truncate or append semantics.</summary>
        /// <param name="location">The authorized existing document URI.</param>
        /// <param name="append">Whether to append using wa instead of truncating using wt.</param>
        /// <returns>A caller-owned, nonseekable sequential write stream.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The URI is invalid.</exception>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Writing was denied.</exception>
        /// <exception cref="NotSupportedException">The document is virtual or the provider cannot support the mode.</exception>
        /// <exception cref="IOException">The entry is a directory or opening failed.</exception>
        public Stream OpenWrite(string location, bool append = false)
        {
            return OpenStream(location, false, append);
        }

        /// <summary>Transfers Java stream ownership only after the open result has been checked.</summary>
        /// <param name="location">The existing document URI.</param>
        /// <param name="readable">Whether to open for reading rather than writing.</param>
        /// <param name="append">Whether a write stream should append.</param>
        /// <returns>A managed wrapper owning exactly one Java stream handle.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The URI is invalid.</exception>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">The provider denied access.</exception>
        /// <exception cref="NotSupportedException">The binary representation or requested mode is unavailable.</exception>
        /// <exception cref="IOException">Opening or adopting the stream failed.</exception>
        private static Stream OpenStream(string location, bool readable, bool append)
        {
            return AndroidDocumentBridge.Invoke<Stream>(() =>
            {
                using var result = readable
                    ? AndroidDocumentBridge.Call(null, "openRead", "Ljava/lang/String;", location)
                    : AndroidDocumentBridge.Call(null, "openWrite", "Ljava/lang/String;Z", location, append);
                AndroidDocumentBridge.CheckResult(result, location);
                var stream = AndroidDocumentBridge.Field<AndroidJavaObject?>(result, AndroidDocumentBridge.ResultClass, "stream",
                    "Lnet/majdata/majdataplay/StorageAccess$StreamHandle;")
                    ?? throw new IOException("The SAF bridge returned no stream handle.");
                try
                {
                    return new AndroidDocumentStream(stream, readable);
                }
                catch
                {
                    AndroidDocumentBridge.CloseHandle(stream);
                    throw;
                }
            });
        }

        /// <summary>Creates one empty child file without writing to or accepting an existing sibling.</summary>
        /// <param name="directoryLocation">The authorized existing parent URI.</param>
        /// <param name="name">One requested child display name.</param>
        /// <param name="mimeType">The non-directory MIME type, defaulting to application/octet-stream.</param>
        /// <returns>The created entry with the provider's actual name and URI.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The location, name, or MIME type is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Creation was denied.</exception>
        /// <exception cref="NotSupportedException">The parent does not support child creation.</exception>
        /// <exception cref="IOException">A sibling exists, names are ambiguous, or creation failed.</exception>
        /// <remarks>SAF has no cross-process exclusive-create primitive; provider mutations are rechecked, not rolled back destructively.</remarks>
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            AndroidDocumentBridge.EnsureAndroid();
            StorageName.Validate(name);
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException("A file MIME type is required.", nameof(mimeType));
            }
            return AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "createFile",
                    "Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;", directoryLocation, name, mimeType);
                AndroidDocumentBridge.CheckResult(result, directoryLocation, true);
                return AndroidDocumentBridge.ReadRequiredEntry(result);
            });
        }

        /// <summary>Creates one immediate child directory or returns an unambiguous existing directory.</summary>
        /// <param name="directoryLocation">The authorized existing parent URI.</param>
        /// <param name="name">One requested child display name.</param>
        /// <returns>The existing or created entry, including its actual provider name and URI.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The location or name is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The parent does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Creation was denied.</exception>
        /// <exception cref="NotSupportedException">The parent does not support child creation.</exception>
        /// <exception cref="IOException">A file occupies the name, names are ambiguous, or creation failed.</exception>
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            AndroidDocumentBridge.EnsureAndroid();
            StorageName.Validate(name);
            return AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "createDirectory", "Ljava/lang/String;Ljava/lang/String;",
                    directoryLocation, name);
                AndroidDocumentBridge.CheckResult(result, directoryLocation, true);
                return AndroidDocumentBridge.ReadRequiredEntry(result);
            });
        }

        /// <summary>Renames within a discoverable authorized parent and returns the provider's new URI.</summary>
        /// <param name="location">The authorized existing document URI.</param>
        /// <param name="name">One requested replacement display name.</param>
        /// <returns>The renamed entry with its authoritative actual name and location.</returns>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The location or name is invalid.</exception>
        /// <exception cref="FileNotFoundException">The document does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Renaming or querying the parent was denied.</exception>
        /// <exception cref="NotSupportedException">The provider cannot rename or its parent cannot be discovered safely.</exception>
        /// <exception cref="IOException">A sibling exists, names or parents are ambiguous, or rename failed.</exception>
        public FileSystemEntry Rename(string location, string name)
        {
            AndroidDocumentBridge.EnsureAndroid();
            StorageName.Validate(name);
            return AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "rename", "Ljava/lang/String;Ljava/lang/String;", location, name);
                AndroidDocumentBridge.CheckResult(result, location);
                return AndroidDocumentBridge.ReadRequiredEntry(result);
            });
        }

        /// <summary>Deletes a file only; an absent file is a no-op and directories are refused.</summary>
        /// <param name="location">The authorized file document URI.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The URI is invalid.</exception>
        /// <exception cref="UnauthorizedAccessException">Deletion was denied.</exception>
        /// <exception cref="NotSupportedException">The document does not support deletion.</exception>
        /// <exception cref="IOException">The entry is a directory or deletion failed.</exception>
        public void DeleteFile(string location)
        {
            AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "deleteFile", "Ljava/lang/String;", location);
                if (AndroidDocumentBridge.ErrorCode(result) != 2)
                {
                    AndroidDocumentBridge.CheckResult(result, location);
                }
                return true;
            });
        }

        /// <summary>Deletes a directory, refusing nonempty directories unless provider subtree deletion is requested.</summary>
        /// <param name="location">The authorized existing directory URI.</param>
        /// <param name="recursive">Whether the provider may delete the directory's entire subtree.</param>
        /// <exception cref="PlatformNotSupportedException">Execution is outside an Android player.</exception>
        /// <exception cref="ArgumentException">The URI is invalid.</exception>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="UnauthorizedAccessException">Deletion or the emptiness query was denied.</exception>
        /// <exception cref="NotSupportedException">The provider does not support deletion.</exception>
        /// <exception cref="IOException">The entry is not a directory, is nonempty without recursion, or deletion failed.</exception>
        /// <remarks>Recursive deletion is one provider operation, not a client walk through potentially shared document IDs.</remarks>
        public void DeleteDirectory(string location, bool recursive = false)
        {
            AndroidDocumentBridge.Invoke(() =>
            {
                using var result = AndroidDocumentBridge.Call(null, "deleteDirectory", "Ljava/lang/String;Z", location, recursive);
                AndroidDocumentBridge.CheckResult(result, location, true);
                return true;
            });
        }
    }
}

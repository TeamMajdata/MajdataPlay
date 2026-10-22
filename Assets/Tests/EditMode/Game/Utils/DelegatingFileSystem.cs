using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.IO.Storage;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>
    /// A storage backend that forwards to another backend but is a different object, so storage helpers treat it as a
    /// separate provider. Individual operations can be made to fail to exercise error handling.
    /// </summary>
    internal sealed class DelegatingFileSystem : IFileSystem
    {
        /// <summary>The backend receiving the forwarded operations.</summary>
        private readonly IFileSystem _inner;

        /// <summary>Creates a backend forwarding to another backend.</summary>
        /// <param name="inner">The backend that performs the operations.</param>
        public DelegatingFileSystem(IFileSystem inner)
        {
            _inner = inner;
        }

        /// <summary>Gets or sets a value indicating whether renaming throws an <see cref="IOException"/>.</summary>
        public bool FailRename { get; set; }

        /// <summary>Gets or sets a value indicating whether deleting a directory throws an <see cref="IOException"/>.</summary>
        public bool FailDeleteDirectory { get; set; }

        /// <summary>Gets or sets a value indicating whether setting a modification time throws a <see cref="NotSupportedException"/>.</summary>
        public bool RejectLastWriteTime { get; set; }

        /// <summary>Gets the number of directories deleted through this backend.</summary>
        public int DeletedDirectories { get; private set; }

        /// <inheritdoc/>
        public FileSystemEntry? GetEntry(string location)
        {
            return _inner.GetEntry(location);
        }

        /// <inheritdoc/>
        public FileSystemEntry? GetChildEntry(string directoryLocation, string name)
        {
            return _inner.GetChildEntry(directoryLocation, name);
        }

        /// <inheritdoc/>
        public IEnumerable<FileSystemEntry> EnumerateEntries(string directoryLocation)
        {
            return _inner.EnumerateEntries(directoryLocation);
        }

        /// <inheritdoc/>
        public Stream OpenRead(string location)
        {
            return _inner.OpenRead(location);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string location, bool append = false)
        {
            return _inner.OpenWrite(location, append);
        }

        /// <inheritdoc/>
        public FileSystemEntry CreateFile(string directoryLocation, string name, string mimeType = "application/octet-stream")
        {
            return _inner.CreateFile(directoryLocation, name, mimeType);
        }

        /// <inheritdoc/>
        public FileSystemEntry CreateDirectory(string directoryLocation, string name)
        {
            return _inner.CreateDirectory(directoryLocation, name);
        }

        /// <inheritdoc/>
        /// <exception cref="IOException"><see cref="FailRename"/> is set.</exception>
        public FileSystemEntry Rename(string location, string name)
        {
            if (FailRename)
            {
                throw new IOException("Rename rejected by the test backend.");
            }
            return _inner.Rename(location, name);
        }

        /// <inheritdoc/>
        public FileSystemEntry Move(string location, string directoryLocation, string name)
        {
            return _inner.Move(location, directoryLocation, name);
        }

        /// <inheritdoc/>
        public void DeleteFile(string location)
        {
            _inner.DeleteFile(location);
        }

        /// <inheritdoc/>
        /// <exception cref="IOException"><see cref="FailDeleteDirectory"/> is set.</exception>
        public void DeleteDirectory(string location, bool recursive = false)
        {
            if (FailDeleteDirectory)
            {
                throw new IOException("Delete rejected by the test backend.");
            }
            _inner.DeleteDirectory(location, recursive);
            DeletedDirectories++;
        }

        /// <inheritdoc/>
        public void SetAttributes(string location, FileAttributes attributes)
        {
            _inner.SetAttributes(location, attributes);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException"><see cref="RejectLastWriteTime"/> is set.</exception>
        public void SetLastWriteTime(string location, DateTime lastWriteTimeUtc)
        {
            if (RejectLastWriteTime)
            {
                throw new NotSupportedException("Timestamps rejected by the test backend.");
            }
            _inner.SetLastWriteTime(location, lastWriteTimeUtc);
        }
    }
}

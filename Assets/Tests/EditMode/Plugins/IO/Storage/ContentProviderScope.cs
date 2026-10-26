#nullable enable
using System;
using MajdataPlay.IO.Storage;

namespace MajdataPlay.Tests.Plugins.IO.Storage
{
    /// <summary>
    /// Registers a content provider for the duration of a test and restores the previously registered provider,
    /// including no provider at all, when disposed.
    /// </summary>
    internal sealed class ContentProviderScope : IDisposable
    {
        /// <summary>The name of the facade's private provider field.</summary>
        private const string ProviderField = "s_contentProvider";

        /// <summary>The provider registered before this scope, or null when none was registered.</summary>
        private readonly IFileSystem? _previous;

        /// <summary>Captures the current provider and optionally registers a replacement.</summary>
        /// <param name="provider">The provider to register, or null to leave no provider registered.</param>
        public ContentProviderScope(IFileSystem? provider)
        {
            _previous = PrivateMember.GetStaticField<IFileSystem?>(typeof(FileSystem), ProviderField);
            if (provider is null)
            {
                PrivateMember.SetStaticField(typeof(FileSystem), ProviderField, null);
            }
            else
            {
                FileSystem.RegisterContentProvider(provider);
            }
        }

        /// <summary>Gets the provider currently registered with the facade.</summary>
        public static IFileSystem? Current => PrivateMember.GetStaticField<IFileSystem?>(typeof(FileSystem), ProviderField);

        /// <summary>Restores the provider that was registered before this scope.</summary>
        public void Dispose()
        {
            PrivateMember.SetStaticField(typeof(FileSystem), ProviderField, _previous);
        }
    }
}

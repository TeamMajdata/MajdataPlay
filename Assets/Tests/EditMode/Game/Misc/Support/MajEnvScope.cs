using System;
using System.Collections.Generic;
using System.IO;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO.Storage;
using MajdataPlay.Net;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Tests;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Support
{
    /// <summary>
    /// Captures the static state of <see cref="MajEnv"/> that tests rearrange and restores it when disposed, so tests can
    /// point the application directories at temporary storage without touching the project or user data folders.
    /// </summary>
    internal sealed class MajEnvScope : IDisposable
    {
        /// <summary>The <see cref="MajEnv"/> properties captured and restored by every scope.</summary>
        private static readonly string[] s_propertyNames =
        {
            nameof(MajEnv.RootDirectory),
            nameof(MajEnv.PersistentDirectory),
            nameof(MajEnv.AssetsDirectory),
            nameof(MajEnv.CacheDirectory),
            nameof(MajEnv.RuntimeCacheDirectory),
            nameof(MajEnv.TempDirectory),
            nameof(MajEnv.ChartDirectory),
            nameof(MajEnv.SettingsPath),
            nameof(MajEnv.SkinDirectory),
            nameof(MajEnv.LogsDirectory),
            nameof(MajEnv.LogPath),
            nameof(MajEnv.RecordOutputsDirectory),
            nameof(MajEnv.Settings),
            nameof(MajEnv.RuntimeConfig),
            nameof(MajEnv.ApiEndpoints),
            nameof(MajEnv.MachineInfo),
        };

        /// <summary>The captured property values keyed by property name.</summary>
        private readonly Dictionary<string, object?> _saved = new();

        /// <summary>The captured minimum log level, which settings initialization changes.</summary>
        private readonly LogLevel _minLogLevel;

        /// <summary>Captures the current static state.</summary>
        public MajEnvScope()
        {
            foreach (var name in s_propertyNames)
            {
                _saved[name] = PrivateMember.GetStaticProperty<object?>(typeof(MajEnv), name);
            }
            _minLogLevel = MajDebug.MinLogLevel;
        }

        /// <summary>Gets the root directory of the temporary layout, or null before <see cref="UseTemporaryLayout"/>.</summary>
        public StorageDirectory? Root { get; private set; }

        /// <summary>
        /// Points every application directory at a fresh layout inside a temporary directory, installs default settings
        /// and runtime configuration, and clears the online endpoints.
        /// </summary>
        /// <param name="temporary">The temporary directory that receives the layout.</param>
        /// <returns>The root of the layout.</returns>
        public StorageDirectory UseTemporaryLayout(TemporaryDirectory temporary)
        {
            var root = temporary.Directory.CreateDirectory("Root");
            var cache = root.CreateDirectory("Cache");
            Set(nameof(MajEnv.RootDirectory), root);
            Set(nameof(MajEnv.PersistentDirectory), root);
            Set(nameof(MajEnv.AssetsDirectory), new StorageDirectory(root.FileSystem, Path.Combine(root.Location, "StreamingAssets")));
            Set(nameof(MajEnv.CacheDirectory), cache);
            Set(nameof(MajEnv.RuntimeCacheDirectory), cache.CreateDirectory("Runtime"));
            Set(nameof(MajEnv.TempDirectory), cache.CreateDirectory("Temp"));
            Set(nameof(MajEnv.ChartDirectory), root.CreateDirectory("MaiCharts"));
            Set(nameof(MajEnv.SkinDirectory), root.CreateDirectory("Skins"));
            Set(nameof(MajEnv.LogsDirectory), root.CreateDirectory("Logs"));
            Set(nameof(MajEnv.RecordOutputsDirectory), root.CreateDirectory("RecordOutputs"));
            Set(nameof(MajEnv.Settings), new GameSetting());
            Set(nameof(MajEnv.RuntimeConfig), new RuntimeConfig());
            Set(nameof(MajEnv.ApiEndpoints), Array.Empty<ApiEndpoint>());
            Root = root;
            return root;
        }

        /// <summary>Replaces one <see cref="MajEnv"/> property for the lifetime of this scope.</summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The value to install.</param>
        public void Set(string name, object? value)
        {
            PrivateMember.SetStaticProperty(typeof(MajEnv), name, value);
        }

        /// <summary>Restores every captured property and the minimum log level.</summary>
        public void Dispose()
        {
            foreach (var pair in _saved)
            {
                PrivateMember.SetStaticProperty(typeof(MajEnv), pair.Key, pair.Value);
            }
            MajDebug.MinLogLevel = _minLogLevel;
        }
    }
}

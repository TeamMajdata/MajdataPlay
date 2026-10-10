#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace MajdataPlay
{
    /// <summary>Supplies only the environment paths consumed by the linked resource workflows.</summary>
    internal static class MajEnv
    {
        /// <summary>Gets or sets the fixture's application root.</summary>
        public static string RootPath { get; set; } = string.Empty;

        /// <summary>Gets the fixture's extracted resource root.</summary>
        public static string AssetsPath => Path.Combine(RootPath, "Assets");

        /// <summary>Gets the fixture's player-managed chart root.</summary>
        public static string ChartPath => Path.Combine(RootPath, "MaiCharts");

        /// <summary>Gets the fixture's player-managed skin root.</summary>
        public static string SkinPath => Path.Combine(RootPath, "Skins");
    }

    /// <summary>Provides controlled validated manifests without substituting any storage behavior.</summary>
    internal static class ResourceManifestLoader
    {
        /// <summary>Gets the test's official previous resource hashes.</summary>
        public static Dictionary<string, string> V1Hashes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the test's packaged resource hashes.</summary>
        public static Dictionary<string, string> V2Hashes { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the test's changed packaged resource paths.</summary>
        public static HashSet<string> DiffPaths { get; } = new(StringComparer.Ordinal);

        /// <summary>Returns controlled manifests when the fixture supplies packaged resources.</summary>
        /// <param name="v1Hashes">Receives previous official hashes.</param>
        /// <param name="v2Hashes">Receives current packaged hashes.</param>
        /// <param name="diffPaths">Receives changed resource paths.</param>
        /// <returns>Whether a packaged manifest is available.</returns>
        public static bool TryGetUpdateManifests(out Dictionary<string, string> v1Hashes,
            out Dictionary<string, string> v2Hashes, out HashSet<string> diffPaths)
        {
            v1Hashes = V1Hashes;
            v2Hashes = V2Hashes;
            diffPaths = DiffPaths;
            return V2Hashes.Count != 0;
        }

        /// <summary>Returns the fixture's validated packaged resource hashes.</summary>
        /// <param name="hashes">Receives current packaged hashes.</param>
        /// <returns>Whether a packaged manifest is available.</returns>
        public static bool TryGetV2Hashes(out Dictionary<string, string> hashes)
        {
            hashes = V2Hashes;
            return hashes.Count != 0;
        }
    }
}

namespace MajdataPlay.Diagnostics
{
    /// <summary>Captures resource failures while discarding routine informational messages.</summary>
    internal static class MajDebug
    {
        /// <summary>Gets all errors logged by the current case.</summary>
        public static List<string> Errors { get; } = new();

        /// <summary>Accepts an informational message without affecting assertions.</summary>
        /// <param name="message">The production workflow's informational message.</param>
        public static void LogInfo(string message)
        {
        }

        /// <summary>Records a production failure for the case to assert.</summary>
        /// <param name="message">The failure and its local fixture context.</param>
        public static void LogError(string message)
        {
            Errors.Add(message);
        }
    }
}

namespace UnityEngine
{
    /// <summary>Supplies only the initialization phase named by the production attribute.</summary>
    internal enum RuntimeInitializeLoadType
    {
        /// <summary>Represents initialization before scene loading.</summary>
        BeforeSceneLoad
    }

    /// <summary>Allows linked initialization code to compile without invoking a Unity lifecycle.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        /// <summary>Creates an inert initialization marker.</summary>
        /// <param name="loadType">The phase requested by production code.</param>
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
        }
    }

    /// <summary>Supplies the packaged fixture path used by the linked updater/restorer.</summary>
    internal static class Application
    {
        /// <summary>Gets or sets the current fixture's packaged resource root.</summary>
        public static string streamingAssetsPath { get; set; } = string.Empty;
    }
}

namespace UnityEngine.Networking
{
    /// <summary>Supplies in-memory downloaded bytes for the Android branch's package request.</summary>
    internal sealed class DownloadHandlerBuffer
    {
        /// <summary>Gets or sets the fixture bytes returned by a completed request.</summary>
        public byte[]? data { get; set; }
    }

    /// <summary>Represents a request that has already completed synchronously in this narrow substitute.</summary>
    internal sealed class UnityWebRequestAsyncOperation
    {
        /// <summary>Gets whether the fixture request completed.</summary>
        public bool isDone => true;
    }

    /// <summary>Substitutes only packaged request transport, while resource writes use production storage.</summary>
    internal sealed class UnityWebRequest : IDisposable
    {
        /// <summary>Owns the path of the packaged fixture being requested.</summary>
        private readonly string _path;

        /// <summary>Describes whether the completed fixture read succeeded.</summary>
        public enum Result
        {
            /// <summary>Indicates that fixture bytes were read.</summary>
            Success,

            /// <summary>Indicates that the fixture source could not be read.</summary>
            ConnectionError
        }

        /// <summary>Gets or sets the buffer populated by a completed fixture read.</summary>
        public DownloadHandlerBuffer downloadHandler { get; set; } = new();

        /// <summary>Gets the fixture request's completion result.</summary>
        public Result result { get; private set; }

        /// <summary>Gets a fixture read failure description, if any.</summary>
        public string? error { get; private set; }

        /// <summary>Creates a request for one packaged fixture path.</summary>
        /// <param name="path">The fixture path to read.</param>
        private UnityWebRequest(string path)
        {
            _path = path;
        }

        /// <summary>Creates an inert request for a packaged fixture.</summary>
        /// <param name="path">The fixture's source path.</param>
        /// <returns>The prepared substitute request.</returns>
        public static UnityWebRequest Get(string path)
        {
            return new UnityWebRequest(path);
        }

        /// <summary>Reads fixture bytes and returns an already completed request operation.</summary>
        /// <returns>The completed operation observed by the production polling loop.</returns>
        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            try
            {
                downloadHandler.data = File.ReadAllBytes(_path);
                result = Result.Success;
            }
            catch (IOException exception)
            {
                result = Result.ConnectionError;
                error = exception.Message;
            }
            return new UnityWebRequestAsyncOperation();
        }

        /// <summary>Releases the substitute request, which owns no native resources.</summary>
        public void Dispose()
        {
        }
    }
}

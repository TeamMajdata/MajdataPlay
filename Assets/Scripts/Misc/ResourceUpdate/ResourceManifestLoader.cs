using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MajdataPlay.Diagnostics;
using Newtonsoft.Json;
using UnityEngine;

#nullable enable
namespace MajdataPlay.ResourceUpdate
{
    /// <summary>Loads and validates the official packaged resource manifests bundled in <c>Resources/ResourceUpdate</c>.</summary>
    internal static class ResourceManifestLoader
    {
        /// <summary>The Resources path of the previous official resource hashes.</summary>
        private const string V1ManifestResourcePath = "ResourceUpdate/V1ResourceHashes";
        /// <summary>The Resources path of the current official resource hashes.</summary>
        private const string V2ManifestResourcePath = "ResourceUpdate/V2ResourceHashes";
        /// <summary>The Resources path of the paths changed between the previous and current resources.</summary>
        private const string DiffManifestResourcePath = "ResourceUpdate/V1ToV2Diff";
        /// <summary>The only hash algorithm accepted in a hash manifest.</summary>
        private const string HashAlgorithm = "SHA-256";

        /// <summary>Mirrors the JSON layout of a hash manifest.</summary>
        private sealed class ResourceHashManifest
        {
            /// <summary>Gets or sets the resource version described by the manifest.</summary>
            [JsonProperty("version")]
            public int Version { get; set; }

            /// <summary>Gets or sets the name of the hash algorithm.</summary>
            [JsonProperty("algorithm")]
            public string Algorithm { get; set; } = string.Empty;

            /// <summary>Gets or sets the hashes keyed by portable relative path.</summary>
            [JsonProperty("files")]
            public Dictionary<string, string>? Files { get; set; } = new(StringComparer.Ordinal);
        }

        /// <summary>Mirrors the JSON layout of the diff manifest.</summary>
        private sealed class ResourceDiffManifest
        {
            /// <summary>Gets or sets the resource version the diff starts from.</summary>
            [JsonProperty("baseVersion")]
            public int BaseVersion { get; set; }

            /// <summary>Gets or sets the resource version the diff leads to.</summary>
            [JsonProperty("targetVersion")]
            public int TargetVersion { get; set; }

            /// <summary>Gets or sets the changed portable relative paths.</summary>
            [JsonProperty("files")]
            public string[]? Files { get; set; } = Array.Empty<string>();
        }

        /// <summary>Loads the previous and current hashes and the diff between them.</summary>
        /// <param name="v1Hashes">Receives the previous official hashes, or an empty dictionary after failure.</param>
        /// <param name="v2Hashes">Receives the current official hashes, or an empty dictionary after failure.</param>
        /// <param name="diffPaths">Receives the paths changed by the update, or an empty set after failure.</param>
        /// <returns>Whether all three manifests were loaded and are consistent.</returns>
        public static bool TryGetUpdateManifests(
            out Dictionary<string, string> v1Hashes,
            out Dictionary<string, string> v2Hashes,
            out HashSet<string> diffPaths)
        {
            v1Hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            v2Hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            diffPaths = new HashSet<string>(StringComparer.Ordinal);

            if (!TryLoadHashManifest(V1ManifestResourcePath, expectedVersion: 1, out v1Hashes) ||
                !TryLoadHashManifest(V2ManifestResourcePath, expectedVersion: 2, out v2Hashes) ||
                !TryLoadDiffManifest(v2Hashes, out var loadedDiffPaths))
            {
                return false;
            }

            diffPaths = new HashSet<string>(loadedDiffPaths, StringComparer.Ordinal);
            return true;
        }

        /// <summary>Loads the current official hashes.</summary>
        /// <param name="hashes">Receives the hashes keyed by portable relative path, or an empty dictionary after failure.</param>
        /// <returns>Whether the manifest was loaded and validated.</returns>
        public static bool TryGetV2Hashes(out Dictionary<string, string> hashes)
        {
            return TryLoadHashManifest(V2ManifestResourcePath, expectedVersion: 2, out hashes);
        }

        /// <summary>Loads one hash manifest and validates its version, algorithm, paths and digests.</summary>
        /// <param name="resourcePath">The Resources path of the manifest.</param>
        /// <param name="expectedVersion">The resource version the manifest must describe.</param>
        /// <param name="hashes">Receives the hashes keyed by portable relative path, or an empty dictionary after failure.</param>
        /// <returns>Whether the manifest was loaded and validated.</returns>
        private static bool TryLoadHashManifest(
            string resourcePath,
            int expectedVersion,
            out Dictionary<string, string> hashes)
        {
            hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!TryDeserializeManifest(resourcePath, out ResourceHashManifest? manifest) || manifest == null ||
                manifest.Version != expectedVersion ||
                !string.Equals(manifest.Algorithm, HashAlgorithm, StringComparison.OrdinalIgnoreCase) ||
                manifest.Files == null || manifest.Files.Count == 0)
            {
                MajDebug.LogError($"Invalid v{expectedVersion} resource manifest: {resourcePath}");
                return false;
            }

            foreach (var (path, rawHash) in manifest.Files)
            {
                var normalizedPath = NormalizeRelativePath(path);
                var hash = rawHash?.Trim().ToLowerInvariant();
                if (normalizedPath == null || normalizedPath != path || hash == null || !IsSha256(hash) ||
                    !hashes.TryAdd(normalizedPath, hash))
                {
                    MajDebug.LogError($"Invalid v{expectedVersion} resource entry: {path}");
                    hashes.Clear();
                    return false;
                }
            }

            return true;
        }

        /// <summary>Loads the diff manifest and checks that it only lists unique paths of the current manifest.</summary>
        /// <param name="v2Hashes">The validated current hashes.</param>
        /// <param name="diffPaths">Receives the changed paths, or an empty array after failure.</param>
        /// <returns>Whether the diff manifest was loaded and validated.</returns>
        private static bool TryLoadDiffManifest(
            IReadOnlyDictionary<string, string> v2Hashes,
            out string[] diffPaths)
        {
            if (!TryDeserializeManifest(DiffManifestResourcePath, out ResourceDiffManifest? manifest) || manifest == null ||
                manifest.BaseVersion != 1 || manifest.TargetVersion != 2 || manifest.Files == null)
            {
                MajDebug.LogError($"Invalid resource diff manifest: {DiffManifestResourcePath}");
                diffPaths = Array.Empty<string>();
                return false;
            }

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in manifest.Files)
            {
                if (NormalizeRelativePath(path) != path || !v2Hashes.ContainsKey(path) || !paths.Add(path))
                {
                    MajDebug.LogError($"Invalid v2 resource diff entry: {path}");
                    diffPaths = Array.Empty<string>();
                    return false;
                }
            }

            diffPaths = manifest.Files;
            return true;
        }

        /// <summary>Deserializes one JSON text asset from Resources.</summary>
        /// <typeparam name="T">The manifest layout.</typeparam>
        /// <param name="resourcePath">The Resources path of the text asset.</param>
        /// <param name="manifest">Receives the manifest, or null after failure.</param>
        /// <returns>Whether the text asset exists and holds a manifest.</returns>
        private static bool TryDeserializeManifest<T>(string resourcePath, out T? manifest)
            where T : class
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                MajDebug.LogError($"Resource manifest not found in Resources: {resourcePath}");
                manifest = null;
                return false;
            }

            try
            {
                manifest = JsonConvert.DeserializeObject<T>(textAsset.text);
                return manifest != null;
            }
            catch (JsonException exception)
            {
                MajDebug.LogError($"Invalid resource manifest JSON: {resourcePath}\n{exception}");
                manifest = null;
                return false;
            }
        }

        /// <summary>Normalizes a manifest path to a portable relative path.</summary>
        /// <param name="path">The manifest path.</param>
        /// <returns>The trimmed path with forward slashes, or null when it is empty, rooted or contains empty, current or parent segments.</returns>
        private static string? NormalizeRelativePath(string? path)
        {
            if (path == null)
            {
                return null;
            }

            var normalized = path.Trim().Replace('\\', '/');
            if (normalized.Length == 0 || Path.IsPathRooted(normalized))
            {
                return null;
            }

            var segments = normalized.Split('/');
            if (segments.Any(segment => segment.Length == 0 || segment == "." || segment == ".."))
            {
                return null;
            }

            return normalized;
        }

        /// <summary>Checks the format of a SHA-256 digest.</summary>
        /// <param name="value">The digest text.</param>
        /// <returns>Whether the text has 64 lowercase hexadecimal digits.</returns>
        private static bool IsSha256(string? value)
        {
            return value != null && value.Length == 64 && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'a' && character <= 'f');
        }
    }
}

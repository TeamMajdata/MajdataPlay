using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>
    /// Collects the localization keys that scenes and prefabs assign to <c>LocalizeTMP</c> components.
    /// </summary>
    /// <remarks>
    /// The assets are read as text-serialized YAML without loading them, so no scene is opened and no component code runs.
    /// Both direct field values and prefab instance overrides are recognized.
    /// </remarks>
    internal static class TranslationAssetAnalyzer
    {
        /// <summary>
        /// The serialized name of the key field of <c>LocalizeTMP</c>.
        /// </summary>
        /// <remarks>
        /// Written out instead of using nameof, so that the editor tools compile without the runtime assembly, as the
        /// isolated translation editor validation requires. Keep it in sync with MajdataPlay.i18n.LocalizeTMP.
        /// </remarks>
        private const string KeyFieldName = "local_key";

        /// <summary>
        /// Matches a serialized <c>local_key: VALUE</c> field line.
        /// </summary>
        private static readonly Regex s_fieldPattern = new Regex(
            @"^[ \t]*" + KeyFieldName + @":[ \t]*(?<value>.*?)[ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// Matches a prefab instance modification that overrides the key field.
        /// </summary>
        private static readonly Regex s_overridePattern = new Regex(
            @"^[ \t]*propertyPath:[ \t]*" + KeyFieldName + @"[ \t]*\r?\n[ \t]*value:[ \t]*(?<value>.*?)[ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>
        /// Adds the keys of every <c>LocalizeTMP</c> component found in the given assets.
        /// </summary>
        /// <param name="assetPaths">The scene and prefab asset paths to read.</param>
        /// <param name="keys">Receives the keys mapped to their asset sources.</param>
        /// <param name="diagnostics">Receives assets that cannot be read or are not text-serialized.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static void Collect(IEnumerable<string> assetPaths, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            if (assetPaths is null)
            {
                throw new ArgumentNullException(nameof(assetPaths));
            }
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            foreach (var assetPath in assetPaths)
            {
                string text;
                try
                {
                    text = File.ReadAllText(assetPath);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    diagnostics.Add($"[Assets] Cannot read {assetPath}: {exception.Message}");
                    continue;
                }
                if (!text.StartsWith("%YAML", StringComparison.Ordinal))
                {
                    diagnostics.Add($"[Assets] {assetPath} is not text-serialized; its LocalizeTMP keys were not collected.");
                    continue;
                }
                CollectText(text, assetPath, keys);
            }
        }

        /// <summary>
        /// Adds the keys found in the YAML text of one asset.
        /// </summary>
        /// <param name="text">The YAML text of the asset.</param>
        /// <param name="assetPath">The asset path recorded as the source.</param>
        /// <param name="keys">Receives the keys mapped to their asset sources.</param>
        internal static void CollectText(string text, string assetPath, IDictionary<string, HashSet<string>> keys)
        {
            AddMatches(s_fieldPattern, text, assetPath + " (LocalizeTMP)", keys);
            AddMatches(s_overridePattern, text, assetPath + " (LocalizeTMP prefab override)", keys);
        }

        /// <summary>
        /// Adds the nonempty values of every match of a pattern.
        /// </summary>
        /// <param name="pattern">The pattern whose <c>value</c> group holds the key.</param>
        /// <param name="text">The YAML text of the asset.</param>
        /// <param name="source">The source recorded for each key.</param>
        /// <param name="keys">Receives the keys mapped to their sources.</param>
        private static void AddMatches(Regex pattern, string text, string source, IDictionary<string, HashSet<string>> keys)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var key = Unquote(match.Groups["value"].Value);
                if (key.Length == 0)
                {
                    continue;
                }
                if (!keys.TryGetValue(key, out var sources))
                {
                    sources = new HashSet<string>(StringComparer.Ordinal);
                    keys.Add(key, sources);
                }
                sources.Add(source);
            }
        }

        /// <summary>
        /// Removes YAML scalar quoting from a single-line value.
        /// </summary>
        /// <param name="value">The raw scalar text.</param>
        /// <returns>The unquoted value; plain scalars are returned unchanged.</returns>
        internal static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            {
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            }
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return Regex.Unescape(value.Substring(1, value.Length - 2));
            }
            return value;
        }
    }
}

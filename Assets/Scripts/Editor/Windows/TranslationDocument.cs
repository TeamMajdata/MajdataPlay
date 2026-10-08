using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>An editable, Unity-serializable language resource and its original source snapshot.</summary>
    [Serializable]
    internal sealed class TranslationDocument
    {
        /// <summary>The runtime language code.</summary>
        public string Code = string.Empty;

        /// <summary>The language author.</summary>
        public string Author = string.Empty;

        /// <summary>The resource asset path, independent of editable metadata.</summary>
        public string AssetPath = string.Empty;

        /// <summary>The last loaded or saved JSON, used to retain metadata and detect external edits.</summary>
        public string SourceJson = string.Empty;

        /// <summary>Whether this resource uses the legacy MappingTable schema.</summary>
        public bool UsesMappingTable;

        /// <summary>Whether this document has unsaved changes.</summary>
        public bool IsDirty;

        /// <summary>The editable entries, stored without unsupported KeyValuePair serialization.</summary>
        public List<TranslationEntry> Entries = new();

        /// <summary>Reads either language schema without initializing runtime localization.</summary>
        /// <param name="assetPath">The path of the language resource.</param>
        /// <param name="source">The original JSON text.</param>
        /// <returns>The editable document.</returns>
        /// <exception cref="JsonException">The source is not valid language JSON.</exception>
        /// <exception cref="FormatException">A translation entry does not contain a key and text.</exception>
        internal static TranslationDocument Read(string assetPath, string source)
        {
            var json = JObject.Parse(source);
            var dictionary = json["Translations"] as JObject;
            var mapping = json["MappingTable"] as JArray;
            var document = new TranslationDocument
            {
                Code = json.Value<string>("Code") ?? string.Empty,
                Author = json.Value<string>("Author") ?? string.Empty,
                AssetPath = assetPath,
                SourceJson = source,
                UsesMappingTable = (dictionary is null || dictionary.Count == 0) && mapping is not null
            };
            if (document.UsesMappingTable && mapping is not null)
            {
                foreach (var token in mapping)
                {
                    if (token is not JObject entry)
                    {
                        throw new FormatException("MappingTable entries must be objects.");
                    }
                    var key = entry.Value<string>("Origin") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new FormatException("MappingTable entries must contain a nonempty Origin.");
                    }
                    var text = ReadText(entry["Content"]);
                    // Runtime mapping tables use the last value when a key occurs more than once.
                    var previous = document.Entries.Find(item => item.Key == key);
                    if (previous is null)
                    {
                        document.Entries.Add(new TranslationEntry { Key = key, Text = text });
                    }
                    else
                    {
                        previous.Text = text;
                    }
                }
            }
            else if (dictionary is not null)
            {
                foreach (var property in dictionary.Properties())
                {
                    document.Entries.Add(new TranslationEntry { Key = property.Name, Text = ReadText(property.Value) });
                }
            }
            return document;
        }

        /// <summary>Writes metadata and translations, preserving the original schema and extra fields.</summary>
        /// <exception cref="JsonException">The retained source JSON is invalid.</exception>
        /// <exception cref="InvalidOperationException">Translation keys are empty or duplicated.</exception>
        /// <exception cref="IOException">The source changed externally or could not be written.</exception>
        /// <exception cref="UnauthorizedAccessException">The resource is not writable.</exception>
        internal void Save()
        {
            ValidateKeys(Entries.Select(entry => entry.Key));
            var json = JObject.Parse(SourceJson);
            json["Code"] = Code;
            json["Author"] = Author;
            var entries = Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal);
            if (UsesMappingTable)
            {
                var mapping = new JArray();
                foreach (var entry in entries)
                {
                    mapping.Add(new JObject { ["Origin"] = entry.Key, ["Content"] = entry.Text });
                }
                json["MappingTable"] = mapping;
            }
            else
            {
                var dictionary = new JObject();
                foreach (var entry in entries)
                {
                    dictionary.Add(entry.Key, entry.Text);
                }
                json["Translations"] = dictionary;
            }
            var source = json.ToString(Formatting.Indented) + Environment.NewLine;
            WriteIfUnchanged(AssetPath, SourceJson, source);
            SourceJson = source;
            IsDirty = false;
        }

        /// <summary>Rejects empty and duplicate keys before writing any document.</summary>
        /// <param name="keys">The document's keys.</param>
        /// <exception cref="InvalidOperationException">A key is empty or duplicated.</exception>
        internal static void ValidateKeys(IEnumerable<string> keys)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key) || !unique.Add(key))
                {
                    throw new InvalidOperationException("Translation keys must be nonempty and unique.");
                }
            }
        }

        /// <summary>Writes UTF-8 text only when the resource still matches the loaded snapshot.</summary>
        /// <param name="assetPath">The resource path.</param>
        /// <param name="original">The last loaded or saved text; empty for a new resource.</param>
        /// <param name="source">The new text.</param>
        /// <exception cref="IOException">The resource changed externally or could not be written.</exception>
        /// <exception cref="UnauthorizedAccessException">The resource is not writable.</exception>
        internal static void WriteIfUnchanged(string assetPath, string original, string source)
        {
            if (File.Exists(assetPath) ? File.ReadAllText(assetPath) != original : original.Length != 0)
            {
                throw new IOException($"{assetPath} changed or was deleted after loading. Reload resources before saving to avoid overwriting external edits.");
            }
            var directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(assetPath, source, new UTF8Encoding(false));
        }

        /// <summary>Reads localized text without accepting non-string JSON values.</summary>
        /// <param name="token">The translation's content token.</param>
        /// <returns>The text, or an empty string for missing or null content.</returns>
        /// <exception cref="FormatException">The token contains something other than text.</exception>
        private static string ReadText(JToken? token)
        {
            if (token is null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }
            if (token.Type != JTokenType.String)
            {
                throw new FormatException("Translation entries must contain text.");
            }
            return token.Value<string>() ?? string.Empty;
        }
    }

    /// <summary>A serializable key/text pair that can be edited and restored by Unity.</summary>
    [Serializable]
    internal sealed class TranslationEntry
    {
        /// <summary>The untranslated lookup key.</summary>
        public string Key = string.Empty;

        /// <summary>The localized text.</summary>
        public string Text = string.Empty;
    }
}

using System.Collections.Generic;
using TMPro;

#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    // Batch dynamic atlas and font feature work before Setting starts rendering new options.
    // This owns requests only; the shared fonts retain their existing feature settings.
    internal sealed class SettingFontWarmup
    {
        readonly Dictionary<TMP_FontAsset, HashSet<uint>> _requests = new();

        internal void AddText(TMP_FontAsset font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text))
            {
                return;
            }
            if (!_requests.TryGetValue(font, out var characters))
            {
                // TMP also requests underline, ellipsis and missing-character glyphs
                // when initializing text, even if the current label does not use them.
                characters = new HashSet<uint> { 0x20, 0x5F, 0x2026, 0x25A1 };
                if (TMP_Settings.missingGlyphCharacter > 0)
                {
                    characters.Add((uint)TMP_Settings.missingGlyphCharacter);
                }
                _requests.Add(font, characters);
            }

            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                uint unicode;
                if (char.IsHighSurrogate(character))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    {
                        continue;
                    }
                    unicode = (uint)char.ConvertToUtf32(character, text[++i]);
                }
                else if (char.IsLowSurrogate(character) || char.IsControl(character))
                {
                    continue;
                }
                else
                {
                    unicode = character;
                }
                characters.Add(unicode);
            }
        }

        internal void Warmup()
        {
            // A shared fallback can be reached from several text fonts. Avoid retrying
            // the same missing characters within this batch, including unsupported glyphs.
            var attempted = new Dictionary<TMP_FontAsset, HashSet<uint>>();
            foreach (var request in _requests)
            {
                var remaining = new HashSet<uint>(request.Value);
                var visited = new HashSet<TMP_FontAsset>();
                WarmFont(request.Key, remaining, visited, attempted);

                var globalFallbacks = TMP_Settings.fallbackFontAssets;
                if (globalFallbacks is not null)
                {
                    foreach (var fallback in globalFallbacks)
                    {
                        WarmFont(fallback, remaining, visited, attempted);
                    }
                }
                WarmFont(TMP_Settings.defaultFontAsset, remaining, visited, attempted);
            }
            _requests.Clear();
        }

        static void WarmFont(
            TMP_FontAsset? font,
            HashSet<uint> remaining,
            HashSet<TMP_FontAsset> visited,
            Dictionary<TMP_FontAsset, HashSet<uint>> attempted)
        {
            if (font == null || remaining.Count == 0 || !visited.Add(font))
            {
                return;
            }

            // Query the live lookup, rather than remembering that a previous warmup ran:
            // TMP may have cleared a dynamic atlas since the last Setting visit.
            var lookup = font.characterLookupTable;
            remaining.RemoveWhere(lookup.ContainsKey);
            if (remaining.Count > 0 && font.atlasPopulationMode != AtlasPopulationMode.Static)
            {
                if (!attempted.TryGetValue(font, out var tried))
                {
                    tried = new HashSet<uint>();
                    attempted.Add(font, tried);
                }
                var missing = new List<uint>(remaining.Count);
                foreach (var unicode in remaining)
                {
                    if (tried.Add(unicode))
                    {
                        missing.Add(unicode);
                    }
                }
                if (missing.Count > 0)
                {
                    font.TryAddCharacters(
                        missing.ToArray(),
                        out _,
                        includeFontFeatures: font.getFontFeatures && TMP_Settings.getFontFeaturesAtRuntime);
                    // Check what was actually added, including a partially filled atlas.
                    remaining.RemoveWhere(font.characterLookupTable.ContainsKey);
                }
            }

            var fallbacks = font.fallbackFontAssetTable;
            if (fallbacks is not null)
            {
                foreach (var fallback in fallbacks)
                {
                    // Keep TMP's depth-first fallback order; real assets contain cycles.
                    WarmFont(fallback, remaining, visited, attempted);
                }
            }
        }
    }
}

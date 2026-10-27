using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>Options that control how <see cref="TmpFontPrewarmer"/> adds glyphs.</summary>
    internal sealed class TmpFontPrewarmOptions
    {
        /// <summary>Whether characters missing from a font's source file are added to its fallback font assets, in TMP's lookup order.</summary>
        public bool IncludeFallbacks { get; set; } = true;

        /// <summary>Whether OpenType features (ligatures, kerning) are read for the new glyphs; slower and rarely needed for CJK.</summary>
        public bool IncludeFontFeatures { get; set; }

        /// <summary>The number of characters passed to one <see cref="TMP_FontAsset.TryAddCharacters(uint[], out uint[], bool)"/> call.</summary>
        public int BatchSize { get; set; } = 64;

        /// <summary>Whether modified persistent font assets are written to disk.</summary>
        public bool SaveAssets { get; set; } = true;
    }

    /// <summary>The outcome of prewarming one font asset and its fallback chain.</summary>
    internal sealed class TmpFontPrewarmResult
    {
        /// <summary>The font asset the characters were requested for.</summary>
        public TMP_FontAsset FontAsset { get; }

        /// <summary>The number of requested code points.</summary>
        public int RequestedCount { get; internal set; }

        /// <summary>The number of requested code points that some asset in the chain already contained.</summary>
        public int AlreadyPresentCount { get; internal set; }

        /// <summary>The number of code points added across the chain.</summary>
        public int AddedCount { get; internal set; }

        /// <summary>The number of code points added to each asset of the chain; assets that gained nothing are absent.</summary>
        public Dictionary<TMP_FontAsset, int> AddedPerAsset { get; } = new();

        /// <summary>The code points no dynamic asset in the chain could add: missing from every source font, or no atlas space without multi-atlas support.</summary>
        public List<uint> UnsupportedCodePoints { get; } = new();

        /// <summary>Whether the progress callback canceled the run.</summary>
        public bool IsCanceled { get; internal set; }

        /// <summary>The number of code points that were not processed because the run was canceled.</summary>
        public int UnprocessedCount { get; internal set; }

        /// <summary>Creates an empty result.</summary>
        /// <param name="fontAsset">The font asset the characters were requested for.</param>
        public TmpFontPrewarmResult(TMP_FontAsset fontAsset)
        {
            FontAsset = fontAsset;
        }
    }

    /// <summary>Adds characters to dynamic TMP font assets ahead of use so they need not be rasterized at runtime.</summary>
    internal static class TmpFontPrewarmer
    {
        /// <summary>The serialized name of the internal <c>clearDynamicDataOnBuild</c> flag.</summary>
        private const string ClearDynamicDataOnBuildProperty = "m_ClearDynamicDataOnBuild";

        /// <summary>Checks whether glyphs can be added to a font asset at edit time.</summary>
        /// <param name="fontAsset">The font asset to check.</param>
        /// <returns>True for the Dynamic and Dynamic OS population modes.</returns>
        public static bool IsDynamic(TMP_FontAsset fontAsset)
        {
            return fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic ||
                   fontAsset.atlasPopulationMode == AtlasPopulationMode.DynamicOS;
        }

        /// <summary>Lists a font asset followed by its fallbacks, depth first, in the order TMP searches them for a character.</summary>
        /// <param name="fontAsset">The font asset at the start of the chain.</param>
        /// <param name="includeFallbacks">Whether to follow <see cref="TMP_FontAsset.fallbackFontAssetTable"/>; when false only the asset itself is returned.</param>
        /// <returns>The chain without null entries or duplicates.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> is null.</exception>
        public static List<TMP_FontAsset> GetLookupChain(TMP_FontAsset fontAsset, bool includeFallbacks)
        {
            if (fontAsset == null)
            {
                throw new ArgumentNullException(nameof(fontAsset));
            }
            var chain = new List<TMP_FontAsset>();
            var visited = new HashSet<TMP_FontAsset>();
            AppendToChain(fontAsset, includeFallbacks, chain, visited);
            return chain;
        }

        /// <summary>Counts the code points that some asset of the lookup chain already contains.</summary>
        /// <param name="fontAsset">The font asset at the start of the chain.</param>
        /// <param name="codePoints">The code points to look up.</param>
        /// <param name="includeFallbacks">Whether fallback font assets count.</param>
        /// <returns>The number of code points already present.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> or <paramref name="codePoints"/> is null.</exception>
        public static int CountPresent(TMP_FontAsset fontAsset, IEnumerable<uint> codePoints, bool includeFallbacks)
        {
            if (codePoints is null)
            {
                throw new ArgumentNullException(nameof(codePoints));
            }
            var chain = GetLookupChain(fontAsset, includeFallbacks);
            var presentCount = 0;
            foreach (var codePoint in codePoints)
            {
                if (ContainsInChain(chain, codePoint))
                {
                    presentCount++;
                }
            }
            return presentCount;
        }

        /// <summary>
        /// Adds the code points that the lookup chain does not contain yet. Each dynamic asset of the chain, in lookup order,
        /// receives the characters its source font has, so the glyphs land where TMP would look for them at runtime.
        /// Static assets are skipped but their fallbacks are still filled.
        /// </summary>
        /// <param name="fontAsset">The font asset at the start of the chain.</param>
        /// <param name="codePoints">The code points to add.</param>
        /// <param name="options">How glyphs are added and saved.</param>
        /// <param name="onProgress">Receives the progress (0 to 1) and a description before each batch; returning true cancels the run.</param>
        /// <returns>What was added, what was already present and what no asset could add.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/>, <paramref name="codePoints"/> or <paramref name="options"/> is null.</exception>
        public static TmpFontPrewarmResult Prewarm(TMP_FontAsset fontAsset,
                                                   IReadOnlyCollection<uint> codePoints,
                                                   TmpFontPrewarmOptions options,
                                                   Func<float, string, bool>? onProgress = null)
        {
            if (codePoints is null)
            {
                throw new ArgumentNullException(nameof(codePoints));
            }
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var chain = GetLookupChain(fontAsset, options.IncludeFallbacks);
            var result = new TmpFontPrewarmResult(fontAsset)
            {
                RequestedCount = codePoints.Count
            };
            var pending = new List<uint>(codePoints.Count);
            foreach (var codePoint in codePoints)
            {
                if (ContainsInChain(chain, codePoint))
                {
                    result.AlreadyPresentCount++;
                }
                else
                {
                    pending.Add(codePoint);
                }
            }

            var dynamicAssets = chain.FindAll(IsDynamic);
            var batchSize = Math.Max(1, options.BatchSize);
            for (var assetIndex = 0; assetIndex < dynamicAssets.Count && pending.Count > 0; assetIndex++)
            {
                var asset = dynamicAssets[assetIndex];
                var remaining = new List<uint>(pending.Count);
                var addedCount = 0;
                for (var offset = 0; offset < pending.Count; offset += batchSize)
                {
                    var progress = (assetIndex + (float)offset / pending.Count) / dynamicAssets.Count;
                    if (onProgress is not null && onProgress(progress, $"{asset.name}: {offset}/{pending.Count}"))
                    {
                        result.IsCanceled = true;
                        for (var i = offset; i < pending.Count; i++)
                        {
                            remaining.Add(pending[i]);
                        }
                        break;
                    }
                    var count = Math.Min(batchSize, pending.Count - offset);
                    var batch = new uint[count];
                    pending.CopyTo(offset, batch, 0, count);
                    asset.TryAddCharacters(batch, out _, options.IncludeFontFeatures);
                    // The returned missing list is unreliable (it echoes the input when only shared glyphs were added),
                    // so the character table is the source of truth.
                    foreach (var codePoint in batch)
                    {
                        if (asset.HasCharacter((int)codePoint))
                        {
                            addedCount++;
                        }
                        else
                        {
                            remaining.Add(codePoint);
                        }
                    }
                }
                if (addedCount > 0)
                {
                    result.AddedPerAsset[asset] = addedCount;
                    result.AddedCount += addedCount;
                    if (options.SaveAssets)
                    {
                        SaveFontAsset(asset);
                    }
                }
                pending = remaining;
                if (result.IsCanceled)
                {
                    break;
                }
            }

            if (result.IsCanceled)
            {
                result.UnprocessedCount = pending.Count;
            }
            else
            {
                result.UnsupportedCodePoints.AddRange(pending);
            }
            return result;
        }

        /// <summary>Reads the font asset's "Clear Dynamic Data On Build" flag, which TMP only exposes internally.</summary>
        /// <param name="fontAsset">The font asset to read.</param>
        /// <returns>True when TMP empties the asset's dynamic glyphs before every build and when the Editor quits.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> is null.</exception>
        public static bool GetClearDynamicDataOnBuild(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                throw new ArgumentNullException(nameof(fontAsset));
            }
            using var serializedObject = new SerializedObject(fontAsset);
            var property = serializedObject.FindProperty(ClearDynamicDataOnBuildProperty);

            return property is not null && property.boolValue;
        }

        /// <summary>Sets the font asset's "Clear Dynamic Data On Build" flag as an undoable change.</summary>
        /// <param name="fontAsset">The font asset to change.</param>
        /// <param name="value">The new flag value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The TMP version has no such serialized property.</exception>
        public static void SetClearDynamicDataOnBuild(TMP_FontAsset fontAsset, bool value)
        {
            if (fontAsset == null)
            {
                throw new ArgumentNullException(nameof(fontAsset));
            }
            using var serializedObject = new SerializedObject(fontAsset);
            var property = serializedObject.FindProperty(ClearDynamicDataOnBuildProperty);
            if (property is null)
            {
                throw new InvalidOperationException($"{nameof(TMP_FontAsset)} has no serialized property {ClearDynamicDataOnBuildProperty}.");
            }
            property.boolValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Appends a font asset and, depth first, its fallbacks to the chain.</summary>
        /// <param name="fontAsset">The font asset to append.</param>
        /// <param name="includeFallbacks">Whether to follow the fallback table.</param>
        /// <param name="chain">The chain being built.</param>
        /// <param name="visited">The assets already in the chain, which guards against fallback cycles.</param>
        private static void AppendToChain(TMP_FontAsset fontAsset, bool includeFallbacks, List<TMP_FontAsset> chain, HashSet<TMP_FontAsset> visited)
        {
            if (!visited.Add(fontAsset))
            {
                return;
            }
            chain.Add(fontAsset);
            if (!includeFallbacks || fontAsset.fallbackFontAssetTable is null)
            {
                return;
            }
            foreach (var fallback in fontAsset.fallbackFontAssetTable)
            {
                if (fallback != null)
                {
                    AppendToChain(fallback, includeFallbacks, chain, visited);
                }
            }
        }

        /// <summary>Checks whether any asset of the chain already has a character for the code point.</summary>
        /// <param name="chain">The lookup chain.</param>
        /// <param name="codePoint">The code point to look up.</param>
        /// <returns>True when some asset contains the character.</returns>
        private static bool ContainsInChain(List<TMP_FontAsset> chain, uint codePoint)
        {
            foreach (var asset in chain)
            {
                if (asset.HasCharacter((int)codePoint))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Marks a persistent font asset, its atlas textures and material dirty and writes them to disk; TMP itself does not.</summary>
        /// <param name="fontAsset">The modified font asset.</param>
        private static void SaveFontAsset(TMP_FontAsset fontAsset)
        {
            if (!EditorUtility.IsPersistent(fontAsset))
            {
                return;
            }
            EditorUtility.SetDirty(fontAsset);
            var atlasTextures = fontAsset.atlasTextures;
            if (atlasTextures is not null)
            {
                foreach (var atlasTexture in atlasTextures)
                {
                    if (atlasTexture != null)
                    {
                        EditorUtility.SetDirty(atlasTexture);
                    }
                }
            }
            if (fontAsset.material != null)
            {
                EditorUtility.SetDirty(fontAsset.material);
            }
            AssetDatabase.SaveAssetIfDirty(fontAsset);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>Options that control how <see cref="TmpFontPrewarmer"/> adds glyphs.</summary>
    internal sealed class TmpFontPrewarmOptions
    {
        /// <summary>Whether characters missing from a font's source file are added to its fallback font assets, in TMP's lookup order.</summary>
        public bool IncludeFallbacks { get; set; } = true;

        /// <summary>Whether OpenType features (ligatures, kerning) are read for the new glyphs of dynamic assets; slower and rarely needed for CJK.</summary>
        public bool IncludeFontFeatures { get; set; }

        /// <summary>
        /// Whether static font assets of the chain are baked as well: each one is switched to dynamic while its characters are added,
        /// then switched back to static with a non-readable atlas. When false, static assets are skipped.
        /// </summary>
        public bool IncludeStaticAssets { get; set; }

        /// <summary>Whether a full single-atlas static asset is rebuilt at the next larger atlas size to fit the new characters.</summary>
        public bool GrowStaticAtlases { get; set; } = true;

        /// <summary>The largest width or height a static atlas may grow to.</summary>
        public int MaxStaticAtlasSize { get; set; } = 4096;

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

        /// <summary>
        /// The number of requested code points that TMP would find in an asset of the chain before reaching an asset that would
        /// generate them at runtime.
        /// </summary>
        public int AlreadyPresentCount { get; internal set; }

        /// <summary>The number of code points added across the chain.</summary>
        public int AddedCount { get; internal set; }

        /// <summary>The number of code points added to each asset of the chain; assets that gained nothing are absent.</summary>
        public Dictionary<TMP_FontAsset, int> AddedPerAsset { get; } = new();

        /// <summary>The new atlas size of each static asset whose atlas was grown to fit the characters.</summary>
        public Dictionary<TMP_FontAsset, Vector2Int> GrownAtlasSizes { get; } = new();

        /// <summary>The static assets that were skipped because they have no source font to rasterize from.</summary>
        public List<TMP_FontAsset> StaticAssetsWithoutSourceFont { get; } = new();

        /// <summary>
        /// The code points no asset in the chain could add: missing from every source font, or no atlas space
        /// (a dynamic atlas without multi-atlas support, or a static atlas that may not grow further).
        /// </summary>
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

    /// <summary>The outcome of clearing glyphs from one font asset and its fallback chain.</summary>
    internal sealed class TmpFontClearResult
    {
        /// <summary>The font asset at the start of the cleared chain.</summary>
        public TMP_FontAsset FontAsset { get; }

        /// <summary>The number of characters removed across the chain.</summary>
        public int RemovedCount { get; internal set; }

        /// <summary>The number of characters removed from each asset of the chain; assets that lost nothing are absent.</summary>
        public Dictionary<TMP_FontAsset, int> RemovedPerAsset { get; } = new();

        /// <summary>The static assets left untouched because static assets are not cleared with the current options.</summary>
        public List<TMP_FontAsset> SkippedStaticAssets { get; } = new();

        /// <summary>The assets that have characters to remove but no source font to rebuild their remaining characters from.</summary>
        public List<TMP_FontAsset> AssetsWithoutSourceFont { get; } = new();

        /// <summary>The characters that should have stayed but no longer fit when their asset was rebuilt.</summary>
        public List<uint> LostCodePoints { get; } = new();

        /// <summary>Creates an empty result.</summary>
        /// <param name="fontAsset">The font asset at the start of the cleared chain.</param>
        public TmpFontClearResult(TMP_FontAsset fontAsset)
        {
            FontAsset = fontAsset;
        }
    }

    /// <summary>
    /// Adds characters to TMP font assets ahead of use so they need not be rasterized at runtime: dynamic assets receive the glyphs
    /// directly, static assets can be baked in place. Prewarmed glyphs can be cleared again.
    /// </summary>
    internal static class TmpFontPrewarmer
    {
        /// <summary>The serialized name of the internal <c>clearDynamicDataOnBuild</c> flag.</summary>
        private const string ClearDynamicDataOnBuildProperty = "m_ClearDynamicDataOnBuild";

        /// <summary>The serialized name of the source font GUID that TMP keeps for static assets.</summary>
        private const string SourceFontGuidProperty = "m_SourceFontFileGUID";

        /// <summary>The full name of the internal editor type that changes an atlas texture's readable flag.</summary>
        private const string FontEngineEditorUtilitiesType = "UnityEditor.TextCore.LowLevel.FontEngineEditorUtilities";

        /// <summary>The <c>SetAtlasTextureIsReadable(Texture2D, bool)</c> method TMP's font asset inspector uses, looked up once.</summary>
        private static readonly Lazy<MethodInfo?> SetAtlasTextureIsReadableMethod = new(FindSetAtlasTextureIsReadable);

        /// <summary>Checks whether glyphs can be added to a font asset at edit time.</summary>
        /// <param name="fontAsset">The font asset to check.</param>
        /// <returns>True for the Dynamic and Dynamic OS population modes.</returns>
        public static bool IsDynamic(TMP_FontAsset fontAsset)
        {
            return fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic ||
                   fontAsset.atlasPopulationMode == AtlasPopulationMode.DynamicOS;
        }

        /// <summary>Finds the font file a font asset rasterizes from, which static assets only keep as a GUID.</summary>
        /// <param name="fontAsset">The font asset.</param>
        /// <returns>The source font, or null when the asset has none or it no longer exists.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> is null.</exception>
        public static Font? GetSourceFont(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                throw new ArgumentNullException(nameof(fontAsset));
            }
            if (fontAsset.sourceFontFile != null)
            {
                return fontAsset.sourceFontFile;
            }
            using var serializedObject = new SerializedObject(fontAsset);
            var guid = serializedObject.FindProperty(SourceFontGuidProperty)?.stringValue;
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Font>(path);
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
        /// Adds the code points that the lookup chain does not contain yet. Each asset of the chain that can take glyphs, in lookup order,
        /// receives the characters its source font has, so the glyphs land where TMP would look for them at runtime.
        /// Static assets are baked when <see cref="TmpFontPrewarmOptions.IncludeStaticAssets"/> is set and skipped otherwise;
        /// their fallbacks are filled either way.
        /// </summary>
        /// <param name="fontAsset">The font asset at the start of the chain.</param>
        /// <param name="codePoints">The code points to add.</param>
        /// <param name="options">How glyphs are added and saved.</param>
        /// <param name="onProgress">Receives the progress (0 to 1) and a description before each batch; returning true cancels the run.</param>
        /// <returns>What was added, what was already present and what no asset could add.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/>, <paramref name="codePoints"/> or <paramref name="options"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A static asset must be baked but the Editor offers no way to change atlas readability.</exception>
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
            var targets = new List<TMP_FontAsset>(chain.Count);
            foreach (var asset in chain)
            {
                if (IsDynamic(asset))
                {
                    targets.Add(asset);
                }
                else if (options.IncludeStaticAssets)
                {
                    if (GetSourceFont(asset) is null)
                    {
                        result.StaticAssetsWithoutSourceFont.Add(asset);
                    }
                    else
                    {
                        targets.Add(asset);
                    }
                }
            }

            // TMP walks the chain in order and generates a missing glyph in the first dynamic asset whose source font has it,
            // so a glyph that only a later asset contains is still rasterized at runtime and must be added.
            var supportedByTarget = new Dictionary<TMP_FontAsset, HashSet<uint>>(targets.Count);
            foreach (var target in targets)
            {
                supportedByTarget[target] = GetSupportedCodePoints(target, codePoints);
            }
            var pending = new List<uint>(codePoints.Count);
            foreach (var codePoint in codePoints)
            {
                if (IsFoundBeforeGeneration(chain, supportedByTarget, codePoint))
                {
                    result.AlreadyPresentCount++;
                }
                else
                {
                    pending.Add(codePoint);
                }
            }

            for (var assetIndex = 0; assetIndex < targets.Count && pending.Count > 0; assetIndex++)
            {
                var asset = targets[assetIndex];
                var isStatic = !IsDynamic(asset);
                var addedCount = 0;
                var isAtlasGrown = false;
                List<uint> remaining;
                var bakeScope = isStatic ? new StaticBakeScope(asset) : null;
                try
                {
                    // A static asset keeps its own font feature setting so a rebuilt atlas keeps the kerning it had.
                    var includeFontFeatures = isStatic ? asset.getFontFeatures : options.IncludeFontFeatures;
                    remaining = AddInBatches(asset, pending, includeFontFeatures, options, assetIndex, targets.Count, result, onProgress, ref addedCount);
                    if (isStatic && !result.IsCanceled && options.GrowStaticAtlases && remaining.Count > 0 && !asset.isMultiAtlasTexturesEnabled)
                    {
                        var supported = supportedByTarget[asset];
                        var overflow = remaining.FindAll(supported.Contains);
                        if (overflow.Count > 0)
                        {
                            onProgress?.Invoke((float)assetIndex / targets.Count, $"{asset.name}: growing atlas for {overflow.Count} characters");
                            isAtlasGrown = TryGrowAtlas(asset, overflow, options.MaxStaticAtlasSize, includeFontFeatures);
                        }
                        if (isAtlasGrown)
                        {
                            var stillMissing = new List<uint>(remaining.Count);
                            foreach (var codePoint in remaining)
                            {
                                if (asset.HasCharacter((int)codePoint))
                                {
                                    addedCount++;
                                }
                                else
                                {
                                    stillMissing.Add(codePoint);
                                }
                            }
                            remaining = stillMissing;
                            result.GrownAtlasSizes[asset] = new Vector2Int(asset.atlasWidth, asset.atlasHeight);
                        }
                    }
                }
                finally
                {
                    bakeScope?.Dispose();
                }
                if (isAtlasGrown)
                {
                    UpdateAtlasSizeOnMaterials(asset, options.SaveAssets);
                }
                if (addedCount > 0)
                {
                    result.AddedPerAsset[asset] = addedCount;
                    result.AddedCount += addedCount;
                }
                if (options.SaveAssets && (addedCount > 0 || isAtlasGrown))
                {
                    SaveFontAsset(asset);
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

        /// <summary>
        /// Empties every dynamic asset of the lookup chain the way TMP's "Reset" context menu does, removing all prewarmed and
        /// runtime-generated glyphs. Static assets are left untouched because their glyphs cannot be generated again at runtime.
        /// </summary>
        /// <param name="fontAsset">The font asset at the start of the chain.</param>
        /// <param name="options">Whether fallbacks are cleared and whether cleared assets are saved.</param>
        /// <param name="onProgress">Receives the progress (0 to 1) and a description before each asset is cleared.</param>
        /// <returns>What was removed from which asset and which static assets were left untouched.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> or <paramref name="options"/> is null.</exception>
        public static TmpFontClearResult ClearDynamicGlyphs(TMP_FontAsset fontAsset,
                                                            TmpFontPrewarmOptions options,
                                                            Action<float, string>? onProgress = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var chain = GetLookupChain(fontAsset, options.IncludeFallbacks);
            var result = new TmpFontClearResult(fontAsset);
            for (var i = 0; i < chain.Count; i++)
            {
                var asset = chain[i];
                if (!IsDynamic(asset))
                {
                    result.SkippedStaticAssets.Add(asset);
                    continue;
                }
                var characterCount = asset.characterTable.Count;
                if (characterCount == 0)
                {
                    continue;
                }
                onProgress?.Invoke((float)i / chain.Count, $"{asset.name}: clearing {characterCount} characters");
                asset.ClearFontAssetData(true);
                result.RemovedPerAsset[asset] = characterCount;
                result.RemovedCount += characterCount;
                if (options.SaveAssets)
                {
                    SaveFontAsset(asset);
                }
            }
            return result;
        }

        /// <summary>
        /// Removes the given characters from every asset of the lookup chain that has them. TMP cannot remove single glyphs,
        /// so each affected asset is rebuilt from its remaining characters at its current atlas size. Static assets are rebuilt
        /// when <see cref="TmpFontPrewarmOptions.IncludeStaticAssets"/> is set and stay static with a non-readable atlas;
        /// otherwise they are left untouched.
        /// </summary>
        /// <param name="fontAsset">The font asset at the start of the chain.</param>
        /// <param name="codePoints">The code points to remove.</param>
        /// <param name="options">Whether fallbacks and static assets are included, how glyphs are re-added and whether assets are saved.</param>
        /// <param name="onProgress">Receives the progress (0 to 1) and a description before each asset is rebuilt.</param>
        /// <returns>What was removed from which asset, what was skipped and which remaining characters no longer fit.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/>, <paramref name="codePoints"/> or <paramref name="options"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A static asset must be rebuilt but the Editor offers no way to change atlas readability.</exception>
        public static TmpFontClearResult RemoveCharacters(TMP_FontAsset fontAsset,
                                                          IReadOnlyCollection<uint> codePoints,
                                                          TmpFontPrewarmOptions options,
                                                          Action<float, string>? onProgress = null)
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
            var result = new TmpFontClearResult(fontAsset);
            var removedCodePoints = new HashSet<uint>(codePoints);
            for (var i = 0; i < chain.Count; i++)
            {
                var asset = chain[i];
                var kept = new List<uint>(asset.characterTable.Count);
                var removedCount = 0;
                foreach (var character in asset.characterTable)
                {
                    if (removedCodePoints.Contains(character.unicode))
                    {
                        removedCount++;
                    }
                    else
                    {
                        kept.Add(character.unicode);
                    }
                }
                if (removedCount == 0)
                {
                    continue;
                }
                var isStatic = !IsDynamic(asset);
                if (isStatic && !options.IncludeStaticAssets)
                {
                    result.SkippedStaticAssets.Add(asset);
                    continue;
                }
                if (GetSourceFont(asset) is null)
                {
                    result.AssetsWithoutSourceFont.Add(asset);
                    continue;
                }

                onProgress?.Invoke((float)i / chain.Count, $"{asset.name}: removing {removedCount} characters, keeping {kept.Count}");
                var bakeScope = isStatic ? new StaticBakeScope(asset) : null;
                try
                {
                    // Same feature choice as Prewarm, so a rebuilt static atlas keeps the kerning it had.
                    var includeFontFeatures = isStatic ? asset.getFontFeatures : options.IncludeFontFeatures;
                    asset.ClearFontAssetData(true);
                    if (kept.Count > 0)
                    {
                        asset.TryAddCharacters(kept.ToArray(), out _, includeFontFeatures);
                    }
                }
                finally
                {
                    bakeScope?.Dispose();
                }
                foreach (var codePoint in kept)
                {
                    if (!asset.HasCharacter((int)codePoint))
                    {
                        result.LostCodePoints.Add(codePoint);
                    }
                }
                result.RemovedPerAsset[asset] = removedCount;
                result.RemovedCount += removedCount;
                if (options.SaveAssets)
                {
                    SaveFontAsset(asset);
                }
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

        /// <summary>Makes every atlas texture of a font asset readable or not, the way TMP's inspector does when the population mode changes.</summary>
        /// <param name="fontAsset">The font asset whose atlas textures change.</param>
        /// <param name="isReadable">Whether the textures keep a CPU copy that glyphs can be written to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="fontAsset"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The Editor offers no way to change atlas readability.</exception>
        internal static void SetAtlasTexturesReadable(TMP_FontAsset fontAsset, bool isReadable)
        {
            if (fontAsset == null)
            {
                throw new ArgumentNullException(nameof(fontAsset));
            }
            var method = SetAtlasTextureIsReadableMethod.Value;
            if (method is null)
            {
                throw new InvalidOperationException($"{FontEngineEditorUtilitiesType}.SetAtlasTextureIsReadable was not found in this Unity version.");
            }
            var atlasTextures = fontAsset.atlasTextures;
            if (atlasTextures is null)
            {
                return;
            }
            foreach (var atlasTexture in atlasTextures)
            {
                if (atlasTexture != null && atlasTexture.isReadable != isReadable)
                {
                    method.Invoke(null, new object[] { atlasTexture, isReadable });
                }
            }
        }

        /// <summary>Adds the pending code points to one asset in batches, reporting progress before each batch.</summary>
        /// <param name="asset">The asset that receives the glyphs; it must accept glyphs (dynamic, or static inside a bake scope).</param>
        /// <param name="pending">The code points still missing from the chain.</param>
        /// <param name="includeFontFeatures">Whether font features are read for the new glyphs.</param>
        /// <param name="options">The batch size source.</param>
        /// <param name="assetIndex">The index of the asset among the assets being filled, for the progress fraction.</param>
        /// <param name="assetCount">The number of assets being filled.</param>
        /// <param name="result">The result whose cancel flag is set when the callback cancels.</param>
        /// <param name="onProgress">The progress callback, which can cancel.</param>
        /// <param name="addedCount">Incremented for every code point the asset gained.</param>
        /// <returns>The code points the asset did not gain, including the unprocessed ones after a cancel.</returns>
        private static List<uint> AddInBatches(TMP_FontAsset asset,
                                               List<uint> pending,
                                               bool includeFontFeatures,
                                               TmpFontPrewarmOptions options,
                                               int assetIndex,
                                               int assetCount,
                                               TmpFontPrewarmResult result,
                                               Func<float, string, bool>? onProgress,
                                               ref int addedCount)
        {
            var batchSize = Math.Max(1, options.BatchSize);
            var remaining = new List<uint>(pending.Count);
            for (var offset = 0; offset < pending.Count; offset += batchSize)
            {
                var progress = (assetIndex + (float)offset / pending.Count) / assetCount;
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
                asset.TryAddCharacters(batch, out _, includeFontFeatures);
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
            return remaining;
        }

        /// <summary>Checks whether TMP would find a code point in an asset of the chain before reaching an asset that would generate it.</summary>
        /// <param name="chain">The lookup chain in TMP's search order.</param>
        /// <param name="supportedByTarget">For each asset that can receive glyphs, the requested code points its source font has.</param>
        /// <param name="codePoint">The code point to look up.</param>
        /// <returns>True when an asset contains the character before any asset that could generate it.</returns>
        private static bool IsFoundBeforeGeneration(List<TMP_FontAsset> chain, Dictionary<TMP_FontAsset, HashSet<uint>> supportedByTarget, uint codePoint)
        {
            foreach (var asset in chain)
            {
                if (asset.HasCharacter((int)codePoint))
                {
                    return true;
                }
                if (supportedByTarget.TryGetValue(asset, out var supported) && supported.Contains(codePoint))
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>Collects the code points that the asset's source font has a glyph for.</summary>
        /// <param name="asset">The font asset whose source font is checked.</param>
        /// <param name="codePoints">The code points to check.</param>
        /// <returns>The supported code points; empty when the asset has no loadable source font.</returns>
        private static HashSet<uint> GetSupportedCodePoints(TMP_FontAsset asset, IEnumerable<uint> codePoints)
        {
            var supported = new HashSet<uint>();
            var sourceFont = GetSourceFont(asset);
            if (sourceFont == null)
            {
                return supported;
            }
            using var serializedObject = new SerializedObject(asset);
            var faceIndex = serializedObject.FindProperty("m_FaceInfo.m_FaceIndex")?.intValue ?? 0;
            if (FontEngine.LoadFontFace(sourceFont, (int)asset.faceInfo.pointSize, faceIndex) != FontEngineError.Success)
            {
                return supported;
            }
            foreach (var codePoint in codePoints)
            {
                if (FontEngine.TryGetGlyphIndex(codePoint, out var glyphIndex) && glyphIndex != 0)
                {
                    supported.Add(codePoint);
                }
            }
            return supported;
        }

        /// <summary>
        /// Rebuilds a single-atlas asset at successively larger power-of-two sizes, re-adding its characters before the overflow,
        /// until the overflow fits or the size limit is reached.
        /// </summary>
        /// <param name="asset">The asset to rebuild; it must accept glyphs.</param>
        /// <param name="overflow">The code points its source font has but the current atlas could not fit.</param>
        /// <param name="maxSize">The largest atlas width or height allowed.</param>
        /// <param name="includeFontFeatures">Whether font features are read for the rebuilt glyphs.</param>
        /// <returns>True when the atlas size changed, even if not every overflow code point fit at the limit.</returns>
        private static bool TryGrowAtlas(TMP_FontAsset asset, List<uint> overflow, int maxSize, bool includeFontFeatures)
        {
            var existing = new uint[asset.characterTable.Count];
            for (var i = 0; i < existing.Length; i++)
            {
                existing[i] = asset.characterTable[i].unicode;
            }
            var overflowArray = overflow.ToArray();
            var width = asset.atlasWidth;
            var height = asset.atlasHeight;
            var isGrown = false;
            while (TryGetNextAtlasSize(width, height, maxSize, out width, out height))
            {
                SetAtlasSize(asset, width, height);
                asset.ClearFontAssetData(true);
                asset.TryAddCharacters(existing, out _, includeFontFeatures);
                asset.TryAddCharacters(overflowArray, out _, includeFontFeatures);
                isGrown = true;
                if (Array.TrueForAll(overflowArray, codePoint => asset.HasCharacter((int)codePoint)))
                {
                    break;
                }
            }
            return isGrown;
        }

        /// <summary>Gets the next atlas size, doubling the smaller side first.</summary>
        /// <param name="width">The current width.</param>
        /// <param name="height">The current height.</param>
        /// <param name="maxSize">The largest width or height allowed.</param>
        /// <param name="nextWidth">The next width.</param>
        /// <param name="nextHeight">The next height.</param>
        /// <returns>False when the next size would exceed <paramref name="maxSize"/>.</returns>
        private static bool TryGetNextAtlasSize(int width, int height, int maxSize, out int nextWidth, out int nextHeight)
        {
            nextWidth = width;
            nextHeight = height;
            if (width <= height)
            {
                nextWidth = width * 2;
            }
            else
            {
                nextHeight = height * 2;
            }
            return nextWidth <= maxSize && nextHeight <= maxSize;
        }

        /// <summary>Sets the atlas size and the matching creation settings, which TMP only exposes internally.</summary>
        /// <param name="asset">The font asset.</param>
        /// <param name="width">The new atlas width.</param>
        /// <param name="height">The new atlas height.</param>
        private static void SetAtlasSize(TMP_FontAsset asset, int width, int height)
        {
            using var serializedObject = new SerializedObject(asset);
            serializedObject.FindProperty("m_AtlasWidth").intValue = width;
            serializedObject.FindProperty("m_AtlasHeight").intValue = height;
            var creationWidth = serializedObject.FindProperty("m_CreationSettings.atlasWidth");
            if (creationWidth is not null)
            {
                creationWidth.intValue = width;
            }
            var creationHeight = serializedObject.FindProperty("m_CreationSettings.atlasHeight");
            if (creationHeight is not null)
            {
                creationHeight.intValue = height;
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Writes the asset's atlas size to its material and, for a persistent asset, to every material preset that samples its atlas.</summary>
        /// <param name="asset">The font asset whose atlas size changed.</param>
        /// <param name="saveAssets">Whether changed preset materials are written to disk.</param>
        private static void UpdateAtlasSizeOnMaterials(TMP_FontAsset asset, bool saveAssets)
        {
            var materials = new List<Material>();
            if (asset.material != null)
            {
                materials.Add(asset.material);
            }
            if (EditorUtility.IsPersistent(asset) && asset.atlasTexture != null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material"))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (material != null && material != asset.material &&
                        material.HasProperty(ShaderUtilities.ID_MainTex) &&
                        material.GetTexture(ShaderUtilities.ID_MainTex) == asset.atlasTexture)
                    {
                        materials.Add(material);
                    }
                }
            }
            foreach (var material in materials)
            {
                material.SetFloat(ShaderUtilities.ID_TextureWidth, asset.atlasWidth);
                material.SetFloat(ShaderUtilities.ID_TextureHeight, asset.atlasHeight);
                if (material != asset.material && EditorUtility.IsPersistent(material))
                {
                    EditorUtility.SetDirty(material);
                    if (saveAssets)
                    {
                        AssetDatabase.SaveAssetIfDirty(material);
                    }
                }
            }
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

        /// <summary>Checks whether any asset of the chain, wherever it is, already has a character for the code point.</summary>
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

        /// <summary>Looks up TMP's internal readability setter in the loaded editor assemblies.</summary>
        /// <returns>The method, or null when this Unity version has none.</returns>
        private static MethodInfo? FindSetAtlasTextureIsReadable()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(FontEngineEditorUtilitiesType, false);
                if (type is null)
                {
                    continue;
                }
                return type.GetMethod("SetAtlasTextureIsReadable",
                                      BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                                      null,
                                      new[] { typeof(Texture2D), typeof(bool) },
                                      null);
            }
            return null;
        }

        /// <summary>
        /// Lets glyphs be added to a static asset: switches it to dynamic with readable atlases, and on dispose switches it back to static
        /// with non-readable atlases, which also drops the runtime source font reference as TMP's inspector does.
        /// </summary>
        private sealed class StaticBakeScope : IDisposable
        {
            /// <summary>The static asset being baked.</summary>
            private readonly TMP_FontAsset _asset;

            /// <summary>Opens the scope.</summary>
            /// <param name="asset">The static asset to bake; it must have a source font.</param>
            /// <exception cref="InvalidOperationException">The Editor offers no way to change atlas readability.</exception>
            public StaticBakeScope(TMP_FontAsset asset)
            {
                _asset = asset;
                SetAtlasTexturesReadable(asset, true);
                asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            }

            /// <summary>Restores the static population mode and non-readable atlases.</summary>
            public void Dispose()
            {
                _asset.atlasPopulationMode = AtlasPopulationMode.Static;
                SetAtlasTexturesReadable(_asset, false);
            }
        }
    }
}

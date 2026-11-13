using System;
using System.Collections.Generic;
using MajdataPlay.Diagnostics;
using UnityEngine;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Shares quality-dependent UI textures and material variants without adding render targets or draw passes.</summary>
    internal static class UiOutlineTheme
    {
        /// <summary>Names the original texture whose slice layout is shared by the outlined artwork.</summary>
        private const string BaseAtlasName = "newui@3x";
        /// <summary>Loads references to the authored atlas and alpha material only when outlined UI is requested.</summary>
        private const string OutlineResourcesPath = "Rendering/UI/OutlineTheme";
        /// <summary>Identifies the original cutout shader that needs an alpha-blended variant.</summary>
        private const string CutoutShaderName = "Maj/Common/Opaque";
        /// <summary>Identifies the original circular List cutout material.</summary>
        private const string CoverShaderName = "Maj/UI/List/CoverShader";
        /// <summary>Identifies procedural rounded panels whose shape must survive non-blended rendering.</summary>
        private const string RoundedRectShaderName = "Maj/UI/Rounded Rect";
        /// <summary>Holds enabled images, including those instantiated from pooled prefabs.</summary>
        private static readonly List<QualityUiImage> s_images = new();
        /// <summary>Caches one material variant per original material, retaining distinct List depth offsets.</summary>
        private static readonly Dictionary<Material, Material> s_alphaMaterials = new();
        /// <summary>Caches one shared non-blended variant per supported original UI material.</summary>
        private static readonly Dictionary<Material, Material> s_cutoffMaterials = new();
        /// <summary>Maps the original logical sprite names to the identical outlined slices.</summary>
        private static Dictionary<string, Sprite>? s_outlinedSprites;
        /// <summary>Reuses successful paired-Sprite validation without repeated native-name and layout queries.</summary>
        private static readonly Dictionary<Sprite, OutlinedMapping> s_verifiedSprites = new();
        /// <summary>Bounds retention of transient clones while keeping the authored atlas allocation-free after warmup.</summary>
        private const int MaximumVerifiedSprites = 256;
        /// <summary>Retains the included alpha-material template after its first use.</summary>
        private static Material? s_alphaTemplate;
        /// <summary>Retains the included alpha-blended circular List material template.</summary>
        private static Material? s_coverAlphaTemplate;
        /// <summary>Retains the mask-aware background cutoff template after its first use.</summary>
        private static Material? s_backgroundCutoffTemplate;
        /// <summary>Stores the single effective UI artwork policy independently of MSAA.</summary>
        private static UiOutlineMode s_mode;
        /// <summary>Prevents repeated missing-resource or incompatible-layout messages during Canvas rebuilds.</summary>
        private static bool s_reportedResourceError;

        /// <summary>Clears runtime caches before a new Player session, including domain-reload-free Play Mode.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ReleaseVariants(s_alphaMaterials);
            ReleaseVariants(s_cutoffMaterials);
            s_images.Clear();
            s_verifiedSprites.Clear();
            s_outlinedSprites = null;
            s_alphaTemplate = null;
            s_coverAlphaTemplate = null;
            s_backgroundCutoffTemplate = null;
            s_mode = UiOutlineMode.None;
            s_reportedResourceError = false;
        }

        /// <summary>Releases only owned material variants and clears their source-to-variant cache.</summary>
        /// <param name="variants">The shared alpha or cutoff variants to release.</param>
        private static void ReleaseVariants(Dictionary<Material, Material> variants)
        {
            foreach (var variant in variants.Values)
            {
                if (variant == null)
                {
                    continue;
                }
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(variant);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(variant);
                }
            }
            variants.Clear();
        }

        /// <summary>Applies the effective three-level UI policy and refreshes every registered renderer when it changes.</summary>
        /// <param name="mode">Whether no UI, only explicit background panels, or all quality-aware UI uses outlined alpha artwork.</param>
        internal static void Configure(UiOutlineMode mode)
        {
            if (s_mode == mode)
            {
                return;
            }
            s_mode = mode;
            s_verifiedSprites.Clear();
            for (var index = s_images.Count - 1; index >= 0; index--)
            {
                var image = s_images[index];
                if (image != null)
                {
                    image.RefreshOutline();
                }
                else
                {
                    s_images.RemoveAt(index);
                }
            }
        }

        /// <summary>Refreshes registered material selection without rebuilding atlas geometry or layout.</summary>
        internal static void RefreshMaterials()
        {
            for (var index = s_images.Count - 1; index >= 0; index--)
            {
                var image = s_images[index];
                if (image != null)
                {
                    image.RefreshMaterialSelection();
                }
                else
                {
                    s_images.RemoveAt(index);
                }
            }
        }

        /// <summary>Registers an enabled image and applies the current texture/material selection.</summary>
        /// <param name="image">The enabled image to refresh on later style changes.</param>
        internal static void Register(QualityUiImage image)
        {
            s_images.Add(image);
            image.RefreshOutline();
        }

        /// <summary>Stops retaining a disabled or destroyed image.</summary>
        /// <param name="image">The image leaving the active UI hierarchy.</param>
        internal static void Unregister(QualityUiImage image)
        {
            s_images.Remove(image);
        }

        /// <summary>Resolves the current sprite, including Button SpriteSwap overrides, to matching outlined artwork.</summary>
        /// <param name="source">The original active sprite.</param>
        /// <param name="outlined">The matching outlined sprite when its texture can reuse the original UVs.</param>
        /// <returns>Whether outlined artwork is enabled and the complete paired resources are usable.</returns>
        internal static bool TryGetOutlinedSprite(Sprite? source, out Sprite outlined)
        {
            return TryGetOutlinedSprite(source, s_mode == UiOutlineMode.All, out outlined);
        }

        /// <summary>Resolves artwork from the authored composition without requiring per-image background markers.</summary>
        /// <param name="image">The actual image, including its current Sprite Swap state.</param>
        /// <param name="outlined">The matching outlined Sprite when the requested resources are valid.</param>
        /// <returns>Whether this particular image should sample the paired outlined atlas.</returns>
        internal static bool TryGetOutlinedSprite(QualityUiImage image, out Sprite outlined)
        {
            var enabled = s_mode == UiOutlineMode.All ||
                s_mode == UiOutlineMode.Performance &&
                UsesAlphaCompositing(image.SourceMaterial);
            return TryGetOutlinedSprite(image.overrideSprite, enabled, out outlined);
        }

        /// <summary>Checks the shared atlas contract for a requested artwork style.</summary>
        /// <param name="source">The original Sprite.</param>
        /// <param name="enabled">Whether this caller requests outlined artwork.</param>
        /// <param name="outlined">The paired Sprite when the atlas contract is valid.</param>
        /// <returns>Whether paired artwork can safely reuse the original sampling coordinates.</returns>
        private static bool TryGetOutlinedSprite(Sprite? source, bool enabled, out Sprite outlined)
        {
            outlined = null!;
            if (!enabled || source == null)
            {
                return false;
            }
            var texture = source.texture;
            if (texture == null)
            {
                return false;
            }
            if (s_outlinedSprites != null && s_alphaTemplate != null && s_verifiedSprites.TryGetValue(source, out var verified) &&
                verified.SourceTexture == texture && verified.Outlined != null && verified.OutlinedTexture != null &&
                !source.packed && !verified.Outlined.packed &&
                texture.width == verified.Size.x && texture.height == verified.Size.y &&
                verified.OutlinedTexture.width == verified.Size.x && verified.OutlinedTexture.height == verified.Size.y)
            {
                outlined = verified.Outlined;
                return true;
            }
            s_verifiedSprites.Remove(source);
            if (texture.name != BaseAtlasName)
            {
                return false;
            }
            LoadResources();
            if (s_alphaTemplate == null || !s_outlinedSprites!.TryGetValue(source.name, out var candidate) ||
                source.packed || candidate.packed || source.rect != candidate.rect ||
                source.pivot != candidate.pivot || source.border != candidate.border ||
                source.pixelsPerUnit != candidate.pixelsPerUnit ||
                source.texture.width != candidate.texture.width || source.texture.height != candidate.texture.height)
            {
                ReportResourceError();
                return false;
            }
            outlined = candidate;
            if (s_verifiedSprites.Count >= MaximumVerifiedSprites)
            {
                s_verifiedSprites.Clear();
            }
            s_verifiedSprites[source] = new OutlinedMapping(texture, candidate, candidate.texture,
                new Vector2Int(texture.width, texture.height));
            return true;
        }

        /// <summary>Reloads paired artwork after asset imports without changing the selected UI mode.</summary>
        internal static void InvalidateSpriteMappings()
        {
            s_verifiedSprites.Clear();
            s_outlinedSprites = null;
            s_alphaTemplate = null;
            s_coverAlphaTemplate = null;
            s_backgroundCutoffTemplate = null;
            s_reportedResourceError = false;
            for (var index = s_images.Count - 1; index >= 0; index--)
            {
                var image = s_images[index];
                if (image != null)
                {
                    image.RefreshOutline();
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>Observes Editor asset changes rather than revalidating immutable import metadata every frame.</summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void ObserveProjectChanges()
        {
            UnityEditor.EditorApplication.projectChanged -= InvalidateSpriteMappings;
            UnityEditor.EditorApplication.projectChanged += InvalidateSpriteMappings;
        }
#endif

        /// <summary>Retains one verified imported pair and the dimensions that must remain compatible.</summary>
        private readonly struct OutlinedMapping
        {
            /// <summary>Gets the source atlas object.</summary>
            internal Texture2D SourceTexture { get; }
            /// <summary>Gets the matching outlined Sprite.</summary>
            internal Sprite Outlined { get; }
            /// <summary>Gets the paired atlas object.</summary>
            internal Texture2D OutlinedTexture { get; }
            /// <summary>Gets the verified source and destination dimensions.</summary>
            internal Vector2Int Size { get; }

            /// <summary>Stores an already verified pair without repeating name or geometry queries.</summary>
            /// <param name="sourceTexture">The original atlas.</param>
            /// <param name="outlined">The matching outlined Sprite.</param>
            /// <param name="outlinedTexture">The paired atlas.</param>
            /// <param name="size">The matching imported texture dimensions.</param>
            internal OutlinedMapping(Texture2D sourceTexture, Sprite outlined, Texture2D outlinedTexture, Vector2Int size)
            {
                SourceTexture = sourceTexture;
                Outlined = outlined;
                OutlinedTexture = outlinedTexture;
                Size = size;
            }
        }

        /// <summary>Applies the UI mode to supported authored alpha images and ordinary cutout artwork.</summary>
        /// <param name="original">The authored material, which is never changed.</param>
        /// <returns>The background cutoff variant or the existing ordinary UI material selection.</returns>
        internal static Material GetMaterial(Material? original)
        {
            if (s_mode == UiOutlineMode.None)
            {
                if (original != null && original.shader.name == RoundedRectShaderName)
                {
                    return GetRoundedRectCutoffMaterial(original);
                }
                if (IsSupportedAlphaMaterial(original))
                {
                    return GetSpriteCutoffMaterial(original);
                }
            }
            return GetAlphaMaterial(original);
        }

        /// <summary>Recognizes the supported authored alpha paths before a mode selects any runtime variant.</summary>
        /// <param name="material">The original material, without quality or stencil substitutions.</param>
        /// <returns>Whether Mixed Transparency should retain blending and request paired outlined artwork.</returns>
        private static bool UsesAlphaCompositing(Material? material)
        {
            return IsSupportedAlphaMaterial(material) ||
                material != null && material.shader.name == RoundedRectShaderName;
        }

        /// <summary>Recognizes ordinary sprite alpha shaders whose geometry and masking the cutoff template preserves.</summary>
        /// <param name="material">The authored material, before any quality-dependent substitution.</param>
        /// <returns>Whether a plain UI alpha image can safely use the outlined or non-blended path.</returns>
        private static bool IsSupportedAlphaMaterial(Material? material)
        {
            var source = material != null ? material : UnityEngine.UI.Graphic.defaultGraphicMaterial;
            var shaderName = source.shader.name;
            return shaderName == "Maj/UI/Default" || shaderName == "UI/Default" || shaderName == "Maj/UI/Atlas Alpha";
        }

        /// <summary>Creates a non-blended rounded-panel variant without replacing its procedural geometry shader.</summary>
        /// <param name="original">The unchanged authored rounded-rectangle material.</param>
        /// <returns>The shared opaque variant retaining the original radii, tint, masking and render order.</returns>
        private static Material GetRoundedRectCutoffMaterial(Material original)
        {
            if (s_cutoffMaterials.TryGetValue(original, out var cached) && cached != null)
            {
                return cached;
            }
            var variant = new Material(original)
            {
                name = original.name + " (Opaque UI)",
                hideFlags = HideFlags.HideAndDontSave
            };
            variant.SetFloat("_MajUIOpaque", 1f);
            variant.SetFloat("_MajUISrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            variant.SetFloat("_MajUIDstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            s_cutoffMaterials[original] = variant;
            return variant;
        }

        /// <summary>Creates a shared binary-compositing variant for supported original sprite alpha shaders.</summary>
        /// <param name="original">The panel's original alpha material, or Unity's default when absent.</param>
        /// <returns>The cached cutoff material, or the original material when a safe conversion is unavailable.</returns>
        private static Material GetSpriteCutoffMaterial(Material? original)
        {
            if (original == null)
            {
                original = UnityEngine.UI.Graphic.defaultGraphicMaterial;
            }
            if (!IsSupportedAlphaMaterial(original))
            {
                return original;
            }
            if (s_cutoffMaterials.TryGetValue(original, out var cached) && cached != null)
            {
                return cached;
            }
            LoadResources();
            if (s_backgroundCutoffTemplate == null)
            {
                ReportResourceError();
                return original;
            }
            var variant = new Material(s_backgroundCutoffTemplate)
            {
                name = original.name + " (Background Cutoff)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = original.enableInstancing,
                renderQueue = original.renderQueue
            };
            variant.SetTexture("_MainTex", original.GetTexture("_MainTex"));
            variant.SetTextureScale("_MainTex", original.GetTextureScale("_MainTex"));
            variant.SetTextureOffset("_MainTex", original.GetTextureOffset("_MainTex"));
            variant.SetColor("_Color", original.GetColor("_Color"));
            CopyBackgroundFloat(original, variant, "_ListDepthOffset");
            CopyBackgroundFloat(original, variant, "_StencilComp");
            CopyBackgroundFloat(original, variant, "_Stencil");
            CopyBackgroundFloat(original, variant, "_StencilOp");
            CopyBackgroundFloat(original, variant, "_StencilReadMask");
            CopyBackgroundFloat(original, variant, "_StencilWriteMask");
            CopyBackgroundFloat(original, variant, "_ColorMask");
            s_cutoffMaterials[original] = variant;
            return variant;
        }

        /// <summary>Copies only compatible optional state, retaining template defaults for absent properties.</summary>
        /// <param name="source">The untouched original panel material.</param>
        /// <param name="destination">The owned background cutoff variant.</param>
        /// <param name="property">The compatible property name.</param>
        private static void CopyBackgroundFloat(Material source, Material destination, string property)
        {
            if (source.HasProperty(property))
            {
                destination.SetFloat(property, source.GetFloat(property));
            }
        }

        /// <summary>Returns a shared alpha variant for opted-in cutout UI, including non-atlas circles and icons.</summary>
        /// <param name="original">The original Image material, which may be null or destroyed and is never modified.</param>
        /// <returns>The matching alpha material, the unchanged transparent material, or Unity's default UI material when absent.</returns>
        internal static Material GetAlphaMaterial(Material? original)
        {
            if (original == null)
            {
                return UnityEngine.UI.Graphic.defaultGraphicMaterial;
            }
            if (s_mode != UiOutlineMode.All)
            {
                return original;
            }
            var isCover = original.shader.name == CoverShaderName;
            if (!isCover && original.shader.name != CutoutShaderName)
            {
                return original;
            }
            if (s_alphaMaterials.TryGetValue(original, out var variant) && variant != null)
            {
                return variant;
            }
            LoadResources();
            var template = isCover ? s_coverAlphaTemplate : s_alphaTemplate;
            if (template == null)
            {
                ReportResourceError();
                return original;
            }
            variant = new Material(template)
            {
                name = original.name + " (Smooth UI)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = original.enableInstancing
            };
            // Copy only properties with the same meaning in both shaders. CopyPropertiesFromMaterial
            // replaces the destination property sheet, losing the alpha template's stencil/color-write
            // defaults when the source Opaque material has never declared those properties.
            variant.SetTexture("_MainTex", original.GetTexture("_MainTex"));
            variant.SetTextureScale("_MainTex", original.GetTextureScale("_MainTex"));
            variant.SetTextureOffset("_MainTex", original.GetTextureOffset("_MainTex"));
            variant.SetColor("_Color", original.GetColor("_Color"));
            variant.SetFloat("_ListDepthOffset", original.GetFloat("_ListDepthOffset"));
            if (isCover)
            {
                variant.SetVector("_Center", original.GetVector("_Center"));
                variant.SetFloat("_Radius", original.GetFloat("_Radius"));
                variant.SetFloat("_Feather", original.GetFloat("_Feather"));
                variant.SetFloat("_Brightness", original.GetFloat("_Brightness"));
                variant.SetFloat("_StencilComp", original.GetFloat("_StencilComp"));
                variant.SetFloat("_Stencil", original.GetFloat("_Stencil"));
                variant.SetFloat("_StencilOp", original.GetFloat("_StencilOp"));
                variant.SetFloat("_StencilReadMask", original.GetFloat("_StencilReadMask"));
                variant.SetFloat("_StencilWriteMask", original.GetFloat("_StencilWriteMask"));
                variant.SetFloat("_ColorMask", original.GetFloat("_ColorMask"));
            }
            s_alphaMaterials[original] = variant;
            return variant;
        }

        /// <summary>Loads templates independently of atlas replacement so standalone UI can also select smooth materials.</summary>
        private static void LoadResources()
        {
            if (s_outlinedSprites != null)
            {
                return;
            }
            var resources = Resources.Load<UiOutlineResources>(OutlineResourcesPath);
            var sprites = resources != null ? resources.OutlinedSprites : null;
            s_outlinedSprites = new Dictionary<string, Sprite>(sprites?.Count ?? 0, StringComparer.Ordinal);
            if (sprites != null)
            {
                foreach (var sprite in sprites)
                {
                    if (sprite != null)
                    {
                        s_outlinedSprites.Add(sprite.name, sprite);
                    }
                }
            }
            s_alphaTemplate = resources != null ? resources.AlphaMaterial : null;
            s_coverAlphaTemplate = resources != null ? resources.CoverAlphaMaterial : null;
            s_backgroundCutoffTemplate = resources != null ? resources.BackgroundCutoffMaterial : null;
        }

        /// <summary>Reports a broken resource pair once while safely retaining original UI rendering.</summary>
        private static void ReportResourceError()
        {
            if (s_reportedResourceError)
            {
                return;
            }
            s_reportedResourceError = true;
            MajDebug.LogError(nameof(UiOutlineTheme), "Outlined UI resources are missing or their sprite layout differs from the base atlas. Keeping original UI artwork.");
        }
    }
}

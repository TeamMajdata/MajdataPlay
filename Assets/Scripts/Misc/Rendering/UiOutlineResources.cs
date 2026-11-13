using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>References optional outlined artwork in its authored folder for lazy runtime loading.</summary>
    public sealed class UiOutlineResources : ScriptableObject
    {
        /// <summary>Retains the outlined slices with the same names and layout as the original atlas.</summary>
        [SerializeField]
        [FormerlySerializedAs("OutlinedSprites")]
        private Sprite[] _outlinedSprites = Array.Empty<Sprite>();

        /// <summary>References the included alpha-blended template without relying on Shader.Find.</summary>
        [SerializeField]
        [FormerlySerializedAs("AlphaMaterial")]
        private Material? _alphaMaterial;

        /// <summary>References the alpha-blended circle template used by List cover and badge images.</summary>
        [SerializeField]
        [FormerlySerializedAs("CoverAlphaMaterial")]
        private Material? _coverAlphaMaterial;

        /// <summary>References the mask-aware, non-blended material used by energy-saving List panels.</summary>
        [SerializeField]
        [FormerlySerializedAs("BackgroundCutoffMaterial")]
        private Material? _backgroundCutoffMaterial;

        /// <summary>References the three-pass quarter-size PV Gaussian template.</summary>
        [SerializeField]
        [FormerlySerializedAs("PvBlurMaterial")]
        private Material? _pvBlurMaterial;

        /// <summary>References the mask-aware PV glass surface used by explicit menu panels.</summary>
        [SerializeField]
        [FormerlySerializedAs("PvGlassMaterial")]
        private Material? _pvGlassMaterial;

        /// <summary>Gets the outlined slices whose names identify the corresponding original sprites.</summary>
        public IReadOnlyList<Sprite> OutlinedSprites => _outlinedSprites;

        /// <summary>Gets the template used to create shared alpha-blended UI material variants.</summary>
        public Material? AlphaMaterial => _alphaMaterial;

        /// <summary>Gets the circle template that retains List depth, brightness and mask properties.</summary>
        public Material? CoverAlphaMaterial => _coverAlphaMaterial;

        /// <summary>Gets the cutoff template used only by explicitly identified List background panels.</summary>
        public Material? BackgroundCutoffMaterial => _backgroundCutoffMaterial;

        /// <summary>Gets the shared PV low-pass and separable Gaussian template.</summary>
        public Material? PvBlurMaterial => _pvBlurMaterial;

        /// <summary>Gets the PV glass template retaining UGUI mask and clipping contracts.</summary>
        public Material? PvGlassMaterial => _pvGlassMaterial;
    }
}

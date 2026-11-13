using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Retains the original atlas sprite geometry independently of expanded artwork slices.</summary>
    public sealed class UiAtlasLayout : ScriptableObject
    {
        /// <summary>Stores the original imported sprites as local subassets sharing the authored source texture.</summary>
        [SerializeField]
        [FormerlySerializedAs("LegacySprites")]
        private Sprite[] _legacySprites = Array.Empty<Sprite>();

        /// <summary>Records the imported texture dimensions used by the preserved sprite coordinates.</summary>
        [SerializeField]
        [FormerlySerializedAs("SourceTextureSize")]
        private Vector2Int _sourceTextureSize;

        /// <summary>Stores the original normalized outer UV rectangle at the matching legacy-sprite index.</summary>
        [SerializeField]
        [FormerlySerializedAs("OriginalOuterUVs")]
        private Vector4[] _originalOuterUVs = Array.Empty<Vector4>();

        /// <summary>Gets the original logical sprites used for native UGUI layout and mesh generation.</summary>
        public IReadOnlyList<Sprite> LegacySprites => _legacySprites;

        /// <summary>Gets the imported texture size whose pixel coordinates the legacy sprites use.</summary>
        public Vector2Int SourceTextureSize => _sourceTextureSize;

        /// <summary>Gets the original normalized outer UV rectangles independently of later texture imports.</summary>
        public IReadOnlyList<Vector4> OriginalOuterUVs => _originalOuterUVs;
    }
}

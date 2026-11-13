using System;
using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Serialization;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Stores source-space glass contours and the shared bounded optical profile.</summary>
    public sealed class UiGlassOpticsAtlas : ScriptableObject
    {
        /// <summary>Identifies the generated Resources asset.</summary>
        internal const string ResourcePath = "Rendering/UI/GlassOpticsAtlas";

        /// <summary>Identifies manually encoded RGBA8 distances sampled with native bilinear filtering.</summary>
        internal const int CurrentEncodingVersion = 7;

        /// <summary>Caches the generated resource without allocating during UI geometry updates.</summary>
        private static UiGlassOpticsAtlas? s_cached;

        /// <summary>Caches device capabilities outside animated panel updates.</summary>
        private static bool s_capabilitiesInitialized;
        /// <summary>Allows sampling the uncompressed RGBA8 contour field.</summary>
        private static bool s_contourSampleSupported;
        /// <summary>Allows hardware bilinear filtering independently of format sampling support.</summary>
        private static bool s_contourLinearSupported;
        /// <summary>Allows the optical profile independently of contour filtering support.</summary>
        private static bool s_profileSupported;
        /// <summary>Bounds both generated textures to the current device's supported size.</summary>
        private static int s_maximumTextureSize;

        /// <summary>Rejects older storage layouts and fields that blur internal clipping creases during interpolation.</summary>
        [SerializeField]
        [FormerlySerializedAs("EncodingVersion")]
        private int _encodingVersion;

        /// <summary>Stores manually encoded source distances independently of artwork texture quality.</summary>
        [SerializeField]
        [FormerlySerializedAs("DistanceTexture")]
        private Texture2D? _distanceTexture;

        /// <summary>Stores displacement divided by bevel width and the curved surface's edge weight.</summary>
        [SerializeField]
        [FormerlySerializedAs("ProfileTexture")]
        private Texture2D? _profileTexture;

        /// <summary>Maps each distinct authored contour into its packed distance tile.</summary>
        [SerializeField]
        [FormerlySerializedAs("Entries")]
        private Entry[] _entries = Array.Empty<Entry>();

        /// <summary>Gets the supported linear-color, uncompressed, mip-free RGBA8 contour texture with bilinear filtering.</summary>
        internal Texture2D? DistanceTexture
        {
            get
            {
                EnsureCapabilities();
                if (!s_contourSampleSupported || !s_contourLinearSupported || _encodingVersion != CurrentEncodingVersion ||
                    !IsValidTexture(_distanceTexture, TextureFormat.RGBA32))
                {
                    return null;
                }
                // Filtering is part of the data contract. Unsupported devices retain the
                // glass color composition without computing substitute refraction.
                if (_distanceTexture!.filterMode != FilterMode.Bilinear)
                {
                    _distanceTexture.filterMode = FilterMode.Bilinear;
                }
                return _distanceTexture;
            }
        }

        /// <summary>Gets whether reported capabilities permit RGBA8 hardware filtering; driver behavior still needs device validation.</summary>
        internal static bool SupportsContourFiltering
        {
            get
            {
                EnsureCapabilities();
                return s_contourSampleSupported && s_contourLinearSupported;
            }
        }

        /// <summary>Gets the linear, mip-free optical profile.</summary>
        internal Texture2D? ProfileTexture
        {
            get
            {
                EnsureCapabilities();
                return s_profileSupported && _encodingVersion == CurrentEncodingVersion &&
                    IsValidTexture(_profileTexture, TextureFormat.RGHalf) ? _profileTexture : null;
            }
        }

        /// <summary>Loads compatible baked data, or returns null so color composition remains available.</summary>
        /// <returns>The generated atlas when either its profile or contour can be used independently.</returns>
        internal static UiGlassOpticsAtlas? Load()
        {
            EnsureCapabilities();
            if ((!s_contourSampleSupported || !s_contourLinearSupported) && !s_profileSupported)
            {
                return null;
            }
            if (s_cached == null)
            {
                s_cached = Resources.Load<UiGlassOpticsAtlas>(ResourcePath);
            }
            if (s_cached == null || (s_cached.ProfileTexture == null && s_cached.DistanceTexture == null))
            {
                return null;
            }
            return s_cached;
        }

        /// <summary>Forgets the borrowed resource when the rendering service resets.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetCache()
        {
            s_cached = null;
            s_capabilitiesInitialized = false;
        }

        /// <summary>Queries independent format capabilities once per subsystem lifetime.</summary>
        private static void EnsureCapabilities()
        {
            if (s_capabilitiesInitialized)
            {
                return;
            }
            s_contourSampleSupported = SystemInfo.SupportsTextureFormat(TextureFormat.RGBA32) &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.Sample);
            var nativeLinear = SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.Linear);
            s_contourLinearSupported = SupportsFiltering(s_contourSampleSupported, nativeLinear, SystemInfo.graphicsShaderLevel);
            s_profileSupported = SystemInfo.SupportsTextureFormat(TextureFormat.RGHalf) &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, GraphicsFormatUsage.Sample | GraphicsFormatUsage.Linear);
            s_maximumTextureSize = SystemInfo.maxTextureSize;
            s_capabilitiesInitialized = true;
            MajDebug.LogInfo(nameof(UiGlassOpticsAtlas),
                $"API={SystemInfo.graphicsDeviceType} ShaderLevel={SystemInfo.graphicsShaderLevel} " +
                $"RGBA8Sample={s_contourSampleSupported} RGBA8Linear={nativeLinear} " +
                $"ProfileSampleLinear={s_profileSupported} FilteredContourEligible={s_contourLinearSupported}; " +
                "reported capabilities, not a GPU self-test.");
        }

        /// <summary>Requires format sampling, hardware filtering and the optical shader's baseline without restricting operating systems.</summary>
        /// <param name="sampleSupported">Whether RGBA8 UNORM sampling is supported.</param>
        /// <param name="linearSupported">Whether the same format supports hardware linear filtering.</param>
        /// <param name="shaderLevel">The reported shader capability level.</param>
        /// <returns>True when the ordinary target-3.0 refraction program can use its data texture.</returns>
        internal static bool SupportsFiltering(bool sampleSupported, bool linearSupported, int shaderLevel)
        {
            return sampleSupported && linearSupported && shaderLevel >= 30;
        }

        /// <summary>Checks generated data without coupling the two independent optical resources.</summary>
        /// <param name="texture">The generated texture to validate.</param><param name="format">Its required storage format.</param>
        /// <returns>True when format and positive dimensions are supported by the current device.</returns>
        private static bool IsValidTexture(Texture2D? texture, TextureFormat format)
        {
            if (texture == null || texture.format != format)
            {
                return false;
            }
            var width = texture.width;
            var height = texture.height;
            return width > 0 && height > 0 && width <= s_maximumTextureSize && height <= s_maximumTextureSize &&
                texture.mipmapCount == 1 && !GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
        }

        /// <summary>Combines the existing atlas UV mapping with one matching source-distance tile.</summary>
        /// <param name="contour">The exact current authored source-space geometry.</param>
        /// <param name="sourceUvToLocal">The existing Sprite UV to original centered source-coordinate mapping.</param>
        /// <param name="uvMapping">The scale and offset mapping the unchanged Sprite UV into the distance atlas.</param>
        /// <param name="uvClamp">The tile's safe minimum and maximum UV sample centers.</param>
        /// <param name="decode">Receives the positive distance scale and signed source-unit offset.</param>
        /// <returns>True when matching generated data is available; false disables refraction.</returns>
        internal bool TryResolve(in Contour contour, Vector4 sourceUvToLocal, out Vector4 uvMapping, out Vector4 uvClamp, out Vector2 decode)
        {
            uvMapping = default;
            uvClamp = default;
            decode = default;
            if (DistanceTexture == null)
            {
                return false;
            }
            foreach (var entry in _entries)
            {
                if (!entry.Contour.Matches(contour))
                {
                    continue;
                }
                var parameters = entry.Decode;
                if (!(parameters.x > 0f) || float.IsNaN(parameters.x) || float.IsInfinity(parameters.x) ||
                    float.IsNaN(parameters.y) || float.IsInfinity(parameters.y))
                {
                    return false;
                }
                var tile = entry.Mapping;
                uvMapping = new Vector4(sourceUvToLocal.x * tile.x, sourceUvToLocal.y * tile.y,
                    sourceUvToLocal.z * tile.x + tile.z, sourceUvToLocal.w * tile.y + tile.w);
                uvClamp = entry.Clamp;
                decode = parameters;
                return true;
            }
            return false;
        }

        /// <summary>Assigns generated subassets without exposing writable runtime geometry.</summary>
        /// <param name="distance">The packed source-distance texture.</param>
        /// <param name="profile">The shared optical profile texture.</param>
        /// <param name="entries">The distinct packed contour descriptors.</param>
        internal void Initialize(Texture2D distance, Texture2D profile, Entry[] entries)
        {
            _encodingVersion = CurrentEncodingVersion;
            _distanceTexture = distance;
            _profileTexture = profile;
            _entries = entries;
        }

        /// <summary>Describes one contour using the same original Sprite coordinates as the production shader.</summary>
        [Serializable]
        internal struct Contour
        {
            /// <summary>Identifies the original atlas Sprite independently of shadow artwork.</summary>
            [SerializeField]
            private string _spriteName;
            /// <summary>Stores the full original Sprite dimensions.</summary>
            [SerializeField]
            private Vector2 _size;
            /// <summary>Stores body center and half size.</summary>
            [SerializeField]
            private Vector4 _rect;
            /// <summary>Stores top-left, top-right, bottom-right and bottom-left corner radii.</summary>
            [SerializeField]
            private Vector4 _corners;
            /// <summary>Selects ellipse and clipped-ellipse contours respectively.</summary>
            [SerializeField]
            private Vector2 _shape;
            /// <summary>Stores the body-relative ellipse center and its horizontal and vertical radii.</summary>
            [SerializeField]
            private Vector4 _circle;
            /// <summary>Stores the polynomial intersection blend width in original source units.</summary>
            [SerializeField]
            private float _clipSmoothness;

            /// <summary>Selects offline artwork-alpha distance baking above this threshold; zero selects the analytic shape.</summary>
            [SerializeField]
            private float _alphaContourThreshold;

            /// <summary>Creates a descriptor from production geometry, without introducing a second layout policy.</summary>
            /// <param name="spriteName">The authored Sprite name.</param><param name="size">The original Sprite dimensions.</param>
            /// <param name="rect">The centered body rectangle.</param><param name="corners">The four corner radii.</param>
            /// <param name="shape">The ellipse and clipped-ellipse selectors.</param><param name="circle">The independent ellipse geometry.</param>
            /// <param name="clipSmoothness">The intersection blend width.</param>
            /// <param name="alphaContourThreshold">The optional artwork-alpha threshold used only during offline contour baking.</param>
            internal Contour(string spriteName, Vector2 size, Vector4 rect, Vector4 corners, Vector2 shape, Vector4 circle, float clipSmoothness,
                float alphaContourThreshold = 0f)
            {
                _spriteName = spriteName;
                _size = size;
                _rect = rect;
                _corners = corners;
                _shape = shape;
                _circle = circle;
                _clipSmoothness = clipSmoothness;
                _alphaContourThreshold = alphaContourThreshold;
            }
            /// <summary>Gets the authored Sprite name.</summary>
            internal string SpriteName => _spriteName;
            /// <summary>Gets the original Sprite dimensions.</summary>
            internal Vector2 Size => _size;
            /// <summary>Gets the body center and half size.</summary>
            internal Vector4 Rect => _rect;
            /// <summary>Gets the four body corner radii.</summary>
            internal Vector4 Corners => _corners;
            /// <summary>Gets the ellipse and clipped-ellipse selectors.</summary>
            internal Vector2 Shape => _shape;
            /// <summary>Gets the body-relative ellipse center and radii.</summary>
            internal Vector4 Circle => _circle;
            /// <summary>Gets the source-space intersection blend width.</summary>
            internal float ClipSmoothness => _clipSmoothness;
            /// <summary>Gets the offline alpha threshold, or zero for an analytic contour.</summary>
            internal float AlphaContourThreshold => _alphaContourThreshold;

            /// <summary>Matches serialized descriptors while tolerating insignificant serialization roundoff.</summary>
            /// <param name="other">The runtime production descriptor.</param>
            /// <returns>True when every shape parameter describes the same contour.</returns>
            internal bool Matches(in Contour other)
            {
                const float tolerance = 1e-6f;
                return string.Equals(_spriteName, other._spriteName, StringComparison.Ordinal) &&
                    (_size - other._size).sqrMagnitude <= tolerance && (_rect - other._rect).sqrMagnitude <= tolerance &&
                    (_corners - other._corners).sqrMagnitude <= tolerance && (_shape - other._shape).sqrMagnitude <= tolerance &&
                    (_circle - other._circle).sqrMagnitude <= tolerance && Mathf.Abs(_clipSmoothness - other._clipSmoothness) <= 0.001f &&
                    Mathf.Abs(_alphaContourThreshold - other._alphaContourThreshold) <= tolerance;
            }
        }

        /// <summary>Associates an immutable descriptor with its packed source-coordinate mapping.</summary>
        [Serializable]
        internal struct Entry
        {
            /// <summary>Stores the authored source contour.</summary>
            [SerializeField]
            private Contour _contour;
            /// <summary>Stores source-space scale and offset into the packed texture.</summary>
            [SerializeField]
            private Vector4 _mapping;
            /// <summary>Stores the minimum and maximum safe tile sample centers.</summary>
            [SerializeField]
            private Vector4 _clamp;
            /// <summary>Stores the decoded distance scale and signed offset in original Sprite units.</summary>
            [SerializeField]
            private Vector2 _decode;
            /// <summary>Gets the authored source contour.</summary>
            internal Contour Contour => _contour;
            /// <summary>Gets the source-to-packed mapping.</summary>
            internal Vector4 Mapping => _mapping;
            /// <summary>Gets the packed tile's safe UV bounds.</summary>
            internal Vector4 Clamp => _clamp;
            /// <summary>Gets the linear decoder's positive scale and source-unit offset.</summary>
            internal Vector2 Decode => _decode;
            /// <summary>Creates one packed contour entry.</summary>
            /// <param name="contour">The authored contour.</param><param name="mapping">The packed mapping.</param><param name="clamp">The safe UV bounds.</param>
            /// <param name="decode">The normalized-distance scale and source-unit offset.</param>
            internal Entry(Contour contour, Vector4 mapping, Vector4 clamp, Vector2 decode)
            {
                _contour = contour;
                _mapping = mapping;
                _clamp = clamp;
                _decode = decode;
            }
        }
    }
}

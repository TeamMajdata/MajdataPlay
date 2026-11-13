using UnityEngine.Scripting;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Selects UI artwork, compositing and backdrop optics independently of anti-aliasing and scene resolution.</summary>
    [Preserve]
    public enum UiOutlineMode
    {
        /// <summary>Uses original cutout UI and cutout rendering for explicitly identified List background panels.</summary>
        None = 0,

        /// <summary>Uses outlined alpha-blended background panels while retaining original cutout rendering for ordinary UI.</summary>
        Performance = 1,

        /// <summary>Uses outlined alpha-blended artwork for both ordinary quality-aware UI and background panels.</summary>
        All = 2,

        /// <summary>Uses outlined alpha-blended artwork with same-frame Gaussian-filtered backdrops.</summary>
        GaussianBlur = 3,

        /// <summary>Uses outlined alpha-blended artwork with same-frame liquid-glass backdrops.</summary>
        LiquidGlass = 4
    }
}

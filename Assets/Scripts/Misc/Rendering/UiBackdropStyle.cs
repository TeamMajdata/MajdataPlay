using UnityEngine.Scripting;
#nullable enable

namespace MajdataPlay.Rendering
{
    /// <summary>Selects the captured-background treatment applied to Refined UI surfaces.</summary>
    [Preserve]
    public enum UiBackdropStyle
    {
        /// <summary>Uses the filtered background with the authored UI color and existing soft edges.</summary>
        GaussianBlur = 0,

        /// <summary>Refracts shared scene-prefix snapshots with continuous glass reflections.</summary>
        LiquidGlass = 1
    }
}

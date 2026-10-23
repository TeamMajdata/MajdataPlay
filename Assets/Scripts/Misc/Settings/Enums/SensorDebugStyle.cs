using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>Specifies how touch sensor states are visualized.</summary>
    [Preserve]
    public enum SensorDebugStyle
    {
        /// <summary>Shades the entire sensor area lightly at rest and darkly when triggered.</summary>
        Brightness = 0,

        /// <summary>Draws a red inward border at rest and a green border when triggered.</summary>
        RedGreen = 1,

        /// <summary>Shades the entire sensor area dark blue at rest and dark red when triggered.</summary>
        Classic = 2
    }
}

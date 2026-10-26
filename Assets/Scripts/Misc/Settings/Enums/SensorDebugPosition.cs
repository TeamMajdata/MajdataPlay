using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>Specifies the display region used for touch sensor visualization.</summary>
    [Preserve]
    public enum SensorDebugPosition
    {
        /// <summary>Displays sensor visualization over the main gameplay screen.</summary>
        MainScreen = 0,

        /// <summary>Centers sensor visualization vertically within the secondary monitor.</summary>
        SubScreen = 1
    }
}

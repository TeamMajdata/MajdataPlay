#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the options of capacitive touch panels.
    /// </summary>
    public class CapacitiveTouchPanelOptions
    {
        /// <summary>
        /// Gets or sets the radius of a touch point in panel units.
        /// </summary>
        public int TouchRadius { get; set; } = 30;

        /// <summary>
        /// Gets or sets the extra radius added to the touch point for each sensor group.
        /// </summary>
        public CapacitiveTouchPanelRadiusOffsetConfig RadiusOffset { get; set; } = new()
        {
            A = 0,
            B = 20,
            C = 0,
            D = 0,
            E = 25
        };
    }
}

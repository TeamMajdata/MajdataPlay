#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the extra touch point radius of each sensor group on capacitive touch panels.
    /// </summary>
    public struct CapacitiveTouchPanelRadiusOffsetConfig
    {
        /// <summary>
        /// Gets or sets the extra radius for the A sensors.
        /// </summary>
        public int A { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the B sensors.
        /// </summary>
        public int B { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the C sensors.
        /// </summary>
        public int C { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the D sensors.
        /// </summary>
        public int D { get; set; }

        /// <summary>
        /// Gets or sets the extra radius for the E sensors.
        /// </summary>
        public int E { get; set; }
    }
}

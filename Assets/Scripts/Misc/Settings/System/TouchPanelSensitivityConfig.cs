#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the touch panel sensitivity of each sensor group.
    /// </summary>
    public struct TouchPanelSensitivityConfig
    {
        /// <summary>
        /// Gets or sets the sensitivity of the A sensors.
        /// </summary>
        public short A { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the B sensors.
        /// </summary>
        public short B { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the C sensors.
        /// </summary>
        public short C { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the D sensors.
        /// </summary>
        public short D { get; set; }

        /// <summary>
        /// Gets or sets the sensitivity of the E sensors.
        /// </summary>
        public short E { get; set; }
    }
}

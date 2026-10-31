#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the volumes of the speaker channel pairs of multichannel outputs.
    /// </summary>
    public class ChannelOptions
    {
        /// <summary>
        /// Gets or sets the volume of the front left and right channels.
        /// </summary>
        public float FrontVolume { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the center and LFE channels.
        /// </summary>
        public float CenterAndLFEVolume { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the side left and right channels.
        /// </summary>
        public float SideVolume { get; set; } = 1f;

        /// <summary>
        /// Gets or sets the volume of the rear left and right channels.
        /// </summary>
        public float RearVolume { get; set; } = 1f;
    }
}

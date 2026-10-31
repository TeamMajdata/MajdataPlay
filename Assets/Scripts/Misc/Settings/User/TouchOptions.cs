#nullable enable
namespace MajdataPlay.Settings
{
    public class TouchOptions
    {
        /// <summary>Gets or sets the simulated touch radius in sensor-space units.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchSimulationRadius { get; set; } = 0.5f;

        /// <summary>Gets or sets the additional hit radius for group A.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchAAreaExtraRadius { get; set; } = 0f;

        /// <summary>Gets or sets the additional hit radius for group B.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchBAreaExtraRadius { get; set; } = 0f;

        /// <summary>Gets or sets the additional hit radius for group C.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchCAreaExtraRadius { get; set; } = 0.25f;

        /// <summary>Gets or sets the additional hit radius for group D.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchDAreaExtraRadius { get; set; } = 0.2f;

        /// <summary>Gets or sets the additional hit radius for group E.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchEAreaExtraRadius { get; set; } = 0.10f;

        /// <summary>Gets or sets the global simulated touch-radius adjustment.</summary>
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchRadiusAdjust { get; set; } = 0f;
    }
}

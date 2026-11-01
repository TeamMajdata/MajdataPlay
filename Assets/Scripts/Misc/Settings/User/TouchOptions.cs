#nullable enable
namespace MajdataPlay.Settings
{
    public class TouchOptions
    {
        [Step("0.05")]
        [Range("0", "2")]
        public float TouchSimulationRadius { get; set; } = 0.5f;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchAAreaExtraRadius { get; set; } = 0f;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchBAreaExtraRadius { get; set; } = 0f;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchCAreaExtraRadius { get; set; } = 0.25f;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchDAreaExtraRadius { get; set; } = 0.2f;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchEAreaExtraRadius { get; set; } = 0.10f;

        [Step("0.05")]
        [Range("0", "2")]
        public float TouchRadiusAdjust { get; set; } = 0f;
    }
}

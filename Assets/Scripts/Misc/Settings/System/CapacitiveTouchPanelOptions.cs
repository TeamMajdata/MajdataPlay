#nullable enable
namespace MajdataPlay.Settings
{
    public class CapacitiveTouchPanelOptions
    {
        public int TouchRadius { get; set; } = 30;

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

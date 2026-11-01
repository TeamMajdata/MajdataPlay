#nullable enable
namespace MajdataPlay.Settings
{
    public class HidOptions
    {
        public string? DeviceName { get; set; } = null;

        public int? ProductId { get; set; } = null;

        public int? VendorId { get; set; } = null;

        public bool Exclusice { get; set; } = false;

        public HidOpenPriority OpenPriority { get; set; } = HidOpenPriority.VeryHigh;
    }
}

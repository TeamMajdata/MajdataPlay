#nullable enable
namespace MajdataPlay.Settings
{
    public class UsbOptions
    {
        public string? DeviceName { get; set; } = null;

        public int? ProductId { get; set; } = null;

        public int? VendorId { get; set; } = null;

        public bool Exclusice { get; set; } = false;
    }
}

#nullable enable
namespace MajdataPlay.Settings
{
    public class UsbOptions
    {
        public string? DeviceName { get; set; } = SettingDefaults.Usb.DeviceName;

        public int? ProductId { get; set; } = SettingDefaults.Usb.ProductId;

        public int? VendorId { get; set; } = SettingDefaults.Usb.VendorId;

        public bool Exclusice { get; set; } = SettingDefaults.Usb.Exclusice;
    }
}

#nullable enable
namespace MajdataPlay.Settings
{
    public class HidOptions
    {
        public string? DeviceName { get; set; } = SettingDefaults.Hid.DeviceName;

        public int? ProductId { get; set; } = SettingDefaults.Hid.ProductId;

        public int? VendorId { get; set; } = SettingDefaults.Hid.VendorId;

        public bool Exclusice { get; set; } = SettingDefaults.Hid.Exclusice;

        public HidOpenPriority OpenPriority { get; set; } = SettingDefaults.Hid.OpenPriority;
    }
}

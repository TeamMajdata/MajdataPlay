#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the identification and open options of a USB device.
    /// </summary>
    public class UsbOptions
    {
        /// <summary>
        /// Gets or sets the device name to match, or null for the default device.
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// Gets or sets the product ID to match, or null for the default device.
        /// </summary>
        public int? ProductId { get; set; } = null;

        /// <summary>
        /// Gets or sets the vendor ID to match, or null for the default device.
        /// </summary>
        public int? VendorId { get; set; } = null;

        /// <summary>
        /// Gets or sets whether the device is opened exclusively.
        /// </summary>
        public bool Exclusice { get; set; } = false;
    }
}

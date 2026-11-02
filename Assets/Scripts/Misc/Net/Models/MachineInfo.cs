#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The description of this machine that is sent to <c>POST machine/register</c> and kept in the system settings.</summary>
    public class MachineInfo
    {
        /// <summary>Gets the display name of the machine.</summary>
        public string? Name { get; init; } = "Private arcade";
        /// <summary>Gets the description of the machine.</summary>
        public string? Description { get; init; }
    }
}

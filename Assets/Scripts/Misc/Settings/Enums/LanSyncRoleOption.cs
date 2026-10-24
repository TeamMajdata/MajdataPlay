using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
    /// <summary>
    /// Role of this machine inside a LAN room.
    /// </summary>
    [Preserve]
    public enum LanSyncRoleOption
    {
        /// <summary>
        /// LAN start synchronization is off, the chart start flow is fully local.
        /// </summary>
        Disabled,
        /// <summary>
        /// This machine decides when the chart starts and broadcasts the instant.
        /// </summary>
        Host,
        /// <summary>
        /// This machine starts the chart at the instant broadcast by the host.
        /// </summary>
        Client
    }
}

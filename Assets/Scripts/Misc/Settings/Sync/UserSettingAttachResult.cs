#nullable enable
namespace MajdataPlay.Settings.Sync
{
    /// <summary>Describes the outcome of attaching the user settings to a logged-in account.</summary>
    internal enum UserSettingAttachResult
    {
        /// <summary>The endpoint does not store user settings, so nothing was exchanged.</summary>
        Skipped,

        /// <summary>The account holds settings, and they replaced the settings in memory.</summary>
        Attached,

        /// <summary>
        /// The account held no readable settings, so the settings in memory were uploaded as the copy of the account.
        /// </summary>
        Repaired,

        /// <summary>
        /// The copy of the account could not be read or written, so the settings in memory stay in use for this session.
        /// </summary>
        Degraded,
    }
}

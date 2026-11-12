#nullable enable
namespace MajdataPlay.Settings.Sync
{
    /// <summary>Describes what a user settings document received from an account could be read as.</summary>
    internal enum UserSettingReadResult
    {
        /// <summary>The document holds user settings.</summary>
        Ok,

        /// <summary>The document is absent or holds no settings, so the account has no copy yet.</summary>
        Empty,

        /// <summary>The document cannot be read as user settings.</summary>
        Invalid,
    }
}

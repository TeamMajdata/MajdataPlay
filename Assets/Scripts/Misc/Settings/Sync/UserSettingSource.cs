#nullable enable
namespace MajdataPlay.Settings.Sync
{
    /// <summary>Tells where the user settings in memory came from and where the next save has to be written.</summary>
    internal enum UserSettingSource
    {
        /// <summary>The settings came from settings.json, and saves write that file.</summary>
        Local,

        /// <summary>The settings came from the account, and saves are uploaded; settings.json is left untouched.</summary>
        Remote,

        /// <summary>A switch between the two copies is in progress, so a save writes neither of them.</summary>
        Switching,
    }
}

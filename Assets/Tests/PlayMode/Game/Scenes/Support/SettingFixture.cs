using MajdataPlay.Settings;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>
    /// A small settings category whose properties exercise the setting UI's metadata: default and overridden keys,
    /// suppressed descriptions, offset options and hidden properties.
    /// </summary>
    public sealed class SettingFixture
    {
        /// <summary>Gets or sets a boolean option with default keys.</summary>
        public bool Toggle { get; set; }

        /// <summary>Gets or sets an enum option with overridden name and description keys.</summary>
        [OptionName("FIXTURE_MODE_NAME")]
        [Description("FIXTURE_MODE_DESC")]
        public FixtureMode Mode { get; set; } = FixtureMode.First;

        /// <summary>Gets or sets a boolean option without a description.</summary>
        [NoDescription]
        public bool Quiet { get; set; }

        /// <summary>Gets or sets an offset option whose description includes the offset unit.</summary>
        public float AudioOffset { get; set; }

        /// <summary>Gets or sets a property hidden from the setting UI.</summary>
        [HideInSettingUI]
        public int Hidden { get; set; }
    }
}

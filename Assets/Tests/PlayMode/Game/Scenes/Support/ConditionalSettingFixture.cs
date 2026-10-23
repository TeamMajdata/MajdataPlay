using MajdataPlay.Settings;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>A settings category whose options are only shown while another option has a given value.</summary>
    public sealed class ConditionalSettingFixture
    {
        /// <summary>Gets or sets the switch that shows <see cref="Detail"/>.</summary>
        public bool Advanced { get; set; }

        /// <summary>Gets or sets an option shown while <see cref="Advanced"/> is set.</summary>
        [ShowInSettingUIWhen(nameof(Advanced), true)]
        public bool Detail { get; set; }

        /// <summary>Gets or sets the mode that shows <see cref="SecondOnly"/>.</summary>
        public FixtureMode Mode { get; set; } = FixtureMode.First;

        /// <summary>Gets or sets an option shown while <see cref="Mode"/> is <see cref="FixtureMode.Second"/>.</summary>
        [ShowInSettingUIWhen(nameof(Mode), FixtureMode.Second)]
        public bool SecondOnly { get; set; }
    }
}

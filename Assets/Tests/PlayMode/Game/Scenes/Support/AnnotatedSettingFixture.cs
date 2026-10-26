using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.Support
{
    /// <summary>A settings category whose option carries the translation selection attributes.</summary>
    public sealed class AnnotatedSettingFixture
    {
        /// <summary>Gets or sets an option annotated with every translation attribute.</summary>
        [UseTranslation(TranslationTarget.OptionName, "FIXTURE_CUSTOM_NAME")]
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        [NoTranslation(TranslationTarget.MenuName)]
        public bool Annotated { get; set; }
    }
}

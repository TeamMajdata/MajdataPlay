using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>A settings root with regular categories and an Audio category that lacks a volume page.</summary>
    internal sealed class SettingRootFixture
    {
        /// <summary>Gets a regular category.</summary>
        public SettingAnalyzerFixture Fixture { get; } = new();

        /// <summary>Gets a category whose title uses a custom key template.</summary>
        [UseTranslation(TranslationTarget.MenuName, "FX_MENU_{0}")]
        public SettingAnalyzerFixture Titled { get; } = new();

        /// <summary>Gets a category whose title is never translated.</summary>
        [NoTranslation(TranslationTarget.MenuName)]
        public SettingAnalyzerFixture Untitled { get; } = new();

        /// <summary>Gets an Audio category without the Volume property the setting UI expects.</summary>
        public SettingRootFixture? Audio { get; }
    }
}

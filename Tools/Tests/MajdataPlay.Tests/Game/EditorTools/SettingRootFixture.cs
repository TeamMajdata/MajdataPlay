#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>A settings root with one regular category and an Audio category that lacks a volume page.</summary>
    internal sealed class SettingRootFixture
    {
        /// <summary>Gets a regular category.</summary>
        public SettingAnalyzerFixture Fixture { get; } = new();

        /// <summary>Gets an Audio category without the Volume property the setting UI expects.</summary>
        public SettingRootFixture? Audio { get; }
    }
}

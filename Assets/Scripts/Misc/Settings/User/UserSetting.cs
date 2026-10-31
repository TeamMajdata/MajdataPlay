#nullable enable
namespace MajdataPlay.Settings
{
    public class UserSetting
    {
        public GameOptions Game { get; init; } = new();

        public JudgeOptions Judge { get; init; } = new();

        public DisplayOptions Display { get; init; } = new();

        public SFXVolume Volume { get; init; } = new();

        public TouchOptions Touch { get; init; } = new();
    }
}

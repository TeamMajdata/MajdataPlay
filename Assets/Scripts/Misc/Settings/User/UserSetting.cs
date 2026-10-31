using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings.User
{
    internal class UserSetting
    {
        public GameOptions Game { get; init; } = new();

        public JudgeOptions Judge { get; init; } = new();

        public DisplayOptions Display { get; init; } = new();

        public SFXVolume Volume { get; set; } = new();

        public TouchOptions Touch { get; set; } = new();
    }
}

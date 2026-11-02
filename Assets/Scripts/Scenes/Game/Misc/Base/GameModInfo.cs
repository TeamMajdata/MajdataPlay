using MajdataPlay.Settings;
using MajdataPlay.Scenes.Game.Notes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Scenes.Game
{
    public readonly struct GameModInfo
    {
        public float PlaybackSpeed { get; init; }
        public AutoplayModeOption AutoPlay { get; init; }
        public JudgeStyleOption JudgeStyle { get; init; }
        public bool SubdivideSlideJudgeGrade { get; init; }
        public bool AllBreak { get; init; }
        public bool AllEx { get; init; }
        public bool AllTouch { get; init; }
        public bool SlideNoHead { get; init; }
        public bool SlideNoTrack { get; init; }
        public bool ButtonRingForTouch { get; init; }
        public string NoteMask { get; init; }

        /// <summary>
        /// Creates the modifiers of a play from the session modifiers and the gameplay options.
        /// </summary>
        /// <param name="modOptions">The session modifiers to copy.</param>
        /// <param name="gameOptions">The gameplay options that supply <see cref="ButtonRingForTouch"/>.</param>
        public GameModInfo(ModOptions modOptions, GameOptions gameOptions)
        {
            PlaybackSpeed = modOptions.PlaybackSpeed;
            AutoPlay = modOptions.AutoPlay;
            JudgeStyle = modOptions.JudgeStyle;
            SubdivideSlideJudgeGrade = modOptions.SubdivideSlideJudgeGrade;
            AllBreak = modOptions.AllBreak;
            AllEx = modOptions.AllEx;
            AllTouch = modOptions.AllTouch;
            SlideNoHead = modOptions.SlideNoHead;
            SlideNoTrack = modOptions.SlideNoTrack;
            ButtonRingForTouch = gameOptions.ButtonRingForTouch;
            NoteMask = modOptions.NoteMask;
        }
        public GameModInfo(GameModInfo options)
        {
            PlaybackSpeed = options.PlaybackSpeed;
            AutoPlay = options.AutoPlay;
            JudgeStyle = options.JudgeStyle;
            SubdivideSlideJudgeGrade = options.SubdivideSlideJudgeGrade;
            AllBreak = options.AllBreak;
            AllEx = options.AllEx;
            AllTouch = options.AllTouch;
            SlideNoHead = options.SlideNoHead;
            SlideNoTrack = options.SlideNoTrack;
            ButtonRingForTouch = options.ButtonRingForTouch;
            NoteMask = options.NoteMask;
        }
    }
}

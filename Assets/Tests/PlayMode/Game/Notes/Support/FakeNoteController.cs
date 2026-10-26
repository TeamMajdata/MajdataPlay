using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// A note controller whose clock, modifiers and materials are set directly by tests instead of being driven by the
    /// gameplay manager and its audio timeline.
    /// </summary>
    internal sealed class FakeNoteController : INoteController
    {
        /// <summary>Gets or sets the chart time of the current frame in seconds.</summary>
        public float ThisFrameSec { get; set; }

        /// <summary>Gets or sets the length of the playing audio in seconds.</summary>
        public float AudioLength { get; set; } = 120f;

        /// <summary>Gets or sets a value indicating whether the chart playback has started.</summary>
        public bool IsStart { get; set; } = true;

        /// <summary>Gets a value indicating whether the modifiers request any autoplay mode.</summary>
        public bool IsAutoplay => ModInfo.AutoPlay != AutoplayModeOption.Disable;

        /// <summary>Gets or sets the modifiers the notes read during Awake.</summary>
        public GameModInfo ModInfo { get; set; } = new GameModInfo()
        {
            PlaybackSpeed = 1f,
            AutoPlay = AutoplayModeOption.Disable,
            JudgeStyle = JudgeStyleOption.DEFAULT,
            NoteMask = string.Empty
        };

        /// <summary>Gets or sets the grade autoplay assigns; values outside the grade range request random grades.</summary>
        public JudgeGrade AutoplayGrade { get; set; } = JudgeGrade.Perfect;

        /// <summary>Gets or sets the material used by break notes.</summary>
        public Material BreakMaterial { get; set; } = null!;

        /// <summary>Gets or sets the material used by ordinary notes.</summary>
        public Material DefaultMaterial { get; set; } = null!;

        /// <summary>Gets or sets the material used by pressed holds.</summary>
        public Material HoldShineMaterial { get; set; } = null!;

        /// <summary>Replaces the modifiers with ones that use the given autoplay mode and judge style.</summary>
        /// <param name="autoplay">The autoplay mode.</param>
        /// <param name="judgeStyle">The judge style.</param>
        /// <param name="subdivideSlideJudgeGrade">Whether slides keep their subdivided perfect grades.</param>
        /// <param name="buttonRingForTouch">Whether the button ring may judge outer touch notes.</param>
        public void SetMods(AutoplayModeOption autoplay,
                            JudgeStyleOption judgeStyle = JudgeStyleOption.DEFAULT,
                            bool subdivideSlideJudgeGrade = false,
                            bool buttonRingForTouch = false)
        {
            ModInfo = new GameModInfo()
            {
                PlaybackSpeed = 1f,
                AutoPlay = autoplay,
                JudgeStyle = judgeStyle,
                SubdivideSlideJudgeGrade = subdivideSlideJudgeGrade,
                ButtonRingForTouch = buttonRingForTouch,
                NoteMask = string.Empty
            };
        }
    }
}

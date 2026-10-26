using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// A settable <see cref="INoteController"/> that lets tests choose the playback position, audio length and mods
    /// seen by gameplay components without running a real <see cref="GamePlayManager"/>.
    /// </summary>
    internal sealed class GamePlayFakeNoteController : INoteController
    {
        /// <summary>Gets or sets the current playback position in seconds.</summary>
        public float ThisFrameSec { get; set; }

        /// <summary>Gets or sets the audio length in seconds.</summary>
        public float AudioLength { get; set; }

        /// <summary>Gets or sets a value indicating whether playback started.</summary>
        public bool IsStart { get; set; }

        /// <summary>Gets or sets a value indicating whether autoplay is active.</summary>
        public bool IsAutoplay { get; set; }

        /// <summary>Gets or sets the active mods.</summary>
        public GameModInfo ModInfo { get; set; }

        /// <summary>Gets or sets the autoplay grade.</summary>
        public JudgeGrade AutoplayGrade { get; set; } = JudgeGrade.Perfect;

        /// <summary>Gets the break material; always null.</summary>
        public Material BreakMaterial => null!;

        /// <summary>Gets the default material; always null.</summary>
        public Material DefaultMaterial => null!;

        /// <summary>Gets the hold shine material; always null.</summary>
        public Material HoldShineMaterial => null!;
    }
}

#nullable enable
namespace MajdataPlay.Scenes.SlideTest
{
    /// <summary>The state of the slide test clock.</summary>
    internal enum SlideTestState
    {
        /// <summary>No chart has been generated.</summary>
        Idle,
        /// <summary>A chart is being generated.</summary>
        Loading,
        /// <summary>The clock runs and the notes update every frame.</summary>
        Playing,
        /// <summary>The clock stands still; the notes update only when stepped.</summary>
        Paused,
        /// <summary>The latest generation failed.</summary>
        Error
    }
}

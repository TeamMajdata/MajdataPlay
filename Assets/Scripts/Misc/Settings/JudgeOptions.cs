using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Contains the judgment timing options.
    /// </summary>
    public class JudgeOptions
    {
        /// <summary>
        /// Gets or sets the offset that aligns the chart to the music in the selected offset unit; negative values are earlier.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AudioOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset of the judgment window in the selected offset unit; negative values are earlier.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float JudgeOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset of the guide sound in the selected offset unit, which does not affect judgment.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float AnswerOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the offset compensating for the input latency of the touch panel in the selected offset unit.
        /// </summary>
        [OptionEnumerator(typeof(GameOffsetEnumerator))]
        public float TouchPanelOffset { get; set; } = 0f;

        /// <summary>
        /// Gets or sets how hold and slide judgments behave.
        /// </summary>
        public JudgeModeOption Mode { get; set; } = JudgeModeOption.Modern;
    }
}

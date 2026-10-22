using MajdataPlay.Game.Notes;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// An each line distance source whose distance and end flag are set directly by tests.
    /// </summary>
    internal sealed class FakeDistanceProvider : IEachLineDistanceProvider
    {
        /// <summary>Gets or sets the distance of the bound notes from the centre.</summary>
        public float Distance { get; set; } = -100f;

        /// <summary>Gets or sets a value indicating whether any bound note has ended.</summary>
        public bool IsAnyNoteEnded { get; set; }
    }
}

using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// An ordinary behaviour without the note component marker, used to check that updaters ignore unrelated
    /// components.
    /// </summary>
    internal sealed class ProbePlainBehaviour : MonoBehaviour
    {
        /// <summary>Gets the number of times <see cref="OnUpdate"/> ran.</summary>
        public int UpdateCount { get; private set; }

        /// <summary>Counts an update callback; it must never be called by a note updater.</summary>
        [OnUpdate]
        internal void OnUpdate()
        {
            UpdateCount++;
        }
    }
}

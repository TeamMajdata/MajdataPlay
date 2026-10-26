using MajdataPlay.Scenes.Game.Misc.Notes;
using MajdataPlay.Scenes.Game.Notes;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Doubles
{
    /// <summary>
    /// A note component that counts how often each player loop callback is invoked by a note component updater.
    /// </summary>
    [NoteComponent]
    internal sealed class ProbeNoteComponent : MajComponent, IStateful<NoteStatus>
    {
        /// <summary>Gets or sets the reported note state.</summary>
        public NoteStatus State { get; set; } = NoteStatus.Running;

        /// <summary>Gets the number of pre-update callbacks.</summary>
        public int PreUpdateCount { get; private set; }

        /// <summary>Gets the number of update callbacks.</summary>
        public int UpdateCount { get; private set; }

        /// <summary>Gets the number of fixed update callbacks.</summary>
        public int FixedUpdateCount { get; private set; }

        /// <summary>Gets the number of late update callbacks.</summary>
        public int LateUpdateCount { get; private set; }

        /// <summary>Counts a pre-update callback.</summary>
        [OnPreUpdate]
        internal void OnPreUpdate()
        {
            PreUpdateCount++;
        }

        /// <summary>Counts an update callback.</summary>
        [OnUpdate]
        internal void OnUpdate()
        {
            UpdateCount++;
        }

        /// <summary>Counts a fixed update callback.</summary>
        [OnFixedUpdate]
        internal void OnFixedUpdate()
        {
            FixedUpdateCount++;
        }

        /// <summary>Counts a late update callback.</summary>
        [OnLateUpdate]
        internal void OnLateUpdate()
        {
            LateUpdateCount++;
        }
    }
}

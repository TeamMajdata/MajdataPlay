using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// A poolable each line stand-in used as an <see cref="EachLinePool"/> prefab; it is not an each line drop, so the
    /// pool cannot activate it.
    /// </summary>
    internal sealed class GamePlayEachLineProbe : MonoBehaviour, IPoolableNote<EachLinePoolingInfo, NoteQueueInfo>
    {
        /// <summary>Gets the number of initializations, which stays zero because the pool never activates probes.</summary>
        public int InitCount { get; private set; }

        /// <summary>Gets the queue info; always null.</summary>
        public NoteQueueInfo QueueInfo => null!;

        /// <summary>Gets the note state.</summary>
        public NoteStatus State => NoteStatus.Start;

        /// <summary>Gets the game object of the probe.</summary>
        public GameObject GameObject => gameObject;

        /// <summary>Counts the initialization.</summary>
        /// <param name="poolingInfo">The pooling info supplied by the pool.</param>
        public void Init(EachLinePoolingInfo poolingInfo)
        {
            InitCount++;
        }
    }
}

using System.Collections.Generic;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// A poolable note behaviour used as a <see cref="NotePool{TInfo, TMember}"/> prefab; it records every pooling
    /// info it is initialized with instead of running gameplay logic.
    /// </summary>
    internal sealed class GamePlayPoolableNoteProbe : MonoBehaviour, IPoolableNote<TapPoolingInfo, TapQueueInfo>
    {
        /// <summary>The pooling infos received through <see cref="Init"/>, in call order.</summary>
        private readonly List<TapPoolingInfo> _initializedWith = new();

        /// <summary>Gets the pooling infos received through <see cref="Init"/>, in call order.</summary>
        public IReadOnlyList<TapPoolingInfo> InitializedWith => _initializedWith;

        /// <summary>Gets the queue info of the last initialization.</summary>
        public TapQueueInfo QueueInfo { get; private set; } = TapQueueInfo.Default;

        /// <summary>Gets the note state, which becomes <see cref="NoteStatus.Inited"/> after initialization.</summary>
        public NoteStatus State { get; private set; } = NoteStatus.Start;

        /// <summary>Gets the game object of the probe.</summary>
        public GameObject GameObject => gameObject;

        /// <summary>Records the pooling info and marks the probe as initialized.</summary>
        /// <param name="poolingInfo">The pooling info supplied by the pool.</param>
        public void Init(TapPoolingInfo poolingInfo)
        {
            _initializedWith.Add(poolingInfo);
            QueueInfo = poolingInfo.QueueInfo;
            State = NoteStatus.Inited;
        }
    }
}

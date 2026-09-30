using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal abstract class NoteUpdater<TNote> : MonoBehaviour where TNote : IStateful<NoteStatus>
    {
        [field: SerializeField, ReadOnlyField]
        public double PreUpdateElapsedMs 
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set; 
        }
        public double UpdateElapsedMs
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set;
        }
        public double FixedUpdateElapsedMs
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set;
        }
        public double LateUpdateElapsedMs
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            protected set;
        }

        [SerializeField]
        [FormerlySerializedAs("noteListRoot")]
        Transform _noteListRoot;

        protected TNote[] NoteInstances = Array.Empty<TNote>();


        public virtual void Init()
        {
            var noteCount = _noteListRoot.childCount;
            using var noteInstances = new PooledList<TNote>(noteCount);
            for (var i = 0; i < noteCount; i++)
            {
                var noteObject = _noteListRoot.GetChild(i);
                var noteInstance = noteObject.GetComponent<TNote>();

                if (noteInstance == null)
                {
                    MajDebug.LogDebug($"Child GameObject '{noteObject.name}' (index {i}) under '{_noteListRoot.name}' is missing required component '{typeof(TNote).FullName}'.");
                    continue;
                }
                noteInstances.Add(noteInstance);
            }
            NoteInstances = noteInstances.ToArray();
        }
    }
}

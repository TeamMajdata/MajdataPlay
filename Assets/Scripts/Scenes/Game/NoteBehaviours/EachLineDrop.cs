using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Game.Notes;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.U2D;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal class EachLineDrop : MajComponent, IPoolableNote<EachLinePoolingInfo, NoteQueueInfo>, IStateful<NoteStatus>, IRendererContainer
    {
        public RendererStatus RendererState
        {
            get => _rendererState;
            set
            {
                if (State < NoteStatus.Inited)
                {
                    return;
                }
                switch (value)
                {
                    case RendererStatus.Off:
                        _sr.enabled = false;
                        break;
                    case RendererStatus.On:
                        _sr.enabled = true;
                        break;
                }
            }
        }
        public NoteStatus State { get; set; } = NoteStatus.Start;
        public bool IsDestroyed => State == NoteStatus.End;
        public NoteQueueInfo QueueInfo => TapQueueInfo.Default;
        public bool IsInitialized => State >= NoteStatus.Inited;

        public float timing;
        public int startPosition = 1;
        public int curvLength = 1;
        public float speed = 1;
        float _noteAppearRate = 0.265f;
        
        SpriteRenderer _sr;
        GameSetting _gameSetting = new();
        INoteController _noteController;
        NotePoolManager _poolManager;

        IEachLineDistanceProvider _distanceProvider;
        
        static Sprite[] _curvSprites = Array.Empty<Sprite>();
        static bool _isCurvSpritesInited = false;

        protected override void Awake()
        {
            base.Awake();
            _noteController = Majdata<INoteController>.Instance!;
            _poolManager = FindObjectOfType<NotePoolManager>();
            _gameSetting = MajEnv.Settings;
            _noteAppearRate = _gameSetting.Debug.NoteAppearRate;
            _sr = gameObject.GetComponent<SpriteRenderer>();
            _sr.sprite = null!;
            RendererState = RendererStatus.Off;
            _sr.enabled = false;
            GameObject.layer = MajEnv.HIDDEN_LAYER;
            Active = true;
            if (!_isCurvSpritesInited)
            {
                _curvSprites = new Sprite[4];
                var skin = MajInstances.SkinManager.GetEachLineSkin();
                skin.EachGuideLines.CopyTo(_curvSprites);
                _isCurvSpritesInited = true;
            }
        }
        public void Init(EachLinePoolingInfo poolingInfo)
        {
            if (State >= NoteStatus.Inited && State < NoteStatus.End)
            {
                return;
            }
            startPosition = poolingInfo.StartPos;
            timing = poolingInfo.Timing;
            speed = poolingInfo.Speed;
            curvLength = poolingInfo.CurvLength;
            _sr.sprite = _curvSprites[curvLength - 1];
            Transform.localScale = new Vector3(1.225f / 4.8f, 1.225f / 4.8f, 1f);
            Transform.rotation = Quaternion.Euler(0, 0, -45f * (startPosition - 1));
            NoteHelper.Sorting.Apply(_sr, NoteHelper.Sorting.NoteLayer.TapLines, -2);
            State = NoteStatus.Inited;
            RendererState = RendererStatus.Off;
            _distanceProvider = poolingInfo.DistanceProvider;
        }
        public void End()
        {
            State = NoteStatus.End;
            RendererState = RendererStatus.Off;
            GameObject.layer = MajEnv.HIDDEN_LAYER;
            _distanceProvider = null!;
            _poolManager.Collect(this);
        }
        [OnLateUpdate]
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create("EachLineDrop.OnLateUpdate"))
            {
                if (State < NoteStatus.Inited || IsDestroyed)
                {
                    return;
                }
                if(_distanceProvider.IsAnyNoteEnded)
                {
                    End();
                    return;
                }
                var distance = _distanceProvider.Distance;
                var scaleRate = _noteAppearRate;
                var destScale = (distance * scaleRate) + (1 - (scaleRate * 1.225f));
                var lineScale = Mathf.Abs(distance / 4.8f);

                switch (State)
                {
                    case NoteStatus.Inited:
                        if (destScale >= 0f)
                        {
                            RendererState = RendererStatus.Off;

                            State = NoteStatus.Scaling;
                            goto case NoteStatus.Scaling;
                        }
                        return;
                    case NoteStatus.Scaling:
                        if (destScale > 0.3f)
                        {
                            RendererState = RendererStatus.On;
                            GameObject.layer = MajEnv.DEFAULT_LAYER;
                        }
                        if (distance < 1.225f)
                        {
                            Transform.localScale = new Vector3(1.225f / 4.8f, 1.225f / 4.8f, 1f);
                        }
                        else
                        {
                            State = NoteStatus.Running;
                            goto case NoteStatus.Running;
                        }
                        break;
                    case NoteStatus.Running:
                        Transform.localScale = new Vector3(lineScale, lineScale, 1f);
                        break;
                }
            }
        }
        void OnDestroy()
        {
            Active = false;
            if(_isCurvSpritesInited)
            {
                _curvSprites = Array.Empty<Sprite>();
                _isCurvSpritesInited = false;
            }
        }
        RendererStatus _rendererState = RendererStatus.Off;
    }
}

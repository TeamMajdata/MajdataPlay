using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Scenes.Game.Misc.Notes;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    [NoteComponent]
    internal class SlideOK : MajComponent, IStateful<NoteStatus>
    {
        public NoteStatus State { get; private set; } = NoteStatus.Start;
        public SlideOKShape Shape { get; set; } = SlideOKShape.Curv;
        public bool IsClassic { get; set; } = false;

        int _indexOffset;
        int _judgeOffset = 0;
        bool _displayCP = false;
        float _elapsedTime = 0f;

        [SerializeField]
        [ReadOnlyField]
        int _sortingOrder = 0;

        Sprite[] _justSprites = Array.Empty<Sprite>();
        SpriteRenderer _spriteRenderer;
        Animator _animator;
        Material _defaultMaterial;

        private static int s_GlobalSortingOrder = 0;

        private readonly static int CLASSIC_ANIM_HASH = Animator.StringToHash("classic");
        private readonly static int MODERN_ANIM_HASH = Animator.StringToHash("modern");
        private readonly static int BREAK_ANIM_HASH = Animator.StringToHash("break");

        const int STRIDE = 13107;

        const short SORTING_ORDER_CRITICAL = short.MinValue;
        const short SORTING_ORDER_PERFECT = (short)(short.MinValue + STRIDE);
        const short SORTING_ORDER_GREAT = (short)(short.MinValue + (STRIDE * 2));
        const short SORTING_ORDER_GOOD = (short)(short.MinValue + (STRIDE * 3));
        const short SORTING_ORDER_MISS = (short)(short.MinValue + (STRIDE * 4));
        protected override void Awake()
        {
            base.Awake();
            _displayCP = MajEnv.Settings.Display.DisplayCriticalPerfect;
            _spriteRenderer = GetComponent<SpriteRenderer>();
            NoteHelper.Sorting.Apply(_spriteRenderer, NoteHelper.Sorting.NoteLayer.SlideOK);
            _animator = GetComponent<Animator>();
            _animator.enabled = false;
            _defaultMaterial = _spriteRenderer.sharedMaterial;
            _justSprites = MajInstances.SkinManager.SelectedSkin.Just;            

            SetActiveInternal(false);
        }
        void Start()
        {
            _sortingOrder = s_GlobalSortingOrder++;
        }
        void OnDestroy()
        {
            s_GlobalSortingOrder = 0;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayResult(in NoteJudgeResult result)
        {
            var isBreak = false;
            var sortingOrder = (int)SORTING_ORDER_CRITICAL;
            switch (result.Grade)
            {
                case JudgeGrade.Perfect:
                    if (_displayCP)
                    {
                        SetJustCP();
                    }
                    else
                    {
                        SetJustP();
                    }
                    sortingOrder = SORTING_ORDER_CRITICAL;
                    isBreak = result.IsBreak;
                    break;
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.FastPerfect3rd:
                    SetFastP();
                    sortingOrder = SORTING_ORDER_PERFECT;
                    break;
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat:
                    SetFastGr();
                    sortingOrder = SORTING_ORDER_GREAT;
                    break;
                case JudgeGrade.FastGood:
                    SetFastGd();
                    sortingOrder = SORTING_ORDER_GOOD;
                    break;
                case JudgeGrade.LateGood:
                    SetLateGd();
                    sortingOrder = SORTING_ORDER_GOOD;
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                    SetLateP();
                    sortingOrder = SORTING_ORDER_PERFECT;
                    break;
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.LateGreat:
                    SetLateGr();
                    sortingOrder = SORTING_ORDER_GREAT;
                    break;
                case JudgeGrade.TooFast:
                    SetTooFast();
                    sortingOrder = SORTING_ORDER_MISS;
                    break;
                default:
                    SetMiss();
                    sortingOrder = SORTING_ORDER_MISS;
                    break;
            }
            if(IsClassic)
            {
                sortingOrder += _sortingOrder;
            }
            else
            {
                sortingOrder += STRIDE - _sortingOrder;
            }
            NoteHelper.Sorting.Apply(_spriteRenderer, NoteHelper.Sorting.NoteLayer.SlideOK, sortingOrder);

            Play(isBreak);
            State = NoteStatus.Running;
            SetActive(true);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Play(bool isBreak)
        {
            if (IsClassic)
            {
                _animator.SetTrigger(CLASSIC_ANIM_HASH);
            }
            else if (isBreak)
            {
                _animator.SetTrigger(BREAK_ANIM_HASH);
            }
            else
            {
                _animator.SetTrigger(MODERN_ANIM_HASH);
            }
            var z = Transform.localPosition.z;
            _animator.Update(0.0000001f);
            NoteHelper.Sorting.SetLocalZPosition(Transform, z);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("SlideOK.OnUpdate"))
            {
                var delta = MajTimeline.DeltaTime;
                var z = Transform.localPosition.z;
                _animator.Update(delta);
                NoteHelper.Sorting.SetLocalZPosition(Transform, z);
                if (_elapsedTime > 0.5f)
                {
                    State = NoteStatus.End;
                    _spriteRenderer.sharedMaterial = _defaultMaterial;
                    SetActiveInternal(false);
                    GameObject.layer = MajEnv.HIDDEN_LAYER;
                }
                else
                {
                    _elapsedTime += delta;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SetR()
        {
            _indexOffset = 0;
            RefreshSprite();
            return (int)Shape;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SetL()
        {
            _indexOffset = 3;
            RefreshSprite();
            return (int)Shape;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetJustCP()
        {
            _judgeOffset = 0;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetJustP()
        {
            _judgeOffset = 6;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetFastP()
        {
            _judgeOffset = 12;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetFastGr()
        {
            _judgeOffset = 18;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetFastGd()
        {
            _judgeOffset = 24;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetLateP()
        {
            _judgeOffset = 30;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetLateGr()
        {
            _judgeOffset = 36;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetLateGd()
        {
            _judgeOffset = 42;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetMiss()
        {
            _judgeOffset = 48;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetTooFast()
        {
            _judgeOffset = 54;
            RefreshSprite();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void RefreshSprite()
        {
            _spriteRenderer.sprite = _justSprites[(int)Shape + _indexOffset + _judgeOffset];
        }
        public override void SetActive(bool state)
        {
            if (Active == state)
            {
                return;
            }
            SetActiveInternal(state);
        }
        void SetActiveInternal(bool state)
        {
            base.SetActive(state);
            if(state)
            {
                _spriteRenderer.enabled = true;
            }
            else
            {
                _spriteRenderer.enabled = false;
            }
        }
    }
}

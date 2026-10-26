using MajdataPlay.Collections;
using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Scenes.Game.Utils;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class JudgeTextDisplayer: MajComponent
    {
        /// <summary>
        /// Gets the world position or updates its X and Y coordinates while preserving sorting depth.
        /// </summary>
        public Vector3 Position
        {
            get => Transform.position;
            set => NoteHelper.Sorting.SetPosition(Transform, value);
        }
        /// <summary>
        /// Gets the local position or updates its X and Y coordinates while preserving sorting depth.
        /// </summary>
        public Vector3 LocalPosition
        {
            get => Transform.localPosition;
            set => NoteHelper.Sorting.SetLocalPosition(Transform, value);
        }

        Animator _animator;

        [SerializeField]
        SpriteRenderer textRenderer;
        [SerializeField]
        SpriteRenderer breakRenderer;

        GameObject[] _children = Array.Empty<GameObject>();
        JudgeTextSkin _skin;

        bool _displayBreakScore = false;
        bool _displayCriticalPerfect = false;

        static readonly int PERFECT_ANIM_HASH = Animator.StringToHash("perfect");
        static readonly int BREAK_ANIM_HASH = Animator.StringToHash("break");

        const float ANIM_LENGTH_SEC = 1 / 60f * 21;

        float _animRemainingTime = 0;
        private long _expectedTrigger = long.MaxValue;

        protected override void Awake()
        {
            base.Awake();
            _animator = GetComponent<Animator>();
            _skin = MajInstances.SkinManager.GetJudgeTextSkin();
            _displayBreakScore = MajEnv.Settings.Display.DisplayBreakScore;
            _displayCriticalPerfect = MajEnv.Settings.Display.DisplayCriticalPerfect;
            _animator.enabled = false;
            Sprite breakSprite;

            if (_displayCriticalPerfect)
            {
                if (_displayBreakScore)
                {
                    breakSprite = _skin.Break_2600_Shine;
                }
                else
                {
                    breakSprite = _skin.CP_Shine;
                }
            }
            else
            {
                if (_displayBreakScore)
                {
                    breakSprite = _skin.Break_2600_Shine;
                }
                else
                {
                    breakSprite = _skin.P_Shine;
                }
            }
            breakRenderer.sprite = breakSprite;
            _children = Transform.GetChildren()
                                 .Select(x => x.gameObject)
                                 .ToArray();
            SetActiveInternal(false);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            if (!Active || _animRemainingTime < 0)
            {
                return;
            }
            if (_expectedTrigger != long.MaxValue)
            {
                _animator.SetTrigger((int)_expectedTrigger);
                _expectedTrigger = long.MaxValue;
            }
            var deltaTime = MajTimeline.DeltaTime;
            _animator.Update(deltaTime);
            _animRemainingTime -= deltaTime;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset()
        {
            SetActive(false);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Play(in NoteJudgeResult judgeResult, bool isClassC = false)
        {
            SetActive(true);
            var isBreak = judgeResult.IsBreak;
            var result = judgeResult.Grade;

            if (isBreak && _displayBreakScore)
            {
                LoadBreakSkin(judgeResult, isClassC);
            }
            else
            {
                LoadTapSkin(judgeResult, isClassC);
            }
            
            if (isBreak && result == JudgeGrade.Perfect)
            {
                _expectedTrigger = BREAK_ANIM_HASH;
            }
            else
            {
                _expectedTrigger = PERFECT_ANIM_HASH;
            }
            _animRemainingTime = ANIM_LENGTH_SEC;
        }
        void LoadTapSkin(in NoteJudgeResult judgeResult,bool isClassC = false)
        {
            switch (judgeResult.Grade)
            {
                case JudgeGrade.LateGood:
                    textRenderer.sprite = isClassC ? _skin.Good.Late : _skin.Good.Normal;
                    break;
                case JudgeGrade.FastGood:
                    textRenderer.sprite = isClassC ? _skin.Good.Fast : _skin.Good.Normal;
                    break;
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                    textRenderer.sprite = isClassC ? _skin.Great.Late : _skin.Great.Normal;
                    break;
                case JudgeGrade.FastGreat:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat3rd:
                    textRenderer.sprite = isClassC ? _skin.Great.Fast : _skin.Great.Normal;
                    break;
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.LatePerfect3rd:
                    textRenderer.sprite = isClassC ? _skin.Perfect.Late : _skin.Perfect.Normal;
                    break;
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.FastPerfect3rd:
                    textRenderer.sprite = isClassC ? _skin.Perfect.Fast : _skin.Perfect.Normal;
                    break;
                case JudgeGrade.Perfect:
                    if (isClassC)
                    {
                        var isJust = judgeResult.Diff == 0;
                        var isFast = judgeResult.IsFast;
                        if(_displayCriticalPerfect)
                        {
                            if (isJust)
                                textRenderer.sprite = _skin.CriticalPerfect.Normal;
                            else if (isFast)
                                textRenderer.sprite = _skin.CriticalPerfect.Fast;
                            else
                                textRenderer.sprite = _skin.CriticalPerfect.Late;
                        }
                        else
                        {
                            if (isJust)
                                textRenderer.sprite = _skin.Perfect.Normal;
                            else if (isFast)
                                textRenderer.sprite = _skin.Perfect.Fast;
                            else
                                textRenderer.sprite = _skin.Perfect.Late;
                        }
                    }
                    else
                    {
                        if(_displayCriticalPerfect)
                            textRenderer.sprite = _skin.CriticalPerfect.Normal;
                        else
                            textRenderer.sprite = _skin.Perfect.Normal;
                    }
                    break;
                default:
                    textRenderer.sprite = _skin.Miss;
                    break;
            }
        }
        void LoadBreakSkin(in NoteJudgeResult judgeResult, bool isClassC = false)
        {
            switch (judgeResult.Grade)
            {
                case JudgeGrade.LateGood:
                    textRenderer.sprite = isClassC ? _skin.Break_1000.Late : _skin.Break_1000.Normal;
                    break;
                case JudgeGrade.FastGood:
                    textRenderer.sprite = isClassC ? _skin.Break_1000.Fast : _skin.Break_1000.Normal;
                    break;
                case JudgeGrade.LateGreat3rd:
                    textRenderer.sprite = isClassC ? _skin.Break_1250.Late : _skin.Break_1250.Normal;
                    break;
                case JudgeGrade.FastGreat3rd:
                    textRenderer.sprite = isClassC ? _skin.Break_1250.Fast : _skin.Break_1250.Normal;
                    break;
                case JudgeGrade.LateGreat2nd:
                    textRenderer.sprite = isClassC ? _skin.Break_1500.Late : _skin.Break_1500.Normal;
                    break;
                case JudgeGrade.FastGreat2nd:
                    textRenderer.sprite = isClassC ? _skin.Break_1500.Fast : _skin.Break_1500.Normal;
                    break;
                case JudgeGrade.LateGreat:
                    textRenderer.sprite = isClassC ? _skin.Break_2000.Late : _skin.Break_2000.Normal;
                    break;
                case JudgeGrade.FastGreat:
                    textRenderer.sprite = isClassC ? _skin.Break_2000.Fast : _skin.Break_2000.Normal;
                    break;
                case JudgeGrade.LatePerfect3rd:
                    textRenderer.sprite = isClassC ? _skin.Break_2500.Late : _skin.Break_2500.Normal;
                    break;
                case JudgeGrade.FastPerfect3rd:
                    textRenderer.sprite = isClassC ? _skin.Break_2500.Fast : _skin.Break_2500.Normal;
                    break;
                case JudgeGrade.LatePerfect2nd:
                    textRenderer.sprite = isClassC ? _skin.Break_2550.Late : _skin.Break_2550.Normal;
                    break;
                case JudgeGrade.FastPerfect2nd:
                    textRenderer.sprite = isClassC ? _skin.Break_2550.Fast : _skin.Break_2550.Normal;
                    break;
                case JudgeGrade.Perfect:
                    {
                        if(isClassC)
                        {
                            var isJust = judgeResult.Diff == 0;
                            var isFast = judgeResult.IsFast;
                            if (isJust)
                                textRenderer.sprite = _skin.Break_2600.Normal;
                            else if (isFast)
                                textRenderer.sprite = _skin.Break_2600.Fast;
                            else
                                textRenderer.sprite = _skin.Break_2600.Late;
                        }
                        else
                        {
                            textRenderer.sprite = _skin.Break_2600.Normal;
                        }
                    }
                    break;
                default:
                    textRenderer.sprite = _skin.Break_0;
                    break;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SetActive(bool state)
        {
            if (Active == state)
                return;
            SetActiveInternal(state);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetActiveInternal(bool state)
        {
            Active = state;
            base.SetActive(state);
            switch (state)
            {
                case true:
                    foreach (var child in ArrayHelper.ToEnumerable(_children))
                    {
                        if (child is null)
                            continue;
                        child.layer = MajEnv.DEFAULT_LAYER;
                    }
                    GameObject.layer = MajEnv.DEFAULT_LAYER;
                    break;
                case false:
                    foreach (var child in ArrayHelper.ToEnumerable(_children))
                    {
                        if (child is null)
                            continue;
                        child.layer = MajEnv.HIDDEN_LAYER;
                    }
                    GameObject.layer = MajEnv.HIDDEN_LAYER;
                    break;
            }
        }
    }
}

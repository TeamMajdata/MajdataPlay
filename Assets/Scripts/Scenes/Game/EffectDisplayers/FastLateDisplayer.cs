using MajdataPlay.Collections;
using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal sealed class FastLateDisplayer: MajComponent
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

        Sprite fastSprite;
        Sprite lateSprite;

        GameObject[] _children = Array.Empty<GameObject>();

        static readonly int PERFECT_ANIM_HASH = Animator.StringToHash("perfect");
        static readonly int BREAK_ANIM_HASH = Animator.StringToHash("break");
        const float ANIM_LENGTH_SEC = 1 / 60f * 21;

        float _animRemainingTime = 0;
        private long _expectedTrigger = long.MaxValue;

        protected override void Awake()
        {
            base.Awake();
            _animator = GameObject.GetComponent<Animator>();
            _animator.enabled = false;
            var skin = MajInstances.SkinManager.GetJudgeTextSkin();
            fastSprite = skin.Fast;
            lateSprite = skin.Late;
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
        public void Play(in NoteJudgeResult judgeResult)
        {
            if (judgeResult.IsMissOrTooFast || judgeResult.Diff == 0)
            {
                Reset();
                return;
            }
            SetActive(true);
            if (judgeResult.IsFast)
            {
                textRenderer.sprite = fastSprite;
            }
            else
            {
                textRenderer.sprite = lateSprite;
            }
            if (judgeResult.IsBreak)
            {
                _expectedTrigger = BREAK_ANIM_HASH;
            }
            else
            {
                _expectedTrigger = PERFECT_ANIM_HASH;
            }
            _animRemainingTime = ANIM_LENGTH_SEC;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SetActive(bool state)
        {
            if (Active == state)
            {
                return;
            }
            SetActiveInternal(state);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SetActiveInternal(bool state)
        {
            Active = state;
            base.SetActive(state);
            switch(state)
            {
                case true:
                    foreach(var child in ArrayHelper.ToEnumerable(_children))
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

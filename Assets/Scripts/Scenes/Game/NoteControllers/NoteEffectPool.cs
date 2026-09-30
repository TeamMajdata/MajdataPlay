using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    public sealed class NoteEffectPool : MonoBehaviour
    {
        private bool _isInited = false;

        [SerializeField]
        [FormerlySerializedAs("tapEffectPrefab")]
        private GameObject _tapEffectPrefab;

        [SerializeField]
        [FormerlySerializedAs("touchHoldEffectPrefab")]
        private GameObject _touchHoldEffectPrefab;

        [SerializeField]
        [FormerlySerializedAs("touchEffectPrefab")]
        private GameObject _touchEffectPrefab;

        [SerializeField]
        [FormerlySerializedAs("holdEffectPrefab")]
        private GameObject _holdEffectPrefab;

        [SerializeField]
        [FormerlySerializedAs("_touchFeedbackEffectPrefab")]
        private GameObject _touchFeedbackEffectPrefab;

        private TapEffectDisplayer[] _tapJudgeEffects = new TapEffectDisplayer[8];
        private TouchHoldEffectDisplayer[] _touchHoldJudgeEffects = new TouchHoldEffectDisplayer[33];
        private TouchEffectDisplayer[] _touchJudgeEffects = new TouchEffectDisplayer[33];

        private HoldEffectDisplayer[] _holdEffects = new HoldEffectDisplayer[8];
        private HoldEffectDisplayer[] _touchHoldEffects = new HoldEffectDisplayer[33];

        private TouchFeedbackDisplayer[] _touchFeedbackEffects = new TouchFeedbackDisplayer[33];

        private PooledArray<TapEffectDisplayer> _rentedArrayForGeneratedTapEffectDisplayers = default;
        private PooledArray<HoldEffectDisplayer> _rentedArrayForGeneratedHoldEffectDisplayers = default;
        private PooledArray<TouchEffectDisplayer> _rentedArrayForGeneratedTouchEffectDisplayers = default;

        private ReadOnlyMemory<TapEffectDisplayer> _generatedTapEffectDisplayers = Array.Empty<TapEffectDisplayer>();
        private ReadOnlyMemory<HoldEffectDisplayer> _generatedHoldEffectDisplayers = Array.Empty<HoldEffectDisplayer>();
        private ReadOnlyMemory<TouchEffectDisplayer> _generatedTouchEffectDisplayers = Array.Empty<TouchEffectDisplayer>();

        private GamePlayManager _gpManager;

        private FireworkDisplayer _fireworkDisplayer;

        private void Awake()
        {
            Majdata<NoteEffectPool>.Instance = this;
        }
        private void Start()
        {
            _fireworkDisplayer = Majdata<FireworkDisplayer>.Instance!;
        }
        internal void Reset()
        {
            foreach (var effect in _tapJudgeEffects)
            {
                effect.Reset();
            }
            foreach (var effect in _touchHoldJudgeEffects)
            {
                effect.Reset();
            }
            foreach (var effect in _touchJudgeEffects)
            {
                effect.Reset();
            }
            foreach (var effect in _holdEffects)
            {
                effect.Reset();
            }
            foreach (var effect in _touchHoldEffects)
            {
                effect.Reset();
            }
        }

        private void OnDestroy()
        {
            Majdata<NoteEffectPool>.Free();
            _rentedArrayForGeneratedTapEffectDisplayers.Dispose();
            _rentedArrayForGeneratedTouchEffectDisplayers.Dispose();
            _rentedArrayForGeneratedHoldEffectDisplayers.Dispose();
        }
        internal void Init()
        {
            if(_isInited)
            {
                return;
            }
            _isInited = false;
            using var generatedTapEffectDisplayers = new PooledList<TapEffectDisplayer>();
            using var generatedHoldEffectDisplayers = new PooledList<HoldEffectDisplayer>();
            using var generatedTouchEffectDisplayers = new PooledList<TouchEffectDisplayer>();
            var tapParent = transform.GetChild(0);
            var touchParent = transform.GetChild(1);
            var touchHoldParent = transform.GetChild(2);
            var touchFeedbackParent = transform.GetChild(3);

            _gpManager = Majdata<GamePlayManager>.Instance!;
            var noteLoader = Majdata<NoteLoader>.Instance!;
            var isHasTap = noteLoader.IsHasTap;
            var isHasHold = noteLoader.IsHasHold;
            var isHasTouch = noteLoader.IsHasTouch;
            var isHasTouchHold = noteLoader.IsHasTouchHold;
            // Judge Effect
            for (var i = 0; i < 8; i++)
            {
                if (!isHasTap[i])
                {
                    continue;
                }
                var rotation = Quaternion.Euler(0, 0, -22.5f + -45f * i);
                var obj = Instantiate(_tapEffectPrefab, tapParent);
                obj.name = $"TapEffect_{i + 1}";
                obj.transform.rotation = rotation;
                if (_gpManager != null && _gpManager.IsClassicMode)
                    obj.transform.GetChild(0).localScale = new Vector3(1.4f, 1.4f, 0);
                var displayer = obj.GetComponent<TapEffectDisplayer>();
                displayer.DistanceRatio = MajEnv.Settings.Display.OuterJudgeDistance;
                displayer.ResetAll();
                _tapJudgeEffects[i] = displayer;
                generatedTapEffectDisplayers.Add(displayer);
            }
            for (var i = 0; i < 33; i++)
            {
                if (!isHasTouch[i])
                {
                    continue;
                }
                var sensorPos = (SensorArea)i;
                var obj = Instantiate(_touchEffectPrefab, touchParent);
                var displayer = obj.GetComponent<TouchEffectDisplayer>();
                displayer.DistanceRatio = MajEnv.Settings.Display.InnerJudgeDistance;
                obj.name = $"TouchEffect_{sensorPos}";
                displayer.SensorPos = sensorPos;
                displayer.ResetAll();
                _touchJudgeEffects[i] = displayer;    
                generatedTouchEffectDisplayers.Add(displayer);
            }
            // Hold Effect
            for (var i = 0; i < 8; i++)
            {
                if (!isHasHold[i])
                {
                    continue;
                }
                var obj = Instantiate(_holdEffectPrefab, tapParent);
                obj.name = $"HoldEffect_{i + 1}";
                var position = NoteHelper.GetTapPosition(i + 1, 4.8f);
                var displayer = obj.GetComponent<HoldEffectDisplayer>();
                displayer.Position = position;
                displayer.Reset();
                _holdEffects[i] = displayer;
                generatedHoldEffectDisplayers.Add(displayer);
            }
            for (var i = 0; i < 33; i++)
            {
                if (!isHasTouchHold[i])
                {
                    continue;
                }
                var sensorPos = (SensorArea)i;
                var obj = Instantiate(_holdEffectPrefab, touchHoldParent);
                obj.name = $"TouchHold_HoldingEffect_{sensorPos}";
                var position = NoteHelper.GetTouchAreaPosition(sensorPos);
                var displayer = obj.GetComponent<HoldEffectDisplayer>();
                displayer.Position = position;
                displayer.Reset();
                _touchHoldEffects[i] = displayer;

                var obj4Hold = Instantiate(_touchHoldEffectPrefab, touchHoldParent);
                var distance = NoteHelper.GetTouchAreaDistance(sensorPos.GetGroup());
                var position2 = Vector3.zero;
                position2.y += distance;
                var rotation = NoteHelper.GetTouchRoation(NoteHelper.GetTouchAreaPosition(sensorPos), sensorPos);
                var displayer4Hold = obj4Hold.GetComponent<TouchHoldEffectDisplayer>();
                obj4Hold.transform.rotation = rotation;
                displayer4Hold.DistanceRatio = MajEnv.Settings.Display.InnerJudgeDistance;
                displayer4Hold.LocalPosition = position2;
                obj4Hold.name = $"TouchHoldEffect_{sensorPos}";
                displayer4Hold.ResetAll();
                _touchHoldJudgeEffects[i] = displayer4Hold;
                generatedTapEffectDisplayers.Add(displayer4Hold);
                generatedHoldEffectDisplayers.Add(displayer);
            }
            // Touch Feedback Effect
            for (var i = 0; i < 33; i++)
            {
                var pos = (SensorArea)i;
                var obj = Instantiate(_touchFeedbackEffectPrefab, touchFeedbackParent);
                obj.name = $"{pos}";
                if (pos < SensorArea.B1)
                {
                    var position = NoteHelper.GetTapPosition(i + 1, 4.8f);
                    obj.transform.position = position;
                }
                else
                {
                    var position = NoteHelper.GetTouchAreaPosition(pos);
                    obj.transform.position = position;
                }
                var displayer = obj.GetComponent<TouchFeedbackDisplayer>();
                displayer.Reset();
                obj.transform.localScale = new Vector3(0.5f, 0.5f, 1);
                _touchFeedbackEffects[i] = displayer;
            }
            _rentedArrayForGeneratedTapEffectDisplayers = Pool<TapEffectDisplayer>.Rent(generatedTapEffectDisplayers.Count);
            _rentedArrayForGeneratedHoldEffectDisplayers = Pool<HoldEffectDisplayer>.Rent(generatedHoldEffectDisplayers.Count);
            _rentedArrayForGeneratedTouchEffectDisplayers = Pool<TouchEffectDisplayer>.Rent(generatedTouchEffectDisplayers.Count);
            generatedTapEffectDisplayers.CopyTo(_rentedArrayForGeneratedTapEffectDisplayers);
            generatedHoldEffectDisplayers.CopyTo(_rentedArrayForGeneratedHoldEffectDisplayers);
            generatedTouchEffectDisplayers.CopyTo(_rentedArrayForGeneratedTouchEffectDisplayers);
            _generatedTapEffectDisplayers = _rentedArrayForGeneratedTapEffectDisplayers.AsMemory(0, generatedTapEffectDisplayers.Count);
            _generatedHoldEffectDisplayers = _rentedArrayForGeneratedHoldEffectDisplayers.AsMemory(0, generatedHoldEffectDisplayers.Count);
            _generatedTouchEffectDisplayers = _rentedArrayForGeneratedTouchEffectDisplayers.AsMemory(0, generatedTouchEffectDisplayers.Count);

            _isInited = true;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create("NoteEffectPool.OnLateUpdate"))
            {
                if (!_isInited)
                {
                    return;
                }
                var s1 = _generatedTapEffectDisplayers.Span;
                var s2 = _generatedHoldEffectDisplayers.Span;
                var s3 = _generatedTouchEffectDisplayers.Span;
                var count1 = _generatedTapEffectDisplayers.Length;
                var count2 = _generatedHoldEffectDisplayers.Length;
                var count3 = _generatedTouchEffectDisplayers.Length;

                for (var i = 0; i < count1; i++)
                {
                    s1[i].OnLateUpdate();
                }
                for (var i = 0; i < count2; i++)
                {
                    s2[i].OnLateUpdate();
                }
                for (var i = 0; i < count3; i++)
                {
                    s3[i].OnLateUpdate();
                }
                _fireworkDisplayer.OnLateUpdate();
            }
        }
        /// <summary>
        /// Tap、Hold、Star
        /// </summary>
        /// <param name="judgeResult"></param>
        /// <param name="keyIndex"></param>
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTapJudgeResult(in NoteJudgeResult judgeResult, int keyIndex)
        {
            var effectDisplayer = _tapJudgeEffects[keyIndex - 1];
            effectDisplayer.Play(judgeResult);
        }
        /// <summary>
        /// Touch
        /// </summary>
        /// <param name="judgeResult"></param>
        /// <param name="sensorPos"></param>
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTouchJudgeResult(in NoteJudgeResult judgeResult, SensorArea sensorPos)
        {
            var effectDisplayer = _touchJudgeEffects[(int)sensorPos];
            effectDisplayer.Play(judgeResult);
        }
        /// <summary>
        /// TouchHold
        /// </summary>
        /// <param name="judgeResult"></param>
        /// <param name="sensorPos"></param>
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTouchHoldJudgeResult(in NoteJudgeResult judgeResult, SensorArea sensorPos)
        {
            var effectDisplayer = _touchHoldJudgeEffects[(int)sensorPos];
            effectDisplayer.Play(judgeResult);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayHoldEffect(in JudgeGrade judgeType, int keyIndex)
        {
            var displayer = _holdEffects[keyIndex - 1];
            displayer.Play(judgeType);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayHoldEffect(in JudgeGrade judgeType, SensorArea sensorPos)
        {
            var displayer = _touchHoldEffects[(int)sensorPos];
            displayer.Play(judgeType);
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetHoldEffect(int keyIndex)
        {
            var displayer = _holdEffects[keyIndex - 1];
            displayer.Reset();
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetHoldEffect(SensorArea sensorPos)
        {
            var displayer = _touchHoldEffects[(int)sensorPos];
            displayer.Reset();
        }
        /// <summary>
        /// Tap、Hold、Star
        /// </summary>
        /// <param name="keyIndex"></param>
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset(int keyIndex)
        {
            var effectDisplayer = _tapJudgeEffects[keyIndex - 1];
            effectDisplayer.Reset();
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayFeedbackEffect(SensorArea sensorPos)
        {
            if (!_isInited)
            {
                return;
            }
            var effect = _touchFeedbackEffects[(int)sensorPos];
            effect.Play();
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetFeedbackEffect(SensorArea sensorPos)
        {
            if (!_isInited)
            {
                return;
            }
            var effect = _touchFeedbackEffects[(int)sensorPos];
            effect.Reset();
        }
    }
}

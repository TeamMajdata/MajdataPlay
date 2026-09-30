using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Controllers
{
    internal class NoteAudioManager : MonoBehaviour
    {
        public float FirstClockTiming { get; private set; }

        XxlbAnimationController _xxlbController;
        INoteController _noteController;

        bool _isTouchHoldRiserPlaying = false;

        Memory<AnswerSoundPoint> _answerTimingPoints = Memory<AnswerSoundPoint>.Empty;
        PooledArray<AnswerSoundPoint> _rentedArrayForAnswerSoundPoints = default;

        readonly static bool[] _noteSFXPlaybackRequests = new bool[16];
        readonly static AudioSampleWrap[] _noteSFXs = new AudioSampleWrap[16];
        readonly AudioManager _audioManager = MajInstances.AudioManager;
        static readonly ReadOnlyMemory<string> SFX_NAMES = new string[16]
        {
            "tap_perfect.wav",
            "tap_great.wav",
            "tap_good.wav",
            "tap_ex.wav",
            "break_tap.wav",
            "break.wav",
            "slide.wav",
            "slide_break_start.wav",
            "slide_break_slide.wav",
            "touch.wav",
            "touch_Hold_riser.wav",
            "touch_hanabi.wav",
            "answer.wav",
            "answer_clock.wav",
            "answer_mine.wav",
            "tap_miss.wav"
        };
        const float ANSWER_PLAYBACK_OFFSET_SEC = -(16.66666f * 1) / 1000;
        const int TAP_PERFECT = 0;
        const int TAP_GREAT = 1;
        const int TAP_GOOD = 2;
        const int TAP_EX = 3;
        const int BREAK_JUDGE = 4;
        const int BREAK_SFX = 5;
        const int SLIDE = 6;
        const int BREAK_SLIDE = 7;
        const int BREAK_SLIDE_JUDGE = 8;
        const int TOUCH = 9;
        const int TOUCHHOLD = 10;
        const int FIREWORK = 11;
        const int ANSWER = 12;
        const int ANSWER_CLOCK = 13;
        const int MINE = 14;
        const int MISS = 15;

        float _answerOffsetSec = 0;

        void Awake()
        {
            Majdata<NoteAudioManager>.Instance = this;
            for (var i = 0; i < SFX_NAMES.Length; i++)
            {
                var name = SFX_NAMES.Span[i];
                var sfx = _audioManager.GetSFX(name);
                if (sfx.Volume == 0)
                {
                    _noteSFXs[i] = AudioSampleWrap.Empty;
                }
                else
                {
                    _noteSFXs[i] = sfx;
                }
            }
            var settings = MajEnv.Settings;
            if (settings.Debug.OffsetUnit == OffsetUnitOption.Second)
            {
                _answerOffsetSec = settings.Judge.AnswerOffset + settings.Debug.DisplayOffset;
            }
            else
            {
                _answerOffsetSec = (settings.Judge.AnswerOffset + settings.Debug.DisplayOffset) * MajEnv.FRAME_LENGTH_SEC;
            }
        }
        private void Start()
        {
            _noteController = Majdata<INoteController>.Instance!;
            _xxlbController = Majdata<XxlbAnimationController>.Instance!;
        }
        void OnDestroy()
        {
            Majdata<NoteAudioManager>.Free();
            foreach (var sfx in _noteSFXs)
            {
                sfx.Stop();
            }
            Array.Clear(_noteSFXPlaybackRequests, 0, _noteSFXPlaybackRequests.Length);
            Array.Clear(_noteSFXs, 0, _noteSFXs.Length);
            _rentedArrayForAnswerSoundPoints.Dispose();
            _rentedArrayForAnswerSoundPoints = default;
            _answerTimingPoints = Memory<AnswerSoundPoint>.Empty;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("NoteAudioManager.OnPreUpdate"))
            {
                for (var i = 0; i < _noteSFXPlaybackRequests.Length; i++)
                {
                    _noteSFXPlaybackRequests[i] = false;
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create("NoteAudioManager.OnLateUpdate"))
            {
                AnswerSFXUpdate();
                for (var i = 0; i < _noteSFXPlaybackRequests.Length; i++)
                {
                    var isRequested = _noteSFXPlaybackRequests[i];
                    switch (i)
                    {
                        case TAP_PERFECT:
                            if (isRequested)
                            {
                                _noteSFXs[TAP_PERFECT].PlayOneShot();
                            }
                            break;
                        case TAP_GREAT:
                            if (isRequested)
                            {
                                _noteSFXs[TAP_GREAT].PlayOneShot();
                            }
                            break;
                        case TAP_GOOD:
                            if (isRequested)
                            {
                                _noteSFXs[TAP_GOOD].PlayOneShot();
                            }
                            break;
                        case TAP_EX:
                            if (isRequested)
                            {
                                _noteSFXs[TAP_EX].PlayOneShot();
                            }
                            break;
                        case BREAK_JUDGE:
                            if (isRequested)
                            {
                                _noteSFXs[BREAK_JUDGE].PlayOneShot();
                            }
                            break;
                        case BREAK_SFX:
                            if (isRequested)
                            {
                                _noteSFXs[BREAK_SFX].PlayOneShot();
                            }
                            break;
                        case SLIDE:
                            if (isRequested)
                            {
                                _noteSFXs[SLIDE].PlayOneShot();
                            }
                            break;
                        case BREAK_SLIDE:
                            if (isRequested)
                            {
                                _noteSFXs[BREAK_SLIDE].PlayOneShot();
                            }
                            break;
                        case BREAK_SLIDE_JUDGE:
                            if (isRequested)
                            {
                                _noteSFXs[BREAK_SLIDE_JUDGE].PlayOneShot();
                                _noteSFXs[BREAK_SFX].PlayOneShot();
                            }
                            break;
                        case TOUCH:
                            if (isRequested)
                            {
                                _noteSFXs[TOUCH].PlayOneShot();
                            }
                            break;
                        case TOUCHHOLD:
                            if (isRequested)
                            {
                                if (_isTouchHoldRiserPlaying)
                                    break;
                                _isTouchHoldRiserPlaying = true;
                                _noteSFXs[TOUCHHOLD].PlayOneShot();
                            }
                            else
                            {
                                if (!_isTouchHoldRiserPlaying)
                                    break;
                                _isTouchHoldRiserPlaying = false;
                                _noteSFXs[TOUCHHOLD].Stop();
                            }
                            break;
                        case FIREWORK:
                            if (isRequested)
                            {
                                _noteSFXs[FIREWORK].PlayOneShot();
                            }
                            break;
                        case ANSWER:
                            if (isRequested)
                            {
                                _noteSFXs[ANSWER].PlayOneShot();
                            }
                            break;
                        case ANSWER_CLOCK:
                            if (isRequested)
                            {
                                _noteSFXs[ANSWER_CLOCK].PlayOneShot();
                            }
                            break;
                        case MINE:
                            if (isRequested)
                            {
                                _noteSFXs[MINE].PlayOneShot();
                            }
                            break;
                        case MISS:
                            if (isRequested)
                            {
                                _noteSFXs[MISS].PlayOneShot();
                            }
                            break;
                    }
                }
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AnswerSFXUpdate()
        {
            try
            {
                if (_answerTimingPoints.IsEmpty)
                {
                    return;
                }
                var timingPoints = _answerTimingPoints.Span;
                var thisFrameSec = _noteController.ThisFrameSec;
                var offset = ANSWER_PLAYBACK_OFFSET_SEC;
                var i = 0;
                for (; i < timingPoints.Length; )
                {
                    ref var sfxInfo = ref _answerTimingPoints.Span[i];
                    var playTiming = sfxInfo.Timing;
                    var delta = thisFrameSec - (playTiming + offset);

                    if (delta > 0)
                    {
                        if (sfxInfo.IsClock)
                        {
                            _noteSFXPlaybackRequests[ANSWER_CLOCK] = true;
                            _xxlbController.Stepping();
                        }
                        else
                        {
                            _noteSFXPlaybackRequests[ANSWER] = sfxInfo.IsNormal;
                            _noteSFXPlaybackRequests[MINE] = sfxInfo.IsMine;
                        }
                        _answerTimingPoints = _answerTimingPoints.Slice(i + 1);
                        return;
                    }
                    else
                    {
                        break;
                    }
                }
                
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTapSound(in NoteJudgeResult judgeResult)
        {
            if (judgeResult.IsMine && judgeResult.IsMissOrTooFast)
            {
                _noteSFXPlaybackRequests[MISS] = true;
                return;
            }

            if (judgeResult.IsMissOrTooFast || judgeResult.IsMine)
            {
                return;
            }

            var isBreak = judgeResult.IsBreak;
            var isEx = judgeResult.IsEX;


            if (isBreak)
            {
                PlayBreakTapSound(judgeResult);
                return;
            }
            else if (isEx)
            {
                _noteSFXPlaybackRequests[TAP_EX] = true;
                //_audioManager.PlaySFX("tap_ex.wav");
                return;
            }

            switch (judgeResult.Grade)
            {
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    _noteSFXPlaybackRequests[TAP_GOOD] = true;
                    //_audioManager.PlaySFX("tap_good.wav");
                    break;
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat:
                    _noteSFXPlaybackRequests[TAP_GREAT] = true;
                    //_audioManager.PlaySFX("tap_great.wav");
                    break;
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.Perfect:
                    _noteSFXPlaybackRequests[TAP_PERFECT] = true;
                    //_audioManager.PlaySFX("tap_perfect.wav");
                    break;
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void PlayBreakTapSound(in NoteJudgeResult judgeResult)
        {
            switch (judgeResult.Grade)
            {
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                case JudgeGrade.LateGreat:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.FastGreat3rd:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat:
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                    _noteSFXPlaybackRequests[BREAK_JUDGE] = true;
                    //_audioManager.PlaySFX("break_tap.wav");
                    break;
                case JudgeGrade.Perfect:
                    _noteSFXPlaybackRequests[BREAK_JUDGE] = true;
                    _noteSFXPlaybackRequests[BREAK_SFX] = true;
                    //_audioManager.PlaySFX("break.wav");
                    //_audioManager.PlaySFX("break_tap.wav");
                    break;
            }
        }
        internal async Task GenerateAnswerSFX(SimaiChart chart, bool isPracticeMode, int clockCount = 4)
        {
            await Task.Run(() =>
            {
                if (chart.NoteTimings.Length == 0)
                {
                    _answerTimingPoints = Memory<AnswerSoundPoint>.Empty;
                    return;
                }
                //Generate ClockSounds
                var firstBpm = 0f;
                if (!chart.NoteTimings.IsEmpty)
                {
                    firstBpm = chart.NoteTimings[0].Bpm;
                }
                var interval = 60 / firstBpm;
                using PooledList<AnswerSoundPoint> answerTimingPoints = new();

                if (!isPracticeMode)
                {
                    if (chart.NoteTimings.Any(o => o.Timing < clockCount * interval))
                    {
                        //if there is something in first measure, we add clock before the bgm
                        for (var i = 0; i < clockCount; i++)
                        {
                            var timing = -(i + 1) * interval;
                            var isClock = true;
                            answerTimingPoints.Add(new AnswerSoundPoint(timing, isClock)
                            {
                                IsPlayed = false
                            });
                        }
                    }
                    else
                    {
                        //if nothing there, we can add it with bgm
                        for (var i = 0; i < clockCount; i++)
                        {
                            var timing = i * interval;
                            var isClock = true;
                            answerTimingPoints.Add(new AnswerSoundPoint(timing, isClock)
                            {
                                IsPlayed = false
                            });
                        }
                    }
                }

                //Generate AnwserSounds
                using var simaiTimingBuffer = new PooledList<SimaiTimingPoint>();
                simaiTimingBuffer.AddRange(chart.NoteTimings);
                var timings = simaiTimingBuffer.SelectMany(x =>
                                          {
                                              var timing = x.Timing + _answerOffsetSec;
                                              using var buffer = new PooledList<AnswerSoundPoint>();
                                              var isAnswer = false;
                                              var isMine = false;
                                              foreach (var note in x.Notes)
                                              {
                                                  if(note.IsSlideNoHead)
                                                  {
                                                      continue;
                                                  }
                                                  if(note.IsMine)
                                                  {
                                                      isMine = true;
                                                  }
                                                  else
                                                  {
                                                      isAnswer = true;
                                                  }
                                                  var isHold = note.Type is SimaiNoteType.Hold or SimaiNoteType.TouchHold;
                                                  if (isHold && note.HoldTime >= MajEnv.FRAME_LENGTH_SEC)
                                                  {
                                                      var holdEndTiming = (float)(timing + note.HoldTime);
                                                      buffer.Add(new AnswerSoundPoint(holdEndTiming, false, note.IsMine, !note.IsMine)
                                                      {
                                                          IsPlayed = false
                                                      });
                                                  }
                                              }
                                              if(isAnswer || isMine)
                                              {
                                                  buffer.Add(new AnswerSoundPoint((float)timing, false, isMine, isAnswer)
                                                  {
                                                      IsPlayed = false
                                                  });
                                              }                                              

                                              return buffer.ToArray();
                                          })
                                          .GroupBy(x => x.Timing)
                                          .Select(x =>
                                          {
                                              var isMine = false;
                                              var isNormal = false;
                                              foreach (var point in x)
                                              {
                                                  if (point.IsMine)
                                                  {
                                                      isMine = true;
                                                  }
                                                  if (point.IsNormal)
                                                  {
                                                      isNormal = true;
                                                  }
                                              }
                                              return new AnswerSoundPoint(x.Key, false, isMine, isNormal)
                                              {
                                                  IsPlayed = false
                                              };
                                          })
                                          .OrderBy(x => x.Timing);
                using (var timingBuffer = new PooledList<AnswerSoundPoint>())
                {
                    timingBuffer.AddRange(timings);
                    for (var i = 0; i < timingBuffer.Count; i++)
                    {
                        var isLast = i == timingBuffer.Count - 1;
                        var current = timingBuffer[i];
                        if (current.IsClock)
                        {
                            continue;
                        }
                        else if (isLast)
                        {
                            break;
                        }                        
                        var next = timingBuffer[i + 1];
                        var timingDiff = next.Timing - current.Timing;
                        if (timingDiff < MajEnv.FRAME_LENGTH_SEC)
                        {
                            timingBuffer.RemoveAt(i + 1);
                            current.IsMine |= next.IsMine;
                            current.IsNormal |= next.IsNormal;
                            timingBuffer[i] = current;
                            i--;
                        }                        
                    }
                    answerTimingPoints.AddRange(timingBuffer);
                }

                _rentedArrayForAnswerSoundPoints = Pool<AnswerSoundPoint>.Rent(answerTimingPoints.Count, true);
                foreach(var (i, tp) in answerTimingPoints.OrderBy(o => o.Timing).WithIndex())
                {
                    _rentedArrayForAnswerSoundPoints[i] = tp;
                }
                print("TimingPoint Count " + answerTimingPoints.Count);
                _answerTimingPoints = _rentedArrayForAnswerSoundPoints.AsMemory(0, answerTimingPoints.Capacity);
                FirstClockTiming = _answerTimingPoints.Span[0].Timing;
            });
        }
        internal void Clear()
        {
            _answerTimingPoints = Memory<AnswerSoundPoint>.Empty;
            foreach (var sfx in _noteSFXs)
            {
                sfx.Stop();
            }
            for (var i = 0; i < _noteSFXPlaybackRequests.Length; i++)
            {
                _noteSFXPlaybackRequests[i] = false;
            }
            _isTouchHoldRiserPlaying = false;
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTouchSound()
        {
            _noteSFXPlaybackRequests[TOUCH] = true;
            //_audioManager.PlaySFX("touch.wav");
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayHanabiSound()
        {
            _noteSFXPlaybackRequests[FIREWORK] = true;
            //_audioManager.PlaySFX("touch_hanabi.wav");
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlayTouchHoldSound()
        {
            _noteSFXPlaybackRequests[TOUCHHOLD] = true;
            //var riser = _audioManager.GetSFX("touch_Hold_riser.wav");
            //if(!riser.IsPlaying)
            //    _audioManager.PlaySFX("touch_Hold_riser.wav");
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StopTouchHoldSound()
        {
            _noteSFXPlaybackRequests[TOUCHHOLD] = false;
            //_audioManager.StopSFX("touch_Hold_riser.wav");
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlaySlideSound(bool isBreak)
        {
            if (isBreak)
            {
                _noteSFXPlaybackRequests[BREAK_SLIDE] = true;
                //_audioManager.PlaySFX("slide_break_start.wav");
            }
            else
            {
                _noteSFXPlaybackRequests[SLIDE] = true;
                //_audioManager.PlaySFX("slide.wav");
            }
        }
        [Il2CppSetOption(Option.NullChecks, false)]
        [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        public void PlayBreakSlideEndSound()
        {
            _noteSFXPlaybackRequests[BREAK_SLIDE_JUDGE] = true;
            _noteSFXPlaybackRequests[BREAK_SFX] = true;
            //_audioManager.PlaySFX("slide_break_slide.wav");
            //_audioManager.PlaySFX("break_slide.wav");
        }
        struct AnswerSoundPoint
        {
            public readonly float Timing;
            public readonly bool IsClock;
            public bool IsMine;
            public bool IsNormal;
            public bool IsPlayed;

            public AnswerSoundPoint(float timing, bool isClock, bool isMine = false, bool isNormal = true)
            {
                Timing = timing;
                IsClock = isClock;
                IsMine = isMine;
                IsNormal = isNormal;
            }
        }
    }
}

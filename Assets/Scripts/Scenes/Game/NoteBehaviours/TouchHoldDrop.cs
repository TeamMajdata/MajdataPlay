using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Rendering;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed class TouchHoldDrop : NoteLongDrop, INoteQueueMember<TouchQueueInfo>, IPoolableNote<TouchHoldPoolingInfo, TouchQueueInfo>, IMajComponent
    {
        public TouchGroup? GroupInfo { get; private set; } = null;
        public TouchHoldGroup? BodyGroupInfo { get; private set; } = null;
        public TouchQueueInfo QueueInfo { get; private set; } = TouchQueueInfo.Default;

        public char areaPosition;
        public bool isFirework;

        Sprite board_On;
        Sprite board_Off;

        readonly GameObject[] _fans = new GameObject[4];
        readonly Transform[] _fanTransforms = new Transform[4];
        readonly SpriteRenderer[] _fanRenderers = new SpriteRenderer[4];

        ButtonZone? _buttonPos;

        float displayDuration;
        float moveDuration;
        float wholeDuration;

        GameObject _pointObject;
        GameObject _borderObject;
        SpriteRenderer _pointRenderer;
        RawSpriteRenderer _borderRenderer;
        NotePoolManager _notePoolManager;
        MultTouchHandler _multTouchHandler;
        private MaterialPropertyBlock _borderMpb;

        int _lastHoldState = HOLD_STATE_NONE;
        int _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;

        [ReadOnlyField]
        [SerializeField]
        float _waitReleaseTimeSec = 0;

        Range<float> _bodyCheckRange;
        //readonly float _touchPanelOffset = MajEnv.UserSetting?.Judge.TouchPanelOffset ?? 0;

        const int _fanSpriteSortOrder = 2;
        const int _borderSortOrder = 6;
        const int _pointBorderSortOrder = 1;

        private static readonly int s_ProgressPropertyId = Shader.PropertyToID("_Progress");

        protected override void Awake()
        {
            base.Awake();
            _notePoolManager = Majdata<NotePoolManager>.Instance!;
            _multTouchHandler = Majdata<MultTouchHandler>.Instance!;

            _fanTransforms[0] = Transform.GetChild(4);
            _fanTransforms[1] = Transform.GetChild(3);
            _fanTransforms[2] = Transform.GetChild(2);
            _fanTransforms[3] = Transform.GetChild(1);

            _fans[0] = _fanTransforms[0].gameObject;
            _fans[1] = _fanTransforms[1].gameObject;
            _fans[2] = _fanTransforms[2].gameObject;
            _fans[3] = _fanTransforms[3].gameObject;

            for (var i = 0; i < 4; i++)
            {
                _fanRenderers[i] = _fans[i].GetComponent<SpriteRenderer>();
            }

            _pointObject = transform.GetChild(5).gameObject;
            _borderObject = transform.GetChild(0).gameObject;
            _pointRenderer = _pointObject.GetComponent<SpriteRenderer>();
            _borderRenderer = _borderObject.GetComponent<RawSpriteRenderer>();
            _borderMpb = new();

            _pointObject.SetActive(true);
            _borderObject.SetActive(true);

            Transform.position = new Vector3(0, 0, 0);
            SetFansColor(new Color(1f, 1f, 1f, 0f));
            SetFansPosition(0.4f);

            SetActiveWithRenderer(false);

            for (var i = 0; i < _fanRenderers.Length; i++)
            {
                var renderer = _fanRenderers[i];
                renderer.enabled = false;
            }
            _borderRenderer.enabled = false;

            Transform.localScale *= USERSETTING_TOUCH_SCALE;
        }
        public void Init(TouchHoldPoolingInfo poolingInfo)
        {
            if (State >= NoteStatus.Inited && State < NoteStatus.End)
            {
                return;
            }

            StartPos = poolingInfo.StartPos;
            areaPosition = poolingInfo.AreaPos;
            Timing = poolingInfo.Timing - TOUCH_HOLD_DISPLAY_OFFSET_SEC;
            JudgeTiming = poolingInfo.Timing;
            SortOrder = poolingInfo.NoteSortOrder;
            Speed = poolingInfo.Speed;
            IsEach = poolingInfo.IsEach;
            IsBreak = poolingInfo.IsBreak;
            IsEX = poolingInfo.IsEX;
            IsMine = poolingInfo.IsMine;
            QueueInfo = poolingInfo.QueueInfo;
            GroupInfo = poolingInfo.GroupInfo;
            BodyGroupInfo = poolingInfo.TouchHoldGroupInfo;
            IsJudged = false;
            _lastHoldState = HOLD_STATE_NONE;
            _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;
            Length = poolingInfo.LastFor;
            isFirework = poolingInfo.IsFirework;
            SensorPos = poolingInfo.SensorPos;
            JudgeResult = JudgeGrade.Miss;
            if (SensorPos < SensorArea.B1 && SensorPos >= SensorArea.A1)
            {
                _buttonPos = SensorPos.ToButtonZone();
            }
            else
            {
                _buttonPos = null;
            }
            PlayerReleaseTimeSec = 0;
            JudgableRange = new(JudgeTimingWithOffset - 0.15f, JudgeTimingWithOffset + 0.316667f, ContainsType.Closed);
            _waitReleaseTimeSec = 0;

            if (Length <= TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC + TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC)
            {
                _bodyCheckRange = DEFAULT_HOLD_BODY_CHECK_RANGE;
            }
            else
            {
                _bodyCheckRange = new Range<float>(Timing + TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC, Timing + Length - TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC, ContainsType.Closed);
            }

            wholeDuration = 3.209385682f * Mathf.Pow(Speed, -0.9549621752f);
            moveDuration = 0.8f * wholeDuration;
            displayDuration = 0.2f * wholeDuration;

            LoadSkin();

            SetBorderProgress(0f);
            SetFansColor(new Color(1f, 1f, 1f, 0f));
            SetActiveWithoutRenderer(true);

            Transform.position = NoteHelper.GetTouchAreaPosition(SensorPos);
            SetFansPosition(0.4f);

            for (var i = 0; i < 4; i++)
            {
                _fanRenderers[i].sortingOrder = SortOrder - (_fanSpriteSortOrder + i);
            }
            _pointRenderer.sortingOrder = SortOrder - _pointBorderSortOrder;
            _borderRenderer.SortingOrder = SortOrder - _borderSortOrder;

            State = NoteStatus.Inited;
        }
        private void End()
        {
            if (IsEnded)
            {
                return;
            }

            State = NoteStatus.End;
            _multTouchHandler.Unregister(SensorPos);
            BodyGroupInfo?.UnregisterTrigger(InstanceID);
            BodyGroupInfo?.Exit();
            if (!IsMine)
            {
                if (_lastHoldState == HOLD_STATE_RELEASED ||
                    (_lastHoldState == HOLD_STATE_NONE && JudgeResult == JudgeGrade.Miss))
                {
                    PlayerReleaseTimeSec += MajTimeline.DeltaTime;
                }
                JudgeResult = HoldEndJudge(JudgeResult, TOUCH_HOLD_HEAD_IGNORE_LENGTH_SEC + TOUCH_HOLD_TAIL_IGNORE_LENGTH_SEC);
            }
            ConvertJudgeGrade(ref JudgeResult);
            var result = new NoteJudgeResult()
            {
                Grade = JudgeResult,
                IsBreak = IsBreak,
                IsMine = IsMine,
                IsEX = IsEX,
                Diff = JudgeDiff,
            };
            SetActive(false);

            ObjectCounter.ReportResult(this, result);
            if (!IsJudged)
            {
                NoteManager.NextTouch(QueueInfo);
            }
            if (isFirework && !result.IsMissOrTooFast)
            {
                EffectManager.PlayFireworkEffect(transform.position);
            }

            PlayJudgeSFX(new NoteJudgeResult()
            {
                Grade = JudgeResult,
                IsBreak = false,
                IsEX = false,
                Diff = JudgeDiff,
                IsMine = IsMine
            });
            _lastHoldState = HOLD_STATE_NONE;
            _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;
            AudioEffMana.StopTouchHoldSound();
            EffectManager.PlayTouchHoldJudgeResult(SensorPos, result);
            EffectManager.ResetHoldEffect(SensorPos);
            _notePoolManager.Collect(this);
        }

        protected override void LoadSkin()
        {
            var skin = MajInstances.SkinManager.GetTouchHoldSkin();

            SetFansMaterial(DefaultMaterial);
            if(IsMine)
            {
                if (IsBreak)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        _fanRenderers[i].sprite = skin.Fans_Break_Mine[i];
                    }
                    _borderRenderer.Sprite = skin.Boader_Break_Mine; // TouchHold Border
                    _pointRenderer.sprite = skin.Point_Break_Mine;
                    board_On = skin.Boader_Break_Mine;
                    SetFansMaterial(BreakMaterial);
                }
                else
                {
                    for (var i = 0; i < 4; i++)
                    {
                        _fanRenderers[i].sprite = skin.Fans_Mine[i];
                    }
                    _borderRenderer.Sprite = skin.Boader_Mine; // TouchHold Border
                    _pointRenderer.sprite = skin.Point_Mine;
                    board_On = skin.Boader_Mine;
                }
            }
            else
            {
                if (IsBreak)
                {
                    for (var i = 0; i < 4; i++)
                    {
                        _fanRenderers[i].sprite = skin.Fans_Break[i];
                    }
                    _borderRenderer.Sprite = skin.Boader_Break; // TouchHold Border
                    _pointRenderer.sprite = skin.Point_Break;
                    board_On = skin.Boader_Break;
                    SetFansMaterial(BreakMaterial);
                }
                else
                {
                    for (var i = 0; i < 4; i++)
                    {
                        _fanRenderers[i].sprite = skin.Fans[i];
                    }
                    _borderRenderer.Sprite = skin.Boader; // TouchHold Border
                    if (IsEach)
                    {
                        _pointRenderer.sprite = skin.Point_Each;
                    }
                    else
                    {
                        _pointRenderer.sprite = skin.Point;
                    }
                    board_On = skin.Boader;
                }
            }
            
            board_Off = skin.Off;
        }
        protected override void Judge(float currentSec)
        {
            if (IsJudged)
            {
                return;
            }

            var diffSec = currentSec - JudgeTimingWithOffset;
            var isFast = diffSec < 0;
            JudgeDiff = diffSec * 1000;
            var diffMSec = MathF.Abs(diffSec * 1000);

            if (isFast && diffMSec > TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC)
            {
                return;
            }

            var result = diffMSec switch
            {
                <= TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC => JudgeGrade.Perfect,
                <= TOUCH_JUDGE_SEG_2ND_PERFECT_MSEC => JudgeGrade.LatePerfect2nd,
                <= TOUCH_JUDGE_SEG_3RD_PERFECT_MSEC => JudgeGrade.LatePerfect3rd,
                <= TOUCH_JUDGE_SEG_1ST_GREAT_MSEC => JudgeGrade.LateGreat,
                <= TOUCH_JUDGE_SEG_2ND_GREAT_MSEC => JudgeGrade.LateGreat2nd,
                <= TOUCH_JUDGE_SEG_3RD_GREAT_MSEC => JudgeGrade.LateGreat3rd,
                _ => JudgeGrade.LateGood
            };

            ConvertJudgeGrade(ref result);
            JudgeResult = result;
            IsJudged = true;
            BodyGroupInfo?.RegisterTrigger(InstanceID);
            _lastHeadState = HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK;
        }
        [OnPreUpdate]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("TouchHoldDrop.OnPreUpdate"))
            {
                TooLateCheck();
                HeadCheck();
                MineHeadCheck();
                MineBodyCheck();
                BodyCheck();
                ForceEndCheck();
                Autoplay();
            }
        }
        [OnUpdate]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("TouchHoldDrop.OnUpdate"))
            {
                var timing = GetTimeSpanToArriveTiming();

                switch (State)
                {
                    case NoteStatus.Inited:
                        if (-timing < wholeDuration)
                        {
                            _multTouchHandler.Register(SensorPos, IsEach, IsBreak);
                            SetPointActive(true);
                            SetFanActive(true);
                            State = NoteStatus.Scaling;
                            goto case NoteStatus.Scaling;
                        }
                        return;
                    case NoteStatus.Scaling:
                        {
                            var newColor = Color.white;
                            if (-timing < moveDuration)
                            {
                                SetFansColor(Color.white);
                                State = NoteStatus.Running;
                                goto case NoteStatus.Running;
                            }
                            var alpha = ((wholeDuration + timing) / displayDuration).Clamp(0, 1);
                            newColor.a = alpha;
                            SetFansColor(newColor);
                        }
                        return;
                    case NoteStatus.Running:
                        {
                            var pow = -Mathf.Exp(8 * (timing * 0.43f / moveDuration) - 0.85f) + 0.42f;
                            var distance = Mathf.Clamp(pow, 0f, 0.4f);
                            if (float.IsNaN(distance))
                            {
                                distance = 0f;
                            }
                            if (timing >= 0)
                            {
                                var _pow = -Mathf.Exp(-0.85f) + 0.42f;
                                var _distance = Mathf.Clamp(_pow, 0f, 0.4f);
                                SetFansPosition(_distance);
                                SetBorderActive(true);
                                State = NoteStatus.Arrived;
                                goto case NoteStatus.Arrived;
                            }
                            else
                            {
                                SetFansPosition(distance);
                            }
                        }
                        return;
                    case NoteStatus.Arrived:
                        {
                            var value = (1 - ((Length - timing) / Length));
                            var progress = value.Clamp(0, 1f);
                            SetBorderProgress(progress);
                        }
                        return;
                }
            }
        }
        private void RegisterGrade()
        {
            if (GroupInfo is not null && !JudgeResult.IsMissOrTooFast())
            {
                GroupInfo.JudgeResult = JudgeResult;
                GroupInfo.JudgeDiff = JudgeDiff;
                GroupInfo.RegisterResult(JudgeResult);
            }
        }
        private void TooLateCheck()
        {
            // Too late check
            if (IsEnded || IsJudged)
            {
                return;
            }

            var timing = GetTimeSpanToJudgeTiming();
            var isTooLate = timing > TOUCH_JUDGE_GOOD_AREA_MSEC / 1000;

            if (!isTooLate)
            {
                if (GroupInfo is not null)
                {
                    if (GroupInfo.Percent > 0.5f && GroupInfo.JudgeResult != null)
                    {
                        IsJudged = true;
                        JudgeResult = (JudgeGrade)GroupInfo.JudgeResult;
                        JudgeDiff = GroupInfo.JudgeDiff;
                        _lastHeadState = HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK;
                        NoteManager.NextTouch(QueueInfo);
                        BodyGroupInfo?.RegisterTrigger(InstanceID);
                    }
                }
            }
            else
            {
                JudgeResult = JudgeGrade.Miss;
                IsJudged = true;
                JudgeDiff = TOUCH_JUDGE_GOOD_AREA_MSEC;
                _lastHeadState = HOLD_HEAD_STATE_JUDGED;
                NoteManager.NextTouch(QueueInfo);
            }
        }
        private void MineHeadCheck()
        {
            if (!IsMine || IsEnded || !IsInited || IsJudged)
            {
                return;
            }
            if (GetTimeSpanToJudgeTiming() > 0)
            {
                IsJudged = true;
                JudgeResult = JudgeGrade.Perfect;
                NoteManager.NextTouch(QueueInfo);
                EffectManager.PlayHoldEffect(SensorPos, JudgeResult);
                PlayHoldEffect();
                _lastHoldState = HOLD_STATE_PRESSED;
            }
        }
        private void HeadCheck()
        {
            if (IsEnded || !IsInited || IsJudged || AutoplayMode == AutoplayModeOption.Enable)
            {
                return;
            }
            else if (!JudgableRange.InRange(ThisFrameSec) || !NoteManager.IsCurrentNoteJudgeable(QueueInfo))
            {
                return;
            }

#if UNITY_ANDROID || UNITY_IOS
            if (NoteManager.IsSensorClickedInThisFrame(SensorPos) && NoteManager.TryUseSensorClickEvent(SensorPos))
            {
                Judge(ThisFrameSec - USERSETTING_TOUCHPANEL_OFFSET_SEC);
            }
            else
            {
                return;
            }
#else
            if (IsUseButtonRingForTouch &&
                NoteManager.IsButtonClickedInThisFrame(_buttonPos) &&
                NoteManager.TryUseButtonClickEvent(_buttonPos))
            {
                Judge(ThisFrameSec);
            }
            else if (NoteManager.IsSensorClickedInThisFrame(SensorPos) && NoteManager.TryUseSensorClickEvent(SensorPos))
            {
                Judge(ThisFrameSec - USERSETTING_TOUCHPANEL_OFFSET_SEC);
            }
            else
            {
                return;
            }
#endif
            if (IsJudged)
            {
                var isMineForceEnd = false;
                if (IsMine)
                {
                    if (JudgeResult >= JudgeGrade.Perfect)
                    {
                        JudgeResult = JudgeGrade.TooFast;
                        isMineForceEnd = true;
                    }
                    else
                    {
                        JudgeResult = JudgeGrade.Miss;
                        isMineForceEnd = true;
                    }
                }
                NoteManager.NextTouch(QueueInfo);
                EffectManager.PlayHoldEffect(SensorPos, JudgeResult);
                RegisterGrade();
                if (isMineForceEnd)
                {
                    End();
                }
            }
        }
        private void MineBodyCheck()
        {
            if (!IsMine)
            {
                return;
            }
            else if (!IsInited || IsEnded || !IsJudged)
            {
                return;
            }
            var on = NoteManager.CheckSensorStatusInThisFrame(SensorPos, SwitchStatus.On);

            if (on)
            {
                JudgeResult = JudgeGrade.Miss;
                End();
            }
        }
        private void BodyCheck()
        {
            if (IsMine)
            {
                return;
            }
            else if (!IsInited || IsEnded)
            {
                return;
            }
            if (_lastHeadState is HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK || 
                (_lastHeadState is HOLD_HEAD_STATE_JUDGED && JudgeResult != JudgeGrade.Miss) ||
                _lastHoldState is HOLD_STATE_PRESSED)
            {
                AudioEffMana.PlayTouchHoldSound();
            }

            if (!_bodyCheckRange.InRange(ThisFrameSec) || !NoteController.IsStart)
            {
                if (_lastHeadState == HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK && GetRemainingTime() < Length)
                {
                    EffectManager.PlayHoldEffect(SensorPos, JudgeResult);
                    _lastHeadState = HOLD_HEAD_STATE_JUDGED;
                }
                return;
            }
            var on = NoteManager.CheckSensorStatusInThisFrame(SensorPos, SwitchStatus.On);

            if(BodyGroupInfo is not null)
            {
                if (on)
                {
                    BodyGroupInfo.RegisterTrigger(InstanceID);
                }
                else
                {
                    BodyGroupInfo.UnregisterTrigger(InstanceID);
                }
                on |= BodyGroupInfo.Percent > 0.5f;
            }

            if (on || IsAutoplay)
            {
                PlayHoldEffect();
                _waitReleaseTimeSec = 0;
                _lastHoldState = HOLD_STATE_PRESSED;
            }
            else
            {
                var isNeverBeenReleased = _lastHoldState != HOLD_STATE_RELEASED;
                var isForgiving = _waitReleaseTimeSec <= DELUXE_HOLD_RELEASE_IGNORE_TIME_SEC;
                var isHeadJudged = _lastHeadState != HOLD_HEAD_STATE_NOT_JUDGED;
                if (isHeadJudged && isNeverBeenReleased && isForgiving)
                {
                    _waitReleaseTimeSec += MajTimeline.DeltaTime;
                    return;
                }
                else if (_waitReleaseTimeSec != 0)
                {
                    PlayerReleaseTimeSec += _waitReleaseTimeSec;
                    _waitReleaseTimeSec = 0;
                }
                PlayerReleaseTimeSec += MajTimeline.DeltaTime;
                StopHoldEffect();
                _lastHoldState = HOLD_STATE_RELEASED;
            }
        }
        private void ForceEndCheck()
        {
            if (!IsJudged || IsEnded)
            {
                return;
            }

            var remainingTime = GetRemainingTime();

            if (remainingTime == 0)
            {
                End();
            }
        }
        public override void SetActive(bool state)
        {
            if (Active == state)
            {
                return;
            }
            SetActiveWithRenderer(state);
        }
        private void SetActiveWithoutRenderer(bool state)
        {
            base.SetActive(state);
        }
        private void SetActiveWithRenderer(bool state)
        {
            base.SetActive(state);
            SetFanActive(state);
            SetBorderActive(state);
            SetPointActive(state);
        }
        private void SetFanActive(bool state)
        {
            for (var i = 0; i < _fanRenderers.Length; i++)
            {
                _fanRenderers[i].enabled = state;
            }
        }
        private void SetPointActive(bool state)
        {
            _pointRenderer.enabled = state;
        }
        private void SetBorderActive(bool state)
        {
            _borderRenderer.enabled = state;
        }

        private void SetFansPosition(in float distance)
        {
            for (var i = 0; i < 4; i++)
            {
                var pos = (0.226f + distance) * GetAngle(i);
                _fanTransforms[i].localPosition = pos;
            }
        }
        private void PlayHoldEffect()
        {
            //var r = MajInstances.AudioManager.GetSFX("touch_Hold_riser.wav");
            //MajDebug.Log($"IsPlaying:{r.IsPlaying}\nCurrent second: {r.CurrentSec}s");
            if (_lastHoldState != HOLD_STATE_PRESSED)
            {
                EffectManager.PlayHoldEffect(SensorPos, JudgeResult);
                _borderRenderer.Sprite = board_On;
                if (_lastHoldState < HOLD_STATE_RELEASED)
                {
                    SetFansMaterial(DefaultMaterial);
                }
            }
        }
        private void StopHoldEffect()
        {
            if (_lastHoldState != HOLD_STATE_RELEASED)
            {
                EffectManager.ResetHoldEffect(SensorPos);
                _borderRenderer.Sprite = board_Off;
                if (_lastHoldState < HOLD_STATE_RELEASED)
                {
                    SetFansMaterial(DefaultMaterial);
                }
            }
        }
        private Vector3 GetAngle(int index)
        {
            var angle = Mathf.PI / 4 + index * (Mathf.PI / 2);
            return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
        }
        private void SetFansColor(Color color)
        {
            foreach (var fan in _fanRenderers.AsSpan())
                fan.color = color;
        }
        private void SetFansMaterial(Material material)
        {
            for (var i = 0; i < 4; i++)
            {
                _fanRenderers[i].sharedMaterial = material;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetBorderProgress(float progress)
        {
            _borderRenderer.GetPropertyBlock(_borderMpb);
            _borderMpb.SetFloat(s_ProgressPropertyId, progress);
            _borderRenderer.SetPropertyBlock(_borderMpb);
        }
        protected override void PlaySFX()
        {
            AudioEffMana.PlayTouchHoldSound();
        }
        protected override void PlayJudgeSFX(in NoteJudgeResult judgeResult)
        {
            AudioEffMana.PlayTapSound(judgeResult);
            if (isFirework)
            {
                //if user touched the mine we dont get firework
                if (judgeResult.IsMissOrTooFast)
                    return;
                AudioEffMana.PlayHanabiSound();
            }
        }

        #region Autoplay Implementation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void Autoplay()
        {
            switch (AutoplayMode)
            {
                case AutoplayModeOption.Enable:
                    if (!IsAutoplay)
                    {
                        return;
                    }
                    else if (IsJudged)
                    {
                        if (GetRemainingTime() == 0)
                        {
                            End();
                        }
                        return;
                    }
                    if (GetTimeSpanToJudgeTiming() >= -0.016667f)
                    {
                        var autoplayGrade = AutoplayGrade;
                        if (((int)autoplayGrade).InRange(0, 14))
                        {
                            JudgeResult = autoplayGrade;
                        }
                        else
                        {
                            JudgeResult = (JudgeGrade)Randomizer.Next(0, 15);
                        }
                        ConvertJudgeGrade(ref JudgeResult);
                        IsJudged = true;
                        JudgeDiff = JudgeResult switch
                        {
                            < JudgeGrade.Perfect => 1,
                            > JudgeGrade.Perfect => -1,
                            _ => 0
                        };
                        PlayJudgeSFX(new NoteJudgeResult()
                        {
                            Grade = JudgeResult,
                            IsBreak = IsBreak,
                            IsEX = IsEX,
                            IsMine = IsMine,
                            Diff = JudgeDiff
                        });
                        _lastHeadState = HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK;
                    }
                    break;
                case AutoplayModeOption.DJAuto_TouchPanel_First:
                case AutoplayModeOption.DJAuto_ButtonRing_First:
                    DJAutoplay();
                    break;
            }

        }
        private void DJAutoplay()
        {
            if (!IsAutoplay || IsEnded)
            {
                return;
            }
            else if (IsJudged)
            {
                NoteManager.SimulateSensorPress(SensorPos);
                return;
            }
            else if (!NoteManager.IsCurrentNoteJudgeable(QueueInfo))
            {
                return;
            }
            else if (GetTimeSpanToArriveTiming() < -FRAME_LENGTH_SEC)
            {
                return;
            }
            NoteManager.SimulateSensorClick(SensorPos);
        }

        #endregion
    }
}
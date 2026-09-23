using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Editor;
using MajdataPlay.Game.Notes;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed class HoldDrop : NoteLongDrop, IDistanceProvider, INoteQueueMember<TapQueueInfo>, IPoolableNote<HoldPoolingInfo, TapQueueInfo>, IMajComponent
    {
        public TapQueueInfo QueueInfo { get; set; } = TapQueueInfo.Default;
        public float Distance { get; private set; } = -100;

        [SerializeField]
        GameObject _tapLinePrefab;

        EachLineBinding? _eachLineBinding;

        Sprite _holdSprite;
        Sprite _holdOnSprite;
        Sprite _holdOffSprite;

        GameObject _exObject;
        GameObject _endPointObject;
        GameObject _tapLineObject;

        Transform _exTransform;
        Transform _endPointTransform;
        Transform _tapLineTransform;

        SpriteRenderer _exRenderer;
        SpriteRenderer _endRenderer;
        SpriteRenderer _thisRenderer;
        SpriteRenderer _tapLineRenderer;

        NotePoolManager _poolManager;

        Vector3 _innerPos = NoteHelper.GetTapPosition(1, 1.225f);
        Vector3 _outerPos = NoteHelper.GetTapPosition(1, 4.8f);

        int _lastHoldState = HOLD_STATE_NONE;
        int _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;

        [ReadOnlyField]
        [SerializeField]
        float _waitReleaseTimeSec = 0;

        ButtonZone? _buttonPos;
        Range<float> _bodyCheckRange;

        readonly float _noteAppearRate = MajEnv.Settings?.Debug.NoteAppearRate ?? 0.265f;
        //readonly float _touchPanelOffset = MajEnv.UserSetting?.Judge.TouchPanelOffset ?? 0;

        const int _spriteSortOrder = 1;
        const int _exSortOrder = 0;
        const int _endSortOrder = 2;

        protected override void Awake()
        {
            base.Awake();
            _poolManager = FindFirstObjectByType<NotePoolManager>();
            var notes = NoteManager.gameObject.transform;

            _tapLineObject = Instantiate(_tapLinePrefab, notes.GetChild(7));
            _tapLineObject.SetActive(true);
            _tapLineTransform = _tapLineObject.transform;
            _tapLineRenderer = _tapLineObject.GetComponent<SpriteRenderer>();

            _exObject = Transform.GetChild(0).gameObject;
            _exTransform = _exObject.transform;
            _exRenderer = _exObject.GetComponent<SpriteRenderer>();

            _thisRenderer = GetComponent<SpriteRenderer>();

            _endPointObject = Transform.GetChild(1).gameObject;
            _endPointTransform = _endPointObject.transform;
            _endRenderer = _endPointObject.GetComponent<SpriteRenderer>();

            Transform.localScale = new Vector3(0, 0);

            SetActiveWithRenderer(false);
        }
        public void Init(HoldPoolingInfo poolingInfo)
        {
            if (State >= NoteStatus.Inited && State < NoteStatus.End)
            {
                return;
            }
            _eachLineBinding = poolingInfo.EachLineBinding;
            if (_eachLineBinding is not null)
            {
                _eachLineBinding.Bind(this);
            }
            StartPos = poolingInfo.StartPos;
            Timing = poolingInfo.Timing;
            JudgeTiming = Timing;
            SortOrder = poolingInfo.NoteSortOrder;
            Speed = poolingInfo.Speed;
            IsEach = poolingInfo.IsEach;
            IsBreak = poolingInfo.IsBreak;
            IsEX = poolingInfo.IsEX;
            IsMine = poolingInfo.IsMine;
            QueueInfo = poolingInfo.QueueInfo;
            IsJudged = false;
            Distance = -100;
            Length = poolingInfo.LastFor;
            _innerPos = NoteHelper.GetTapPosition(StartPos, 1.225f);
            _outerPos = NoteHelper.GetTapPosition(StartPos, 4.8f);
            SensorPos = (SensorArea)(StartPos - 1);
            _buttonPos = SensorPos.ToButtonZone();
            PlayerReleaseTimeSec = 0;
            JudgeResult = JudgeGrade.Miss;
            if (IsMine)
            {
                JudgableRange = new(JudgeTimingWithOffset - (TAP_JUDGE_SEG_3RD_PERFECT_MSEC / 1000), JudgeTimingWithOffset + (TAP_JUDGE_SEG_3RD_PERFECT_MSEC / 1000), ContainsType.Closed);
            }
            else
            {
                JudgableRange = new(JudgeTimingWithOffset - (TAP_JUDGE_GOOD_AREA_MSEC / 1000), JudgeTimingWithOffset + (TAP_JUDGE_GOOD_AREA_MSEC / 1000), ContainsType.Closed);
            }
            _lastHoldState = HOLD_STATE_NONE;
            _lastHeadState = HOLD_HEAD_STATE_NOT_JUDGED;
            _waitReleaseTimeSec = 0;

            if(IsClassic)
            {
                _bodyCheckRange = new Range<float>(JudgeTimingWithOffset - TAP_JUDGE_GOOD_AREA_MSEC / 1000, float.MaxValue, ContainsType.Closed);
            }
            else if (Length <= HOLD_HEAD_IGNORE_LENGTH_SEC + HOLD_TAIL_IGNORE_LENGTH_SEC)
            {
                _bodyCheckRange = DEFAULT_HOLD_BODY_CHECK_RANGE;
            }
            else
            {
                _bodyCheckRange = new Range<float>(JudgeTimingWithOffset + HOLD_HEAD_IGNORE_LENGTH_SEC, JudgeTimingWithOffset + Length - HOLD_TAIL_IGNORE_LENGTH_SEC, ContainsType.Closed);
            }

            Transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (StartPos - 1));
            Transform.localScale = new Vector3(0, 0);

            _tapLineTransform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (StartPos - 1));
            _thisRenderer.size = new Vector2(1.22f, 1.4f);
            _exRenderer.size = new Vector2(1.22f, 1.4f);
            _thisRenderer.sortingOrder = SortOrder - _spriteSortOrder;
            _exRenderer.sortingOrder = SortOrder - _exSortOrder;
            _endRenderer.sortingOrder = SortOrder - _endSortOrder;

            LoadSkin();
            SetActiveWithoutRenderer(true);

            State = NoteStatus.Inited;
        }
        private void End(float endJudgeOffset = 0)
        {
            if (IsEnded)
            {
                return;
            }

            State = NoteStatus.End;

            if (_eachLineBinding is not null)
            {
                _eachLineBinding.Unbind(this);
                _eachLineBinding = null;
            }
            if (!IsMine)
            {
                if (IsClassic)
                {
                    JudgeResult = HoldClassicEndJudge(JudgeResult, endJudgeOffset);
                }
                else
                {
                    if (_lastHoldState == HOLD_STATE_RELEASED ||
                        (_lastHoldState == HOLD_STATE_NONE && JudgeResult == JudgeGrade.Miss))
                    {
                        PlayerReleaseTimeSec += MajTimeline.DeltaTime;
                    }
                    JudgeResult = HoldEndJudge(JudgeResult, HOLD_HEAD_IGNORE_LENGTH_SEC + HOLD_TAIL_IGNORE_LENGTH_SEC);
                }
            }
            ConvertJudgeGrade(ref JudgeResult);

            var result = new NoteJudgeResult()
            {
                Grade = JudgeResult,
                IsBreak = IsBreak,
                IsMine = IsMine,
                IsEX = IsEX,
                Diff = JudgeDiff
            };
            PlayJudgeSFX(new NoteJudgeResult()
            {
                Grade = JudgeResult,
                IsMine = IsMine,
                IsBreak = false,
                IsEX = false,
                Diff = JudgeDiff
            });
            SetActive(false);
            EffectManager.ResetHoldEffect(StartPos);
            EffectManager.PlayTapJudgeResult(StartPos, result);
            ObjectCounter.ReportResult(this, result);
            _poolManager.Collect(this);
        }
        protected override void Judge(float currentSec)
        {
            base.Judge(currentSec);
            if (!IsJudged)
            {
                return;
            }
            _lastHeadState = HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK;
        }
        protected override void PlaySFX()
        {
            PlayJudgeSFX(new NoteJudgeResult()
            {
                Grade = JudgeResult,
                IsBreak = IsBreak,
                IsEX = IsEX,
                IsMine = IsMine,
                Diff = JudgeDiff
            });
        }
        protected override void PlayJudgeSFX(in NoteJudgeResult judgeResult)
        {
            AudioEffMana.PlayTapSound(judgeResult);
        }
        [OnPreUpdate]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("HoldDrop.OnPreUpdate"))
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
            using (UnityProfiler.Create("HoldDrop.OnUpdate"))
            {
                var timing = GetTimeSpanToArriveTiming();
                var distance = timing * Speed + 4.8f;
                var scaleRate = _noteAppearRate;
                var destScale = distance * scaleRate + (1 - scaleRate * 1.225f);

                var remaining = GetRemainingTimeWithoutOffset();
                var holdTime = timing - Length;
                var holdDistance = holdTime * Speed + 4.8f;

                switch (State)
                {
                    case NoteStatus.Inited:
                        if (destScale >= 0f)
                        {
                            _exRenderer.size = new Vector2(1.22f, 1.42f);
                            _thisRenderer.size = new Vector2(1.22f, 1.42f);
                            _tapLineTransform.localScale = new Vector3(0.2552f, 0.2552f, 1f);
                            Transform.position = _innerPos;
                            SetExRendererActive(true);
                            SetNoteRendererActive(true);

                            State = NoteStatus.Scaling;
                            goto case NoteStatus.Scaling;
                        }
                        return;
                    case NoteStatus.Scaling:
                        if (destScale > 0.3f)
                        {
                            SetTapLineActive(true);
                        }
                        if (distance < 1.225f)
                        {
                            Distance = distance;
                            Transform.localScale = new Vector3(destScale, destScale) * USERSETTING_HOLD_SCALE;
                        }
                        else
                        {
                            Transform.localScale = new Vector3(1f, 1f) * USERSETTING_HOLD_SCALE;
                            State = NoteStatus.Running;
                            goto case NoteStatus.Running;
                        }
                        break;
                    case NoteStatus.Running:
                        if (remaining == 0)
                        {
                            State = NoteStatus.Arrived;
                            goto case NoteStatus.Arrived;
                        }
                        if (holdDistance < 1.225f && distance >= 4.8f) // 头到达 尾未出现
                        {
                            holdDistance = 1.225f;
                            distance = 4.8f;
                        }
                        else if (holdDistance < 1.225f && distance < 4.8f) // 头未到达 尾未出现
                        {
                            holdDistance = 1.225f;
                        }
                        else if (holdDistance >= 1.225f && distance >= 4.8f) // 头到达 尾出现
                        {
                            distance = 4.8f;

                            SetEndPointActive(true);
                        }
                        else if (holdDistance >= 1.225f && distance < 4.8f) // 头未到达 尾出现
                        {
                            SetEndPointActive(true);
                        }
                        Distance = distance;
                        var dis = (distance - holdDistance) / 2 + holdDistance;
                        var size = (distance - holdDistance + 1.4f * USERSETTING_HOLD_SCALE) / USERSETTING_HOLD_SCALE;
                        var lineScale = Mathf.Abs(distance / 4.8f);

                        lineScale = lineScale >= 1f ? 1f : lineScale;

                        Transform.position = _outerPos * (dis / 4.8f); //0.325
                        _tapLineTransform.localScale = new Vector3(lineScale, lineScale, 1f);
                        _thisRenderer.size = new Vector2(1.22f, size);
                        _exRenderer.size = new Vector2(1.22f, size);
                        _endPointTransform.localPosition = new Vector3(0f, 0.6825f - size / 2);

                        break;
                    case NoteStatus.Arrived:
                        var endTiming = timing - Length;
                        var endDistance = endTiming * Speed + 4.8f;
                        var ratio = endDistance / 4.8f;
                        var scale = Mathf.Abs(ratio);
                        _tapLineTransform.localScale = new Vector3(1f, 1f, 1f);
                        Distance = endDistance;
                        Transform.position = _outerPos * ratio;
                        _tapLineTransform.localScale = new Vector3(scale, scale, 1f);
                        break;
                    default:
                        return;
                }
            }
        }
        private void TooLateCheck()
        {
            // Too late check
            if (IsEnded || IsJudged || AutoplayMode == AutoplayModeOption.Enable)
            {
                return;
            }

            var timing = GetTimeSpanToJudgeTiming();
            var isTooLate = timing > TAP_JUDGE_GOOD_AREA_MSEC / 1000;

            if (isTooLate)
            {
                JudgeResult = JudgeGrade.Miss;
                IsJudged = true;
                JudgeDiff = 150;
                _lastHeadState = HOLD_HEAD_STATE_JUDGED;
                NoteManager.NextNote(QueueInfo);
                if (USERSETTING_DISPLAY_HOLD_HEAD_JUDGE_RESULT)
                {
                    EffectManager.PlayTapJudgeResult(StartPos, new NoteJudgeResult()
                    {
                        Grade = JudgeResult,
                        IsBreak = IsBreak,
                        IsEX = IsEX,
                        Diff = JudgeDiff
                    });
                }
            }
        }
        private void MineHeadCheck()
        {
            if (!IsMine || IsEnded || !IsInited || IsJudged)
            {
                return;
            }
            if (GetTimeSpanToJudgeTiming() > TAP_JUDGE_SEG_3RD_PERFECT_MSEC / 1000)
            {
                IsJudged = true;
                JudgeResult = JudgeGrade.Perfect;
                NoteManager.NextNote(QueueInfo);
                EffectManager.ResetEffect(StartPos);
                PlayHoldEffect();
                _lastHoldState = HOLD_STATE_PRESSED;
            }
        }
        private void HeadCheck()
        {
            if (IsEnded || !IsInited || IsJudged)
            {
                return;
            }
            else if (!JudgableRange.InRange(ThisFrameSec) || !NoteManager.IsCurrentNoteJudgeable(QueueInfo))
            {
                return;
            }

            if (NoteManager.IsButtonClickedInThisFrame(_buttonPos) && NoteManager.TryUseButtonClickEvent(_buttonPos))
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
                PlaySFX();
                if (USERSETTING_DISPLAY_HOLD_HEAD_JUDGE_RESULT)
                {
                    EffectManager.PlayTapJudgeResult(StartPos, new NoteJudgeResult()
                    {
                        Grade = JudgeResult,
                        IsBreak = IsBreak,
                        IsEX = IsEX,
                        Diff = JudgeDiff
                    });
                }
                EffectManager.ResetEffect(StartPos);
                NoteManager.NextNote(QueueInfo);
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
            var isButtonPressed = NoteManager.CheckButtonStatusInThisFrame(_buttonPos, SwitchStatus.On);
            var isSensorPressed = NoteManager.CheckSensorStatusInThisFrame(SensorPos, SwitchStatus.On);
            var isPressed = isButtonPressed || isSensorPressed;

            if(isPressed)
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

            if (_lastHeadState is HOLD_HEAD_STATE_JUDGED or HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK || _lastHoldState is HOLD_STATE_PRESSED)
            {
                EffectManager.ResetEffect(StartPos);
            }

            if (_lastHeadState == HOLD_HEAD_STATE_JUDGED_AND_NOT_FEEDBACK && GetRemainingTime() < Length)
            {
                EffectManager.PlayHoldEffect(StartPos, JudgeResult);
                EffectManager.ResetEffect(StartPos);
                _lastHeadState = HOLD_HEAD_STATE_JUDGED;
                if(IsClassic)
                {
                    _thisRenderer.sprite = _holdOnSprite;
                    _thisRenderer.sharedMaterial = HoldShineMaterial;
                }
            }
            if (!_bodyCheckRange.InRange(ThisFrameSec) || !NoteController.IsStart)
            {
                return;
            }
            var isButtonPressed = NoteManager.CheckButtonStatusInThisFrame(_buttonPos, SwitchStatus.On);
            var isSensorPressed = NoteManager.CheckSensorStatusInThisFrame(SensorPos, SwitchStatus.On);
            var isPressed = isButtonPressed || isSensorPressed;

            if(IsClassic)
            {
                if (!IsJudged || AutoplayMode == AutoplayModeOption.Enable)
                {
                    return;
                }
                if (isPressed)
                {
                    if(GetRemainingTime() == 0)
                    {
                        EffectManager.ResetHoldEffect(StartPos);
                    }
                }
                else
                {
                    var isButtonReleased = NoteManager.CheckSensorStatusInPreviousFrame(SensorPos, SwitchStatus.On) &&
                                           !isButtonPressed;
                    var offset = isButtonReleased ? 0 : USERSETTING_TOUCHPANEL_OFFSET_SEC;
                    End(offset);
                }
            }
            else
            {
                if (isPressed || AutoplayMode == AutoplayModeOption.Enable)
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
                    else if(_waitReleaseTimeSec != 0)
                    {
                        PlayerReleaseTimeSec += _waitReleaseTimeSec;
                        _waitReleaseTimeSec = 0;
                    }
                    PlayerReleaseTimeSec += MajTimeline.DeltaTime;
                    StopHoldEffect();
                    _lastHoldState = HOLD_STATE_RELEASED;
                }
            }
        }
        private void ForceEndCheck()
        {
            if (!IsJudged || IsEnded)
            {
                return;
            }

            var timing = GetTimeSpanToJudgeTiming();
            var endTiming = timing - Length;
            var remainingTime = GetRemainingTime();

            if (IsClassic)
            {
                if (AutoplayMode == AutoplayModeOption.Enable && remainingTime == 0)
                {
                    End();
                }
                else if (endTiming > CLASSIC_HOLD_ALLOW_OVER_LENGTH_SEC || JudgeResult.IsMissOrTooFast())
                {
                    End();
                }
            }
            else if (remainingTime == 0)
            {
                End();
            }
        }
        private void PlayHoldEffect()
        {
            if (_lastHoldState != HOLD_STATE_PRESSED)
            {
                EffectManager.PlayHoldEffect(StartPos, JudgeResult);
                _thisRenderer.sprite = _holdOnSprite;
                _thisRenderer.sharedMaterial = HoldShineMaterial;
            }
        }
        private void StopHoldEffect()
        {
            if (_lastHoldState != HOLD_STATE_RELEASED)
            {
                EffectManager.ResetHoldEffect(StartPos);
                _thisRenderer.sprite = _holdOffSprite;
                _thisRenderer.sharedMaterial = DefaultMaterial;
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
            SetNoteRendererActive(state);
            SetExRendererActive(state);
            SetTapLineActive(state);
            SetEndPointActive(state);
        }
        private void SetNoteRendererActive(bool state)
        {
            _thisRenderer.enabled = state;
        }
        private void SetExRendererActive(bool state)
        {
            if (state)
            {
                _exRenderer.enabled = IsEX;
            }
            else
            {
                _exRenderer.enabled = false;
            }
        }
        private void SetTapLineActive(bool state)
        {
            switch (state)
            {
                case true:
                    _tapLineObject.layer = MajEnv.DEFAULT_LAYER;
                    break;
                case false:
                    _tapLineObject.layer = MajEnv.HIDDEN_LAYER;
                    break;
            }
        }
        private void SetEndPointActive(bool state)
        {
            _endRenderer.enabled = state;
        }
        protected override void LoadSkin()
        {
            var skin = MajInstances.SkinManager.GetHoldSkin();

            if (IsMine)
            {
                _holdSprite = skin.Mine;
                _holdOnSprite = skin.Mine_On;
                _holdOffSprite = skin.Off;

                _exRenderer.sprite = skin.Ex;
                _exRenderer.color = skin.ExEffects[0];
                _endRenderer.sprite = skin.Ends[0];
                _thisRenderer.sharedMaterial = DefaultMaterial;
                _tapLineRenderer.sprite = skin.GuideLines[0];

                if (IsBreak)
                {
                    _holdSprite = skin.BreakMine;
                    _holdOnSprite = skin.BreakMine_On;
                    _endRenderer.sprite = skin.Ends[2];
                    _thisRenderer.sharedMaterial = BreakMaterial;
                    _tapLineRenderer.sprite = skin.GuideLines[2];
                    _exRenderer.color = skin.ExEffects[2];
                }
            }
            else
            {
                _holdSprite = skin.Normal;
                _holdOnSprite = skin.Normal_On;
                _holdOffSprite = skin.Off;

                _exRenderer.sprite = skin.Ex;
                _exRenderer.color = skin.ExEffects[0];
                _endRenderer.sprite = skin.Ends[0];
                _thisRenderer.sharedMaterial = DefaultMaterial;
                _tapLineRenderer.sprite = skin.GuideLines[0];

                if (IsEach)
                {
                    _holdSprite = skin.Each;
                    _holdOnSprite = skin.Each_On;
                    _endRenderer.sprite = skin.Ends[1];
                    _tapLineRenderer.sprite = skin.GuideLines[1];
                    _exRenderer.color = skin.ExEffects[1];
                }

                if (IsBreak)
                {
                    _holdSprite = skin.Break;
                    _holdOnSprite = skin.Break_On;
                    _endRenderer.sprite = skin.Ends[2];
                    _thisRenderer.sharedMaterial = BreakMaterial;
                    _tapLineRenderer.sprite = skin.GuideLines[2];
                    _exRenderer.color = skin.ExEffects[2];
                }
            }
            _thisRenderer.sprite = _holdSprite;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void Autoplay()
        {
            if (IsMine)
            {
                return;
            }
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
                        PlaySFX();
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
            var isBtnFirst = AutoplayMode == AutoplayModeOption.DJAuto_ButtonRing_First;
            if (!IsAutoplay || IsEnded)
            {
                return;
            }
            else if (IsJudged)
            {
                var remainingTime = GetRemainingTime();
                if (remainingTime <= 2 * FRAME_LENGTH_SEC)
                {
                    return;
                }
                if (isBtnFirst)
                {
                    NoteManager.SimulateButtonPress(_buttonPos);
                }
                else
                {
                    NoteManager.SimulateSensorPress(SensorPos);
                }
                return;
            }
            else if (!NoteManager.IsCurrentNoteJudgeable(QueueInfo))
            {
                return;
            }
            else if (GetTimeSpanToArriveTiming() < (-FRAME_LENGTH_SEC * 2 + FRAME_LENGTH_SEC / 2))
            {
                return;
            }

            if (isBtnFirst)
            {
                _ = NoteManager.SimulateButtonClick(_buttonPos) ||
                    (USERSETTING_DJAUTO_POLICY == DJAutoPolicyOption.Permissive && NoteManager.SimulateSensorClick(SensorPos));
            }
            else
            {
                _ = NoteManager.SimulateSensorClick(SensorPos) ||
                    (USERSETTING_DJAUTO_POLICY == DJAutoPolicyOption.Permissive && NoteManager.SimulateButtonClick(_buttonPos));
            }
        }
    }
}
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    internal sealed class TouchDrop : NoteDrop, IPoolableNote<TouchPoolingInfo, TouchQueueInfo>, INoteQueueMember<TouchQueueInfo>, IMajComponent
    {
        public TouchGroup? GroupInfo { get; set; }
        public TouchQueueInfo QueueInfo { get; set; } = TouchQueueInfo.Default;


        bool _isFirework = false;
        /// <summary>
        /// Undefined
        /// </summary>
        float _displayDuration;
        /// <summary>
        /// Touch淡入结束，开始移动
        /// </summary>
        float _moveDuration;
        /// <summary>
        /// Touch开始淡入的时刻
        /// </summary>
        float _wholeDuration;

        readonly SpriteRenderer[] _fanRenderers = new SpriteRenderer[4];
        readonly GameObject[] _fans = new GameObject[4];
        readonly Transform[] _fanTransforms = new Transform[4];

        ButtonZone? _buttonPos;

        GameObject _pointObject;
        GameObject _justBorderObject;

        SpriteRenderer _pointRenderer;
        SpriteRenderer _justBorderRenderer;

        MultTouchHandler _multTouchHandler;
        NotePoolManager _notePoolManager;

        //readonly float _touchPanelOffset = MajEnv.UserSetting?.Judge.TouchPanelOffset ?? 0;

        const int _fanSpriteSortOrder = 3;
        const int _justBorderSortOrder = 1;
        const int _pointBorderSortOrder = 2;

        protected override void Awake()
        {
            base.Awake();
            _notePoolManager = Majdata<NotePoolManager>.Instance!;
            _multTouchHandler = Majdata<MultTouchHandler>.Instance!;

            _fanTransforms[0] = Transform.GetChild(3);
            _fanTransforms[1] = Transform.GetChild(2);
            _fanTransforms[2] = Transform.GetChild(1);
            _fanTransforms[3] = Transform.GetChild(4);

            _fans[0] = _fanTransforms[0].gameObject;
            _fans[1] = _fanTransforms[1].gameObject;
            _fans[2] = _fanTransforms[2].gameObject;
            _fans[3] = _fanTransforms[3].gameObject;

            for (var i = 0; i < 4; i++)
            {
                _fanRenderers[i] = _fans[i].GetComponent<SpriteRenderer>();
            }

            _pointObject = Transform.GetChild(0).gameObject;
            _pointRenderer = _pointObject.GetComponent<SpriteRenderer>();

            _justBorderObject = Transform.GetChild(5).gameObject;
            _justBorderRenderer = _justBorderObject.GetComponent<SpriteRenderer>();

            _pointObject.SetActive(true);
            _justBorderObject.SetActive(true);

            Transform.position = new Vector3(0, 0, 0);
            SetFansColor(new Color(1f, 1f, 1f, 0f));
            SetFansPosition(0.4f);

            SetActiveWithRenderer(false);

            for (var i = 0; i < _fanRenderers.Length; i++)
            {
                var renderer = _fanRenderers[i];
                renderer.enabled = false;
            }

            Transform.localScale *= USERSETTING_TOUCH_SCALE;
        }
        public void Init(TouchPoolingInfo poolingInfo)
        {
            if (State >= NoteStatus.Inited && State < NoteStatus.End)
            {
                return;
            }

            StartPos = poolingInfo.StartPos;
            Timing = poolingInfo.Timing - TOUCH_DISPLAY_OFFSET_SEC;
            JudgeTiming = poolingInfo.Timing;
            SortOrder = poolingInfo.NoteSortOrder;
            Speed = poolingInfo.Speed;
            IsEach = poolingInfo.IsEach;
            IsBreak = poolingInfo.IsBreak;
            IsEX = poolingInfo.IsEX;
            IsMine = poolingInfo.IsMine;
            QueueInfo = poolingInfo.QueueInfo;
            IsJudged = false;
            _isFirework = poolingInfo.IsFirework;
            GroupInfo = poolingInfo.GroupInfo;
            SensorPos = poolingInfo.SensorPos;
            if (SensorPos < SensorArea.B1 && SensorPos >= SensorArea.A1)
            {
                _buttonPos = SensorPos.ToButtonZone();
            }
            else
            {
                _buttonPos = null;
            }
            JudgableRange = new(JudgeTimingWithOffset - 0.15f, JudgeTimingWithOffset + 0.316667f, ContainsType.Closed);

            _wholeDuration = 3.209385682f * Mathf.Pow(Speed, -0.9549621752f);
            _moveDuration = 0.8f * _wholeDuration;
            _displayDuration = 0.2f * _wholeDuration;

            LoadSkin();

            Transform.position = NoteHelper.GetTouchAreaPosition(SensorPos);
            //_pointObject.SetActive(false);
            //_justBorderObject.SetActive(false);

            SetFansColor(new Color(1f, 1f, 1f, 0f));
            SetFansPosition(0.4f);
            SetActiveWithoutRenderer(true);

            for (var i = 0; i < 4; i++)
            {
                _fanRenderers[i].sortingOrder = SortOrder - (_fanSpriteSortOrder + i);
            }
            _pointRenderer.sortingOrder = SortOrder - _pointBorderSortOrder;
            _justBorderRenderer.sortingOrder = SortOrder - _justBorderSortOrder;

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
            var result = new NoteJudgeResult()
            {
                Grade = JudgeResult,
                Diff = JudgeDiff,
                IsEX = IsEX,
                IsBreak = IsBreak,
                IsMine = IsMine
            };
            // disable SpriteRenderer
            SetActive(false);

            if (_isFirework && !result.IsMissOrTooFast)
            {
                EffectManager.PlayFireworkEffect(Transform.position);
            }

            PlayJudgeSFX(result);
            NoteManager.NextTouch(QueueInfo);
            EffectManager.PlayTouchJudgeResult(SensorPos, result);
            ObjectCounter.ReportResult(this, result);
            _notePoolManager.Collect(this);
        }
        protected override void LoadSkin()
        {
            var skin = MajInstances.SkinManager.GetTouchSkin();

            SetFansMaterial(DefaultMaterial);
            if (IsMine)
            {
                if (IsBreak)
                {
                    SetFansSprite(skin.BreakMine);
                    SetFansMaterial(BreakMaterial);
                    _pointRenderer.sprite = skin.Point_Break_Mine;
                }
                else
                {
                    SetFansSprite(skin.Mine);
                    _pointRenderer.sprite = skin.Point_Mine;
                }
            }
            else
            {
                if (IsBreak)
                {
                    SetFansSprite(skin.Break);
                    SetFansMaterial(BreakMaterial);
                    _pointRenderer.sprite = skin.Point_Break;
                }
                else if (IsEach)
                {
                    SetFansSprite(skin.Each);
                    _pointRenderer.sprite = skin.Point_Each;
                }
                else
                {
                    SetFansSprite(skin.Normal);
                    _pointRenderer.sprite = skin.Point_Normal;
                }
            }
            _justBorderRenderer.sprite = skin.JustBorder;
        }
        private void TooLateCheck()
        {
            // Too late check
            if (IsEnded || IsJudged || AutoplayMode == AutoplayModeOption.Enable)
            {
                return;
            }

            var isTooLate = GetTimeSpanToJudgeTiming() > TOUCH_JUDGE_GOOD_AREA_MSEC / 1000;

            if (!isTooLate)
            {
                if (GroupInfo is not null)
                {
                    if (GroupInfo.Percent > 0.5f && GroupInfo.JudgeResult != null)
                    {
                        IsJudged = true;
                        JudgeResult = (JudgeGrade)GroupInfo.JudgeResult;
                        JudgeDiff = GroupInfo.JudgeDiff;
                        End();
                    }
                }
            }
            else
            {
                JudgeResult = JudgeGrade.Miss;
                IsJudged = true;
                End();
            }
        }
        private void Check()
        {
            if (IsEnded || !IsInited)
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
                if (IsMine)
                {
                    if (JudgeResult >= JudgeGrade.Perfect)
                    {
                        JudgeResult = JudgeGrade.TooFast;
                    }
                    else
                    {
                        JudgeResult = JudgeGrade.Miss;
                    }
                }
                RegisterGrade();
                End();
            }
        }
        private void MineCheck()
        {
            if (!IsMine || IsEnded || !IsInited || IsJudged)
            {
                return;
            }
            if (GetTimeSpanToJudgeTiming() > 0)
            {
                IsJudged = true;
                JudgeResult = JudgeGrade.Perfect;
                End();
            }
        }
        protected override void Autoplay()
        {
            if (IsMine)
            {
                return;
            }
            switch (AutoplayMode)
            {
                case AutoplayModeOption.Enable:
                    base.Autoplay();
                    if (IsJudged)
                    {
                        End();
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
            if (IsJudged)
            {
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
        private void RegisterGrade()
        {
            if (GroupInfo is not null && !JudgeResult.IsMissOrTooFast())
            {
                GroupInfo.JudgeResult = JudgeResult;
                GroupInfo.JudgeDiff = JudgeDiff;
                GroupInfo.RegisterResult(JudgeResult);
            }
        }
        [OnPreUpdate]
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("TouchDrop.OnPreUpdate"))
            {
                TooLateCheck();
                Check();
                MineCheck();
                Autoplay();
            }
        }
        [OnUpdate]
        internal void OnUpdate()
        {
            using (UnityProfiler.Create("TouchDrop.OnUpdate"))
            {
                var timing = GetTimeSpanToArriveTiming();

                switch (State)
                {
                    case NoteStatus.Inited:
                        if (-timing < _wholeDuration)
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
                            if (-timing < _moveDuration)
                            {
                                SetFansColor(Color.white);
                                State = NoteStatus.Running;
                                goto case NoteStatus.Running;
                            }
                            var alpha = ((_wholeDuration + timing) / _displayDuration).Clamp(0, 1);
                            newColor.a = alpha;
                            SetFansColor(newColor);
                        }
                        return;
                    case NoteStatus.Running:
                        {
                            var pow = -Mathf.Exp(8 * (timing * 0.43f / _moveDuration) - 0.85f) + 0.42f;
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
                                SetJustBorderActive(true);
                                State = NoteStatus.Arrived;
                            }
                            else
                            {
                                SetFansPosition(distance);
                            }
                        }
                        return;
                    case NoteStatus.Arrived:
                        return;
                }
            }
        }
        protected override void Judge(float currentSec)
        {
            if (IsJudged)
                return;

            var diffSec = currentSec - JudgeTimingWithOffset;
            var isFast = diffSec < 0;
            JudgeDiff = diffSec * 1000;
            var diffMSec = MathF.Abs(diffSec * 1000);

            if (isFast && diffMSec > TOUCH_JUDGE_SEG_1ST_PERFECT_MSEC)
                return;

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
        }

        public override void SetActive(bool state)
        {
            if (Active == state)
                return;
            base.SetActive(state);
            SetFanActive(state);
            SetJustBorderActive(state);
            SetPointActive(state);
            Active = state;
        }
        private void SetActiveWithoutRenderer(bool state)
        {
            base.SetActive(state);
        }
        private void SetActiveWithRenderer(bool state)
        {
            base.SetActive(state);
            SetFanActive(state);
            SetJustBorderActive(state);
            SetPointActive(state);
        }
        private void SetFanActive(bool state)
        {
            for(var i = 0; i < _fanRenderers.Length; i++)
            {
                _fanRenderers[i].enabled = state;
            }
        }
        private void SetPointActive(bool state)
        {
            _pointRenderer.enabled = state;
        }
        private void SetJustBorderActive(bool state)
        {
            _justBorderRenderer.enabled = state;
        }

        private void SetFansPosition(in float distance)
        {
            for (var i = 0; i < 4; i++)
            {
                var pos = (0.226f + distance) * GetAngle(i);
                _fanTransforms[i].localPosition = pos;
            }
        }
        private Vector3 GetAngle(int index)
        {
            var angle = index * (Mathf.PI / 2);
            return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
        }
        private void SetFansColor(Color color)
        {
            foreach (var fan in _fanRenderers) fan.color = color;
        }
        private void SetFansSprite(Sprite sprite)
        {
            for (var i = 0; i < 4; i++)
                _fanRenderers[i].sprite = sprite;
        }
        private void SetFansMaterial(Material material)
        {
            for (var i = 0; i < 4; i++)
                _fanRenderers[i].sharedMaterial = material;
        }

        protected override void PlaySFX()
        {
            PlayJudgeSFX(new NoteJudgeResult()
            {
                Grade = JudgeResult,
                IsBreak = IsBreak,
                IsEX = IsEX,
                Diff = JudgeDiff
            });
        }
        protected override void PlayJudgeSFX(in NoteJudgeResult judgeResult)
        {
            if (judgeResult.IsBreak || judgeResult.IsMine)
            {
                AudioEffMana.PlayTapSound(judgeResult);
            }
            else
            {
                if(judgeResult.Grade != JudgeGrade.Miss)
                    AudioEffMana.PlayTouchSound();
            }
            if (_isFirework)
            {
                if (judgeResult.IsMissOrTooFast)
                    return;
                AudioEffMana.PlayHanabiSound();
            }
        }
    }
}
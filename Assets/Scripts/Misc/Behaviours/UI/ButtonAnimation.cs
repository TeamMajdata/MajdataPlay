using LitMotion;
using LitMotion.Extensions;
using MajdataPlay.IO;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MajdataPlay.Behaviours.UI
{
    public class ButtonAnimation : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("BindSensor")]
        private SensorArea[] _bindSensor;

        [SerializeField]
        [FormerlySerializedAs("BindButton")]
        private ButtonZone[] _bindButton;

        [SerializeField]
        [FormerlySerializedAs("HoldTriggerTime")]
        private float _holdTriggerTime = 0f;   // 按住多久算完成

        [SerializeField]
        [FormerlySerializedAs("ReleasedScale")]
        private float _releasedScale = 1f;

        [SerializeField]
        [FormerlySerializedAs("PressedScale")]
        private float _pressedScale = 0.9f;

        [SerializeField]
        [FormerlySerializedAs("AnimationDuration")]
        private float _animationDuration = InputManager.UI_CLICK_ANIMATION_DURATION_SEC;

        [SerializeField]
        [FormerlySerializedAs("EaseType")]
        private Ease _easeType = Ease.OutQuad;

        [SerializeField]
        [FormerlySerializedAs("HoldCircle")]
        private Image _holdCircle;            // 圆环 Image

        // 颜色渐变
        [SerializeField]
        [FormerlySerializedAs("StartColor")]
        private Color _startColor = Color.white;
        [SerializeField]
        [FormerlySerializedAs("EndColor")]
        private Color _endColor = Color.green;

        private float _holdTimer = 0f;       // 按住计时器
        private bool _holdCompleted = false;
        private MotionHandle _holdMotion;    // 圆环动画句柄
        private MotionHandle _scaleMotion;

        private void Start()
        {
            if (_holdCircle != null)
            {
                _holdCircle.type = Image.Type.Filled;
                _holdCircle.fillMethod = Image.FillMethod.Radial360;
                _holdCircle.fillOrigin = (int)Image.Origin360.Top;
                _holdCircle.fillClockwise = true;

                _holdCircle.fillAmount = 0f;
                _holdCircle.color = _startColor;
            }
        }

        private void Update()
        {
            var isDown = false;
            var isUp = false;
            var isPressed = false;

            foreach (var button in _bindButton)
            {
                ref readonly var state = ref InputManager.GetButtonState(button);
                isDown |= state.PressedThisFrame;
                isUp |= state.ReleasedThisFrame;
                isPressed |= state.IsPressed;
            }

            foreach (var sensor in _bindSensor)
            {
                ref readonly var state = ref InputManager.GetSensorState(sensor);
                isDown |= state.PressedThisFrame;
                isUp |= state.ReleasedThisFrame;
                isPressed |= state.IsPressed;
            }

            // -------------------------
            // 按下缩放动画
            // -------------------------
            if (isDown)
            {
                PlayPressScaleAnimation(_holdTriggerTime <= 0f);
                if (_holdTriggerTime > 0f)
                {
                    _holdTimer = 0f;
                    _holdCompleted = false;
                    if (_holdCircle != null)
                    {
                        _holdMotion.TryCancel();
                        _holdCircle.fillAmount = 0f;
                        _holdCircle.color = _startColor;
                    }
                }
            }

            // -------------------------
            // 松开缩放动画 + 圆环清零
            // -------------------------
            if (isUp && !isPressed && _holdTriggerTime > 0f)
            {
                CancelScaleMotion();
                PlayReleaseScaleAnimation();

                _holdTimer = 0f;

                if (!_holdCompleted && _holdCircle != null)
                {
                    if (_holdMotion.IsPlaying() && _holdMotion.IsValid())
                    {
                        _holdMotion.Cancel();
                    }

                    _holdMotion = LMotion
                        .Create(_holdCircle.fillAmount, 0f, 0.15f)
                        .WithEase(Ease.OutQuad)
                        .Bind(value =>
                        {
                            _holdCircle.fillAmount = value;
                            _holdCircle.color = Color.Lerp(_startColor, _endColor, value);
                        });
                }

                _holdCompleted = false;
            }

            // -------------------------
            // 按住计时 + 圆环填充（带 LitMotion 缓动 + 颜色渐变）
            // -------------------------
            if (isPressed && _holdTriggerTime > 0f && !_holdCompleted)
            {
                _holdTimer += Time.deltaTime;
                float targetFill = Mathf.Clamp01(_holdTimer / _holdTriggerTime);

                if (targetFill >= 1f)
                {
                    _holdCompleted = true;
                    if (_holdCircle != null)
                    {
                        _holdMotion.TryCancel();
                        _holdCircle.fillAmount = 1f;
                        _holdCircle.color = _endColor;
                        _holdMotion = LMotion.Create(_endColor.a, 0f, 0.15f)
                            .WithEase(Ease.OutQuad)
                            .BindToColorA(_holdCircle);
                    }
                    return;
                }

                if (_holdCircle != null)
                {
                    if (_holdMotion.IsPlaying() && _holdMotion.IsValid())
                    {
                        _holdMotion.Cancel();
                    }

                    _holdMotion = LMotion
                        .Create(_holdCircle.fillAmount, targetFill, 0.15f)
                        .WithEase(Ease.OutQuad)
                        .Bind(value =>
                        {
                            _holdCircle.fillAmount = value;
                            _holdCircle.color = Color.Lerp(_startColor, _endColor, value);
                        });
                }
            }
        }

        private void PlayPressScaleAnimation(bool autoRelease)
        {
            var pressedScale = new Vector3(_pressedScale, _pressedScale, _pressedScale);
            CancelScaleMotion();
            _scaleMotion = LMotion.Create(transform.localScale, pressedScale, _animationDuration)
                .WithEase(_easeType)
                .WithOnComplete(() =>
                {
                    if (autoRelease)
                    {
                        PlayReleaseScaleAnimation();
                    }
                })
                .BindToLocalScale(transform);
        }

        private void PlayReleaseScaleAnimation()
        {
            var releasedScale = new Vector3(_releasedScale, _releasedScale, _releasedScale);
            _scaleMotion = LMotion.Create(transform.localScale, releasedScale, _animationDuration)
                .WithEase(_easeType)
                .BindToLocalScale(transform);
        }

#if UNITY_EDITOR
        public void PlayClickAnimationPreview()
        {
            CancelScaleMotion();
            transform.localScale = new Vector3(_releasedScale, _releasedScale, _releasedScale);
            PlayPressScaleAnimation(true);
        }

        public void StopAnimationPreview(Vector3 restoreScale)
        {
            CancelScaleMotion();
            transform.localScale = restoreScale;
        }
#endif

        private void CancelScaleMotion()
        {
            if (_scaleMotion.IsValid() && _scaleMotion.IsPlaying())
            {
                _scaleMotion.Cancel();
            }
        }

        private void OnDisable()
        {
            _holdTimer = 0f;
            _holdCompleted = false;
            CancelScaleMotion();
            if (_holdMotion.IsValid() && _holdMotion.IsPlaying())
            {
                _holdMotion.Cancel();
            }
            if (_holdCircle != null)
            {
                _holdCircle.fillAmount = 0f;
                _holdCircle.color = _startColor;
            }
            transform.localScale = new Vector3(_releasedScale, _releasedScale, _releasedScale);
        }
    }
}

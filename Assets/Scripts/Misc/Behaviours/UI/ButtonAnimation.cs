using MajdataPlay.IO;
using UnityEngine;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine.UI;

namespace MajdataPlay.Behaviours.UI
{
    public class ButtonAnimation : MonoBehaviour
    {
        public SensorArea[] BindSensor;
        public ButtonZone[] BindButton;

        public float HoldTriggerTime = 0f;   // 按住多久算完成
        public float ReleasedScale = 1f;
        public float PressedScale = 0.9f;
        public float AnimationDuration = InputManager.UI_CLICK_ANIMATION_DURATION_SEC;
        public Ease EaseType = Ease.OutQuad;

        public Image HoldCircle;            // 圆环 Image

        // 颜色渐变
        public Color StartColor = Color.white;
        public Color EndColor = Color.green;

        private float _holdTimer = 0f;       // 按住计时器
        private bool _holdCompleted = false;
        private MotionHandle _holdMotion;    // 圆环动画句柄
        private MotionHandle _scaleMotion;

        void Start()
        {
            if (HoldCircle != null)
            {
                HoldCircle.type = Image.Type.Filled;
                HoldCircle.fillMethod = Image.FillMethod.Radial360;
                HoldCircle.fillOrigin = (int)Image.Origin360.Top;
                HoldCircle.fillClockwise = true;

                HoldCircle.fillAmount = 0f;
                HoldCircle.color = StartColor;
            }
        }

        void Update()
        {
            var isDown = false;
            var isUp = false;
            var isPressed = false;

            foreach (var button in BindButton)
            {
                ref readonly var state = ref InputManager.GetButtonState(button);
                isDown |= state.PressedThisFrame;
                isUp |= state.ReleasedThisFrame;
                isPressed |= state.IsPressed;
            }

            foreach (var sensor in BindSensor)
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
                PlayPressScaleAnimation(HoldTriggerTime <= 0f);
                if (HoldTriggerTime > 0f)
                {
                    _holdTimer = 0f;
                    _holdCompleted = false;
                    if (HoldCircle != null)
                    {
                        _holdMotion.TryCancel();
                        HoldCircle.fillAmount = 0f;
                        HoldCircle.color = StartColor;
                    }
                }
            }

            // -------------------------
            // 松开缩放动画 + 圆环清零
            // -------------------------
            if (isUp && !isPressed && HoldTriggerTime > 0f)
            {
                CancelScaleMotion();
                PlayReleaseScaleAnimation();

                _holdTimer = 0f;

                if (!_holdCompleted && HoldCircle != null)
                {
                    if (_holdMotion.IsPlaying() && _holdMotion.IsValid())
                        _holdMotion.Cancel();

                    _holdMotion = LMotion
                        .Create(HoldCircle.fillAmount, 0f, 0.15f)
                        .WithEase(Ease.OutQuad)
                        .Bind(value =>
                        {
                            HoldCircle.fillAmount = value;
                            HoldCircle.color = Color.Lerp(StartColor, EndColor, value);
                        });
                }

                _holdCompleted = false;
            }

            // -------------------------
            // 按住计时 + 圆环填充（带 LitMotion 缓动 + 颜色渐变）
            // -------------------------
            if (isPressed && HoldTriggerTime > 0f && !_holdCompleted)
            {
                _holdTimer += Time.deltaTime;
                float targetFill = Mathf.Clamp01(_holdTimer / HoldTriggerTime);

                if (targetFill >= 1f)
                {
                    _holdCompleted = true;
                    if (HoldCircle != null)
                    {
                        _holdMotion.TryCancel();
                        HoldCircle.fillAmount = 1f;
                        HoldCircle.color = EndColor;
                        _holdMotion = LMotion.Create(EndColor.a, 0f, 0.15f)
                            .WithEase(Ease.OutQuad)
                            .BindToColorA(HoldCircle);
                    }
                    return;
                }

                if (HoldCircle != null)
                {
                    if (_holdMotion.IsPlaying() && _holdMotion.IsValid())
                        _holdMotion.Cancel();

                    _holdMotion = LMotion
                        .Create(HoldCircle.fillAmount, targetFill, 0.15f)
                        .WithEase(Ease.OutQuad)
                        .Bind(value =>
                        {
                            HoldCircle.fillAmount = value;
                            HoldCircle.color = Color.Lerp(StartColor, EndColor, value);
                        });
                }
            }
        }

        void PlayPressScaleAnimation(bool autoRelease)
        {
            var pressedScale = new Vector3(PressedScale, PressedScale, PressedScale);
            CancelScaleMotion();
            _scaleMotion = LMotion.Create(transform.localScale, pressedScale, AnimationDuration)
                .WithEase(EaseType)
                .WithOnComplete(() =>
                {
                    if (autoRelease)
                    {
                        PlayReleaseScaleAnimation();
                    }
                })
                .BindToLocalScale(transform);
        }

        void PlayReleaseScaleAnimation()
        {
            var releasedScale = new Vector3(ReleasedScale, ReleasedScale, ReleasedScale);
            _scaleMotion = LMotion.Create(transform.localScale, releasedScale, AnimationDuration)
                .WithEase(EaseType)
                .BindToLocalScale(transform);
        }

#if UNITY_EDITOR
        public void PlayClickAnimationPreview()
        {
            CancelScaleMotion();
            transform.localScale = new Vector3(ReleasedScale, ReleasedScale, ReleasedScale);
            PlayPressScaleAnimation(true);
        }

        public void StopAnimationPreview(Vector3 restoreScale)
        {
            CancelScaleMotion();
            transform.localScale = restoreScale;
        }
#endif

        void CancelScaleMotion()
        {
            if (_scaleMotion.IsValid() && _scaleMotion.IsPlaying())
            {
                _scaleMotion.Cancel();
            }
        }

        void OnDisable()
        {
            _holdTimer = 0f;
            _holdCompleted = false;
            CancelScaleMotion();
            if (_holdMotion.IsValid() && _holdMotion.IsPlaying())
            {
                _holdMotion.Cancel();
            }
            if (HoldCircle != null)
            {
                HoldCircle.fillAmount = 0f;
                HoldCircle.color = StartColor;
            }
            transform.localScale = new Vector3(ReleasedScale, ReleasedScale, ReleasedScale);
        }
    }
}

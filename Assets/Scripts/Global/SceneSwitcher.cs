using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using LitMotion;
using LitMotion.Extensions;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.UI;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay
{
    [DontDestroyOnLoad]
    public sealed partial class SceneSwitcher : MajComponent
    {
        public static Camera MainCamera
        {
            get => _mainCamera;
            set => _mainCamera = value;
        }

        public static event EventHandler<(MajScenes NewScene, MajScenes OldScene)>? OnSceneChanged;
        public static MajScenes CurrentScene { get; private set; } = MajScenes.Init;
        public static MajScenes LastScene { get; private set; } = MajScenes.Init;

        private Canvas _canvas;

        [SerializeField]
        [FormerlySerializedAs("SubImage")]
        private Image _subImage;

        [SerializeField]
        [FormerlySerializedAs("OverlayImage")]
        private Image _overlayImage;

        [SerializeField]
        [FormerlySerializedAs("MainImage")]
        private Image _mainImage;

        [SerializeField]
        [FormerlySerializedAs("loadingText")]
        private TMP_Text _loadingText;

        [SerializeField]
        [FormerlySerializedAs("LoadingLightColor")]
        private Color _loadingLightColor;

        [Header("Transition Animation")]
        [SerializeField]
        RectTransform _mainMaskRect;
        [SerializeField, Min(1f)]
        float _coveredMaskSize = 1080f;
        [SerializeField, Min(0.01f)]
        float _closeDuration = 0.9f;
        [SerializeField, Min(0.01f)]
        float _openDuration = 0.8f;
        [SerializeField, Range(6, 24)]
        int _triangleColumns = 12;
        [SerializeField, Range(0.2f, 0.4f)]
        float _triangleFadeSpan = 0.2f;
        [SerializeField, Range(-30f, 30f)]
        float _triangleGridRotation;
        [SerializeField, Range(0f, 180f)]
        float _triangleSpinDegrees = 90f;

        MotionHandle _maskMotion;
        MotionHandle _overlayImageMotion;
        MotionHandle _mainImageMotion;
        MotionHandle _loadingTextMotion;
        [Header("Transition Rendering")]
        [SerializeField]
        Canvas _transitionOverlayCanvas = null!;
        Graphic[] _maskDecorations = Array.Empty<Graphic>();
        float _maskProgress;
        bool _isClosingTransition;

        static Camera _mainCamera;

        readonly string[] SCENE_NAMES = Enum.GetNames(typeof(MajScenes));

        const int AUTO_FADE_OUT_DELAY_MS = 50;
        const float CLOSE_CONTENT_START_SCALE = 1.015f;
        const float OPEN_CONTENT_END_SCALE = 1.01f;
        const float OVERLAY_IMAGE_DELAY_SEC = 0.05f;
        const float LOADING_TEXT_FADE_DURATION_SEC = 0.2f;
        static readonly int TRIANGLE_COLUMNS_ID = Shader.PropertyToID("_MajSceneTriangleColumns");
        static readonly int TRIANGLE_FADE_SPAN_ID = Shader.PropertyToID("_MajSceneTriangleFadeSpan");
        static readonly int TRIANGLE_GRID_ROTATION_ID = Shader.PropertyToID("_MajSceneTriangleGridRotation");
        static readonly int TRIANGLE_SPIN_DEGREES_ID = Shader.PropertyToID("_MajSceneTriangleSpinDegrees");
        static readonly int TRIANGLE_CLOSING_ID = Shader.PropertyToID("_MajSceneTriangleClosing");
        static readonly int TRANSITION_PROGRESS_ID = Shader.PropertyToID("_MajSceneTransitionProgress");
        protected override void Awake()
        {
            base.Awake();
            Majdata<SceneSwitcher>.SetAsSingleton(this);
            SceneManager.activeSceneChanged += OnUnitySceneChanged;
            MainCamera = Camera.main;
            var currentScene = SceneManager.GetActiveScene();
            var index = Array.FindIndex(SCENE_NAMES, x => x == currentScene.name);
            if(index != -1)
            {
                CurrentScene = Enum.Parse<MajScenes>(SCENE_NAMES[index]);
            }
            _canvas = GetComponent<Canvas>();
            BindTransitionOverlay();
            if (ResolveTransitionReferences())
            {
                // The game boots fully open. Init -> Title has no transition;
                // the first Close is played when Title enters Login or List.
                _isClosingTransition = false;
                _maskProgress = 0f;
                _mainMaskRect.sizeDelta = Vector2.one * _coveredMaskSize;
                ConfigureTransitionShader();
                SetMaskProgress(_maskProgress);
                SetGraphicAlpha(_overlayImage, 0f);
                _mainImage.rectTransform.localScale = Vector3.one * OPEN_CONTENT_END_SCALE;
                _mainImage.gameObject.SetActive(false);
            }
            SetGraphicAlpha(_loadingText, 0f);
            _loadingText.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnUnitySceneChanged;
            CancelTransitionMotions();
            if (_transitionOverlayCanvas != null)
            {
                Destroy(_transitionOverlayCanvas.gameObject);
            }
        }

        void OnUnitySceneChanged(Scene current, Scene next)
        {
            InputManager.ResetUIInputForSceneChange();
            //MajDebug.LogDebug(ZString.Format("Scene unloaded: {0}", current.name));
            MajDebug.LogDebug(ZString.Format("Scene loaded: {0}", next.name));
            //var currentScene = SceneManager.GetActiveScene();
            var index = Array.FindIndex(SCENE_NAMES, x => x == next.name);
            var lastScene = CurrentScene;
            if (index != -1)
            {
                CurrentScene = Enum.Parse<MajScenes>(SCENE_NAMES[index]);
            }
            LastScene = lastScene;
            if(OnSceneChanged is not null)
            {
                OnSceneChanged(this, (CurrentScene, LastScene));
            }
            CabinetLed.SetCabinetLight(1.0f);
        }

        public void SwitchScene(string sceneName, bool autoFadeOut = true)
        {
            SwitchSceneInternal(sceneName, autoFadeOut).Forget();
        }
        public UniTask SwitchSceneAsync(string sceneName, bool autoFadeOut = true)
        {
            return SwitchSceneInternal(sceneName, autoFadeOut);
        }
        public void FadeOut()
        {
            StartTransition(false);
            CabinetLed.SetAllLight(Color.white);
            CabinetLed.SetCabinetLight(1.0f);
        }
        public async UniTask FadeOutAsync()
        {
            await PlayTransitionAsync(false);
            CabinetLed.SetAllLight(Color.white);
            CabinetLed.SetCabinetLight(1.0f);
        }
        public void FadeIn()
        {
            _loadingText.text = string.Empty;
            _loadingText.gameObject.SetActive(true);
            StartTransition(true);
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
        }
        public async UniTask FadeInAsync()
        {
            _loadingText.text = string.Empty;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
        }
        public void SetLoadingText(string text , Color color)
        {
            _loadingText.text = text;
            _loadingText.color = color;
        }
        public void SetLoadingText(string text)
        {
            _loadingText.text = text;
            _loadingText.color = Color.white;
        }

        async UniTask SwitchSceneInternal(string sceneName, bool autoFadeOut)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.text = "";
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, autoFadeOut);
        }
        async UniTask SwitchSceneCoreAsync(string sceneName, bool autoFadeOut)
        {
            //await SceneManager.UnloadSceneAsync(SceneManager.GetActiveScene());
            //await Resources.UnloadUnusedAssets();
            await SceneManager.LoadSceneAsync(sceneName);
            await UniTask.DelayFrame(1);
            await UniTask.Delay(AUTO_FADE_OUT_DELAY_MS);
            if (autoFadeOut)
            {
                StartTransition(false);
                CabinetLed.SetAllLight(Color.white);
                CabinetLed.SetCabinetLight(1.0f);
            }
        }

        bool ResolveTransitionReferences()
        {
            if (_mainMaskRect == null && _mainImage != null)
            {
                _mainMaskRect = _mainImage.rectTransform.parent as RectTransform;
            }
            if (_mainMaskRect != null)
            {
                _maskDecorations = _mainMaskRect
                    .GetComponentsInChildren<Graphic>(true)
                    .Where(graphic => graphic != _mainImage)
                    .ToArray();
            }
            return _mainMaskRect != null
                && _mainImage != null
                && _overlayImage != null
                && _loadingText != null;
        }

        void BindTransitionOverlay()
        {
            if (!ResolveTransitionReferences() || _transitionOverlayCanvas == null)
            {
                MajDebug.LogError("Scene transition overlay is not configured.");
                return;
            }

            if (_mainMaskRect.GetComponentInParent<Canvas>() != _transitionOverlayCanvas)
            {
                MajDebug.LogError("Scene transition UI is not serialized under the configured overlay canvas.");
            }
        }

        void CancelTransitionMotions()
        {
            _maskMotion.TryCancel();
            _overlayImageMotion.TryCancel();
            _mainImageMotion.TryCancel();
            _loadingTextMotion.TryCancel();
        }
        bool StartTransition(bool closing)
        {
            if (!ResolveTransitionReferences())
            {
                MajDebug.LogWarning("Scene transition preview is missing its mask or image references.");
                return false;
            }

            CancelTransitionMotions();
            _isClosingTransition = closing;
            var targetProgress = closing ? 1f : 0f;
            if (Mathf.Approximately(_maskProgress, targetProgress))
            {
                ApplyTransitionState(closing);
                return false;
            }

            _mainImage.gameObject.SetActive(true);
            if (closing)
            {
                _loadingText.gameObject.SetActive(true);
            }
            _mainMaskRect.sizeDelta = Vector2.one * _coveredMaskSize;
            ConfigureTransitionShader();
            SetMaskProgress(_maskProgress);

            if (closing && _maskProgress <= 0.001f)
            {
                _mainImage.rectTransform.localScale = Vector3.one * CLOSE_CONTENT_START_SCALE;
            }

            var duration = closing ? _closeDuration : _openDuration;
            Action onComplete = closing ? ApplyClosedState : ApplyOpenState;
            _maskMotion = LMotion.Create(_maskProgress, targetProgress, duration)
                // A hard initial push followed by a pronounced brake. The fold
                // shader uses raw local progress so this is the only speed curve.
                .WithEase(Ease.OutQuint)
                .WithOnComplete(onComplete)
                .Bind(SetMaskProgress);

            var targetAlpha = closing ? 1f : 0f;
            _overlayImageMotion = LMotion.Create(_overlayImage.color.a, targetAlpha, Mathf.Max(0.01f, duration - OVERLAY_IMAGE_DELAY_SEC))
                .WithDelay(OVERLAY_IMAGE_DELAY_SEC)
                .WithEase(Ease.OutQuint)
                .BindToColorA(_overlayImage);

            var targetScale = Vector3.one * (closing ? 1f : OPEN_CONTENT_END_SCALE);
            _mainImageMotion = LMotion.Create(_mainImage.rectTransform.localScale, targetScale, duration)
                .WithEase(Ease.OutQuint)
                .BindToLocalScale(_mainImage.rectTransform);

            if (closing)
            {
                _loadingTextMotion = LMotion.Create(_loadingText.color.a, 1f, LOADING_TEXT_FADE_DURATION_SEC)
                    .WithDelay(Mathf.Max(0f, duration - LOADING_TEXT_FADE_DURATION_SEC))
                    .WithEase(Ease.OutQuint)
                    .BindToColorA(_loadingText);
            }
            else
            {
                _loadingTextMotion = LMotion.Create(_loadingText.color.a, 0f, LOADING_TEXT_FADE_DURATION_SEC)
                    .WithEase(Ease.OutQuint)
                    .WithOnComplete(() => _loadingText.gameObject.SetActive(false))
                    .BindToColorA(_loadingText);
            }
            return true;
        }
        async UniTask PlayTransitionAsync(bool closing)
        {
            if (StartTransition(closing))
            {
                await _maskMotion;
            }
        }

#if UNITY_EDITOR
        public void PreviewTransition(bool closing)
        {
            if (!ResolveTransitionReferences())
            {
                Debug.LogWarning("SceneSwitcher preview requires MainImage, SubImage, and a parent mask RectTransform.", this);
                return;
            }

            CancelTransitionMotions();
            ApplyTransitionState(!closing);
            StartTransition(closing);
        }
        public void FinishEditorPreview(bool closed)
        {
            CancelTransitionMotions();
            ApplyTransitionState(closed);
        }
        public float GetEditorPreviewDuration(bool closing) => closing ? _closeDuration : _openDuration;
#endif

        void ApplyClosedState() => ApplyTransitionState(true);
        void ApplyOpenState() => ApplyTransitionState(false);
        void ApplyTransitionState(bool closed)
        {
            _isClosingTransition = closed;
            _mainImage.gameObject.SetActive(true);
            _mainMaskRect.sizeDelta = Vector2.one * _coveredMaskSize;
            ConfigureTransitionShader();
            SetMaskProgress(closed ? 1f : 0f);
            SetGraphicAlpha(_overlayImage, closed ? 1f : 0f);
            _mainImage.rectTransform.localScale = Vector3.one * (closed ? 1f : OPEN_CONTENT_END_SCALE);
            SetGraphicAlpha(_loadingText, closed ? 1f : 0f);
            _loadingText.gameObject.SetActive(closed);
            if (!closed)
            {
                _mainImage.gameObject.SetActive(false);
            }
        }

        void ConfigureTransitionShader()
        {
            Shader.SetGlobalFloat(TRIANGLE_COLUMNS_ID, Mathf.Clamp(_triangleColumns, 6, 24));
            Shader.SetGlobalFloat(TRIANGLE_FADE_SPAN_ID, Mathf.Clamp(_triangleFadeSpan, 0.2f, 0.4f));
            Shader.SetGlobalFloat(TRIANGLE_GRID_ROTATION_ID, _triangleGridRotation);
            Shader.SetGlobalFloat(TRIANGLE_SPIN_DEGREES_ID, _triangleSpinDegrees);
            Shader.SetGlobalFloat(TRIANGLE_CLOSING_ID, _isClosingTransition ? 1f : 0f);

            SetGraphicAlpha(_mainImage, 1f);
        }

        void SetMaskProgress(float progress)
        {
            _maskProgress = Mathf.Clamp01(progress);
            Shader.SetGlobalFloat(TRANSITION_PROGRESS_ID, _maskProgress);
            var decorationAlpha = Mathf.SmoothStep(0f, 1f, _maskProgress);
            foreach (var decoration in _maskDecorations)
            {
                SetGraphicAlpha(decoration, decorationAlpha);
            }
        }

        private void UpdateBGSprite()
        {
            var sprite = MajInstances.SkinManager?.SelectedSkin?.SubDisplay;
            if(sprite == null)
            {
                var oColor = _overlayImage.color;
                _overlayImage.color = new(0, 0, 0, oColor.a);
                _subImage.color = Color.black;
            }
            else
            {
                _overlayImage.sprite = sprite;
                _subImage.sprite = sprite;
                var oColor = _overlayImage.color;
                _overlayImage.color = new(1, 1, 1, oColor.a);
                _subImage.color = Color.white;
            }            
        }

        static void SetGraphicAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
    public sealed partial class SceneSwitcher
    {
        // Task
        async UniTask SwitchSceneInternalAsync(string sceneName, Task taskToRun)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            while (!taskToRun.IsCompleted)
            {
                await UniTask.Yield();
            }
            if (taskToRun.IsFaulted)
            {
                throw taskToRun.Exception;
            }
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, true);
        }
        public async UniTaskVoid SwitchSceneAfterTaskAsync(string sceneName, Task taskToRun)
        {
            await SwitchSceneInternalAsync(sceneName, taskToRun);
        }
        // ValueTasl
        async UniTask SwitchSceneInternalAsync(string sceneName, ValueTask taskToRun)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            while (!taskToRun.IsCompleted)
            {
                await UniTask.Yield();
            }
            if(taskToRun.IsFaulted)
            {
                throw taskToRun.AsTask().Exception;
            }
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, true);
        }
        public async UniTaskVoid SwitchSceneAfterTaskAsync(string sceneName, ValueTask taskToRun)
        {
            await SwitchSceneInternalAsync(sceneName, taskToRun);
        }
        // UniTask
        async UniTask SwitchSceneInternalAsync(string sceneName, UniTask taskToRun)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            while (taskToRun.Status is not (UniTaskStatus.Succeeded or UniTaskStatus.Faulted or UniTaskStatus.Canceled))
            {
                await UniTask.Yield();
            }
            switch (taskToRun.Status)
            {
                case UniTaskStatus.Canceled:
                case UniTaskStatus.Faulted:
                    throw taskToRun.AsTask().Exception;
            }            
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, true);
        }
        public async UniTaskVoid SwitchSceneAfterTaskAsync(string sceneName, UniTask taskToRun)
        {
            await SwitchSceneInternalAsync(sceneName, taskToRun);
        }


        // Task
        async UniTask<T> SwitchSceneInternalAsync<T>(string sceneName, Task<T> taskToRun)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            while (!taskToRun.IsCompleted)
            {
                await UniTask.Yield();
            }
            if (taskToRun.IsFaulted)
            {
                throw taskToRun.Exception;
            }
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, true);
            return taskToRun.Result;
        }
        public async UniTask<T> SwitchSceneAfterTaskAsync<T>(string sceneName, Task<T> taskToRun)
        {
            return await SwitchSceneInternalAsync(sceneName, taskToRun);
        }
        // ValueTasl
        async UniTask<T> SwitchSceneInternalAsync<T>(string sceneName, ValueTask<T> taskToRun)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            while (!taskToRun.IsCompleted)
            {
                await UniTask.Yield();
            }
            if (taskToRun.IsFaulted)
            {
                throw taskToRun.AsTask().Exception;
            }
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, true);
            return taskToRun.Result;
        }
        public async UniTask<T> SwitchSceneAfterTaskAsync<T>(string sceneName, ValueTask<T> taskToRun)
        {
            return await SwitchSceneInternalAsync(sceneName, taskToRun);
        }
        // UniTask
        async UniTask<T> SwitchSceneInternalAsync<T>(string sceneName, UniTask<T> taskToRun)
        {
            InputManager.ClearAllSubscriber();
            UpdateBGSprite();
            //MainImage.sprite = MajInstances.SkinManager.SelectedSkin.LoadingSplash;
            _loadingText.gameObject.SetActive(true);
            await PlayTransitionAsync(true);
            while (taskToRun.Status is not (UniTaskStatus.Succeeded or UniTaskStatus.Faulted or UniTaskStatus.Canceled))
            {
                await UniTask.Yield();
            }
            switch (taskToRun.Status)
            {
                case UniTaskStatus.Canceled:
                case UniTaskStatus.Faulted:
                    throw taskToRun.AsTask().Exception;
            }
            CabinetLed.SetAllLight(_loadingLightColor);
            CabinetLed.SetCabinetLight(0.5f);
            await SwitchSceneCoreAsync(sceneName, true);

            return taskToRun.AsValueTask().Result;
        }
        public async UniTask<T> SwitchSceneAfterTaskAsync<T>(string sceneName, UniTask<T> taskToRun)
        {
            return await SwitchSceneInternalAsync(sceneName, taskToRun);
        }
    }
}

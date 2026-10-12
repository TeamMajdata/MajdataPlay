using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using MajdataPlay.Settings;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MajdataPlay.Diagnostics;
#nullable enable
namespace MajdataPlay.Scenes.Title
{
    public class TitleManager : MonoBehaviour
    {
        [SerializeField]
        RectTransform _xxlbRect = null!;

        [SerializeField]
        Image _xxlbImage = null!;

        [SerializeField]
        RectTransform _majdataPlayRect = null!;

        [SerializeField]
        Image _majdataPlayImage = null!;

        [SerializeField]
        Image _ribbonImage = null!;

        [SerializeField]
        RectTransform _echoBackground = null!;

        [SerializeField]
        TextMeshProUGUI _echoText = null!;

        [SerializeField]
        RectTransform _loadingIndicator = null!;

        bool _flag = false;

        readonly CompositeMotionHandle _titleEntranceMotions = new(5);
        MotionHandle _loadingRotationMotion;
        float _xxlbTargetX;
        float _majdataPlayTargetX;
        float _echoBackgroundTargetWidth;
        bool _isInitializationDone;

        const float XXLB_ENTRANCE_DURATION_SEC = 80f / 60f;
        const float XXLB_START_OFFSET_X = -540f;
        const float MAJDATA_PLAY_START_OFFSET_X = 540f;
        const float RIBBON_REVEAL_DELAY_SEC = XXLB_ENTRANCE_DURATION_SEC;
        const float RIBBON_REVEAL_DURATION_SEC = 27f / 60f;
        const float ECHO_BACKGROUND_DELAY_SEC = 102f / 60f;
        const float ECHO_BACKGROUND_DURATION_SEC = 23f / 60f;
        const float ECHO_TEXT_DELAY_SEC = 122f / 60f;
        const float ECHO_TEXT_FADE_DURATION_SEC = 20f / 60f;
        const float LOADING_ROTATION_DURATION_SEC = 3f;

        void Awake()
        {
            _xxlbTargetX = _xxlbRect.anchoredPosition.x;
            _majdataPlayTargetX = _majdataPlayRect.anchoredPosition.x;
            _echoBackgroundTargetWidth = _echoBackground.sizeDelta.x;
            PrepareTitleEntrance();
        }

        void Start()
        {
            PlayTitleEntranceAnimation();
            InitAsync().Forget();
            CabinetLed.SetAllLight(Color.white);
            if (InputManager.IsTouchPanelConnected)
            {
                Destroy(GameObject.Find("EventSystem"));
            }
        }
        /// <summary>Initializes title services and song collections within the component's lifetime.</summary>
        /// <returns>A player-loop initialization operation.</returns>
        async UniTaskVoid InitAsync()
        {
            var cancellationToken = destroyCancellationToken;
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, cancellationToken);
            MajInstances.AudioManager.PlaySFX("MajdataPlay.wav");
            MajInstances.AudioManager.PlaySFX("bgm_title.mp3");

            _echoText.text = $"{"MAJTEXT_LOADING_SCORE_STORAGE".i18n()}...";
            await UniTask.DelayFrame(9, cancellationToken: cancellationToken);
            var task1 = ScoreManager.InitAsync().AsValueTask();
            while (!task1.IsCompleted)
            {
                await UniTask.Yield(cancellationToken);
            }

            task1 = ChartSettingStorage.InitAsync();

            while (!task1.IsCompleted)
            {
                await UniTask.Yield(cancellationToken);
            }
            await UniTask.Delay(2000, cancellationToken: cancellationToken);
            _echoText.text = $"{"MAJTEXT_LOADING_SKIN".i18n()}...";
            var task2 = MajInstances.SkinManager.InitAsync();
            while (!task2.IsCompleted)
            {
                await UniTask.Yield(cancellationToken);
            }

            _echoText.text = $"{"MAJTEXT_SCANNING_CHARTS".i18n()}...";
            var task3 = InitializeSongStorageAsync(cancellationToken);
            try
            {
                while (true)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

                    if (task3.IsCompleted)
                    {
                        if (task3.IsCanceled)
                        {
                            return;
                        }
                        else if (task3.IsFaulted)
                        {
                            _echoText.text = "MAJTEXT_ERR_SCAN_CHARTS_FAILED".i18n();
                            MajDebug.LogException(task3.Exception);
                        }
                        else
                        {
                            _echoText.text = "MAJTEXT_PRESS_ANY_KEY".i18n();
                            InputManager.BindAnyArea(OnAreaClick);

                            var list = new string[] { "game_init.wav", "game_init_2.wav", "game_init_3.wav" };
                            MajInstances.AudioManager.PlaySFX(list[UnityEngine.Random.Range(0, list.Length)]);

                        }
                        break;
                    }
                }
                FinishLoadingAnimation();
            }
            finally
            {
                _flag = true;
            }
        }

        /// <summary>Initializes song storage, refreshes local and online collections, and restores the saved selection.</summary>
        /// <param name="cancellationToken">Cancels initialization and refreshes when the title component is destroyed.</param>
        /// <returns>A task that completes after the song library and selection are ready.</returns>
        /// <exception cref="OperationCanceledException">The title component was destroyed before initialization completed.</exception>
        async Task InitializeSongStorageAsync(CancellationToken cancellationToken)
        {
            var listConfig = MajEnv.RuntimeConfig.List;
            var selectedDir = listConfig.SelectedDir;
            var dirId = listConfig.SelectedDirGuid;
            var selectedIndex = listConfig.SelectedSongIndex;
            var selectedSongHash = listConfig.SelectedSongHash;
            var progress = new Progress<string>();
            progress.ProgressChanged += (o, e) =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _echoText.text = e;
                }
            };
            await Task.Delay(3000, cancellationToken);
            await SongStorage.InitAsync(cancellationToken);
            await SongStorage.RefreshLocal(progress, cancellationToken);

            if (!SongStorage.IsEmpty)
            {
                // Restore saved identities after both sources are available, not from an intermediate refresh cursor.
                listConfig.SelectedDir = selectedDir;
                var isDirMatched = false;

                if (dirId != Guid.Empty)
                {
                    var dirIndex = Array.FindIndex(SongStorage.Collections, x => x.Id == dirId);
                    if (dirIndex != -1)
                    {
                        listConfig.SelectedDir = dirIndex;
                        isDirMatched = true;
                    }
                }
                SongStorage.CollectionIndex = listConfig.SelectedDir;
                listConfig.SelectedDir = SongStorage.CollectionIndex;
                var selectedCollection = SongStorage.WorkingCollection;
                listConfig.SelectedDirGuid = selectedCollection.Id;

                if (isDirMatched && !string.IsNullOrEmpty(selectedSongHash))
                {
                    selectedIndex = Array.FindIndex(selectedCollection.ToArray(), x => x.Hash == selectedSongHash);
                    if (selectedIndex == -1)
                    {
                        selectedIndex = 0;
                    }
                }

                if (selectedCollection.IsEmpty)
                {
                    listConfig.SelectedSongIndex = 0;
                    listConfig.SelectedSongHash = string.Empty;
                    return;
                }
                else if (selectedIndex >= selectedCollection.Count)
                {
                    selectedCollection.Index = 0;
                    listConfig.SelectedSongIndex = 0;
                }
                else
                {
                    selectedCollection.Index = selectedIndex;
                }
                listConfig.SelectedSongIndex = selectedCollection.Index;
                listConfig.SelectedSongHash = selectedCollection.Current.Hash;
            }
        }

        void PrepareTitleEntrance()
        {
            _titleEntranceMotions.Cancel();
            _loadingRotationMotion.TryCancel();
            SetXxlbPositionX(_xxlbTargetX + XXLB_START_OFFSET_X);
            SetGraphicAlpha(_xxlbImage, 0f);
            SetMajdataPlayPositionX(_majdataPlayTargetX + MAJDATA_PLAY_START_OFFSET_X);
            SetGraphicAlpha(_majdataPlayImage, 0f);
            _ribbonImage.type = Image.Type.Filled;
            _ribbonImage.fillMethod = Image.FillMethod.Horizontal;
            _ribbonImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _ribbonImage.fillAmount = 0f;
            SetGraphicAlpha(_ribbonImage, 1f);
            SetGraphicAlpha(_echoText, 0f);
            SetEchoBackgroundWidth(0f);
            _loadingIndicator.localEulerAngles = Vector3.zero;
            _loadingIndicator.gameObject.SetActive(false);
        }

        void PlayTitleEntranceAnimation()
        {
            LMotion.Create(0f, 1f, XXLB_ENTRANCE_DURATION_SEC)
                   .WithEase(Ease.OutQuart)
                   .Bind(progress =>
                   {
                       SetXxlbPositionX(Mathf.LerpUnclamped(
                           _xxlbTargetX + XXLB_START_OFFSET_X,
                           _xxlbTargetX,
                           progress));
                       SetGraphicAlpha(_xxlbImage, progress);
                   })
                   .AddTo(_titleEntranceMotions);

            LMotion.Create(0f, 1f, XXLB_ENTRANCE_DURATION_SEC)
                   .WithEase(Ease.OutQuart)
                   .Bind(progress =>
                   {
                       SetMajdataPlayPositionX(Mathf.LerpUnclamped(
                           _majdataPlayTargetX + MAJDATA_PLAY_START_OFFSET_X,
                           _majdataPlayTargetX,
                           progress));
                       SetGraphicAlpha(_majdataPlayImage, progress);
                   })
                   .AddTo(_titleEntranceMotions);

            LMotion.Create(0f, 1f, RIBBON_REVEAL_DURATION_SEC)
                   .WithDelay(RIBBON_REVEAL_DELAY_SEC)
                   .WithEase(Ease.InOutSine)
                   .BindToFillAmount(_ribbonImage)
                   .AddTo(_titleEntranceMotions);

            LMotion.Create(0f, _echoBackgroundTargetWidth, ECHO_BACKGROUND_DURATION_SEC)
                   .WithDelay(ECHO_BACKGROUND_DELAY_SEC)
                   .WithEase(Ease.OutBack)
                   .Bind(SetEchoBackgroundWidth)
                   .AddTo(_titleEntranceMotions);

            LMotion.Create(0f, 1f, ECHO_TEXT_FADE_DURATION_SEC)
                   .WithDelay(ECHO_TEXT_DELAY_SEC)
                   .WithEase(Ease.InOutSine)
                   .WithOnComplete(StartLoadingAnimation)
                   .Bind(alpha => SetGraphicAlpha(_echoText, alpha))
                   .AddTo(_titleEntranceMotions);
        }

        void StartLoadingAnimation()
        {
            if (_isInitializationDone)
            {
                return;
            }

            _loadingIndicator.gameObject.SetActive(true);
            _loadingRotationMotion.TryCancel();
            _loadingRotationMotion = LMotion.Create(0f, -360f, LOADING_ROTATION_DURATION_SEC)
                                            .WithEase(Ease.Linear)
                                            .WithLoops(-1)
                                            .BindToLocalEulerAnglesZ(_loadingIndicator);
        }

        void FinishLoadingAnimation()
        {
            _isInitializationDone = true;
            _loadingRotationMotion.TryCancel();
            _loadingIndicator.localEulerAngles = Vector3.zero;
            _loadingIndicator.gameObject.SetActive(false);
        }

        void SetXxlbPositionX(float x)
        {
            var position = _xxlbRect.anchoredPosition;
            position.x = x;
            _xxlbRect.anchoredPosition = position;
        }

        void SetMajdataPlayPositionX(float x)
        {
            var position = _majdataPlayRect.anchoredPosition;
            position.x = x;
            _majdataPlayRect.anchoredPosition = position;
        }

        void SetEchoBackgroundWidth(float width)
        {
            var size = _echoBackground.sizeDelta;
            size.x = width;
            _echoBackground.sizeDelta = size;
        }

        static void SetGraphicAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        private void OnAreaClick(object sender, InputEventArgs e)
        {
            if (e.IsDown)
                return;
            if (e.IsButton)
            {
                switch (e.BZone)
                {
                    case ButtonZone.Test:
                        if (_flag)
                        {
                            EnterTestMode();
                        }
                        return;
                }
                NextScene();
            }
            else
            {
#if UNITY_ANDROID || UNITY_IOS
                NextScene();
#else
                switch (e.SArea)
                {
                    case SensorArea.A8:
                        MajInstances.AudioManager.OpenAsioPannel();
                        break;
                    case SensorArea.E4:
                        NextScene();
                        break;
                }
#endif
            }
        }

        void EnterTestMode()
        {
            InputManager.UnbindAnyArea(OnAreaClick);
            _flag = false;
            MajInstances.AudioManager.StopSFX("bgm_title.mp3");
            MajInstances.AudioManager.StopSFX("MajdataPlay.wav");
            MajInstances.SceneSwitcher.SwitchScene("SensorTest");
        }
        void NextScene()
        {
            InputManager.UnbindAnyArea(OnAreaClick);
            _flag = false;
            MajInstances.AudioManager.StopSFX("bgm_title.mp3");
            MajInstances.AudioManager.StopSFX("MajdataPlay.wav");
            if (MajEnv.Settings.Online.Enable)
            {
                MajInstances.SceneSwitcher.SwitchScene("Login", false);
            }
            else
            {
                MajInstances.SceneSwitcher.SwitchScene("List", false);
            }
        }

        void OnDestroy()
        {
            _titleEntranceMotions.Cancel();
            _loadingRotationMotion.TryCancel();
        }
    }
}

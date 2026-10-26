using MajdataPlay.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using MajdataPlay.Collections;
using System.Linq;
using System;
using SkiaSharp;
using System.Collections.Generic;
using MajdataPlay.Drawing;
using Random = UnityEngine.Random;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.List;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Settings;
using System.Threading.Tasks;
using MajdataPlay.Recording;
using MajdataPlay.Net;
using MajdataPlay.Scenes.Result.Components;
using UnityEngine.Serialization;
using System.Threading;
using LitMotion;
using LitMotion.Extensions;
using MajdataPlay.Diagnostics;

#if UNITY_EDITOR
using System.Globalization;
using MajdataPlay.i18n;
#endif

#nullable enable
namespace MajdataPlay.Scenes.Result
{
    public partial class ResultScreenManager : MajBehaviour
    {
        public bool IsDebug;
        public TextMeshProUGUI title;
        public TextMeshProUGUI artist;
        public TextMeshProUGUI designer;
        public TextMeshProUGUI level;

        public TextMeshProUGUI accDX;
        public TextMeshProUGUI accHistory;
        public TextMeshProUGUI rank;

        public TextMeshProUGUI criticalCount;
        public TextMeshProUGUI perfectCount;
        public TextMeshProUGUI greatCount;
        public TextMeshProUGUI goodCount;
        public TextMeshProUGUI missCount;
        public TextMeshProUGUI criticalLabel;
        public TextMeshProUGUI perfectLabel;
        public TextMeshProUGUI greatLabel;
        public TextMeshProUGUI goodLabel;
        public TextMeshProUGUI missLabel;

        public TextMeshProUGUI fastCount;
        public TextMeshProUGUI lateCount;
        public TextMeshProUGUI avgJudgeTime;

        public TextMeshProUGUI omg;
        public ResultsSubDisplayManager subMonitorManager;
        public Color critColor;
        public Color perfectColor;
        public Color greatColor;
        public Color goodColor;


        public TextMeshProUGUI clearText;
        public GameObject xxlb;

        public UserInfoDisplayer userInfoDisplayer;

        public Image coverImg;

        public RawImage _noteJudgeDiffGraph;

        public FavoriteAdder favoriteAdder;

        [SerializeField]
        [FormerlySerializedAs("levelCircleDisplayer")]
        Image _levelCircleDisplayer;

        [SerializeField]
        [FormerlySerializedAs("levelBGDisplayer")]
        Image _levelBGDisplayer;

        [SerializeField]
        [FormerlySerializedAs("dxScoreDisplayer")]
        DXScoreDisplayer _dxScoreDisplayer;

        [SerializeField]
        [FormerlySerializedAs("onlineInteractionSender")]
        OnlineInteractionSender _onlineInteractionSender;

        [SerializeField]
        SongFeedbackPanel _feedbackPanel;

        bool _isAdvancePending;
        int _lastNavigationFrame = -1;

        GameInfo _gameInfo = Majdata<GameInfo>.Instance!;

        Task _scoreSaveTask = Task.CompletedTask;
        bool _isAllTaskFinished = false;
        bool _isInited = false;
        bool _isExited = false;
#if UNITY_EDITOR
        bool _hasAchievementRollTarget = false;
        bool _achievementRollIsClassic = false;
        double _achievementRollTarget = 0d;
#endif
        MotionHandle _achievementRollAnim;
        readonly CompositeMotionHandle _judgeTextFadeAnims = new(5);
        readonly CompositeMotionHandle _clearMarkAnims = new(2);
        bool _isClearMarkScaleCached;
        Vector3 _clearMarkOriginalScale;

        const string PERFECT_COUNT_TEXT_TEMPLATE = "<line-height=60%>{0}\n<size=24>(-{1})</size>";
        public const float ACHIEVEMENT_ROLL_DURATION_SEC = 1.5f;
        public const float CLEAR_MARK_APPEAR_DURATION_SEC = 0.4f;
#if UNITY_EDITOR
        public const float RESULT_ANIMATION_DURATION_SEC = ACHIEVEMENT_ROLL_DURATION_SEC + CLEAR_MARK_APPEAR_DURATION_SEC;
#endif
        const int JUDGE_TEXT_ROW_COUNT = 5;
        const float JUDGE_TEXT_FADE_DURATION_SEC = ACHIEVEMENT_ROLL_DURATION_SEC / JUDGE_TEXT_ROW_COUNT;
        const float CLEAR_MARK_START_SCALE_MULTIPLIER = 5f;

        protected override void Awake()
        {
            base.Awake();
            InputManager.TouchButtonRingEdge = 4.8f;
#if UNITY_EDITOR
            // Direct scene previews bypass the startup scene's localization setup.
            if (string.IsNullOrEmpty(Localization.Current.Code))
            {
                Localization.Init();
                if (!Localization.SetLang(MajEnv.Settings.Display.Language)
                 && !Localization.SetLangByCode(Application.systemLanguage.ToLocale()))
                    Localization.SetLangByCode("en-US");
            }
#endif
        }
        void Start()
        {
            if (IsDebug)
            {
                var rect1 = _noteJudgeDiffGraph.GetComponent<RectTransform>().rect;
                var width = (int)rect1.width;
                var height = (int)rect1.height;


                // 生成随机样本用于测试
                const int sampleCount = 1000; // 可调整样本数量
                var arr = new float[sampleCount];
                var rng = new System.Random();
                for (int i = 0; i < sampleCount; i++)
                {
                    // 在 -150 .. 150 范围内生成随机浮点数
                    arr[i] = (float)(rng.NextDouble() * 300.0 - 150.0);
                }
                var testSpan = new ReadOnlySpan<float>(arr);
                

                _noteJudgeDiffGraph.texture = DrawNoteJudgeDiffGraph(testSpan, height, width);
                return;
            }


            rank.text = "";
            var listConfig = MajEnv.RuntimeConfig.List;
            var result = _gameInfo.GetLastResult();
            var isClassic = MajEnv.Settings.Judge.Mode == JudgeModeOption.Classic;

            CabinetLed.SetAllLight(Color.white);

            var totalJudgeRecord = JudgeDetail.UnpackJudgeRecord(result.JudgeRecord.TotalJudgeInfo);
            var song = result.SongDetail;
            var historyResult = ScoreManager.GetScore(song, listConfig.SelectedDiff);
            var score = MaiScore.CreateFromResult(result, result.Level);
            var diffColor = RuntimeDatabase.DifficultyColors[(int)_gameInfo.CurrentLevel];
            _levelCircleDisplayer.color = diffColor;
            _levelBGDisplayer.color = diffColor;
            _onlineInteractionSender.Init(song, score);
            favoriteAdder.SetSong(song);
            _feedbackPanel.Init(song, !_gameInfo.IsDanMode);
            userInfoDisplayer.DisplayFromSong(song);


            if (result.Acc.DX < 97)
            {
                omg.text = "您输了";
                xxlb.GetComponent<Animator>().SetTrigger("Bad");
            }
            else
            {
                omg.text = "您赢了";
                xxlb.GetComponent<Animator>().SetTrigger("Good");
            }

            title.text = song.Title;
            artist.text = song.Artist;
            designer.text = song.Designers[(int)_gameInfo.CurrentLevel] ?? "Undefined";
            level.text = song.Levels[(int)_gameInfo.CurrentLevel];

            var nowAcc = isClassic ? result.Acc.Classic : result.Acc.DX;
            var historyAcc = isClassic ? historyResult.Acc.Classic : historyResult.Acc.DX;
            accHistory.text = $"{nowAcc - historyAcc:+0.0000;-0.0000;0}%";

            _dxScoreDisplayer.SetScore(result, ACHIEVEMENT_ROLL_DURATION_SEC);

            criticalCount.text = $"{totalJudgeRecord.CriticalPerfect}";
            perfectCount.text = $"{totalJudgeRecord.Perfect}";
            greatCount.text = $"{totalJudgeRecord.Great}";
            goodCount.text = $"{totalJudgeRecord.Good}";
            missCount.text = $"{totalJudgeRecord.Miss}";
            PlayResultAnimation(nowAcc, isClassic);

            fastCount.text = $"{result.Fast}";
            lateCount.text = $"{result.Late}";

            subMonitorManager.SetJudgeDetail(result.JudgeRecord);

            var rect = _noteJudgeDiffGraph.GetComponent<RectTransform>().rect;
            _noteJudgeDiffGraph.texture = DrawNoteJudgeDiffGraph(result.NoteJudgeDiffs.Span, (int)rect.height, (int)rect.width);
            if(MajEnv.Settings.Debug.OffsetUnit == OffsetUnitOption.Second)
            {
                if (result.NoteJudgeDiffs.IsEmpty)
                {
                    avgJudgeTime.text = $"0.000s";
                }
                else
                {
                    avgJudgeTime.text = $"{result.NoteJudgeDiffs.ToArray().Average() / 1000f:F3}s";
                }
            }
            else
            {
                if (result.NoteJudgeDiffs.IsEmpty)
                {
                    avgJudgeTime.text = $"0.0f";
                }
                else
                {
                    avgJudgeTime.text = $"{result.NoteJudgeDiffs.ToArray().Average() / MajEnv.FRAME_LENGTH_MSEC:F1}f";
                }
            }

            
            LoadCover(song).Forget();

            var breakJudgeInfo = JudgeDetail.UnpackJudgeRecord(result.JudgeRecord[ScoreNoteType.Break]);


            if (totalJudgeRecord.IsAllPerfect)
            {
                if (breakJudgeInfo.IsTheoretical)
                {
                    clearText.text = "AP+";
                }
                else
                {
                    clearText.text = "AP";
                }
            }
            else if (totalJudgeRecord.IsFullCombo)
            {
                if (totalJudgeRecord.IsFullComboPlus) { 
                    clearText.text = "FC+";
                }
                else
                {
                    clearText.text = "FC";
                }
            }
            else
            {
                clearText.text = "";
            }
            PlayClearMarkAnimation();

            MajInstances.AudioManager.PlaySFX("bgm_result.mp3", true);
            PlayVoice(result.Acc.DX, 
                song,
                totalJudgeRecord.IsAllPerfect, 
                totalJudgeRecord.IsFullCombo, 
                gameObject.GetCancellationTokenOnDestroy()).Forget();
            if (!MajInstances.GameManager.Settings.Mod.IsAnyModActive())
            {
                var localScoreSaveTask = ScoreManager.SaveScore(result, result.Level);
                if (song is OnlineSongDetail onlineSong && onlineSong.ServerInfo.RuntimeConfig.AuthMethod != NetAuthMethodOption.None)
                {
                    var task = _onlineInteractionSender.SendScoreAsync();
                    _scoreSaveTask = Task.WhenAll(localScoreSaveTask, task);
                }
                else
                {
                    _scoreSaveTask = localScoreSaveTask;
                }
            }
            WaitForTaskAsync().Forget();
        }
        void PlayResultAnimation(double targetAchievement, bool isClassic)
        {
            PlayAchievementRollAnimation(targetAchievement, isClassic);
            PlayJudgeTextFadeInAnimation();
        }
        public void PlayAchievementRollAnimation(double targetAchievement, bool isClassic)
        {
            _achievementRollAnim.TryCancel();
#if UNITY_EDITOR
            _hasAchievementRollTarget = true;
            _achievementRollTarget = targetAchievement;
            _achievementRollIsClassic = isClassic;
#endif
            accDX.text = FormatAchievement(0d, isClassic);
            _achievementRollAnim = LMotion.Create(0d, targetAchievement, ACHIEVEMENT_ROLL_DURATION_SEC)
                                          .WithEase(Ease.OutQuart)
                                          .Bind(value => accDX.text = FormatAchievement(value, isClassic));
        }
#if UNITY_EDITOR
        public bool PreviewAchievementRollAnimation()
        {
            if (_hasAchievementRollTarget)
            {
                PlayResultAnimation(_achievementRollTarget, _achievementRollIsClassic);
                _dxScoreDisplayer.PlayAnimationPreview(ACHIEVEMENT_ROLL_DURATION_SEC);
                PlayClearMarkAnimation();
                return true;
            }

            var scoreText = accDX.text.Trim().TrimEnd('%');
            if (!double.TryParse(scoreText, NumberStyles.Float, CultureInfo.CurrentCulture, out var previewAchievement))
            {
                return false;
            }

            var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var separatorIndex = scoreText.LastIndexOf(decimalSeparator, StringComparison.Ordinal);
            var decimalPlaces = separatorIndex < 0 ? 0 : scoreText.Length - separatorIndex - decimalSeparator.Length;
            PlayResultAnimation(previewAchievement, decimalPlaces <= 2);
            _dxScoreDisplayer.PlayAnimationPreview(ACHIEVEMENT_ROLL_DURATION_SEC);
            PlayClearMarkAnimation();
            return true;
        }
#endif
        void PlayJudgeTextFadeInAnimation()
        {
            _judgeTextFadeAnims.Cancel();
            var judgeRows = new[]
            {
                (Label: criticalLabel, Count: criticalCount),
                (Label: perfectLabel, Count: perfectCount),
                (Label: greatLabel, Count: greatCount),
                (Label: goodLabel, Count: goodCount),
                (Label: missLabel, Count: missCount)
            };

            for (var i = 0; i < judgeRows.Length; i++)
            {
                var (label, count) = judgeRows[i];
                var delay = i * JUDGE_TEXT_FADE_DURATION_SEC;
                SetTextAlpha(label, 0f);
                SetTextAlpha(count, 0f);
                LMotion.Create(0f, 1f, JUDGE_TEXT_FADE_DURATION_SEC)
                       .WithDelay(delay)
                       .WithEase(Ease.OutQuad)
                       .Bind(alpha =>
                       {
                           SetTextAlpha(label, alpha);
                           SetTextAlpha(count, alpha);
                       })
                       .AddTo(_judgeTextFadeAnims);
            }
        }
        void PlayClearMarkAnimation()
        {
            _clearMarkAnims.Cancel();
            var clearMarkTransform = clearText.transform;
            if (!_isClearMarkScaleCached)
            {
                _isClearMarkScaleCached = true;
                _clearMarkOriginalScale = clearMarkTransform.localScale;
            }

            var startScale = _clearMarkOriginalScale * CLEAR_MARK_START_SCALE_MULTIPLIER;
            clearMarkTransform.localScale = startScale;
            SetTextAlpha(clearText, 0f);
            if (string.IsNullOrEmpty(clearText.text))
            {
                return;
            }

            LMotion.Create(startScale, _clearMarkOriginalScale, CLEAR_MARK_APPEAR_DURATION_SEC)
                   .WithDelay(ACHIEVEMENT_ROLL_DURATION_SEC)
                   .WithEase(Ease.OutBack)
                   .BindToLocalScale(clearMarkTransform)
                   .AddTo(_clearMarkAnims);
            LMotion.Create(0f, 1f, CLEAR_MARK_APPEAR_DURATION_SEC)
                   .WithDelay(ACHIEVEMENT_ROLL_DURATION_SEC)
                   .WithEase(Ease.OutQuad)
                   .BindToColorA(clearText)
                   .AddTo(_clearMarkAnims);
        }
#if UNITY_EDITOR
        public void StopResultAnimationPreview()
        {
            _achievementRollAnim.TryCancel();
            _judgeTextFadeAnims.Cancel();
            _clearMarkAnims.Cancel();
            _dxScoreDisplayer.StopAnimationPreview();
            if (_hasAchievementRollTarget)
            {
                accDX.text = FormatAchievement(_achievementRollTarget, _achievementRollIsClassic);
            }

            SetTextAlpha(criticalCount, 1f);
            SetTextAlpha(perfectCount, 1f);
            SetTextAlpha(greatCount, 1f);
            SetTextAlpha(goodCount, 1f);
            SetTextAlpha(missCount, 1f);
            SetTextAlpha(criticalLabel, 1f);
            SetTextAlpha(perfectLabel, 1f);
            SetTextAlpha(greatLabel, 1f);
            SetTextAlpha(goodLabel, 1f);
            SetTextAlpha(missLabel, 1f);
            if (_isClearMarkScaleCached)
            {
                clearText.transform.localScale = _clearMarkOriginalScale;
            }
            SetTextAlpha(clearText, string.IsNullOrEmpty(clearText.text) ? 0f : 1f);
        }
#endif
        static void SetTextAlpha(TextMeshProUGUI text, float alpha)
        {
            var color = text.color;
            color.a = alpha;
            text.color = color;
        }
        static string FormatAchievement(double achievement, bool isClassic)
        {
            var precision = isClassic ? 100d : 10000d;
            var digits = isClassic ? 2 : 4;
            var truncatedAchievement = Math.Floor(achievement * precision) / precision;
            return $"{truncatedAchievement.ToString($"F{digits}")}%";
        }
        async UniTask WaitForTaskAsync()
        {
            await UniTask.Delay(3000);
            _isInited = true;
            CabinetLed.SetButtonLight(Color.yellow, 4);
            var t1 = _scoreSaveTask;
            if (Recorder.IsEnabled)
            {
                await UniTask.Delay(3000);
            }
            var t2 = Recorder.StopRecordAsync();
            while (!t1.IsCompleted || !t2.IsCompleted)
            {
                await UniTask.Yield();
            }
            _isAllTaskFinished = true;
            CabinetLed.SetButtonLight(Color.green, 3);
        }
        async UniTask LoadCover(ISongDetail song)
        {
            var cover = await song.GetCoverAsync(true);
            await UniTask.SwitchToMainThread();
            coverImg.sprite = cover;
        }

        async UniTask PlayVoice(double dxacc, ISongDetail song, bool isAP, bool isFC, CancellationToken token = default)
        {
            try
            {
                AudioSampleWrap? lastSample = null;
                if (dxacc >= 97)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("clear.wav")!;
                    while (lastSample.IsPlaying) await UniTask.Yield(token, true);
                }
                if (dxacc >= 100.5f)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("SSS+.wav")!;
                    rank.text = "SSS+";
                }
                else if (dxacc >= 100f)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("SSS.wav")!;
                    rank.text = "SSS";
                }
                else if (dxacc >= 99.5f)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("SS+.wav")!;
                    rank.text = "SS+";
                }
                else if (dxacc >= 99f)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("SS.wav")!;
                    rank.text = "SS";
                }
                else if (dxacc >= 98f)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("S+.wav")!;
                    rank.text = "S+";
                }
                else if (dxacc >= 97f)
                {
                    lastSample = MajInstances.AudioManager.PlaySFX("S.wav")!;
                    rank.text = "S";
                }

                while (lastSample != null && lastSample.IsPlaying)
                {
                    await UniTask.Yield(token, true);
                }

                if (isAP)
                {
                    var list = new string[] { "ap_comment.wav", "ap_comment_2.wav" };
                    lastSample = MajInstances.AudioManager.PlaySFX(list[Random.Range(0, list.Length)]);
                }
                else if(isFC)
                {
                    var list = new string[] { "fc_comment.wav", "fc_comment_2.wav" };
                    lastSample = MajInstances.AudioManager.PlaySFX(list[Random.Range(0, list.Length)]);
                }else if (dxacc >= 97f)
                {
                    var list = new string[] { "clear_comment.wav", "clear_comment_2.wav", "clear_comment_3.wav", "clear_comment_4.wav" };
                    lastSample = MajInstances.AudioManager.PlaySFX(list[Random.Range(0, list.Length)]);
                }
                else
                {
                    var list = new string[] { "fail_comment.wav", "fail_comment_2.wav", "fail_comment_3.wav", "fail_comment_4.wav", "fail_comment_5.wav" };
                    lastSample = MajInstances.AudioManager.PlaySFX(list[Random.Range(0, list.Length)]);
                }

                while (lastSample != null && lastSample.IsPlaying)
                {
                    await UniTask.Yield(token, true);
                }

                if (song is OnlineSongDetail)
                {
                    MajInstances.AudioManager.PlaySFX("dian_zan.wav");
                }
            }
            catch (Exception e)
            { 
                MajDebug.LogException(e); 
            }
        }

        void Update()
        {
            if (!_isInited || _isExited) return;
            if (InputManager.IsButtonPressedInThisFrame(ButtonZone.A5))
            {
                BackOrFavorite();
            }
            if (InputManager.IsButtonClickCompletedInThisFrame(ButtonZone.A4))
            {
                Advance();
            }
            else if (InputManager.IsSensorClickCompletedInThisFrame(SensorArea.B4)
                  || InputManager.IsSensorClickCompletedInThisFrame(SensorArea.E4))
            {
                OpenFeedback();
            }
        }

        public void BackOrFavorite()
        {
            if (!_isInited || _isExited || _isAdvancePending || _lastNavigationFrame == Time.frameCount) return;
            _lastNavigationFrame = Time.frameCount;
            if (_feedbackPanel.IsOpen)
            {
                ExitResults();
            }
            else
            {
                favoriteAdder.FavoratePressed();
            }
        }

        public void Advance()
        {
            if (!_isInited || !_isAllTaskFinished || _isExited || _isAdvancePending
             || _lastNavigationFrame == Time.frameCount) return;
            _lastNavigationFrame = Time.frameCount;
            if (_feedbackPanel.IsAvailable && _feedbackPanel.IsFirstPlay && !_feedbackPanel.IsOpen)
            {
                OpenFeedbackAsync().Forget();
                return;
            }
            if (_feedbackPanel.IsOpen)
            {
                SubmitFeedbackAsync().Forget();
                return;
            }
            ExitResults();
        }

        public void OpenFeedback()
        {
            if (!_isInited || !_isAllTaskFinished || _isExited || _isAdvancePending
             || !_feedbackPanel.IsAvailable || _feedbackPanel.IsFirstPlay || _feedbackPanel.IsOpen
             || _lastNavigationFrame == Time.frameCount) return;
            _lastNavigationFrame = Time.frameCount;
            OpenFeedbackAsync().Forget();
        }

        async UniTask OpenFeedbackAsync()
        {
            _isAdvancePending = true;
            _onlineInteractionSender.enabled = false;
            try
            {
                await _feedbackPanel.OpenAsync();
            }
            catch (OperationCanceledException) when (this == null) { }
            finally
            {
                _isAdvancePending = false;
            }
        }

        async UniTask SubmitFeedbackAsync()
        {
            _isAdvancePending = true;
            try
            {
                if (await _feedbackPanel.SubmitAsync() && this != null)
                {
                    ExitResults();
                }
            }
            finally
            {
                _isAdvancePending = false;
            }
        }

        void ExitResults()
        {
            if (_isExited) return;
            var canNextRound = _gameInfo.NextRound();
            _isExited = true;
            if (_gameInfo.IsDanMode && !canNextRound)
            {
                MajInstances.SceneSwitcher.SwitchScene("TotalResult");
                return;
            }
            MajInstances.AudioManager.StopSFX("bgm_result.mp3");
            MajInstances.SceneSwitcher.SwitchScene(_gameInfo.IsDanMode ? "Game" : "List", false);
        }
        void OnDestroy()
        {
            _achievementRollAnim.TryCancel();
            _judgeTextFadeAnims.Cancel();
            _clearMarkAnims.Cancel();
            InputManager.TouchButtonRingEdge = 5.4f;
            DestroyImmediate(_noteJudgeDiffGraph.texture, true);
        }
        Texture DrawNoteJudgeDiffGraph(ReadOnlySpan<float> dataset,
                               int height,
                               int width)
        {
            const float DIFF_MIN = -150f;
            const float DIFF_MAX = 150f;
            const float DIFF_RANGE = DIFF_MAX - DIFF_MIN;

            const int CHART_PADDING_LEFT = 0;
            const int CHART_PADDING_RIGHT = 0;
            const int CHART_PADDING_TOP = 0;
            const int CHART_PADDING_BOTTOM = 0;

            const int BAR_COUNT = 72; 

            var chartWidth = width - CHART_PADDING_LEFT - CHART_PADDING_RIGHT;
            var chartHeight = height - CHART_PADDING_TOP - CHART_PADDING_BOTTOM;

            var imageInfo = new SKImageInfo(width, height);
            Span<Point> points = stackalloc Point[BAR_COUNT];
            var maxSampleCount = 0;
            using var surface = SKSurface.Create(imageInfo);
            using var critPaint = new SKPaint();
            using var perfectPaint = new SKPaint();
            using var greatPaint = new SKPaint();
            using var goodPaint = new SKPaint();
            using var emptyPaint = new SKPaint();
            //using var linePaint = new SKPaint();
            //using var midLinePaint = new SKPaint();
            var canvas = surface.Canvas;

            canvas.Clear(SKColor.Empty);

            critPaint.Color = critColor.ToSkColor();
            critPaint.IsAntialias = true;
            critPaint.Style = SKPaintStyle.Fill;

            perfectPaint.Color = perfectColor.ToSkColor();
            perfectPaint.IsAntialias = true;
            perfectPaint.Style = SKPaintStyle.Fill;

            greatPaint.Color = greatColor.ToSkColor();
            greatPaint.IsAntialias = true;
            greatPaint.Style = SKPaintStyle.Fill;

            goodPaint.Color = goodColor.ToSkColor();
            goodPaint.IsAntialias = true;
            goodPaint.Style = SKPaintStyle.Fill;

            emptyPaint.Color = SKColors.Transparent;
            emptyPaint.IsAntialias = true;
            emptyPaint.Style = SKPaintStyle.Fill;

            //linePaint.Color = SKColors.White.WithAlpha(180);
            //linePaint.IsAntialias = true;
            //linePaint.Style = SKPaintStyle.Stroke;
            //linePaint.StrokeWidth = 1f;

            //midLinePaint.Color = SKColors.Red.WithAlpha(180);
            //midLinePaint.IsAntialias = true;
            //midLinePaint.Style = SKPaintStyle.Stroke;
            //midLinePaint.StrokeWidth = 2f;

            // 计算每个 bin 的区间
            var binWidthDiff = DIFF_RANGE / (float)BAR_COUNT; // 每个 bin 覆盖的 diff 范围
            for (int i = 0; i < BAR_COUNT; i++)
            {
                var binCenter = DIFF_MIN + (i + 0.5f) * binWidthDiff;
                var binStart = DIFF_MIN + i * binWidthDiff;
                var binEnd = binStart + binWidthDiff;
                var range = new Range<float>(binStart, binEnd, ContainsType.Closed);
                var samples = dataset.FindAll(x => range.InRange(x));
                var sampleCount = samples.Length;

                if (sampleCount > maxSampleCount) maxSampleCount = sampleCount;

                points[i] = new Point()
                {
                    X = binCenter, // 存储中心 diff 以便后续判断颜色
                    Y = sampleCount,
                    Diff = binCenter,
                    IsEmpty = samples.IsEmpty
                };
            }

            // 柱状图参数：间距与圆角
            var barSpacing = 2f; // 每个柱子之间的 padding（像素），可调整
            var totalSpacing = barSpacing * (BAR_COUNT - 1);
            var barWidth = Math.Max(1f, (chartWidth - totalSpacing) / BAR_COUNT);
            var cornerRadius = MathF.Min(barWidth, chartHeight) * 0.5f;

            // 绘制每个柱子
            for (int i = 0; i < BAR_COUNT; i++)
            {
                var origin = points[i];
                if (maxSampleCount == 0)
                {
                    // 没有样本，直接跳出（或可绘制占位）
                    continue;
                }

                // 归一化高度
                var normalizedHeight = origin.Y / (float)maxSampleCount; // 0..1
                var drawHeight = normalizedHeight * chartHeight;

                // 计算 x 位置：从左 padding 开始，按宽度+间距排列
                var x = CHART_PADDING_LEFT + i * (barWidth + barSpacing);
                var yTop = CHART_PADDING_TOP + (chartHeight - drawHeight);
                var rect = new SKRect(x, yTop, x + barWidth, CHART_PADDING_TOP + chartHeight);

                // 选择颜色（根据柱子中心 diff）
                var absDiff = Math.Abs(origin.Diff);
                SKPaint paintToUse;
                if (absDiff <= MajEnv.FRAME_LENGTH_MSEC)
                    paintToUse = critPaint;
                else if (absDiff <= MajEnv.FRAME_LENGTH_MSEC * 3)
                    paintToUse = perfectPaint;
                else if (absDiff <= MajEnv.FRAME_LENGTH_MSEC * 6)
                    paintToUse = greatPaint;
                else
                    paintToUse = goodPaint;

                // 如果你想跳过空桶：保持 continue；否则可绘制浅色占位
                if (origin.IsEmpty)
                {
                    // 跳过绘制空桶（若想显示占位，改为绘制 emptyPaint）
                    continue;
                }

                // 绘制圆角矩形柱子
                canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, paintToUse);

                // 可选细边框增强分隔感
                using var borderPaint = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(0),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f,
                    IsAntialias = true
                };
                canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, borderPaint);
            }

            // 绘制底线
            //canvas.DrawLine(CHART_PADDING_LEFT,
            //                CHART_PADDING_TOP + chartHeight + 0.5f,
            //                CHART_PADDING_LEFT + chartWidth,
            //                CHART_PADDING_TOP + chartHeight + 0.5f, linePaint);

            // 绘制竖向网格线（可保留）
            //for (var i = -9; i < 10; i++)
            //{
            //    var index = i + 9f;
            //    var gridX = (chartWidth * (index / 18f)) + CHART_PADDING_LEFT;
            //    var start = new SKPoint()
            //    {
            //        X = gridX,
            //        Y = CHART_PADDING_TOP
            //    };
            //    var end = new SKPoint()
            //    {
            //        X = gridX,
            //        Y = CHART_PADDING_TOP + chartHeight
            //    };
            //    canvas.DrawLine(start, end, linePaint);
            //}

            // 绘制中线（Diff = 0）
            // 计算中线 x 坐标：Diff=0 对应的相对位置为 (0 - DIFF_MIN) / DIFF_RANGE
            //var centerRatio = (0f - DIFF_MIN) / DIFF_RANGE; // 0..1
            //var centerX = CHART_PADDING_LEFT + centerRatio * chartWidth;
            //// 中线从顶部到底部
            //canvas.DrawLine(centerX, CHART_PADDING_TOP, centerX, CHART_PADDING_TOP + chartHeight, midLinePaint);

            return surface.ToTexture2D(imageInfo);
        }

        readonly struct Point
        {
            public float X { get; init; }
            public float Y { get; init; }
            public float Diff { get; init; }
            public bool IsEmpty { get; init; }
        }
    }
}

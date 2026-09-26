using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TMPro;
using Unity.Burst.Intrinsics;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Scenes.Game
{
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    public class ObjectCounter : MonoBehaviour
    {
        #region UIconsts

        public Color AchievementDudColor; // = new Color32(63, 127, 176, 255);
        public Color AchievementBronzeColor; // = new Color32(127, 48, 32, 255);
        public Color AchievementSilverColor; // = new Color32(160, 160, 160, 255);
        public Color AchievementGoldColor; // = new Color32(224, 191, 127, 255);

        public Color CPComboColor;
        public Color PComboColor;
        public Color ComboColor;
        public Color DXScoreColor;

        public Color EarlyDiffColor;
        public Color LateDiffColor;

        [SerializeField]
        [FormerlySerializedAs("mainScreenInfoDisplayerColorPalette")]
        Color[] _mainScreenInfoDisplayerColorPalette = Array.Empty<Color>();
        [SerializeField]
        [FormerlySerializedAs("subScreenInfoDisplayerColorPalette")]
        Color[] _subScreenInfoDisplayerColorPalette = Array.Empty<Color>();

        const string DX_ACC_RATE_STRING = "{0:F4}%";
        const string CLASSIC_ACC_RATE_STRING = "{0:F2}%";
        const string COMBO_OR_DXSCORE_STRING = "{0}";
        const string DIFF_STRING = "{0:F2}";
        const string DXSCORE_RANK_HEADER_STRING = "✧{0}";
        const string DXSCORE_RANK_BODY_STRING = "+{0}";
        const string LATE_STRING = "LATE";
        const string FAST_STRING = "FAST";
        const string JUDGE_RESULT_STRING = "{0}";

        const int COLOR_PALETTE_CRITICAL_COMBO_COLOR_INDEX = 0;
        const int COLOR_PALETTE_PERFECT_COMBO_COLOR_INDEX = 1;
        const int COLOR_PALETTE_COMBO_COLOR_INDEX = 2;
        const int COLOR_PALETTE_ACHIEVEMENT_DUB_COLOR_INDEX = 3;
        const int COLOR_PALETTE_ACHIEVEMENT_BRONZE_COLOR_INDEX = 4;
        const int COLOR_PALETTE_ACHIEVEMENT_SILVER_COLOR_INDEX = 5;
        const int COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX = 6;
        const int COLOR_PALETTE_DX_SCORE_COLOR_INDEX = 7;
        const int COLOR_PALETTE_DIFF_EARLY_COLOR_INDEX = 8;
        const int COLOR_PALETTE_DIFF_LATE_COLOR_INDEX = 9;

        readonly static Utf16PreparedFormat<double> DX_ACC_RATE_FORMAT = ZString.PrepareUtf16<double>(DX_ACC_RATE_STRING);
        readonly static Utf16PreparedFormat<double> CLASSIC_ACC_RATE_FORMAT = ZString.PrepareUtf16<double>(CLASSIC_ACC_RATE_STRING);
        readonly static Utf16PreparedFormat<double> COMBO_OR_DXSCORE_FORMAT = ZString.PrepareUtf16<double>(COMBO_OR_DXSCORE_STRING);
        readonly static Utf16PreparedFormat<double> DIFF_FORMAT = ZString.PrepareUtf16<double>(DIFF_STRING);
        readonly static Utf16PreparedFormat<double> DXSCORE_RANK_HEADER_FORMAT = ZString.PrepareUtf16<double>(DXSCORE_RANK_HEADER_STRING);
        readonly static Utf16PreparedFormat<double> DXSCORE_RANK_BODY_FORMAT = ZString.PrepareUtf16<double>(DXSCORE_RANK_BODY_STRING);
        readonly static Utf16PreparedFormat<long> JUDGMENT_COUNT_FORMAT = ZString.PrepareUtf16<long>(JUDGE_RESULT_STRING);

        #endregion

        #region count&scores
        public bool AllFinished
        {
            get
            {
                return _noteJudgeStats.TapJudgedCount == TapSum &&
                       _noteJudgeStats.HoldJudgedCount == HoldSum &&
                       _noteJudgeStats.SlideJudgedCount == SlideSum &&
                       _noteJudgeStats.TouchJudgedCount == TouchSum &&
                       _noteJudgeStats.BreakJudgedCount == BreakSum;
            }
        }

        public int TapSum { get; private set; }
        public int HoldSum { get; private set; }
        public int SlideSum { get; private set; }
        public int TouchSum { get; private set; }
        public int BreakSum { get; private set; }
        public int NoteSum { get; private set; }

        public long TotalNoteScore => TotalNoteBaseScore + TotalNoteExtraScore;
        public long TotalNoteBaseScore { get; private set; }
        public long TotalNoteExtraScore { get; private set; }

        public long CurrentNoteScore => CurrentNoteBaseScore + CurrentNoteExtraScore;
        public long CurrentNoteScoreClassic => CurrentNoteBaseScore + CurrentNoteExtraScoreClassic;
        public long CurrentNoteBaseScore { get; private set; }
        public long CurrentNoteExtraScore { get; private set; }
        public long CurrentNoteExtraScoreClassic { get; private set; }

        public long TotalLostNoteScore => LostNoteBaseScore + LostNoteExtraScore;
        public long TotalLostNoteScoreClassic => LostNoteBaseScore + LostNoteExtraScoreClassic;
        public long LostNoteBaseScore { get; private set; }
        public long LostNoteExtraScore { get; private set; }
        public long LostNoteExtraScoreClassic { get; private set; }

        public ref readonly AccurateStatistics AccurateStats
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Unsafe.As<double, AccurateStatistics>(ref _accRate[0]);
            }
        }
        public ref readonly NoteJudgeStatistics JudgeStats
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Unsafe.As<NoteJudgeStatisticsInternal, NoteJudgeStatistics>(ref _noteJudgeStats);
            }
        }

        readonly double[] _accRate = new double[5]
        {
            0.00,    // classic acc (+)
            100.00,  // classic acc (-)
            101.0000,// acc 101(-)
            100.0000,// acc 100(-)
            0.0000,  // acc (+)
        };

        NoteJudgeStatisticsInternal _noteJudgeStats;
        JudgeDisplayOption _breakFastLateDisplayOption;
        JudgeDisplayOption _noteFastLateDisplayOption;

        float _lastJudgeDiff = 0; // Note judge diff
        float _diffTimer = 3;

        long _totalDXScore = 0;
        long _lostDXScore = 0;

        long _combo = 0; // Combo
        internal long OniimaiCombo => _combo;
        internal long OniimaiRemainingDxScore => _totalDXScore + _lostDXScore;
        long _pCombo = 0; // Perfect Combo
        long _cPCombo = 0; // Critical Perfect


        List<float> _noteJudgeDiffList = new();

        readonly static int[] _judgedTapCount = new int[15];
        readonly static int[] _judgedHoldCount = new int[15];
        readonly static int[] _judgedTouchCount = new int[15];
        readonly static int[] _judgedTouchHoldCount = new int[15];
        readonly static int[] _judgedSlideCount = new int[15];
        readonly static int[] _judgedBreakCount = new int[15];
        readonly static int[] _totalJudgedCount = new int[15];

        readonly static Dictionary<JudgeGrade, int> _dictJudgedTapCount = new();
        readonly static Dictionary<JudgeGrade, int> _dictJudgedHoldCount = new();
        readonly static Dictionary<JudgeGrade, int> _dictJudgedTouchCount = new();
        readonly static Dictionary<JudgeGrade, int> _dictJudgedTouchHoldCount = new();
        readonly static Dictionary<JudgeGrade, int> _dictJudgedSlideCount = new();
        readonly static Dictionary<JudgeGrade, int> _dictJudgedBreakCount = new();
        readonly static Dictionary<JudgeGrade, int> _dictTotalJudgedCount = new();
        #endregion

        #region UIrefs
        [SerializeField]
        [FormerlySerializedAs("criticalCountDisplayer")]
        TextMeshProUGUI _criticalCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("perfectCountDisplayer")]
        TextMeshProUGUI _perfectCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("greatCountDisplayer")]
        TextMeshProUGUI _greatCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("goodCountDisplayer")]
        TextMeshProUGUI _goodCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("missCountDisplayer")]
        TextMeshProUGUI _missCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("fastCountDisplayer")]
        TextMeshProUGUI _fastCountDisplayer;

        [SerializeField]
        [FormerlySerializedAs("lateCountDisplayer")]
        TextMeshProUGUI _lateCountDisplayer;

        TextMeshProUGUI _rate;

        [SerializeField]
        [FormerlySerializedAs("centerInfoDisplayerHeader")]
        TextMeshProUGUI _centerInfoDisplayerHeader;
        [SerializeField]
        [FormerlySerializedAs("centerInfoDisplayerText")]
        TextMeshProUGUI _centerInfoDisplayerText;

        GameObject _centerInfoDisplayerObject;
        GameObject _centerInfoDisplayerHeaderObject;

        [SerializeField]
        [FormerlySerializedAs("secondaryInfoDisplayerHeader")]
        TextMeshProUGUI _secondaryInfoDisplayerHeader;
        [SerializeField]
        [FormerlySerializedAs("secondaryInfoDisplayerText")]
        TextMeshProUGUI _secondaryInfoDisplayerText;

        GameObject _secondaryInfoDisplayerObject;
        GameObject _secondaryInfoDisplayerHeaderObject;

        [SerializeField]
        [FormerlySerializedAs("subScreenInfoDisplayerText")]
        TextMeshProUGUI _subScreenInfoDisplayerText;

        GameObject _subScreenInfoDisplayerObject;

        [SerializeField]
        GameObject _topInfoJudgeParent;
        [SerializeField]
        TextMeshProUGUI _topInfoPerfect;
        [SerializeField]
        TextMeshProUGUI _topInfoGreat;
        [SerializeField]
        TextMeshProUGUI _topInfoGood;
        [SerializeField]
        TextMeshProUGUI _topInfoMiss;

        [SerializeField]
        GameObject _topInfoTimingParent;
        [SerializeField]
        TextMeshProUGUI _topInfoFast;
        [SerializeField]
        TextMeshProUGUI _topInfoLate;

        [SerializeField]
        [FormerlySerializedAs("judgeTimingGaugeParent")]
        GameObject _judgeTimingGaugeParent;
        [SerializeField]
        [FormerlySerializedAs("judgeTimingGaugeScaleParent")]
        GameObject _judgeTimingGaugeScaleParent;

        [SerializeField]
        [FormerlySerializedAs("judgeTimingGaugeCursor")]
        RectTransform _judgeTimingGaugeCursor;

        GameObject[] _judgeTimingGaugeScales = Array.Empty<GameObject>();
        RectTransform[] _judgeTimingGaugeScaleTransforms = Array.Empty<RectTransform>();
        GaugeScaleBinding[] _gaugeScaleBindings = Array.Empty<GaugeScaleBinding>();

        #endregion

        float _judgeTimingGaugeAvg = 0f;
        int _gaugeScaleBindingCursor = 0;
        int _judgeTimingGaugeScaleCount = 0;
        bool _isOutlinePlayRequested = false;
        bool _isJudgeTimingGaugeEnabled = false;
        BGInfoOption _centerInfoDisplayOption = BGInfoOption.None;
        BGInfoOption _secondaryInfoDisplayOption = BGInfoOption.None;
        BGInfoOption _subScreenInfoDisplayOption = BGInfoOption.None;

        XxlbDanceRequest _xxlbDanceRequest = new();

        GameInfo _gameInfo = Majdata<GameInfo>.Instance!;
        OutlineLoader _outline;
        GamePlayManager _gpManager;
        XxlbAnimationController _xxlbController;

        Utf16ValueStringBuilder _sb = ZString.CreateStringBuilder();


        void Awake()
        {
            Majdata<ObjectCounter>.Instance = this;
            _rate = GameObject.Find("ObjectRate").GetComponent<TextMeshProUGUI>();

            _centerInfoDisplayerObject = _centerInfoDisplayerText.gameObject;
            _centerInfoDisplayerHeaderObject = _centerInfoDisplayerHeader.gameObject;

            _secondaryInfoDisplayerObject = _secondaryInfoDisplayerText.gameObject;
            _secondaryInfoDisplayerHeaderObject = _secondaryInfoDisplayerHeader.gameObject;

            _subScreenInfoDisplayerObject = _subScreenInfoDisplayerText.gameObject;

            _judgeTimingGaugeScaleCount = _judgeTimingGaugeScaleParent.transform.childCount;
            _judgeTimingGaugeScales = new GameObject[_judgeTimingGaugeScaleCount];
            _judgeTimingGaugeScaleTransforms = new RectTransform[_judgeTimingGaugeScaleCount];
            _gaugeScaleBindings = new GaugeScaleBinding[_judgeTimingGaugeScaleCount];
            for (var i = 0; i < _judgeTimingGaugeScales.Length; i++)
            {
                _judgeTimingGaugeScales[i] = _judgeTimingGaugeScaleParent.transform.GetChild(i).gameObject;
                _judgeTimingGaugeScaleTransforms[i] = _judgeTimingGaugeScales[i].GetComponent<RectTransform>();
                var scaleImage = _judgeTimingGaugeScales[i].GetComponent<RawImage>();
                scaleImage.enabled = false;
                _gaugeScaleBindings[i] = new(_judgeTimingGaugeScaleTransforms[i], scaleImage);
            }

            _breakFastLateDisplayOption = MajEnv.Settings.Display.BreakFastLateType;
            _noteFastLateDisplayOption = MajEnv.Settings.Display.FastLateType;
            _centerInfoDisplayOption = MajEnv.Settings.Game.BGInfo;
            _secondaryInfoDisplayOption = MajEnv.Settings.Game.SecondaryBGInfo;
            _subScreenInfoDisplayOption = MajEnv.Settings.Game.SubScreenBGInfo;
            _isJudgeTimingGaugeEnabled = MajEnv.Settings.Game.EnableJudgeTimingGauge;

            if (_isJudgeTimingGaugeEnabled)
            {
                _secondaryInfoDisplayOption = BGInfoOption.None;
            }
            else
            {
                _judgeTimingGaugeParent.SetActive(false);
            }

            //clean up
            Clear();

            switch (MajEnv.Settings.Game.TopInfo)
            {
                case TopInfoDisplayOption.Judge:
                    _topInfoJudgeParent.SetActive(true);
                    _topInfoTimingParent.SetActive(false);
                    break;
                case TopInfoDisplayOption.Timing:
                    _topInfoJudgeParent.SetActive(false);
                    _topInfoTimingParent.SetActive(true);
                    break;
                case TopInfoDisplayOption.None:
                default:
                    _topInfoJudgeParent.SetActive(false);
                    _topInfoTimingParent.SetActive(false);
                    break;
            }

            SetInfoDisplayerActive(_centerInfoDisplayerObject, _centerInfoDisplayerHeaderObject, true);
            SetInfoDisplayerActive(_secondaryInfoDisplayerObject, _secondaryInfoDisplayerHeaderObject, true);
            SetInfoDisplayerActive(_subScreenInfoDisplayerObject, null, true);

            SetInfoDisplayerInitText(_centerInfoDisplayerText,
                _centerInfoDisplayerHeader,
                _mainScreenInfoDisplayerColorPalette,
                _centerInfoDisplayOption);
            SetInfoDisplayerInitText(_secondaryInfoDisplayerText,
                _secondaryInfoDisplayerHeader,
                _mainScreenInfoDisplayerColorPalette,
                _secondaryInfoDisplayOption);
            SetInfoDisplayerInitText(_subScreenInfoDisplayerText,
                null,
                _subScreenInfoDisplayerColorPalette,
                _subScreenInfoDisplayOption);
        }
        void OnDestroy()
        {
            Majdata<ObjectCounter>.Free();
            _sb.Dispose();
        }
        void Start()
        {
            _outline = Majdata<OutlineLoader>.Instance!;
            _xxlbController = Majdata<XxlbAnimationController>.Instance!;
            _gpManager = Majdata<GamePlayManager>.Instance!;

            if (MajEnv.Mode == RunningMode.View)
            {
                return;
            }

            if (_gameInfo.IsDanLifeEnabled)
            {
                SetInfoDisplayerActive(_centerInfoDisplayerObject, _centerInfoDisplayerHeaderObject, true);
                _centerInfoDisplayerHeader.text = "LIFE";
                _centerInfoDisplayerHeader.color = ComboColor;
                _centerInfoDisplayerText.text = _gameInfo.CurrentHP.ToString();
                _centerInfoDisplayerText.color = ComboColor;
            }
            else if (_gpManager.IsAutoplay)
            {
                _centerInfoDisplayerHeader.text = "AUTOPLAY";
            }
        }
        internal void OnPreUpdate()
        {
            using (UnityProfiler.Create("ObjectCounter.OnPreUpdate"))
            {
                if (_isJudgeTimingGaugeEnabled)
                {
                    var deltaTime = MajTimeline.DeltaTime;
                    for (var i = 0; i < _gaugeScaleBindings.Length; i++)
                    {
                        ref var binding = ref _gaugeScaleBindings[i];
                        binding.OnPreUpdate(deltaTime);
                    }
                }
            } 
        }

        internal void OnLateUpdate()
        {
            using (UnityProfiler.Create("ObjectCounter.OnLateUpdate"))
            {
                using (UnityProfiler.Create("ObjectCounter.UpdateAccRate"))
                {
                    UpdateAccRate();
                }
                using (UnityProfiler.Create("ObjectCounter.UpdateOutput"))
                {
                    UpdateOutput();
                }
                if (_xxlbDanceRequest.IsRequested)
                {
                    _xxlbController.Dance(_xxlbDanceRequest.Grade);
                    _xxlbDanceRequest = new();
                }
                if (_isOutlinePlayRequested)
                {
                    _outline.Play();
                    _isOutlinePlayRequested = false;
                }
                if(_isJudgeTimingGaugeEnabled)
                {
                    var diffSum = 0f;
                    for (var i = 0; i < _gaugeScaleBindings.Length; i++)
                    {
                        ref var binding = ref _gaugeScaleBindings[i];
                        diffSum += binding.DiffMSec;
                    }
                    var sampleCount = Mathf.Min(_judgeTimingGaugeScaleCount, _noteJudgeDiffList.Count);
                    var diffAvg = diffSum / sampleCount;
                    var percent = Mathf.Clamp(diffAvg / 150f, -1, 1);
                    var posX = 282.5f * percent;
                    _judgeTimingGaugeCursor.anchoredPosition = new Vector2(posX, -30);
                }
            }
        }
        internal void Clear()
        {
            Array.Fill(_judgedTapCount, 0);
            Array.Fill(_judgedHoldCount, 0);
            Array.Fill(_judgedTouchCount, 0);
            Array.Fill(_judgedTouchHoldCount, 0);
            Array.Fill(_judgedSlideCount, 0);
            Array.Fill(_judgedBreakCount, 0);
            Array.Fill(_totalJudgedCount, 0);
            _noteJudgeDiffList.Clear();
            for (var i = 0; i < 15; i++)
            {
                _dictJudgedTapCount[(JudgeGrade)i] = 0;
                _dictJudgedHoldCount[(JudgeGrade)i] = 0;
                _dictJudgedTouchCount[(JudgeGrade)i] = 0;
                _dictJudgedTouchHoldCount[(JudgeGrade)i] = 0;
                _dictJudgedSlideCount[(JudgeGrade)i] = 0;
                _dictJudgedBreakCount[(JudgeGrade)i] = 0;
                _dictTotalJudgedCount[(JudgeGrade)i] = 0;
            }
            Span<double> newAccRate = stackalloc double[5]
            {
                0.00,    // classic acc (+)
                100.00,  // classic acc (-)
                101.0000,// acc 101(-)
                100.0000,// acc 100(-)
                0.0000,  // acc (+)
            };
            newAccRate.CopyTo(_accRate);

            TapSum = 0;
            HoldSum = 0;
            SlideSum = 0;
            TouchSum = 0;
            BreakSum = 0;


            _noteJudgeStats = new();

            _lastJudgeDiff = 0; // Note judge diff
            _diffTimer = 3;

            _totalDXScore = 0;
            _lostDXScore = 0;

            _combo = 0; // Combo
            _pCombo = 0; // Perfect Combo
            _cPCombo = 0; // Critical Perfect
        }
        internal GameResult GetPlayRecord(ISongDetail song, ChartLevel level)
        {
            //var fast = totalJudgedCount.Where(x => x.Key > JudgeType.Perfect && x.Key != JudgeType.Miss)
            //                           .Select(x => x.Value)
            //                           .Sum();
            //var late = totalJudgedCount.Where(x => x.Key < JudgeType.Perfect && x.Key != JudgeType.Miss)
            //                           .Select(x => x.Value)
            //                           .Sum();
            for (var i = 0; i < 15; i++)
            {
                _dictJudgedTapCount[(JudgeGrade)i] = _judgedTapCount[i];
                _dictJudgedHoldCount[(JudgeGrade)i] = _judgedHoldCount[i];
                _dictJudgedTouchCount[(JudgeGrade)i] = _judgedTouchCount[i];
                _dictJudgedTouchHoldCount[(JudgeGrade)i] = _judgedTouchHoldCount[i];
                _dictJudgedSlideCount[(JudgeGrade)i] = _judgedSlideCount[i];
                _dictJudgedBreakCount[(JudgeGrade)i] = _judgedBreakCount[i];
                _dictTotalJudgedCount[(JudgeGrade)i] = _totalJudgedCount[i];
            }
            var holdRecord = _dictJudgedHoldCount.ToDictionary(
                kv => kv.Key,
                kv => _dictJudgedHoldCount[kv.Key] + _dictJudgedTouchHoldCount[kv.Key]
            );
            var record = new Dictionary<ScoreNoteType, JudgeInfo>()
            {
                { ScoreNoteType.Tap, new (_dictJudgedTapCount) },
                { ScoreNoteType.Hold, new (holdRecord)},
                { ScoreNoteType.Slide,new (_dictJudgedSlideCount)},
                { ScoreNoteType.Break, new (_dictJudgedBreakCount)},
                { ScoreNoteType.Touch, new (_dictJudgedTouchCount) }
            };
            var judgeRecord = new JudgeDetail(record);

            var unpackedinfo = JudgeDetail.UnpackJudgeRecord(judgeRecord.TotalJudgeInfo);
            var breakunpackedinfo = JudgeDetail.UnpackJudgeRecord(judgeRecord[ScoreNoteType.Break]);
            var cState = ComboState.None;
            if (unpackedinfo.IsAllPerfect && breakunpackedinfo.IsTheoretical)
            {
                cState = ComboState.APPlus;
            }
            else if (unpackedinfo.IsAllPerfect)
            {
                cState = ComboState.AP;
            }
            else if (unpackedinfo.IsFullComboPlus)
            {
                cState = ComboState.FCPlus;
            }
            else if (unpackedinfo.IsFullCombo)
            {
                cState = ComboState.FC;
            }

            return new GameResult()
            {
                Acc = new()
                {
                    DX = _accRate[4],
                    Classic = _accRate[0]
                },
                SongDetail = song,
                Level = level,
                JudgeRecord = judgeRecord,
                Fast = _noteJudgeStats.TotalFastCount,
                Late = _noteJudgeStats.TotalLateCount,
                DXScore = _totalDXScore + _lostDXScore,
                TotalDXScore = _totalDXScore,
                ComboState = cState,
                NoteJudgeDiffs = _noteJudgeDiffList.ToArray()
            };
        }
        internal UnpackJudgeInfo GetCurrentTotalJudgeInfo()
        {
            for (var i = 0; i < 15; i++)
            {
                _dictTotalJudgedCount[(JudgeGrade)i] = _totalJudgedCount[i];
            }

            return JudgeDetail.UnpackJudgeRecord(new JudgeInfo(_dictTotalJudgedCount));
        }

        private void UpdateOutput()
        {
            UpdateMainOutput();
            UpdateJudgeResult();
        }
        void UpdateAccRate()
        {
            // classic acc (+)
            // classic acc (-)
            // acc 101(-)
            // acc 100(-)
            // acc (+)
            //var currentNoteScore = GetNoteScoreSum();
            //var totalScore = (TapSum + TouchSum) * 500 + HoldSum * 1000 + SlideSum * 1500 + BreakSum * 2500;
            //var totalExtraScore = Math.Max(BreakSum * 100, 1);
            Span<decimal> newAccRate = stackalloc decimal[5];

            newAccRate[0] = CurrentNoteScoreClassic / (decimal)TotalNoteBaseScore;
            newAccRate[1] = (TotalNoteBaseScore - LostNoteBaseScore + CurrentNoteExtraScoreClassic) / (decimal)TotalNoteBaseScore;
            newAccRate[2] = ((TotalNoteBaseScore - LostNoteBaseScore) / (decimal)TotalNoteBaseScore) + ((TotalNoteExtraScore - LostNoteExtraScore) / ((decimal)(TotalNoteExtraScore is 0 ? 1 : TotalNoteExtraScore) * 100));
            newAccRate[3] = ((TotalNoteBaseScore - LostNoteBaseScore) / (decimal)TotalNoteBaseScore) + ((CurrentNoteExtraScore) / ((decimal)(TotalNoteExtraScore is 0 ? 1 : TotalNoteExtraScore) * 100));
            newAccRate[4] = ((CurrentNoteBaseScore) / (decimal)TotalNoteBaseScore) + ((CurrentNoteExtraScore) / ((decimal)(TotalNoteExtraScore is 0 ? 1 : TotalNoteExtraScore) * 100));

            _accRate[0] = decimal.ToDouble(newAccRate[0] * 100);
            _accRate[1] = decimal.ToDouble(newAccRate[1] * 100);
            _accRate[2] = decimal.ToDouble(newAccRate[2] * 100);
            _accRate[3] = decimal.ToDouble(newAccRate[3] * 100);
            _accRate[4] = decimal.ToDouble(newAccRate[4] * 100);
        }
        internal async ValueTask CountNoteSumAsync(SimaiChart chart)
        {
            await Task.Run(() =>
            {
                foreach (var timing in chart.NoteTimings)
                {
                    foreach (var note in timing.Notes)
                    {
                        if (!note.IsBreak)
                        {
                            switch (note.Type)
                            {
                                case SimaiNoteType.Tap:
                                    TapSum++;
                                    break;
                                case SimaiNoteType.Hold:
                                case SimaiNoteType.TouchHold:
                                    HoldSum++;
                                    break;
                                case SimaiNoteType.Slide:
                                    if (!note.IsSlideNoHead)
                                        TapSum++;
                                    if (note.IsSlideBreak)
                                        BreakSum++;
                                    else
                                        SlideSum++;
                                    break;
                                case SimaiNoteType.Touch:
                                    TouchSum++;
                                    break;
                            }
                        }
                        else
                        {
                            if (note.Type == SimaiNoteType.Slide)
                            {
                                if (!note.IsSlideNoHead)
                                    BreakSum++;
                                if (note.IsSlideBreak)
                                    BreakSum++;
                                else
                                    SlideSum++;
                            }
                            else
                            {
                                BreakSum++;
                            }
                        }
                    }
                }
                NoteSum = TapSum + HoldSum + TouchSum + BreakSum + SlideSum;
                TotalNoteBaseScore = (TapSum + TouchSum) * 500 + HoldSum * 1000 + SlideSum * 1500 + BreakSum * 2500;
                TotalNoteExtraScore = BreakSum * 100;
                _totalDXScore = NoteSum * 3;
                _noteJudgeDiffList = new(NoteSum);
            });
            await UniTask.SwitchToMainThread();
            UpdateAccRate();
            UpdateOutput();
        }
        internal void ReportResult<T>(T note, in NoteJudgeResult judgeResult, int multiple = 1) where T : NoteDrop
        {
            var grade = judgeResult.Grade;
            var isSlide = note is SlideBase;

            if (!isSlide && !judgeResult.IsMissOrTooFast)
            {
                _lastJudgeDiff = judgeResult.Diff;
                _diffTimer = 3;
            }

            if (!judgeResult.IsMissOrTooFast)
            {
                _combo += multiple;
                switch (note)
                {
                    case TapDrop:
                    case HoldDrop:
                        _isOutlinePlayRequested = true;
                        var diffMSec = judgeResult.Diff;
                        _noteJudgeDiffList.Add(diffMSec);
                        if(_isJudgeTimingGaugeEnabled)
                        {
                            ref var binding = ref _gaugeScaleBindings[_gaugeScaleBindingCursor++ % _judgeTimingGaugeScaleCount];
                            binding.DiffMSec = diffMSec;
                        }
                        break;
                }
            }

            if (MajEnv.Mode == RunningMode.Play && _gameInfo.IsDanLifeEnabled)
            {
                _gameInfo.OnNoteJudged(judgeResult.Grade, multiple);
                if (_gameInfo.CurrentHP == 0 && _gameInfo.IsForceGameover)
                {
                    _gpManager.GameOver();
                }
            }

            _xxlbDanceRequest = new()
            {
                IsRequested = true,
                Grade = grade,
            };

            UpdateComboCount(grade, multiple);
            UpdateJudgeCount(note, judgeResult, multiple);
            UpdateNoteScoreCount(note, judgeResult, multiple);
            UpdateFastLateCount(judgeResult, multiple);
        }

        /// <summary>
        /// 更新BgInfo
        /// </summary>
        void UpdateMainOutput()
        {
            UpdateCenterInfoOutput();
            UpdateSecondaryInfoOutput();
            UpdateSubScreenInfoOutput();
        }
        void UpdateCenterInfoOutput()
        {
            if (MajEnv.Mode != RunningMode.View && _gameInfo.IsDanLifeEnabled)
            {
                _sb.Clear();
                _sb.Append(_gameInfo.CurrentHP);
                var a = _sb.AsArraySegment();
                _centerInfoDisplayerText.SetCharArray(a.Array, a.Offset, a.Count);
                _centerInfoDisplayerText.color = ComboColor;
                SetInfoDisplayerActive(_centerInfoDisplayerObject, _centerInfoDisplayerHeaderObject, true);
                return;
            }
            UpdateInfoDisplayerOutput(_centerInfoDisplayerText,
                _centerInfoDisplayerObject,
                _centerInfoDisplayerHeader,
                _centerInfoDisplayerHeaderObject,
                _mainScreenInfoDisplayerColorPalette,
                _centerInfoDisplayOption);
        }
        void UpdateSecondaryInfoOutput()
        {
            UpdateInfoDisplayerOutput(_secondaryInfoDisplayerText,
                _secondaryInfoDisplayerObject,
                _secondaryInfoDisplayerHeader,
                _secondaryInfoDisplayerHeaderObject,
                _mainScreenInfoDisplayerColorPalette,
                _secondaryInfoDisplayOption);
        }
        void UpdateSubScreenInfoOutput()
        {
            UpdateInfoDisplayerOutput(_subScreenInfoDisplayerText,
                _subScreenInfoDisplayerObject,
                null,
                null,
                _subScreenInfoDisplayerColorPalette,
                _subScreenInfoDisplayOption);
        }
        void UpdateInfoDisplayerOutput(TextMeshProUGUI text,
            GameObject textObject,
            TextMeshProUGUI? header,
            GameObject? headerObject,
            in ReadOnlySpan<Color> colorPalette,
            in BGInfoOption option)
        {
            switch (option)
            {
                case BGInfoOption.CPCombo:
                    UpdateCombo(text, textObject, headerObject, colorPalette, _cPCombo);
                    break;
                case BGInfoOption.PCombo:
                    UpdateCombo(text, textObject, headerObject, colorPalette, _pCombo);
                    break;
                case BGInfoOption.Combo:
                    UpdateCombo(text, textObject, headerObject, colorPalette, _combo);
                    break;
                case BGInfoOption.Achievement_101:
                    {
                        var accRate = Math.Floor(_accRate[2] * 10000) / 10000;

                        _sb.Clear();
                        DX_ACC_RATE_FORMAT.FormatTo(ref _sb, accRate);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);

                        UpdateAchievementColor(_accRate[2], text, colorPalette);
                    }
                    break;
                case BGInfoOption.Achievement_100:
                    {
                        var accRate = Math.Floor(_accRate[3] * 10000) / 10000;

                        _sb.Clear();
                        DX_ACC_RATE_FORMAT.FormatTo(ref _sb, accRate);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);

                        UpdateAchievementColor(_accRate[3], text, colorPalette);
                    }
                    break;
                case BGInfoOption.Achievement:
                    {
                        var accRate = Math.Floor(_accRate[4] * 10000) / 10000;

                        _sb.Clear();
                        DX_ACC_RATE_FORMAT.FormatTo(ref _sb, accRate);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);

                        UpdateAchievementColor(_accRate[4], text, colorPalette);
                    }
                    break;
                case BGInfoOption.AchievementClassical:
                    {
                        var accRate = Math.Floor(_accRate[0] * 100) / 100;

                        _sb.Clear();
                        CLASSIC_ACC_RATE_FORMAT.FormatTo(ref _sb, accRate);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);

                        UpdateAchievementColor(_accRate[0], text, colorPalette);
                    }
                    break;
                case BGInfoOption.AchievementClassical_100:
                    {
                        var accRate = Math.Floor(_accRate[1] * 100) / 100;

                        _sb.Clear();
                        CLASSIC_ACC_RATE_FORMAT.FormatTo(ref _sb, accRate);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);

                        UpdateAchievementColor(_accRate[1], text, colorPalette);
                    }
                    break;
                case BGInfoOption.DXScore:
                    {
                        _sb.Clear();
                        COMBO_OR_DXSCORE_FORMAT.FormatTo(ref _sb, _lostDXScore);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);
                    }
                    break;
                case BGInfoOption.DXScoreRank:
                    UpdateDXScoreRank(text, textObject, header, headerObject, colorPalette, option);
                    break;
                case BGInfoOption.S_Border:
                case BGInfoOption.SS_Border:
                case BGInfoOption.SSS_Border:
                case BGInfoOption.MyBest:
                    UpdateRankBoard(text, textObject, headerObject, option);
                    break;
                case BGInfoOption.Diff:
                    {
                        _sb.Clear();
                        DIFF_FORMAT.FormatTo(ref _sb, _lastJudgeDiff);
                        var a = _sb.AsArraySegment();
                        text.SetCharArray(a.Array, a.Offset, a.Count);

                        var oldColor = colorPalette[COLOR_PALETTE_DIFF_EARLY_COLOR_INDEX];
                        if (_lastJudgeDiff < 0)
                        {
                            oldColor = colorPalette[COLOR_PALETTE_DIFF_EARLY_COLOR_INDEX];
                            if (header is not null)
                            {
                                header.text = FAST_STRING;
                            }
                        }
                        else
                        {
                            oldColor = colorPalette[COLOR_PALETTE_DIFF_LATE_COLOR_INDEX];
                            if (header is not null)
                            {
                                header.text = LATE_STRING;
                            }
                        }
                        var newColor = new Color()
                        {
                            r = oldColor.r,
                            g = oldColor.g,
                            b = oldColor.b,
                            a = _diffTimer.Clamp(0, 1)
                        };
                        if (header is not null)
                        {
                            header.color = newColor;
                        }
                        text.color = newColor;
                        _diffTimer -= MajTimeline.DeltaTime * 3;
                    }
                    break;
                default:
                    return;
            }
        }
        /// <summary>
        /// 更新Combo
        /// </summary>
        /// <param name="combo"></param>
        void UpdateCombo(TextMeshProUGUI text,
            GameObject textObject,
            GameObject? headerObject,
            in ReadOnlySpan<Color> colorPalette,
            in long combo)
        {
            if (combo == 0)
            {
                SetInfoDisplayerActive(textObject, headerObject, false);
            }
            else
            {
                SetInfoDisplayerActive(textObject, headerObject, true);
                _sb.Clear();
                COMBO_OR_DXSCORE_FORMAT.FormatTo(ref _sb, combo);
                var a = _sb.AsArraySegment();
                text.SetCharArray(a.Array, a.Offset, a.Count);
            }
        }
        void UpdateRankBoard(TextMeshProUGUI text,
            GameObject textObject,
            GameObject? headerObject,
            in BGInfoOption bgInfo)
        {
            double rate;
            switch (bgInfo)
            {
                case BGInfoOption.S_Border:
                    rate = _accRate[2] - 97;
                    break;
                case BGInfoOption.SS_Border:
                    rate = _accRate[2] - 99;
                    break;
                case BGInfoOption.SSS_Border:
                    rate = _accRate[2] - 100;
                    break;
                case BGInfoOption.MyBest:
                    rate = _accRate[2] - (_gpManager.HistoryScore?.Acc.DX ?? 0);
                    break;
                default:
                    return;
            }
            if (rate >= 0)
            {
                _sb.Clear();
                rate = Math.Floor(rate * 10000) / 10000;
                DX_ACC_RATE_FORMAT.FormatTo(ref _sb, rate);
                var a = _sb.AsArraySegment();
                text.SetCharArray(a.Array, a.Offset, a.Count);
            }
            else
            {
                SetInfoDisplayerActive(textObject, headerObject, false);
            }
        }
        void UpdateDXScoreRank(TextMeshProUGUI text,
            GameObject textObject,
            TextMeshProUGUI? header,
            GameObject? headerObject,
            in ReadOnlySpan<Color> colorPalette,
            in BGInfoOption bgInfo)
        {
            var remainingDXScore = _totalDXScore + _lostDXScore;
            var dxRank = new DXScoreRank(remainingDXScore, _totalDXScore);
            var num = remainingDXScore - (dxRank.Lower + 1);
            if (dxRank.Rank == 0)
            {
                SetInfoDisplayerActive(textObject, headerObject, false);
                return;
            }
            else
            {
                SetInfoDisplayerActive(textObject, headerObject, true);
            }
            _sb.Clear();
            DXSCORE_RANK_HEADER_FORMAT.FormatTo(ref _sb, dxRank.Rank);
            var a = _sb.AsArraySegment();
            header?.SetCharArray(a.Array, a.Offset, a.Count);

            _sb.Clear();
            DXSCORE_RANK_BODY_FORMAT.FormatTo(ref _sb, num);
            var b = _sb.AsArraySegment();
            text.SetCharArray(b.Array, b.Offset, b.Count);

            switch (dxRank.Rank)
            {
                case 5:
                case 4:
                case 3:
                    if (header is not null)
                    {
                        header.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    break;
                case 2:
                case 1:
                    if (header is not null)
                    {
                        header.color = colorPalette[COLOR_PALETTE_DX_SCORE_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_DX_SCORE_COLOR_INDEX];
                    break;
            }
        }
        void SetInfoDisplayerActive(GameObject displayerObject, GameObject? displayerHeaderObject, bool state)
        {
            if (state)
            {
                displayerObject.layer = MajEnv.DEFAULT_LAYER;
                if (displayerHeaderObject is not null)
                {
                    displayerHeaderObject.layer = MajEnv.DEFAULT_LAYER;
                }
            }
            else
            {
                displayerObject.layer = MajEnv.HIDDEN_LAYER;
                if (displayerHeaderObject is not null)
                {
                    displayerHeaderObject.layer = MajEnv.HIDDEN_LAYER;
                }
            }
        }
        void SetInfoDisplayerInitText(TextMeshProUGUI text,
            TextMeshProUGUI? header,
            in ReadOnlySpan<Color> colorPalette,
            in BGInfoOption option)
        {
            switch (option)
            {
                case BGInfoOption.CPCombo:
                    if (header is not null)
                    {
                        header.color = colorPalette[COLOR_PALETTE_CRITICAL_COMBO_COLOR_INDEX];
                        header.text = "CPCombo";
                    }
                    text.color = colorPalette[COLOR_PALETTE_CRITICAL_COMBO_COLOR_INDEX];
                    break;
                case BGInfoOption.PCombo:
                    if (header is not null)
                    {
                        header.color = colorPalette[COLOR_PALETTE_PERFECT_COMBO_COLOR_INDEX];
                        header.text = "PCombo";
                    }
                    text.color = colorPalette[COLOR_PALETTE_PERFECT_COMBO_COLOR_INDEX];
                    break;
                case BGInfoOption.Combo:
                    if (header is not null)
                    {
                        header.color = colorPalette[COLOR_PALETTE_COMBO_COLOR_INDEX];
                        header.text = "Combo";
                    }
                    text.color = colorPalette[COLOR_PALETTE_COMBO_COLOR_INDEX];
                    break;
                case BGInfoOption.Achievement_101:
                case BGInfoOption.Achievement_100:
                case BGInfoOption.Achievement:
                case BGInfoOption.AchievementClassical:
                case BGInfoOption.AchievementClassical_100:
                    if (header is not null)
                    {
                        header.text = "Achievement";
                        header.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    }
                    break;
                case BGInfoOption.DXScore:
                case BGInfoOption.DXScoreRank:
                    if (header is not null)
                    {
                        header.text = "でらっくす SCORE";
                        header.color = colorPalette[COLOR_PALETTE_DX_SCORE_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_DX_SCORE_COLOR_INDEX];
                    break;
                case BGInfoOption.S_Border:
                    if (header is not null)
                    {
                        header.text = "S  BORDER";
                        header.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_SILVER_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_SILVER_COLOR_INDEX];
                    text.text = "4.0000%";
                    break;
                case BGInfoOption.SS_Border:
                    if (header is not null)
                    {
                        header.text = "SS  BORDER";
                        header.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    text.text = "2.0000%";
                    break;
                case BGInfoOption.SSS_Border:
                    if (header is not null)
                    {
                        header.text = "SSS  BORDER";
                        header.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    text.text = "1.0000%";
                    break;
                case BGInfoOption.MyBest:
                    if (header is not null)
                    {
                        header.text = "MyBestScore BORDER";
                        header.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    }
                    text.color = colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX];
                    text.text = "101.0000%";
                    break;
                case BGInfoOption.Diff:
                    if (header is not null)
                    {
                        header.color = colorPalette[COLOR_PALETTE_COMBO_COLOR_INDEX];
                        header.text = "";
                    }
                    text.color = colorPalette[COLOR_PALETTE_COMBO_COLOR_INDEX];
                    text.text = "";
                    break;
                case BGInfoOption.None:
                    SetInfoDisplayerActive(text.gameObject, header?.gameObject, false);
                    break;
                default:
                    return;
            }
        }
        /// <summary>
        /// 更新SubDisplay的JudgeResult
        /// </summary>
        void UpdateJudgeResult()
        {
            //_judgeResultCount.text = ZString.Format(JUDGE_RESULT_STRING, 
            //                                        _cPerfectCount, 
            //                                        _perfectCount, 
            //                                        _greatCount, 
            //                                        _goodCount, 
            //                                        _missCount, 
            //                                        _fastCount, 
            //                                        _lateCount);
            ref var noteJudgeStats = ref _noteJudgeStats;
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalCriticalCount);
            _criticalCountDisplayer.SetText(_sb);
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalPerfectCount);
            _perfectCountDisplayer.SetText(_sb);
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalGreatCount);
            _greatCountDisplayer.SetText(_sb);
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalGoodCount);
            _goodCountDisplayer.SetText(_sb);
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalMissCount);
            _missCountDisplayer.SetText(_sb);
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalFastCount);
            _fastCountDisplayer.SetText(_sb);
            _sb.Clear();

            JUDGMENT_COUNT_FORMAT.FormatTo(ref _sb, noteJudgeStats.TotalLateCount);
            _lateCountDisplayer.SetText(_sb);
            _sb.Clear();

            switch (MajEnv.Settings.Game.TopInfo)
            {
                case TopInfoDisplayOption.Judge:
                    {
                        var p = noteJudgeStats.TotalCriticalCount + noteJudgeStats.TotalPerfectCount;
                        _sb.Clear();
                        if (p != 0)
                        {
                            _sb.Append(p);
                        }
                        var a = _sb.AsArraySegment();
                        _topInfoPerfect.SetCharArray(a.Array, a.Offset, a.Count);

                        _sb.Clear();
                        if (noteJudgeStats.TotalGreatCount != 0)
                        {
                            _sb.Append(noteJudgeStats.TotalGreatCount);
                        }
                        var b = _sb.AsArraySegment();
                        _topInfoGreat.SetCharArray(b.Array, b.Offset, b.Count);

                        _sb.Clear();
                        if (noteJudgeStats.TotalGoodCount != 0)
                        {
                            _sb.Append(noteJudgeStats.TotalGoodCount);
                        }
                        var c = _sb.AsArraySegment();
                        _topInfoGood.SetCharArray(c.Array, c.Offset, c.Count);

                        _sb.Clear();
                        if (noteJudgeStats.TotalMissCount != 0)
                        {
                            _sb.Append(noteJudgeStats.TotalMissCount);
                        }
                        var d = _sb.AsArraySegment();
                        _topInfoMiss.SetCharArray(d.Array, d.Offset, d.Count);
                    }
                    break;
                case TopInfoDisplayOption.Timing:
                    {
                        _sb.Clear();
                        if (noteJudgeStats.TotalFastCount != 0)
                        {
                            _sb.Append(noteJudgeStats.TotalFastCount);
                        }
                        var a = _sb.AsArraySegment();
                        _topInfoFast.SetCharArray(a.Array, a.Offset, a.Count);

                        _sb.Clear();
                        if (noteJudgeStats.TotalLateCount != 0)
                        {
                            _sb.Append(noteJudgeStats.TotalLateCount);
                        }
                        var b = _sb.AsArraySegment();
                        _topInfoLate.SetCharArray(b.Array, b.Offset, b.Count);
                    }
                    break;
                case TopInfoDisplayOption.None:
                default:
                    break;
            }
        }
        /// <summary>
        /// 根据Achievement更新BgInfoHeader的颜色
        /// </summary>
        /// <param name="achievementRate"></param>
        /// <param name="textElement"></param>
        void UpdateAchievementColor(double achievementRate, TextMeshProUGUI textElement, in ReadOnlySpan<Color> colorPalette)
        {
            var newColor = achievementRate switch
            {
                >= 100 => colorPalette[COLOR_PALETTE_ACHIEVEMENT_GOLD_COLOR_INDEX],
                >= 97f => colorPalette[COLOR_PALETTE_ACHIEVEMENT_SILVER_COLOR_INDEX],
                >= 80f => colorPalette[COLOR_PALETTE_ACHIEVEMENT_BRONZE_COLOR_INDEX],
                _ => colorPalette[COLOR_PALETTE_ACHIEVEMENT_DUB_COLOR_INDEX]
            };

            if (textElement.color != newColor)
            {
                textElement.color = newColor;
            }
        }

        #region Counter update
        void UpdateJudgeCount<T>(T note, in NoteJudgeResult judgeResult, int multiple = 1) where T : NoteDrop
        {
            const int FIELD_CRITICAL_OFFSET = 0;
            const int FIELD_PERFECT_OFFSET = 1;
            const int FIELD_GREAT_OFFSET = 2;
            const int FIELD_GOOD_OFFSET = 3;
            const int FIELD_MISS_OFFSET = 4;
            var isBreak = judgeResult.IsBreak;
            var grade = judgeResult.Grade;
            ref var noteJudgeStats = ref _noteJudgeStats;
            ref var fieldHeadRef = ref Unsafe.NullRef<long>();
            ref var totalFieldHeadRef = ref noteJudgeStats.TotalCriticalCount;
            if (isBreak)
            {
                _judgedBreakCount[(int)grade] += multiple;
                noteJudgeStats.BreakJudgedCount += multiple;
                fieldHeadRef = ref noteJudgeStats.BreakCriticalCount;
            }
            else
            {
                switch (note)
                {
                    case TapDrop:
                        _judgedTapCount[(int)grade] += multiple;
                        noteJudgeStats.TapJudgedCount += multiple;
                        fieldHeadRef = ref noteJudgeStats.TapCriticalCount;
                        break;
                    case HoldDrop:
                        _judgedHoldCount[(int)grade] += multiple;
                        noteJudgeStats.HoldJudgedCount += multiple;
                        fieldHeadRef = ref noteJudgeStats.HoldCriticalCount;
                        break;
                    case SlideBase:
                        _judgedSlideCount[(int)grade] += multiple;
                        noteJudgeStats.SlideJudgedCount += multiple;
                        fieldHeadRef = ref noteJudgeStats.SlideCriticalCount;
                        break;
                    case TouchDrop:
                        _judgedTouchCount[(int)grade] += multiple;
                        noteJudgeStats.TouchJudgedCount += multiple;
                        fieldHeadRef = ref noteJudgeStats.TouchCriticalCount;
                        break;
                    case TouchHoldDrop:
                        _judgedTouchHoldCount[(int)grade] += multiple;
                        noteJudgeStats.HoldJudgedCount += multiple;
                        fieldHeadRef = ref noteJudgeStats.HoldCriticalCount;
                        break;
                }
                
            }
            if (!Unsafe.IsNullRef(ref fieldHeadRef))
            {
                ref var fieldRef = ref fieldHeadRef;
                ref var totalFieldRef = ref totalFieldHeadRef;
                switch (grade)
                {
                    case JudgeGrade.Miss:
                    case JudgeGrade.TooFast:
                        fieldRef = ref Unsafe.Add(ref fieldRef, FIELD_MISS_OFFSET);
                        totalFieldRef = ref Unsafe.Add(ref totalFieldHeadRef, FIELD_MISS_OFFSET);
                        break;
                    case JudgeGrade.LateGood:
                    case JudgeGrade.FastGood:
                        fieldRef = ref Unsafe.Add(ref fieldRef, FIELD_GOOD_OFFSET);
                        totalFieldRef = ref Unsafe.Add(ref totalFieldHeadRef, FIELD_GOOD_OFFSET);
                        break;
                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.LateGreat2nd:
                    case JudgeGrade.LateGreat:
                    case JudgeGrade.FastGreat:
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.FastGreat3rd:
                        fieldRef = ref Unsafe.Add(ref fieldRef, FIELD_GREAT_OFFSET);
                        totalFieldRef = ref Unsafe.Add(ref totalFieldHeadRef, FIELD_GREAT_OFFSET);
                        break;
                    case JudgeGrade.LatePerfect3rd:
                    case JudgeGrade.LatePerfect2nd:
                    case JudgeGrade.FastPerfect2nd:
                    case JudgeGrade.FastPerfect3rd:
                        fieldRef = ref Unsafe.Add(ref fieldRef, FIELD_PERFECT_OFFSET);
                        totalFieldRef = ref Unsafe.Add(ref totalFieldHeadRef, FIELD_PERFECT_OFFSET);
                        break;
                    default:
                        fieldRef = ref Unsafe.Add(ref fieldRef, FIELD_CRITICAL_OFFSET);
                        totalFieldRef = ref Unsafe.Add(ref totalFieldHeadRef, FIELD_CRITICAL_OFFSET);
                        break;
                }
                fieldRef += multiple;
                totalFieldRef += multiple;
            }
            _totalJudgedCount[(int)grade] += multiple;
            noteJudgeStats.TotalJudgedCount += multiple;
        }
        void UpdateComboCount(JudgeGrade grade, int multiple = 1)
        {
            switch (grade)
            {
                case JudgeGrade.Perfect:
                    _cPCombo += multiple;
                    _pCombo += multiple;
                    break;
                case JudgeGrade.LatePerfect2nd:
                case JudgeGrade.FastPerfect2nd:
                case JudgeGrade.LatePerfect3rd:
                case JudgeGrade.FastPerfect3rd:
                    _cPCombo = 0;
                    _pCombo += multiple;
                    _lostDXScore -= 1 * multiple;
                    break;
                case JudgeGrade.LateGreat3rd:
                case JudgeGrade.LateGreat2nd:
                case JudgeGrade.LateGreat:
                case JudgeGrade.FastGreat:
                case JudgeGrade.FastGreat2nd:
                case JudgeGrade.FastGreat3rd:
                    _cPCombo = 0;
                    _pCombo = 0;
                    _lostDXScore -= 2 * multiple;
                    break;
                case JudgeGrade.LateGood:
                case JudgeGrade.FastGood:
                    _cPCombo = 0;
                    _pCombo = 0;
                    _lostDXScore -= 3 * multiple;
                    break;
                case JudgeGrade.TooFast:
                case JudgeGrade.Miss:
                    _combo = 0;
                    _cPCombo = 0;
                    _pCombo = 0;
                    _lostDXScore -= 3 * multiple;
                    break;
            }
        }
        void UpdateNoteScoreCount<T>(T note, in NoteJudgeResult judgeResult, int multiple = 1) where T : NoteDrop
        {
            var baseScore = 500;

            switch (note)
            {
                case TapDrop:
                case TouchDrop:
                    baseScore = 500 * multiple;
                    break;
                case HoldDrop:
                case TouchHoldDrop:
                    baseScore = 1000 * multiple;
                    break;
                case SlideBase:
                    baseScore = 1500 * multiple;
                    break;
            }
            if (!judgeResult.IsBreak)
            {
                switch (judgeResult.Grade)
                {
                    case JudgeGrade.Miss:
                    case JudgeGrade.TooFast:
                        //CurrentNoteBaseScore += baseScore * 0;
                        LostNoteBaseScore += baseScore;
                        break;
                    case JudgeGrade.LateGood:
                    case JudgeGrade.FastGood:
                        CurrentNoteBaseScore += (long)(baseScore * 0.5);
                        LostNoteBaseScore += (long)(baseScore * 0.5);
                        break;
                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.LateGreat2nd:
                    case JudgeGrade.LateGreat:
                    case JudgeGrade.FastGreat:
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.FastGreat3rd:
                        CurrentNoteBaseScore += (long)(baseScore * 0.8);
                        LostNoteBaseScore += (long)(baseScore * 0.2);
                        break;
                    default:
                        CurrentNoteBaseScore += baseScore;
                        //LostNoteBaseScore += 0;
                        break;
                }
            }
            else
            {

                switch (judgeResult.Grade)
                {
                    case JudgeGrade.Miss:
                    case JudgeGrade.TooFast:
                        LostNoteBaseScore += 2500 * multiple;
                        LostNoteExtraScore += 100 * multiple;
                        LostNoteExtraScoreClassic += 100 * multiple;
                        break;
                    case JudgeGrade.LateGood:
                    case JudgeGrade.FastGood:
                        CurrentNoteBaseScore += 1000 * multiple;
                        CurrentNoteExtraScore += 30 * multiple;
                        LostNoteBaseScore += 1500 * multiple;
                        LostNoteExtraScore += 70 * multiple;
                        LostNoteExtraScoreClassic += 100 * multiple;
                        break;
                    case JudgeGrade.LateGreat3rd:
                    case JudgeGrade.FastGreat3rd:
                        CurrentNoteBaseScore += 1250 * multiple;
                        CurrentNoteExtraScore += 40 * multiple;
                        LostNoteBaseScore += 1250 * multiple;
                        LostNoteExtraScore += 60 * multiple;
                        LostNoteExtraScoreClassic += 100 * multiple;
                        break;
                    case JudgeGrade.FastGreat2nd:
                    case JudgeGrade.LateGreat2nd:
                        CurrentNoteBaseScore += 1500 * multiple;
                        CurrentNoteExtraScore += 40 * multiple;
                        LostNoteBaseScore += 1000 * multiple;
                        LostNoteExtraScore += 60 * multiple;
                        LostNoteExtraScoreClassic += 100 * multiple;
                        break;
                    case JudgeGrade.LateGreat:
                    case JudgeGrade.FastGreat:
                        CurrentNoteBaseScore += 2000 * multiple;
                        CurrentNoteExtraScore += 40 * multiple;
                        LostNoteBaseScore += 500 * multiple;
                        LostNoteExtraScore += 60 * multiple;
                        LostNoteExtraScoreClassic += 100 * multiple;
                        break;
                    case JudgeGrade.LatePerfect3rd:
                    case JudgeGrade.FastPerfect3rd:
                        CurrentNoteBaseScore += 2500 * multiple;
                        CurrentNoteExtraScore += 50 * multiple;
                        LostNoteExtraScore += 50 * multiple;
                        LostNoteExtraScoreClassic += 100 * multiple;
                        break;
                    case JudgeGrade.LatePerfect2nd:
                    case JudgeGrade.FastPerfect2nd:
                        CurrentNoteBaseScore += 2500 * multiple;
                        CurrentNoteExtraScore += 75 * multiple;
                        CurrentNoteExtraScoreClassic += 50 * multiple;
                        LostNoteExtraScore += 25 * multiple;
                        LostNoteExtraScoreClassic += 50 * multiple;
                        break;
                    case JudgeGrade.Perfect:
                        CurrentNoteBaseScore += 2500 * multiple;
                        CurrentNoteExtraScore += 100 * multiple;
                        CurrentNoteExtraScoreClassic += 100 * multiple;
                        LostNoteExtraScore += 0 * multiple;
                        LostNoteExtraScoreClassic += 0 * multiple;
                        break;
                }
            }
        }
        /// <summary>
        /// Update Fast/Late count
        /// </summary>
        /// <param name="judgeResult"></param>
        void UpdateFastLateCount(in NoteJudgeResult judgeResult, int multiple = 1)
        {
            var gameSetting = judgeResult.IsBreak ? _breakFastLateDisplayOption : _noteFastLateDisplayOption;
            var resultValue = (int)judgeResult.Grade;
            var absValue = Math.Abs(7 - resultValue);
            ref var noteJudgeStats = ref _noteJudgeStats;
            switch (gameSetting)
            {
                case JudgeDisplayOption.All:
                    {
                        if (judgeResult.Diff == 0 || judgeResult.IsMissOrTooFast)
                        {
                            break;
                        }
                        else if (judgeResult.IsFast)
                        {
                            noteJudgeStats.TotalFastCount += multiple;
                        }
                        else
                        {
                            noteJudgeStats.TotalLateCount += multiple;
                        }
                    }
                    break;
                case JudgeDisplayOption.BelowCP:
                    {
                        if (judgeResult.IsMissOrTooFast || judgeResult.Grade == JudgeGrade.Perfect)
                        {
                            break;
                        }
                        else if (judgeResult.IsFast)
                        {
                            noteJudgeStats.TotalFastCount += multiple;
                        }
                        else
                        {
                            noteJudgeStats.TotalLateCount += multiple;
                        }
                    }
                    break;
                //默认只统计Great、Good的Fast/Late
                case JudgeDisplayOption.BelowP:
                case JudgeDisplayOption.BelowGR:
                case JudgeDisplayOption.Disable:
                    {
                        if (judgeResult.IsMissOrTooFast || absValue <= 2)
                        {
                            break;
                        }
                        else if (judgeResult.IsFast)
                        {
                            noteJudgeStats.TotalFastCount += multiple;
                        }
                        else
                        {
                            noteJudgeStats.TotalLateCount += multiple;
                        }
                    }
                    break;
            }
        }
        #endregion
        readonly struct XxlbDanceRequest
        {
            public bool IsRequested { get; init; }
            public JudgeGrade Grade { get; init; }
        }
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct AccurateStatistics
        {
            /// <summary>
            /// classic acc (+)
            /// </summary>
            public readonly double ClassicAchievement_A;
            /// <summary>
            /// classic acc (-)
            /// </summary>
            public readonly double ClassicAchievement_B;
            /// <summary>
            /// acc 101(-)
            /// </summary>
            public readonly double Achievement_A;
            /// <summary>
            /// acc 100(-)
            /// </summary>
            public readonly double Achievement_B;
            /// <summary>
            /// acc (+)
            /// </summary>
            public readonly double Achievement_C;
        }
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct NoteJudgeStatistics
        {
            public readonly long TapCriticalCount;
            public readonly long TapPerfectCount;
            public readonly long TapGreatCount;
            public readonly long TapGoodCount;
            public readonly long TapMissCount;
            public readonly long TapJudgedCount;

            public readonly long HoldCriticalCount;
            public readonly long HoldPerfectCount;
            public readonly long HoldGreatCount;
            public readonly long HoldGoodCount;
            public readonly long HoldMissCount;
            public readonly long HoldJudgedCount;

            public readonly long SlideCriticalCount;
            public readonly long SlidePerfectCount;
            public readonly long SlideGreatCount;
            public readonly long SlideGoodCount;
            public readonly long SlideMissCount;
            public readonly long SlideJudgedCount;

            public readonly long TouchCriticalCount;
            public readonly long TouchPerfectCount;
            public readonly long TouchGreatCount;
            public readonly long TouchGoodCount;
            public readonly long TouchMissCount;
            public readonly long TouchJudgedCount;

            public readonly long BreakCriticalCount;
            public readonly long BreakPerfectCount;
            public readonly long BreakGreatCount;
            public readonly long BreakGoodCount;
            public readonly long BreakMissCount;
            public readonly long BreakJudgedCount;

            public readonly long TotalCriticalCount;
            public readonly long TotalPerfectCount;
            public readonly long TotalGreatCount;
            public readonly long TotalGoodCount;
            public readonly long TotalMissCount;
            public readonly long TotalJudgedCount;

            public readonly long TotalFastCount;
            public readonly long TotalLateCount;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct NoteJudgeStatisticsInternal
        {
            public long TapCriticalCount;
            public long TapPerfectCount;
            public long TapGreatCount;
            public long TapGoodCount;
            public long TapMissCount;
            public long TapJudgedCount;

            public long HoldCriticalCount;
            public long HoldPerfectCount;
            public long HoldGreatCount;
            public long HoldGoodCount;
            public long HoldMissCount;
            public long HoldJudgedCount;

            public long SlideCriticalCount;
            public long SlidePerfectCount;
            public long SlideGreatCount;
            public long SlideGoodCount;
            public long SlideMissCount;
            public long SlideJudgedCount;

            public long TouchCriticalCount;
            public long TouchPerfectCount;
            public long TouchGreatCount;
            public long TouchGoodCount;
            public long TouchMissCount;
            public long TouchJudgedCount;

            public long BreakCriticalCount;
            public long BreakPerfectCount;
            public long BreakGreatCount;
            public long BreakGoodCount;
            public long BreakMissCount;
            public long BreakJudgedCount;

            public long TotalCriticalCount;
            public long TotalPerfectCount;
            public long TotalGreatCount;
            public long TotalGoodCount;
            public long TotalMissCount;
            public long TotalJudgedCount;

            public long TotalFastCount;
            public long TotalLateCount;
        }

        struct GaugeScaleBinding
        {
            const float FADE_OUT_DURATION_SEC = 2.5f;
            public float DiffMSec
            {
                get
                {
                    return _diffMSec;
                }
                set
                {
                    if(_scaleTransform is not null)
                    {
                        var percent = Mathf.Clamp(value / 150f, -1, 1);
                        var posX = 282.5f * percent;
                        _diffMSec = 150f * percent;
                        if (!_isActive)
                        {
                            _scaleImage!.enabled = true;
                            _isActive = true;
                        }
                        _fadeOutTimer = 0f;
                        _scaleImage!.color = Color.white;
                        _scaleTransform.SetAsLastSibling();
                        _scaleTransform.anchoredPosition = new Vector2(posX, 0);
                    }
                }
            }
            
            bool _isActive;
            float _diffMSec;
            float _fadeOutTimer;
            RawImage? _scaleImage;
            GameObject? _scaleObject;
            RectTransform? _scaleTransform;

            public GaugeScaleBinding(RectTransform scaleTransform, RawImage scaleImage)
            {
                _scaleObject = scaleTransform.gameObject;
                _scaleTransform = scaleTransform;
                _scaleImage = scaleImage;
                _fadeOutTimer = FADE_OUT_DURATION_SEC;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnPreUpdate(float deltaTime)
            {
                if(_fadeOutTimer == FADE_OUT_DURATION_SEC)
                {
                    return;
                }
                _fadeOutTimer += deltaTime;
                _fadeOutTimer = Mathf.Clamp(_fadeOutTimer, 0f, FADE_OUT_DURATION_SEC);
                var newColor = new Color(1f, 1f, 1f, 1 - (_fadeOutTimer / FADE_OUT_DURATION_SEC));
                _scaleImage!.color = newColor;
            }
        }
    }
}

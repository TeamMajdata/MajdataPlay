using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Rendering;
using MajdataPlay.Databases;
using MajdataPlay.Buffers;
using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using MajdataPlay.Game.Notes;
using MajdataPlay.IO;
using MajdataPlay.Numerics;
using MajdataPlay.Scenes.Game.Buffers;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Controllers;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.Game.Notes.Touch;
using MajdataPlay.Scenes.Game.Parsing;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.Settings;
using MajSimai;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MajdataPlay.Scenes.Game
{
#nullable enable
    public class NoteLoader : MonoBehaviour
    {
        public double Progress { get; set; } = 0;
        public float NoteSpeed { get; set; } = 7f;
        public int ChartRotation { get; set; } = 0;
        public float TouchSpeed
        {
            get => _touchSpeed;
            set => _touchSpeed = Math.Abs(value);
        }
        public long NoteCount { get; private set; } = 0;

        public ReadOnlySpan<bool> IsHasTap
        {
            get
            {
                return _isHasTap;
            }
        }
        public ReadOnlySpan<bool> IsHasHold
        {
            get
            {
                return _isHasHold;
            }
        }
        public ReadOnlySpan<bool> IsHasTouch
        {
            get
            {
                return _isHasTouch;
            }
        }
        public ReadOnlySpan<bool> IsHasTouchHold
        {
            get
            {
                return _isHasTouchHold;
            }
        }


        public GameObject tapPrefab;
        public GameObject holdPrefab;
        public GameObject starPrefab;
        public GameObject touchHoldPrefab;
        public GameObject touchPrefab;
        public GameObject eachLine;
        public GameObject starLine;
        public GameObject notes;
        public GameObject star_slidePrefab;
        public GameObject[] slidePrefab;
        public Material breakMaterial;
        public RuntimeAnimatorController BreakShine;
        public RuntimeAnimatorController JudgeBreakShine;
        public RuntimeAnimatorController HoldShine;

        bool _isSlideNoHead = false;
        bool _isSlideNoTrack = false;

        float _touchSpeed = 7.5f;
        long _noteCount = 0;
        int _slideLayer = -1;
        int _noteSortOrder = short.MaxValue;
        int _touchSortOrder = short.MaxValue;
        int _slideIndex = 0;

        NoteManager _noteManager;

        readonly static bool[] _isHasTap = new bool[8];
        readonly static bool[] _isHasHold = new bool[8];
        readonly static bool[] _isHasTouch = new bool[33];
        readonly static bool[] _isHasTouchHold = new bool[33];

        readonly static List<SlideQueueInfo> _slideQueueInfos = new();
        readonly static Dictionary<int, int> _noteIndex = new();
        readonly static Dictionary<SensorArea, int> _touchIndex = new();

        SlideUpdater _slideUpdater;
        GamePlayManager? _gpManager;
        ObjectCounter _objectCounter;
        NotePoolManager _poolManager;

        readonly static bool USERSETTING_NOTE_FOLDING = MajEnv.Settings?.Debug.NoteFolding ?? true;

        readonly static IReadOnlyDictionary<SimaiNoteType, int> NOTE_LAYER_COUNT = new Dictionary<SimaiNoteType, int>()
        {
            {SimaiNoteType.Tap, 2 },
            {SimaiNoteType.Hold, 3 },
            {SimaiNoteType.Slide, 2 },
            {SimaiNoteType.Touch, 6 },
            {SimaiNoteType.TouchHold, 6 },
        };
        readonly static IReadOnlyDictionary<string, int> SLIDE_PREFAB_MAP = new Dictionary<string, int>()
        {
            {"line3", 0 },
            {"line4", 1 },
            {"line5", 2 },
            {"line6", 3 },
            {"line7", 4 },
            {"circle1", 5 },
            {"circle2", 6 },
            {"circle3", 7 },
            {"circle4", 8 },
            {"circle5", 9 },
            {"circle6", 10 },
            {"circle7", 11 },
            {"circle8", 12 },
            {"v1", 41 },
            {"v2", 13 },
            {"v3", 14 },
            {"v4", 15 },
            {"v6", 16 },
            {"v7", 17 },
            {"v8", 18 },
            {"ppqq1", 19 },
            {"ppqq2", 20 },
            {"ppqq3", 21 },
            {"ppqq4", 22 },
            {"ppqq5", 23 },
            {"ppqq6", 24 },
            {"ppqq7", 25 },
            {"ppqq8", 26 },
            {"pq1", 27 },
            {"pq2", 28 },
            {"pq3", 29 },
            {"pq4", 30 },
            {"pq5", 31 },
            {"pq6", 32 },
            {"pq7", 33 },
            {"pq8", 34 },
            {"s", 35 },
            {"wifi", 36 },
            {"L2", 37 },
            {"L3", 38 },
            {"L4", 39 },
            {"L5", 40 },
            {"Ex", 42 },
        };

        readonly static IReadOnlyDictionary<SensorArea, SensorArea[]> TOUCH_GROUPS = new Dictionary<SensorArea, SensorArea[]>()
        {
            { SensorArea.A1, new SensorArea[]{ SensorArea.D1, SensorArea.D2, SensorArea.E1, SensorArea.E2, SensorArea.B1 } },
            { SensorArea.A2, new SensorArea[]{ SensorArea.D2, SensorArea.D3, SensorArea.E2, SensorArea.E3, SensorArea.B2 } },
            { SensorArea.A3, new SensorArea[]{ SensorArea.D3, SensorArea.D4, SensorArea.E3, SensorArea.E4, SensorArea.B3 } },
            { SensorArea.A4, new SensorArea[]{ SensorArea.D4, SensorArea.D5, SensorArea.E4, SensorArea.E5, SensorArea.B4 } },
            { SensorArea.A5, new SensorArea[]{ SensorArea.D5, SensorArea.D6, SensorArea.E5, SensorArea.E6, SensorArea.B5 } },
            { SensorArea.A6, new SensorArea[]{ SensorArea.D6, SensorArea.D7, SensorArea.E6, SensorArea.E7, SensorArea.B6 } },
            { SensorArea.A7, new SensorArea[]{ SensorArea.D7, SensorArea.D8, SensorArea.E7, SensorArea.E8, SensorArea.B7 } },
            { SensorArea.A8, new SensorArea[]{ SensorArea.D8, SensorArea.D1, SensorArea.E8, SensorArea.E1, SensorArea.B8 } },

            { SensorArea.D1, new SensorArea[]{ SensorArea.A1, SensorArea.A8, SensorArea.E1 } },
            { SensorArea.D2, new SensorArea[]{ SensorArea.A2, SensorArea.A1, SensorArea.E2 } },
            { SensorArea.D3, new SensorArea[]{ SensorArea.A3, SensorArea.A2, SensorArea.E3 } },
            { SensorArea.D4, new SensorArea[]{ SensorArea.A4, SensorArea.A3, SensorArea.E4 } },
            { SensorArea.D5, new SensorArea[]{ SensorArea.A5, SensorArea.A4, SensorArea.E5 } },
            { SensorArea.D6, new SensorArea[]{ SensorArea.A6, SensorArea.A5, SensorArea.E6 } },
            { SensorArea.D7, new SensorArea[]{ SensorArea.A7, SensorArea.A6, SensorArea.E7 } },
            { SensorArea.D8, new SensorArea[]{ SensorArea.A8, SensorArea.A7, SensorArea.E8 } },

            { SensorArea.E1, new SensorArea[]{ SensorArea.D1, SensorArea.A1, SensorArea.A8, SensorArea.B1, SensorArea.B8 } },
            { SensorArea.E2, new SensorArea[]{ SensorArea.D2, SensorArea.A2, SensorArea.A1, SensorArea.B2, SensorArea.B1 } },
            { SensorArea.E3, new SensorArea[]{ SensorArea.D3, SensorArea.A3, SensorArea.A2, SensorArea.B3, SensorArea.B2 } },
            { SensorArea.E4, new SensorArea[]{ SensorArea.D4, SensorArea.A4, SensorArea.A3, SensorArea.B4, SensorArea.B3 } },
            { SensorArea.E5, new SensorArea[]{ SensorArea.D5, SensorArea.A5, SensorArea.A4, SensorArea.B5, SensorArea.B4 } },
            { SensorArea.E6, new SensorArea[]{ SensorArea.D6, SensorArea.A6, SensorArea.A5, SensorArea.B6, SensorArea.B5 } },
            { SensorArea.E7, new SensorArea[]{ SensorArea.D7, SensorArea.A7, SensorArea.A6, SensorArea.B7, SensorArea.B6 } },
            { SensorArea.E8, new SensorArea[]{ SensorArea.D8, SensorArea.A8, SensorArea.A7, SensorArea.B8, SensorArea.B7 } },

            { SensorArea.B1, new SensorArea[]{ SensorArea.E1, SensorArea.E2, SensorArea.B8, SensorArea.B2, SensorArea.A1, SensorArea.C } },
            { SensorArea.B2, new SensorArea[]{ SensorArea.E2, SensorArea.E3, SensorArea.B1, SensorArea.B3, SensorArea.A2, SensorArea.C } },
            { SensorArea.B3, new SensorArea[]{ SensorArea.E3, SensorArea.E4, SensorArea.B2, SensorArea.B4, SensorArea.A3, SensorArea.C } },
            { SensorArea.B4, new SensorArea[]{ SensorArea.E4, SensorArea.E5, SensorArea.B3, SensorArea.B5, SensorArea.A4, SensorArea.C } },
            { SensorArea.B5, new SensorArea[]{ SensorArea.E5, SensorArea.E6, SensorArea.B4, SensorArea.B6, SensorArea.A5, SensorArea.C } },
            { SensorArea.B6, new SensorArea[]{ SensorArea.E6, SensorArea.E7, SensorArea.B5, SensorArea.B7, SensorArea.A6, SensorArea.C } },
            { SensorArea.B7, new SensorArea[]{ SensorArea.E7, SensorArea.E8, SensorArea.B6, SensorArea.B8, SensorArea.A7, SensorArea.C } },
            { SensorArea.B8, new SensorArea[]{ SensorArea.E8, SensorArea.E1, SensorArea.B7, SensorArea.B1, SensorArea.A8, SensorArea.C } },

            { SensorArea.C, new SensorArea[]{ SensorArea.B1, SensorArea.B2, SensorArea.B3, SensorArea.B4, SensorArea.B5, SensorArea.B6, SensorArea.B7, SensorArea.B8} },
        };

        IReadOnlyDictionary<int, int> _buttonRingMappingTable;
        IReadOnlyDictionary<SensorArea, SensorArea> _touchPanelMappingTable;
        NoteLoader()
        {
            (_buttonRingMappingTable, _touchPanelMappingTable) = NoteCreateHelper.GenerateMappingTable();
        }

        void Awake()
        {
            Majdata<NoteLoader>.Instance = this;

            SlideDataBuilder.InitializeSlideAreaLookup();

            Array.Clear(_isHasTap, 0, _isHasTap.Length);
            Array.Clear(_isHasHold, 0, _isHasHold.Length);
            Array.Clear(_isHasTouch, 0, _isHasTouch.Length);
            Array.Clear(_isHasTouchHold, 0, _isHasTouchHold.Length);
        }
        void OnDestroy()
        {
            Majdata<NoteLoader>.Free();
            _slideQueueInfos.Clear();
            _noteIndex.Clear();
            _touchIndex.Clear();
        }
        private void Start()
        {
            _objectCounter = Majdata<ObjectCounter>.Instance!;
            _noteManager = Majdata<NoteManager>.Instance!;
            _poolManager = Majdata<NotePoolManager>.Instance!;
            _gpManager = Majdata<GamePlayManager>.Instance;
            _slideUpdater = Majdata<SlideUpdater>.Instance!;
            _isSlideNoHead = Majdata<INoteController>.Instance?.ModInfo.SlideNoHead ?? false;
            _isSlideNoTrack = Majdata<INoteController>.Instance?.ModInfo.SlideNoTrack ?? false;
        }
        internal void Clear()
        {
            _noteManager.ResetCounter();
            _noteIndex.Clear();
            _touchIndex.Clear();
            _slideQueueInfos.Clear();

            Array.Clear(_isHasTap, 0, _isHasTap.Length);
            Array.Clear(_isHasHold, 0, _isHasHold.Length);
            Array.Clear(_isHasTouch, 0, _isHasTouch.Length);
            Array.Clear(_isHasTouchHold, 0, _isHasTouchHold.Length);
        }
        internal async UniTask LoadNotesIntoPoolAsync(SimaiChart maiChart, CancellationToken token = default)
        {
            await UniTask.SwitchToMainThread(token);
            var skin = MajInstances.SkinManager.SelectedSkin;
            var materials = GameRuntime.Instance.Note;
            NoteSpriteResources.PreloadMaterials(skin, materials.DefaultMaterial,
                materials.BreakMaterial, materials.HoldShineMaterial, breakMaterial);

            List<Task> touchTasks = new();

            _noteManager.ResetCounter();
            _noteIndex.Clear();
            _touchIndex.Clear();

            for (int i = 1; i < 9; i++)
            {
                _noteIndex.Add(i, 0);
            }
            for (int i = 0; i < 33; i++)
            {
                _touchIndex.Add((SensorArea)i, 0);
            }


            await _objectCounter.CountNoteSumAsync(maiChart);

            NoteCount = _objectCounter.TapSum +
                      _objectCounter.HoldSum +
                      _objectCounter.TouchSum +
                      _objectCounter.BreakSum +
                      _objectCounter.SlideSum;

            if (maiChart.NoteTimings.Length != 0)
            {

                var lastNoteTime = maiChart.NoteTimings[^1].Timing;
                var randomMappingTableLifeTime = new Range<double>(double.MinValue, double.MinValue, ContainsType.Closed);
                var isSRandomEnabled = MajEnv.Settings.Game.Random == RandomModeOption.S_RANDOM;
                for (var i = 0; i < maiChart.NoteTimings.Length; i++)
                {
                    var timing = maiChart.NoteTimings[i];
                    RentedList<NotePoolingInfo?> eachNotes = new();
                    RentedList<ITouchGroupInfoProvider> touchGroupMembers = new();
                    RentedList<ITouchHoldGroupInfoProvider> touchHoldGroupMembers = new();
                    var foldedNotes = NoteCreateHelper.NoteFolding(timing.Notes);
                    if (isSRandomEnabled)
                    {
                        if (!randomMappingTableLifeTime.InRange(timing.Timing))
                        {
                            (_buttonRingMappingTable, _touchPanelMappingTable) = NoteCreateHelper.GenerateMappingTable();
                            randomMappingTableLifeTime = new Range<double>(timing.Timing, timing.Timing, ContainsType.RightOpen);
                        }
                    }
                    foreach (var note in foldedNotes)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            switch (note.Type)
                            {
                                case SimaiNoteType.Tap:
                                    {
                                        var obj = CreateTap(note, timing);
                                        _poolManager.AddTap(obj);
                                        eachNotes.Add(obj);
                                    }
                                    break;
                                case SimaiNoteType.Hold:
                                    {
                                        var obj = CreateHold(note, timing);
                                        _poolManager.AddHold(obj);
                                        eachNotes.Add(obj);

                                        if (isSRandomEnabled)
                                        {
                                            var endTiming = timing.Timing + note.HoldTime;
                                            if (!randomMappingTableLifeTime.InRange(endTiming))
                                            {
                                                randomMappingTableLifeTime = new Range<double>(randomMappingTableLifeTime.Start, endTiming, ContainsType.RightOpen);
                                            }
                                        }
                                    }
                                    break;
                                case SimaiNoteType.TouchHold:
                                    _poolManager.AddTouchHold(CreateTouchHold(note, timing, touchGroupMembers, touchHoldGroupMembers));
                                    if (isSRandomEnabled)
                                    {
                                        var endTiming = timing.Timing + note.HoldTime;
                                        if (!randomMappingTableLifeTime.InRange(endTiming))
                                        {
                                            randomMappingTableLifeTime = new Range<double>(randomMappingTableLifeTime.Start, endTiming, ContainsType.RightOpen);
                                        }
                                    }
                                    break;
                                case SimaiNoteType.Touch:
                                    _poolManager.AddTouch(CreateTouch(note, timing, touchGroupMembers));
                                    break;
                                case SimaiNoteType.Slide:
                                    var foldedSlide = note as FoldedSimaiNote;
                                    foldedSlide ??= new FoldedSimaiNote()
                                    {
                                        Type = note.Type,
                                        StartPosition = note.StartPosition,
                                        HoldTime = note.HoldTime,
                                        IsBreak = note.IsBreak,
                                        IsEx = note.IsEx,
                                        IsFakeRotate = note.IsFakeRotate,
                                        IsForceStar = note.IsForceStar,
                                        IsHanabi = note.IsHanabi,
                                        IsSlideBreak = note.IsSlideBreak,
                                        IsSlideNoHead = note.IsSlideNoHead,
                                        IsMine = note.IsMine,
                                        IsMineSlide = note.IsMineSlide,
                                        RawContent = note.RawContent,
                                        SlideStartTime = note.SlideStartTime,
                                        SlideTime = note.SlideTime,
                                        TouchArea = note.TouchArea,
                                        Count = 1
                                    };
                                    if (foldedSlide.RawContent.Contains('K'))
                                    {
                                        CreateExtendSlide(timing, foldedSlide, eachNotes);
                                    }
                                    else
                                    {
                                        CreateSlideGroup(timing, foldedSlide, eachNotes); // 星星组
                                    }
                                    _noteCount += foldedSlide.Count - 1;
                                    break;
                            }
                            _noteCount++;
                            Progress = (double)_noteCount / NoteCount;
                            if (_noteCount % 100 == 0)
                            {
                                await UniTask.DelayFrame(3);
                            }
                        }
                        catch (InvalidSimaiSyntaxException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            MajDebug.LogException(e);
                            throw;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (touchGroupMembers.Count != 0)
                    {
                        touchTasks.Add(AllocTouchGroup(touchGroupMembers));
                    }
                    if (touchHoldGroupMembers.Count != 0)
                    {
                        touchTasks.Add(AllocTouchHoldGroup(touchHoldGroupMembers));
                    }
                    var eachNoteCount = 0;
                    for (var x = 0; x < eachNotes.Count; x++)
                    {
                        var note = eachNotes[x];
                        if (note is null || note.IsMine)
                        {
                            eachNotes.RemoveAt(x);
                            x--;
                        }
                        else
                        {
                            eachNoteCount++;
                        }
                    }
                    if (eachNoteCount > 1) //有多个非touchnote
                    {
                        for (var x = 0; x < eachNoteCount; x++)
                        {
                            var isLast = x == eachNoteCount - 1;
                            if (isLast)
                            {
                                break;
                            }
                            var eachLinePoolingInfo = CreateEachLine(timing, eachNotes[x]!, eachNotes[x + 1]!);
                            if (eachLinePoolingInfo is not null)
                            {
                                _poolManager.AddEachLine(eachLinePoolingInfo);
                            }
                        }
                    }
                }

                var allTask = Task.WhenAll(touchTasks);
                while (!allTask.IsCompleted)
                {
                    token.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                    if (allTask.IsFaulted)
                    {
                        throw allTask.Exception.GetBaseException();
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            _slideUpdater.AddSlideQueueInfos(_slideQueueInfos);
            _poolManager.Init();
        }
        EachLinePoolingInfo? CreateEachLine(SimaiTimingPoint timing, NotePoolingInfo noteA, NotePoolingInfo noteB)
        {
            static void SetNoteBinding(NotePoolingInfo note, EachLineBinding binding)
            {
                if (note is TapPoolingInfo tapInfo)
                {
                    tapInfo.EachLineBinding = binding;
                }
                else if (note is HoldPoolingInfo holdInfo)
                {
                    holdInfo.EachLineBinding = binding;
                }
            }
            static EachLineBinding? GetEachLineBindingFromNoteInfo(NotePoolingInfo noteInfo)
            {
                if (noteInfo is TapPoolingInfo tapInfo)
                {
                    return tapInfo.EachLineBinding;
                }
                else if (noteInfo is HoldPoolingInfo holdInfo)
                {
                    return holdInfo.EachLineBinding;
                }
                return null;
            }
            try
            {
                var startPos = noteA.StartPos;
                var endPos = noteB.StartPos;
                endPos = endPos - startPos;
                if (endPos == 0)
                {
                    return null;
                }
                var binding = GetEachLineBindingFromNoteInfo(noteA) ?? GetEachLineBindingFromNoteInfo(noteB);
                binding ??= new EachLineBinding();
                var time = (float)timing.Timing;
                var speed = NoteSpeed * timing.HSpeed;
                var scaleRate = MajEnv.Settings.Debug.NoteAppearRate;
                var appearDiff = (-(1 - (scaleRate * 1.225f)) - (4.8f * scaleRate)) / (speed * scaleRate);
                var appearTiming = time + appearDiff;

                endPos = endPos < 0 ? endPos + 8 : endPos;
                endPos = endPos > 8 ? endPos - 8 : endPos;
                endPos++;

                if (endPos > 4)
                {
                    startPos = noteB.StartPos;
                    endPos = noteA.StartPos;
                    endPos = endPos - startPos;
                    endPos = endPos < 0 ? endPos + 8 : endPos;
                    endPos = endPos > 8 ? endPos - 8 : endPos;
                    endPos++;
                }

                var startPosition = startPos;
                var curvLength = endPos - 1;

                SetNoteBinding(noteA, binding);
                SetNoteBinding(noteB, binding);

                return new EachLinePoolingInfo()
                {
                    StartPos = startPosition,
                    Timing = time,
                    AppearTiming = appearTiming,
                    CurvLength = curvLength,
                    MemberA = noteA,
                    MemberB = noteB,
                    Speed = speed,
                    DistanceProvider = binding,
                };
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                return null;
            }
        }
        TapPoolingInfo CreateTap(in SimaiNote note, in SimaiTimingPoint timing)
        {
            try
            {
                var startPos = note.StartPosition;
                var noteTiming = (float)timing.Timing;
                var speed = NoteSpeed * timing.HSpeed;
                var scaleRate = MajEnv.Settings.Debug.NoteAppearRate;
                var appearDiff = (-(1 - (scaleRate * 1.225f)) - (4.8f * scaleRate)) / (Math.Abs(speed) * scaleRate);
                var appearTiming = Math.Min(noteTiming + appearDiff, noteTiming - 0.15f);
                var sortOrder = _noteSortOrder;
                var isMine = note.IsMine;
                var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
                if (appearTiming < -5f && _gpManager is not null)
                {
                    _gpManager.FirstNoteAppearTiming = Mathf.Min(_gpManager.FirstNoteAppearTiming, appearTiming);
                }
                _noteSortOrder -= NOTE_LAYER_COUNT[note.Type];
                startPos = NoteCreateHelper.Rotation(startPos, ChartRotation);
                NoteCreateHelper.SetNewPositionIfRequested(ref startPos, _buttonRingMappingTable);
                _isHasTap[startPos - 1] = true;
                return new()
                {
                    StartPos = startPos,
                    Timing = noteTiming,
                    AppearTiming = appearTiming,
                    NoteSortOrder = sortOrder,
                    Speed = speed,
                    IsEach = isEach,
                    IsBreak = note.IsBreak,
                    IsEX = note.IsEx,
                    IsMine = isMine,
                    IsStar = note.IsForceStar,
                    RotateSpeed = note.IsFakeRotate ? -440f : 0,
                    QueueInfo = new TapQueueInfo()
                    {
                        Index = _noteIndex[startPos]++,
                        KeyIndex = startPos
                    }
                };
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                var line = timing.RawTextPositionY;
                var column = timing.RawTextPositionX;
                throw new InvalidSimaiSyntaxException(line,
                                                      column,
                                                      note.RawContent,
                                                      BuildSyntaxErrorMessage(line, column, note.RawContent));
            }
        }
        HoldPoolingInfo CreateHold(in SimaiNote note, in SimaiTimingPoint timing)
        {
            try
            {
                var startPos = note.StartPosition;
                var noteTiming = (float)timing.Timing;
                var speed = Math.Abs(NoteSpeed * timing.HSpeed);
                var scaleRate = MajEnv.Settings.Debug.NoteAppearRate;
                var appearDiff = (-(1 - (scaleRate * 1.225f)) - (4.8f * scaleRate)) / (speed * scaleRate);
                var appearTiming = Math.Min(noteTiming + appearDiff, noteTiming - 0.15f);
                var sortOrder = _noteSortOrder;
                var isMine = note.IsMine;
                var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
                if (appearTiming < -5f && _gpManager is not null)
                {
                    _gpManager.FirstNoteAppearTiming = Mathf.Min(_gpManager.FirstNoteAppearTiming, appearTiming);
                }
                _noteSortOrder -= NOTE_LAYER_COUNT[note.Type];
                startPos = NoteCreateHelper.Rotation(startPos, ChartRotation);
                NoteCreateHelper.SetNewPositionIfRequested(ref startPos, _buttonRingMappingTable);
                _isHasHold[startPos - 1] = true;
                _isHasTap[startPos - 1] = true;
                return new()
                {
                    StartPos = startPos,
                    Timing = noteTiming,
                    LastFor = (float)note.HoldTime,
                    AppearTiming = appearTiming,
                    NoteSortOrder = sortOrder,
                    Speed = speed,
                    IsEach = isEach,
                    IsBreak = note.IsBreak,
                    IsMine = isMine,
                    IsEX = note.IsEx,
                    QueueInfo = new TapQueueInfo()
                    {
                        Index = _noteIndex[startPos]++,
                        KeyIndex = startPos
                    }
                };
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                var line = timing.RawTextPositionY;
                var column = timing.RawTextPositionX;
                throw new InvalidSimaiSyntaxException(line,
                                                      column,
                                                      note.RawContent,
                                                      BuildSyntaxErrorMessage(line, column, note.RawContent));
            }
        }
        TapPoolingInfo CreateStar(int startPos, SimaiNote note, in SimaiTimingPoint timing)
        {
            try
            {
                var noteTiming = (float)timing.Timing;
                var speed = NoteSpeed * timing.HSpeed;
                var scaleRate = MajEnv.Settings.Debug.NoteAppearRate;
                var slideFadeInTiming = (-3.926913f / speed) + MajEnv.Settings.Game.SlideFadeInOffset + (float)timing.Timing;
                var appearDiff = (-(1 - (scaleRate * 1.225f)) - (4.8f * scaleRate)) / (Math.Abs(speed) * scaleRate);
                var appearTiming = Math.Min(noteTiming + appearDiff, noteTiming - 0.15f);
                var sortOrder = _noteSortOrder;
                var isMine = note.IsMine;
                var isEach = NoteCreateHelper.IsEachNote(note, SimaiNoteType.Tap, timing.Notes);
                var isDouble = NoteCreateHelper.IsStarDouble(note, timing.Notes);
                TapQueueInfo? queueInfo = null;

                appearTiming = Math.Min(appearTiming, slideFadeInTiming);

                if (appearTiming < -5f && _gpManager is not null)
                {
                    _gpManager.FirstNoteAppearTiming = Mathf.Min(_gpManager.FirstNoteAppearTiming, appearTiming);
                }
                _noteSortOrder -= NOTE_LAYER_COUNT[note.Type];
                _isHasTap[startPos - 1] = true;

                queueInfo = new TapQueueInfo()
                {
                    Index = _noteIndex[startPos]++,
                    KeyIndex = startPos
                };

                return new()
                {
                    StartPos = startPos,
                    Timing = noteTiming,
                    AppearTiming = appearTiming,
                    NoteSortOrder = sortOrder,
                    Speed = speed,
                    IsEach = isEach,
                    IsBreak = note.IsBreak,
                    IsEX = note.IsEx,
                    IsMine = note.IsMine,
                    IsStar = true,
                    IsDouble = isDouble,
                    RotateSpeed = -180 / (float)note.SlideTime,
                    QueueInfo = queueInfo ?? TapQueueInfo.Default
                };
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                var line = timing.RawTextPositionY;
                var column = timing.RawTextPositionX;
                throw new InvalidSimaiSyntaxException(line,
                                                      column,
                                                      note.RawContent,
                                                      BuildSyntaxErrorMessage(line, column, note.RawContent));
            }
        }
        TouchPoolingInfo CreateTouch(in SimaiNote note,
                                     in SimaiTimingPoint timing,
                                     in IList<ITouchGroupInfoProvider> members)
        {
            try
            {
                note.StartPosition = NoteCreateHelper.Rotation(note.StartPosition, ChartRotation);
                var sensorPos = NoteHelper.GetSensor(note.TouchArea, note.StartPosition);
                NoteCreateHelper.SetNewPositionIfRequested(ref sensorPos, _touchPanelMappingTable);
                var queueInfo = new TouchQueueInfo()
                {
                    SensorPos = sensorPos,
                    Index = _touchIndex[sensorPos]++
                };
                var noteTiming = (float)timing.Timing;
                var areaPosition = note.TouchArea;
                var startPosition = note.StartPosition;
                var isBreak = note.IsBreak;
                var isMine = note.IsMine;
                var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
                var speed = TouchSpeed * Math.Abs(timing.HSpeed);
                var isFirework = note.IsHanabi;
                var noteSortOrder = _touchSortOrder;
                var moveDuration = 3.209385682f * Mathf.Pow(speed, -0.9549621752f);
                var appearTiming = Math.Min(noteTiming - moveDuration, noteTiming - 0.15f);
                if (appearTiming < -5f && _gpManager is not null)
                {
                    _gpManager.FirstNoteAppearTiming = Mathf.Min(_gpManager.FirstNoteAppearTiming, appearTiming);
                }
                _isHasTouch[(int)sensorPos] = true;
                _touchSortOrder -= NOTE_LAYER_COUNT[note.Type];
                var poolingInfo = new TouchPoolingInfo()
                {
                    SensorPos = sensorPos,
                    Timing = noteTiming,
                    AppearTiming = appearTiming,
                    AreaPos = areaPosition,
                    StartPos = startPosition,
                    Speed = speed,
                    IsFirework = isFirework,
                    IsEach = isEach,
                    IsBreak = isBreak,
                    IsEX = false,
                    IsMine = isMine,
                    NoteSortOrder = noteSortOrder,
                    QueueInfo = queueInfo,
                };
                if (isEach)
                {
                    members.Add(poolingInfo);
                }
                return poolingInfo;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                var line = timing.RawTextPositionY;
                var column = timing.RawTextPositionX;
                throw new InvalidSimaiSyntaxException(line,
                                                      column,
                                                      note.RawContent,
                                                      BuildSyntaxErrorMessage(line, column, note.RawContent));
            }
        }
        TouchHoldPoolingInfo CreateTouchHold(in SimaiNote note,
                                             in SimaiTimingPoint timing,
                                             in IList<ITouchGroupInfoProvider> touchGroupMembers,
                                             in IList<ITouchHoldGroupInfoProvider> touchHoldGroupMembers)
        {
            try
            {
                note.StartPosition = NoteCreateHelper.Rotation(note.StartPosition, ChartRotation);
                var sensorPos = NoteHelper.GetSensor(note.TouchArea, note.StartPosition);
                NoteCreateHelper.SetNewPositionIfRequested(ref sensorPos, _touchPanelMappingTable);
                var queueInfo = new TouchQueueInfo()
                {
                    SensorPos = sensorPos,
                    Index = _touchIndex[sensorPos]++
                };
                var startPosition = note.StartPosition;
                var areaPosition = note.TouchArea;
                var noteTiming = (float)timing.Timing;
                var lastFor = (float)note.HoldTime;
                var speed = TouchSpeed * Math.Abs(timing.HSpeed);
                var isFirework = note.IsHanabi;
                var isBreak = note.IsBreak;
                var isMine = note.IsMine;
                var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
                var moveDuration = 3.209385682f * Mathf.Pow(speed, -0.9549621752f);
                var appearTiming = Math.Min(noteTiming - moveDuration, noteTiming - 0.15f);
                var noteSortOrder = _touchSortOrder;
                if (appearTiming < -5f && _gpManager is not null)
                {
                    _gpManager.FirstNoteAppearTiming = Mathf.Min(_gpManager.FirstNoteAppearTiming, appearTiming);
                }

                _touchSortOrder -= NOTE_LAYER_COUNT[note.Type];
                _isHasTouchHold[(int)sensorPos] = true;
                var poolingInfo = new TouchHoldPoolingInfo()
                {
                    SensorPos = sensorPos,
                    Timing = noteTiming,
                    AppearTiming = appearTiming,
                    AreaPos = areaPosition,
                    StartPos = startPosition,
                    Speed = speed,
                    IsFirework = isFirework,
                    IsEach = isEach,
                    IsBreak = isBreak,
                    IsEX = false,
                    IsMine = isMine,
                    LastFor = lastFor,
                    NoteSortOrder = noteSortOrder,
                    QueueInfo = queueInfo,
                };
                if (isEach)
                {
                    touchGroupMembers.Add(poolingInfo);
                    touchHoldGroupMembers.Add(poolingInfo);
                }
                return poolingInfo;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                var line = timing.RawTextPositionY;
                var column = timing.RawTextPositionX;
                throw new InvalidSimaiSyntaxException(line,
                                                      column,
                                                      note.RawContent,
                                                      BuildSyntaxErrorMessage(line, column, note.RawContent));
            }
        }
        Task AllocTouchGroup(IList<ITouchGroupInfoProvider> members, CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                var sensorTypes = members.GroupBy(x => x.SensorPos)
                                         .Select(x => x.Key)
                                         .ToList();
                using var sensorGroups = new RentedList<RentedList<SensorArea>>();

                while (sensorTypes.Count > 0)
                {
                    var sensorType = sensorTypes[0];
                    var groupMembers = new RentedList<SensorArea>();
                    groupMembers.Add(sensorType);

                    for (var i = 0; i < groupMembers.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var currentArea = groupMembers[i];
                        var nearbyArea = TOUCH_GROUPS[currentArea];
                        for (var j = 0; j < sensorTypes.Count; j++)
                        {
                            token.ThrowIfCancellationRequested();
                            var area = sensorTypes[j];
                            if (groupMembers.Contains(area))
                            {
                                continue;
                            }
                            else if (nearbyArea.Contains(area))
                            {
                                groupMembers.Add(area);
                            }
                        }
                    }

                    foreach (var area in groupMembers)
                    {
                        token.ThrowIfCancellationRequested();
                        sensorTypes.Remove(area);
                    }
                    token.ThrowIfCancellationRequested();
                    sensorGroups.Add(groupMembers);
                }
                using var touchGroups = new RentedList<TouchGroup>();
                var memberMapping = members.GroupBy(x => x.SensorPos).ToDictionary(x => x.Key);
                token.ThrowIfCancellationRequested();
                foreach (var group in sensorGroups)
                {
                    token.ThrowIfCancellationRequested();
                    touchGroups.Add(new TouchGroup()
                    {
                        Members = group.SelectMany(x => memberMapping[x]).ToArray()
                    });
                }
                foreach (var member in members)
                {
                    token.ThrowIfCancellationRequested();
                    member.GroupInfo = touchGroups.Find(x => x.Members.Any(y => y == member));
                }
            });
        }
        Task AllocTouchHoldGroup(IList<ITouchHoldGroupInfoProvider> members, CancellationToken token = default)
        {
            return Task.Run(() =>
            {
                var sensorTypes = members.GroupBy(x => x.SensorPos)
                                         .Select(x => x.Key)
                                         .ToList();
                using var sensorGroups = new RentedList<RentedList<SensorArea>>();

                while (sensorTypes.Count > 0)
                {
                    var sensorType = sensorTypes[0];
                    var groupMembers = new RentedList<SensorArea>();
                    groupMembers.Add(sensorType);

                    for (var i = 0; i < groupMembers.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var currentArea = groupMembers[i];
                        var nearbyArea = TOUCH_GROUPS[currentArea];
                        for (var j = 0; j < sensorTypes.Count; j++)
                        {
                            token.ThrowIfCancellationRequested();
                            var area = sensorTypes[j];
                            if (groupMembers.Contains(area))
                            {
                                continue;
                            }
                            else if (nearbyArea.Contains(area))
                            {
                                groupMembers.Add(area);
                            }
                        }
                    }

                    foreach (var area in groupMembers)
                    {
                        token.ThrowIfCancellationRequested();
                        sensorTypes.Remove(area);
                    }
                    token.ThrowIfCancellationRequested();
                    sensorGroups.Add(groupMembers);
                }
                using var touchHoldGroups = new RentedList<TouchHoldGroup>();
                var memberMapping = members.GroupBy(x => x.SensorPos).ToDictionary(x => x.Key);
                token.ThrowIfCancellationRequested();
                foreach (var group in sensorGroups)
                {
                    token.ThrowIfCancellationRequested();
                    touchHoldGroups.Add(new TouchHoldGroup()
                    {
                        Members = group.SelectMany(x => memberMapping[x]).ToArray()
                    });
                }
                foreach (var member in members)
                {
                    token.ThrowIfCancellationRequested();
                    member.TouchHoldGroupInfo = touchHoldGroups.Find(x => x.Members.Any(y => y == member));
                }
            });
        }

        private void CreateSlideGroup(SimaiTimingPoint timing, FoldedSimaiNote note, in IList<NotePoolingInfo?> eachNotes)
        {
            try
            {
                int charIntParse(char c)
                {
                    return c - '0';
                }

                using var preprocessSubSlides = new RentedList<SubSlideNote>();
                using var subBarCount = new RentedList<int>();
                var sumBarCount = 0;

                var noteContent = note.RawContent;
                var latestStartIndex = charIntParse(noteContent[0]); // 存储上一个Slide的结尾 也就是下一个Slide的起点
                var ptr = 1; // 指向目前处理的字符

                var specTimeFlag = 0; // 表示此组合slide是指定总时长 还是指定每一段的时长
                                      // 0-目前还没有读取
                                      // 1-读取到了一个未指定时长的段落
                                      // 2-读取到了一个指定时长的段落
                                      // 3-（期望）读取到了最后一个时长指定
                using (var sb = ZString.CreateStringBuilder())
                {
                    while (ptr < noteContent.Length)
                    {
                        if (char.IsNumber(noteContent[ptr]))
                        {
                            // 理论上来说 不应该读取到数字 因此如果读取到了 说明有语法错误
                            throw new InvalidSimaiSyntaxException(timing.RawTextPositionY,
                                                                  timing.RawTextPositionX,
                                                                  noteContent,
                                                                  "组合星星有错误\nSLIDE CHAIN ERROR");
                        }
                        sb.Clear();
                        // 读取到字符
                        var slideTypeChar = noteContent[ptr++].ToString();
                        var slidePart = new SubSlideNote();
                        slidePart.Type = SimaiNoteType.Slide;
                        slidePart.StartPosition = latestStartIndex;
                        if (slideTypeChar == "V")
                        {
                            // 转折星星
                            var middlePos = noteContent[ptr++];
                            var endPos = noteContent[ptr++];
                            sb.Append(latestStartIndex);
                            sb.Append(slideTypeChar);
                            sb.Append(middlePos);
                            sb.Append(endPos);

                            //slidePart.RawContent = sb.ToString();
                            latestStartIndex = charIntParse(endPos);
                        }
                        else
                        {
                            // 其他普通星星
                            // 额外检查pp和qq
                            if (noteContent[ptr] == slideTypeChar[0])
                            {
                                if (ptr + 1 < noteContent.Length)
                                {
                                    slideTypeChar += noteContent[ptr++];
                                }
                            }
                            var endPos = noteContent[ptr++];

                            sb.Append(latestStartIndex);
                            sb.Append(slideTypeChar);
                            sb.Append(endPos);

                            //slidePart.RawContent = sb.ToString();
                            latestStartIndex = charIntParse(endPos);
                        }

                        if (noteContent[ptr] == '[')// 如果指定了速度
                        {
                            switch (specTimeFlag)
                            {
                                case 0: // 之前未读取过
                                    specTimeFlag = 2;
                                    break;
                                case 1: // 之前读取到的都是未指定时长的段落 那么将flag设为3 如果之后又读取到时长 则报错
                                    specTimeFlag = 3;
                                    break;
                                case 3: // 之前读取到了指定时长 并期待那个时长就是最终时长 但是又读取到一个新的时长 则报错
                                    throw new InvalidSimaiSyntaxException(timing.RawTextPositionY,
                                                                          timing.RawTextPositionX,
                                                                          noteContent,
                                                                          "组合星星有错误\nSLIDE CHAIN ERROR");
                            }
                            while (ptr < noteContent.Length)
                            {
                                sb.Append(noteContent[ptr]);
                                //slidePart.RawContent += noteContent[ptr++];
                                if (noteContent[ptr] == ']')
                                {
                                    break;
                                }
                                ptr++;
                            }
                            slidePart.RawContent = sb.ToString();
                            ptr++;
                        }
                        else // 没有指定速度
                        {
                            switch (specTimeFlag)
                            {
                                case 0: // 之前未读取过
                                    specTimeFlag = 1;
                                    break;
                                case 2:
                                case 3: // 之前读取到指定时长的段落了 说明这一条组合星星有的指定时长 有的没指定 则需要报错
                                    throw new InvalidSimaiSyntaxException(timing.RawTextPositionY,
                                                                          timing.RawTextPositionX,
                                                                          noteContent,
                                                                          "组合星星有错误\nSLIDE CHAIN ERROR");
                            }
                        }
                        if (string.IsNullOrEmpty(slidePart.RawContent))
                        {
                            slidePart.RawContent = sb.ToString();
                        }
                        string slideShape = NoteCreateHelper.DetectShapeFromText(slidePart.RawContent);
                        if (slideShape.StartsWith("-"))
                        {
                            slideShape = slideShape.Substring(1);
                        }
                        int slideIndex = SLIDE_PREFAB_MAP[slideShape];
                        if (slideIndex < 0)
                        {
                            slideIndex = -slideIndex;
                        }

                        var barCount = slidePrefab[slideIndex].transform.childCount;
                        subBarCount.Add(barCount);
                        sumBarCount += barCount;

                        slidePart.Origin = note;
                        preprocessSubSlides.Add(slidePart);
                    }
                }
                foreach (var subSlide in preprocessSubSlides)
                {
                    subSlide.IsBreak = note.IsBreak;
                    subSlide.IsEx = note.IsEx;
                    subSlide.IsSlideBreak = note.IsSlideBreak;
                    subSlide.IsSlideNoHead = true;
                    subSlide.IsMine = note.IsMine;
                    subSlide.IsMineSlide = note.IsMineSlide;
                }
                preprocessSubSlides[0].IsSlideNoHead = note.IsSlideNoHead;

                if (specTimeFlag == 1 || specTimeFlag == 0) // 如果到结束还是1 那说明没有一个指定了时长 报错
                {
                    throw new InvalidSimaiSyntaxException(timing.RawTextPositionY,
                                                          timing.RawTextPositionX,
                                                          noteContent,
                                                          "组合星星有错误\nSLIDE CHAIN ERROR");
                }
                // 此时 flag为2表示每条指定语法 为3表示整体指定语法

                // 整体指定语法 使用slideTime来计算
                var tempBarCount = 0;
                for (var i = 0; i < preprocessSubSlides.Count; i++)
                {
                    preprocessSubSlides[i].SlideStartTime = note.SlideStartTime + (double)tempBarCount / sumBarCount * note.SlideTime;
                    preprocessSubSlides[i].SlideTime = (double)subBarCount[i] / sumBarCount * note.SlideTime;
                    tempBarCount += subBarCount[i];
                }

                IConnectableSlide? parent = null;
                using var subSlides = new RentedList<SlideDrop>();
                float totalLen = (float)preprocessSubSlides.Select(x => x.SlideTime).Sum();
                float startTiming = (float)preprocessSubSlides[0].SlideStartTime;
                float totalSlideLen = 0;
                int? extraRotation = null;
                CreateSlideResult<SlideDrop>? slideResult = null;
                for (var i = 0; i <= preprocessSubSlides.Count - 1; i++)
                {
                    bool isConn = preprocessSubSlides.Count != 1;
                    bool isGroupHead = i == 0;
                    bool isGroupEnd = i == preprocessSubSlides.Count - 1;
                    SlideBase sliObj;

                    if (note.RawContent!.Contains('w')) //wifi
                    {
                        if (isConn)
                        {
                            throw new InvalidOperationException("不允许Wifi Slide作为Connection Slide的一部分");
                        }
                        var result = CreateWifi(timing, preprocessSubSlides[i], note.Count);
                        sliObj = result.SlideInstance;
                        foreach (var starInfo in result.StarInfos)
                        {
                            if (starInfo is null)
                            {
                                continue;
                            }
                            eachNotes.Add(starInfo);
                        }
                        //AddSlideToQueue(timing, result.SlideInstance);
                        UpdateStarRotateSpeed(result, (float)preprocessSubSlides[i].SlideTime, 20);
                        sliObj.Init();
                    }
                    else
                    {
                        var info = new ConnSlideInfo()
                        {
                            TotalLength = totalLen,
                            IsGroupPart = isConn,
                            IsGroupPartHead = isGroupHead,
                            IsGroupPartEnd = isGroupEnd,
                            Parent = parent,
                            StartTiming = startTiming
                        };
                        var result = CreateSlide(timing, preprocessSubSlides[i], info, note.Count, ref extraRotation);
                        parent = result.SlideInstance;
                        sliObj = result.SlideInstance;
                        foreach (var starInfo in result.StarInfos)
                        {
                            if (starInfo is null)
                            {
                                continue;
                            }
                            eachNotes.Add(starInfo);
                        }
                        subSlides.Add(result.SlideInstance);
                        if (i == 0)
                        {
                            slideResult = result;
                        }
                    }
                    AddSlideToQueue(timing, sliObj);
                }
                long judgeQueueLen = 0;
                var slideCount = subSlides.Count;
                foreach (var (i, s) in subSlides.WithIndex())
                {
                    var isFirst = i == 0;
                    var isEnd = i == slideCount - 1;
                    var table = SlideTables.FindTableByName(s.SlideType);

                    totalSlideLen += s.SlideLength;
                    if (isEnd)
                    {
                        judgeQueueLen += table!.JudgeQueue.Length;
                    }
                    else
                    {
                        judgeQueueLen += table!.JudgeQueue.Length - 1;
                    }
                }
                foreach (var subSlide in subSlides)
                {
                    //subSlide.ConnectInfo.TotalSlideLen = totalSlideLen;
                    subSlide.ConnectInfo.TotalJudgeQueueLen = judgeQueueLen;
                }
                foreach (var subSlide in subSlides)
                {
                    subSlide.Init();
                }
                if (slideResult is not null)
                {
                    UpdateStarRotateSpeed((CreateSlideResult<SlideDrop>)slideResult, totalLen, totalSlideLen);
                }
            }
            catch (UnityException)
            {
                throw;
            }
            catch (Exception e)
            {
                MajDebug.LogException(e);
                var line = timing.RawTextPositionY;
                var column = timing.RawTextPositionX;
                throw new InvalidSimaiSyntaxException(line,
                                                      column,
                                                      note.RawContent,
                                                      BuildSyntaxErrorMessage(line, column, note.RawContent));
            }
        }
        void UpdateStarRotateSpeed<T>(CreateSlideResult<T> result, float totalLen, float totalSlideLen) where T : SlideBase
        {
            var speed = (totalSlideLen * 0.47f) / (totalLen * 1000);
            var ratio = speed / 0.0034803742562305f;

            foreach (var starInfo in result.StarInfos)
            {
                if (starInfo is not null)
                {
                    starInfo.RotateSpeed = Math.Max(-(68.54838709677419f) * ratio, -1080);
                }
            }
        }
        void AddSlideToQueue<T>(SimaiTimingPoint timing, T SliCompo) where T : SlideBase
        {
            var speed = NoteSpeed * timing.HSpeed;
            var scaleRate = MajEnv.Settings.Debug.NoteAppearRate;
            var slideFadeInTiming = Math.Max((-3.926913f / speed) + MajEnv.Settings.Game.SlideFadeInOffset + (float)timing.Timing, -5f);
            var appearDiff = (-(1 - (scaleRate * 1.225f)) - (4.8f * scaleRate)) / (Math.Abs(speed) * scaleRate);
            var appearTiming = (float)timing.Timing + appearDiff;
            _slideQueueInfos.Add(new()
            {
                Index = _slideIndex++,
                SlideObject = SliCompo,
                AppearTiming = Math.Min(appearTiming, slideFadeInTiming)
            });
        }
        private CreateSlideResult<SlideDrop> CreateSlide(SimaiTimingPoint timing,
                                                         SubSlideNote note,
                                                         ConnSlideInfo info,
                                                         in int multiple,
                                                         ref int? extraRotation)
        {
            string slideShape = NoteCreateHelper.DetectShapeFromText(note.RawContent);
            var isMirror = false;
            var isMine = note.IsMineSlide;
            var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
            if (slideShape.StartsWith("-"))
            {
                isMirror = true;
                slideShape = slideShape.Substring(1);
            }
            var slideIndex = SLIDE_PREFAB_MAP[slideShape];
            var slide = Instantiate(slidePrefab[slideIndex], notes.transform.GetChild(3));
            //var slide_star = Instantiate(star_slidePrefab, notes.transform.GetChild(3));
            var SliCompo = slide.GetComponent<SlideDrop>();
            var isJustR = NoteCreateHelper.DetectJustType(note.RawContent, out int endPos);
            var startPos = note.StartPosition;

            //slide_star.SetActive(true);
            slide.SetActive(true);
            if (extraRotation is null)
            {
                startPos = NoteCreateHelper.Rotation(startPos, ChartRotation);
                endPos = NoteCreateHelper.Rotation(endPos, ChartRotation);
                var oldStartPos = startPos;
                NoteCreateHelper.SetSlideNewPositionIfRequested(ref startPos, ref endPos, _buttonRingMappingTable);
                var diff = oldStartPos - startPos;
                if (diff > 0)
                {
                    diff = 8 - diff;
                }
                else if (diff < 0)
                {
                    diff = Math.Abs(diff);
                }
                extraRotation = diff;
            }
            else if (extraRotation is int eR)
            {
                startPos = NoteCreateHelper.Rotation(startPos, ChartRotation + eR);
                endPos = NoteCreateHelper.Rotation(endPos, ChartRotation + eR);
            }

            TapPoolingInfo?[] starInfos = new TapPoolingInfo?[multiple];
            if (!note.IsSlideNoHead)
            {

                for (var i = 0; i < multiple; i++)
                {
                    var _info = CreateStar(startPos, note, timing);
                    _poolManager.AddTap(_info);
                    starInfos[i] = _info;
                }
            }

            //SliCompo.SlideType = slideShape;

            if (isEach)
            {
                var slides = NoteCreateHelper.GetEachSlides(note, timing.Notes);
                var index = slides.FindIndex(x => x == note.Origin) + 1;
                if (_gpManager is not null && _gpManager.IsClassicMode)
                {
                    if (index == slides.Length && index % 2 != 0)
                    {
                        isEach = false;
                    }
                }
            }

            SliCompo.ConnectInfo = info;
            SliCompo.IsBreak = note.IsSlideBreak;
            SliCompo.IsEach = !isMine && (isEach || multiple > 1);
            SliCompo.IsMirror = isMirror;
            SliCompo.IsMine = isMine;
            SliCompo.IsJustR = isJustR;
            SliCompo.EndPos = endPos;
            SliCompo.Speed = Math.Abs(NoteSpeed * timing.HSpeed);
            SliCompo.StartTiming = (float)note.SlideStartTime;
            SliCompo.StartPos = startPos;
            //SliCompo._stars = new GameObject[] { slide_star };
            SliCompo.Timing = (float)timing.Timing;
            SliCompo.Length = (float)note.SlideTime;
            SliCompo.IsSlideNoHead = _isSlideNoHead;
            SliCompo.IsSlideNoTrack = _isSlideNoTrack;
            SliCompo.Multiple = multiple;
            //SliCompo.sortIndex = -7000 + (int)((lastNoteTime - timing.Timing) * -100) + sort * 5;
            var slideBarCount = slide.transform.childCount - 1;
            if (MajEnv.Settings.Display.SlideSortOrder == JudgeModeOption.Classic)
            {
                _slideLayer += slideBarCount;
                SliCompo.SortOrder = _slideLayer;
            }
            else
            {
                SliCompo.SortOrder = _slideLayer;
                _slideLayer -= slideBarCount;
            }
            //slideLayer += 5;

            return new()
            {
                SlideInstance = SliCompo,
                StarInfos = starInfos
            };
        }
        private CreateSlideResult<WifiDrop> CreateWifi(SimaiTimingPoint timing, SubSlideNote note, in int multiple)
        {
            var str = note.RawContent.Substring(0, 3);
            var digits = str.Split('w');
            var startPos = int.Parse(digits[0]);
            var endPos = int.Parse(digits[1]);
            var isMine = note.IsMineSlide;
            var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
            endPos = endPos - startPos;
            endPos = endPos < 0 ? endPos + 8 : endPos;
            endPos = endPos > 8 ? endPos - 8 : endPos;
            endPos++;

            var slideWifi = Instantiate(slidePrefab[SLIDE_PREFAB_MAP["wifi"]], notes.transform.GetChild(3));
            var WifiCompo = slideWifi.GetComponent<WifiDrop>();
            var isJustR = NoteCreateHelper.DetectJustType(note.RawContent, out endPos);

            startPos = NoteCreateHelper.Rotation(startPos, ChartRotation);
            endPos = NoteCreateHelper.Rotation(endPos, ChartRotation);
            NoteCreateHelper.SetSlideNewPositionIfRequested(ref startPos, ref endPos, _buttonRingMappingTable);
            slideWifi.SetActive(true);

            TapPoolingInfo?[] starInfos = new TapPoolingInfo?[multiple];
            if (!note.IsSlideNoHead)
            {

                for (var i = 0; i < multiple; i++)
                {
                    var _info = CreateStar(startPos, note, timing);
                    _poolManager.AddTap(_info);
                    starInfos[i] = _info;
                }
            }

            if (isEach)
            {
                var slides = NoteCreateHelper.GetEachSlides(note, timing.Notes);
                var index = slides.FindIndex(x => x == note.Origin) + 1;
                if (_gpManager is not null && _gpManager.IsClassicMode)
                {
                    if (index == slides.Length && index % 2 != 0)
                    {
                        isEach = false;
                    }
                }
            }

            WifiCompo.IsBreak = note.IsSlideBreak;
            WifiCompo.IsEach = !isMine && (isEach || multiple > 1);
            WifiCompo.IsMine = isMine;
            WifiCompo.IsJustR = isJustR;
            WifiCompo.EndPos = endPos;
            WifiCompo.Speed = Math.Abs(NoteSpeed * timing.HSpeed);
            WifiCompo.StartTiming = (float)note.SlideStartTime;
            WifiCompo.StartPos = startPos;
            WifiCompo.Timing = (float)timing.Timing;
            WifiCompo.Length = (float)note.SlideTime;
            WifiCompo.IsSlideNoHead = _isSlideNoHead;
            WifiCompo.IsSlideNoTrack = _isSlideNoTrack;
            WifiCompo.Multiple = multiple;
            //var centerStar = Instantiate(star_slidePrefab, notes.transform.GetChild(3));
            //var leftStar = Instantiate(star_slidePrefab, notes.transform.GetChild(3));
            //var rightStar = Instantiate(star_slidePrefab, notes.transform.GetChild(3));
            //WifiCompo._stars = new GameObject[3]
            //{
            //    rightStar,
            //    centerStar,
            //    leftStar
            //};
            var slideBarCount = slideWifi.transform.childCount - 1;
            if (MajEnv.Settings.Display.SlideSortOrder == JudgeModeOption.Classic)
            {
                _slideLayer += slideBarCount;
                WifiCompo.SortOrder = _slideLayer;
            }
            else
            {
                WifiCompo.SortOrder = _slideLayer;
                _slideLayer -= slideBarCount;
            }
            //slideLayer += 5;

            return new()
            {
                SlideInstance = WifiCompo,
                StarInfos = starInfos
            };
        }

        private void CreateExtendSlide(SimaiTimingPoint timing, FoldedSimaiNote note, in IList<NotePoolingInfo?> eachNotes)
        {
            var slideText = note.RawContent.AsSpan();
            var endFlagPos = slideText.IndexOf('K');
            var startPos = 0;
            var endPos = 0;
            var isInvalid = endFlagPos == -1 ||
                            endFlagPos == slideText.Length - 1 ||
                            !int.TryParse(slideText.Slice(0, 1), out startPos) ||
                            !int.TryParse(slideText.Slice(endFlagPos + 1, 1), out endPos);
            if (isInvalid)
            {
                throw new InvalidSimaiSyntaxException(timing.RawTextPositionX, timing.RawTextPositionY, note.RawContent);
            }
            var slideCode = note.RawContent.Substring(0, endFlagPos + 2);
            var multiple = note.Count;
            var isMine = note.IsMineSlide;
            var isEach = NoteCreateHelper.IsEachNote(note, timing.Notes);
            var pathMetadata = SlideCodeParser.Parse(slideCode);
            var slideMetadata = ExtendSlideHelper.CreateSlideEntry(pathMetadata);
            var extendSlideObject = Instantiate(slidePrefab[SLIDE_PREFAB_MAP["Ex"]], notes.transform.GetChild(3));
            var extendSlide = extendSlideObject.GetComponent<ExtendSlideDrop>();
            var starInfos = new TapPoolingInfo?[multiple];

            if (!note.IsSlideNoHead)
            {
                for (var i = 0; i < multiple; i++)
                {
                    var _info = CreateStar(startPos, note, timing);
                    _poolManager.AddTap(_info);
                    starInfos[i] = _info;
                }
            }

            extendSlide.Metadata = slideMetadata;
            extendSlide.StartPos = startPos;
            extendSlide.EndPos = endPos;
            extendSlide.IsBreak = note.IsSlideBreak;
            extendSlide.IsEach = !isMine && (isEach || multiple > 1);
            extendSlide.IsMine = isMine;
            extendSlide.Speed = Math.Abs(NoteSpeed * timing.HSpeed);
            extendSlide.StartTiming = (float)note.SlideStartTime;
            extendSlide.Timing = (float)timing.Timing;
            extendSlide.Length = (float)note.SlideTime;
            extendSlide.IsSlideNoHead = _isSlideNoHead;
            extendSlide.IsSlideNoTrack = _isSlideNoTrack;
            extendSlide.Multiple = multiple;

            var slideBarCount = slideMetadata.ArrowPoses.Length - 2;
            if (MajEnv.Settings.Display.SlideSortOrder == JudgeModeOption.Classic)
            {
                _slideLayer += slideBarCount;
                extendSlide.SortOrder = _slideLayer;
            }
            else
            {
                extendSlide.SortOrder = _slideLayer;
                _slideLayer -= slideBarCount;
            }
            extendSlide.Init();
            foreach (var starInfo in starInfos)
            {
                if (starInfo is null)
                {
                    continue;
                }
                eachNotes.Add(starInfo);
            }
            UpdateStarRotateSpeed(new CreateSlideResult<ExtendSlideDrop>()
            {
                SlideInstance = extendSlide,
                StarInfos = starInfos
            }, extendSlide.Length, slideMetadata.SlideLength);
            AddSlideToQueue(timing, extendSlide);
        }

        string BuildSyntaxErrorMessage(int line, int column, string noteContent)
        {
            return $"(at L{line}:C{column}) \"{noteContent}\" is not a valid note syntax";
        }
        string BuildSyntaxErrorMessage(int line, int column)
        {
            return $"(at L{line}:C{column})";
        }
        static class NoteCreateHelper
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static SensorArea Rotation(SensorArea sensorIndex, int diff)
            {
                return sensorIndex.Diff(diff);
            }
            public static bool IsEachNote(SimaiNote origin, ReadOnlySpan<SimaiNote> notes)
            {
                return IsEachNote(origin, origin.Type, notes);
            }
            public static bool IsEachNote(SimaiNote origin, SimaiNoteType type, ReadOnlySpan<SimaiNote> notes)
            {
                switch (type)
                {
                    case SimaiNoteType.Tap:
                    case SimaiNoteType.Hold:
                    case SimaiNoteType.Touch:
                    case SimaiNoteType.TouchHold:
                        {
                            if (origin.IsMine)
                            {
                                return false;
                            }
                            var noteCount = notes.Count(x =>
                            {
                                var isMineNoteOrMineStar = x.IsMine;
                                var isNoHeadSlide = x.IsSlideNoHead;

                                return !(isMineNoteOrMineStar || isNoHeadSlide);
                            });
                            return noteCount > 1;
                        }
                    case SimaiNoteType.Slide:
                        {
                            if (origin.IsMineSlide)
                            {
                                return false;
                            }
                            var noteCount = GetEachSlideCount(origin, notes);
                            return noteCount > 1;
                        }
                }
                return false;
            }
            public static bool IsStarDouble(SimaiNote origin, ReadOnlySpan<SimaiNote> notes)
            {
                var slideCount = notes.Count(x =>
                {
                    var isSlide = x.Type == SimaiNoteType.Slide;
                    var isMineSlide = x.IsMineSlide;
                    var isSameHead = x.StartPosition == origin.StartPosition;

                    return isSlide && !isMineSlide && isSameHead;
                });

                return slideCount > 1;
            }
            public static int GetEachSlideCount(SimaiNote origin, ReadOnlySpan<SimaiNote> notes)
            {
                var noteCount = notes.Count(x =>
                {
                    var isSlide = x.Type == SimaiNoteType.Slide;
                    var isMineSlide = x.IsMineSlide;

                    return isSlide && !isMineSlide;
                });

                return noteCount;
            }
            public static SimaiNote[] GetEachSlides(SimaiNote origin, IEnumerable<SimaiNote> notes)
            {
                return notes.FindAll(x =>
                {
                    var isSlide = x.Type == SimaiNoteType.Slide;
                    var isMineSlide = x.IsMineSlide;

                    return isSlide && !isMineSlide;
                });
            }
            public static int Rotation(int keyIndex, int diff)
            {
                if (!keyIndex.InRange(1, 8))
                    throw new ArgumentOutOfRangeException();
                var key = (SensorArea)(keyIndex - 1);
                var newKey = key.Diff(diff);
                return newKey.GetIndex();
            }
            public static int MirrorKeys(int key)
            {
                switch (key)
                {
                    case 1:
                        return 1;
                    case 2:
                        return 8;
                    case 3:
                        return 7;
                    case 4:
                        return 6;
                    case 5:
                        return 5;
                    case 6:
                        return 4;
                    case 7:
                        return 3;
                    case 8:
                        return 2;
                    default:
                        throw new Exception("Keys out of range: " + key);
                }
            }
            public static bool IsRightHalf(int key)
            {
                switch (key)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                        return true;
                    default:
                        return false;

                }
            }
            public static bool IsUpperHalf(int key)
            {
                switch (key)
                {
                    case 7:
                    case 8:
                    case 1:
                    case 2:
                        return true;
                    default:
                        return false;

                }
            }
            public static string DetectShapeFromText(string content)
            {
                int getRelativeEndPos(int startPos, int endPos)
                {
                    endPos = endPos - startPos;
                    endPos = endPos < 0 ? endPos + 8 : endPos;
                    endPos = endPos > 8 ? endPos - 8 : endPos;
                    return endPos + 1;
                }

                //print(content);
                if (content.Contains('-'))
                {
                    // line
                    var str = content.Substring(0, 3); //something like "8-6"
                    var digits = str.Split('-');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (endPos < 3 || endPos > 7) throw new Exception("-星星至少隔开一键\n-スライドエラー");
                    return "line" + endPos;
                }

                if (content.Contains('>'))
                {
                    // circle 默认顺时针
                    var str = content.Substring(0, 3);
                    var digits = str.Split('>');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (NoteCreateHelper.IsUpperHalf(startPos))
                    {
                        return "circle" + endPos;
                    }

                    endPos = NoteCreateHelper.MirrorKeys(endPos);
                    return "-circle" + endPos; //Mirror
                }

                if (content.Contains('<'))
                {
                    // circle 默认顺时针
                    var str = content.Substring(0, 3);
                    var digits = str.Split('<');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (!NoteCreateHelper.IsUpperHalf(startPos))
                    {
                        return "circle" + endPos;
                    }

                    endPos = NoteCreateHelper.MirrorKeys(endPos);
                    return "-circle" + endPos; //Mirror
                }

                if (content.Contains('^'))
                {
                    var str = content.Substring(0, 3);
                    var digits = str.Split('^');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);

                    if (endPos == 1 || endPos == 5)
                    {
                        throw new Exception("^星星不合法\n^スライドエラー");
                    }

                    if (endPos < 5)
                    {
                        return "circle" + endPos;
                    }
                    if (endPos > 5)
                    {
                        return "-circle" + NoteCreateHelper.MirrorKeys(endPos);
                    }
                }

                if (content.Contains('v'))
                {
                    // v
                    var str = content.Substring(0, 3);
                    var digits = str.Split('v');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (endPos == 5) throw new Exception("v星星不合法\nvスライドエラー");
                    return "v" + endPos;
                }

                if (content.Contains("pp"))
                {
                    // ppqq 默认为pp
                    var str = content.Substring(0, 4);
                    var digits = str.Split('p');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[2]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    return "ppqq" + endPos;
                }

                if (content.Contains("qq"))
                {
                    // ppqq 默认为pp
                    var str = content.Substring(0, 4);
                    var digits = str.Split('q');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[2]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    endPos = NoteCreateHelper.MirrorKeys(endPos);
                    return "-ppqq" + endPos;
                }

                if (content.Contains('p'))
                {
                    // pq 默认为p
                    var str = content.Substring(0, 3);
                    var digits = str.Split('p');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    return "pq" + endPos;
                }

                if (content.Contains('q'))
                {
                    // pq 默认为p
                    var str = content.Substring(0, 3);
                    var digits = str.Split('q');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    endPos = NoteCreateHelper.MirrorKeys(endPos);
                    return "-pq" + endPos;
                }

                if (content.Contains('s'))
                {
                    // s
                    var str = content.Substring(0, 3);
                    var digits = str.Split('s');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (endPos != 5) throw new Exception("s星星尾部错误\nsスライドエラー");
                    return "s";
                }

                if (content.Contains('z'))
                {
                    // s镜像
                    var str = content.Substring(0, 3);
                    var digits = str.Split('z');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (endPos != 5) throw new Exception("z星星尾部错误\nzスライドエラー");
                    return "-s";
                }

                if (content.Contains('V'))
                {
                    // L
                    var str = content.Substring(0, 4);
                    var digits = str.Split('V');
                    var startPos = int.Parse(digits[0]);
                    var turnPos = int.Parse(digits[1][0].ToString());
                    var endPos = int.Parse(digits[1][1].ToString());

                    turnPos = getRelativeEndPos(startPos, turnPos);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (turnPos == 7)
                    {
                        if (endPos < 2 || endPos > 5) throw new Exception("V星星终点不合法\nVスライドエラー");
                        return "L" + endPos;
                    }

                    if (turnPos == 3)
                    {
                        if (endPos < 5) throw new Exception("V星星终点不合法\nVスライドエラー");
                        return "-L" + NoteCreateHelper.MirrorKeys(endPos);
                    }

                    throw new Exception("V星星拐点只能隔开一键\nVスライドエラー");
                }

                if (content.Contains('w'))
                {
                    // wifi
                    var str = content.Substring(0, 3);
                    var digits = str.Split('w');
                    var startPos = int.Parse(digits[0]);
                    var endPos = int.Parse(digits[1]);
                    endPos = getRelativeEndPos(startPos, endPos);
                    if (endPos != 5) throw new Exception("w星星尾部错误\nwスライドエラー");
                    return "wifi";
                }

                return "";
            }
            /// <summary>
            /// 判断Slide SlideOK是否需要镜像翻转
            /// </summary>
            /// <param name="content"></param>
            /// <param name="endPos"></param>
            /// <returns></returns>
            public static bool DetectJustType(string content, out int endPos)
            {
                // > < ^ V w
                if (content.Contains('>'))
                {
                    var str = content.Substring(0, 3);
                    var digits = str.Split('>');
                    var startPos = int.Parse(digits[0]);
                    endPos = int.Parse(digits[1]);

                    if (NoteCreateHelper.IsUpperHalf(startPos))
                        return true;
                    return false;
                }

                if (content.Contains('<'))
                {
                    var str = content.Substring(0, 3);
                    var digits = str.Split('<');
                    var startPos = int.Parse(digits[0]);
                    endPos = int.Parse(digits[1]);

                    if (!NoteCreateHelper.IsUpperHalf(startPos))
                        return true;
                    return false;
                }

                if (content.Contains('^'))
                {
                    var str = content.Substring(0, 3);
                    var digits = str.Split('^');
                    var startPos = int.Parse(digits[0]);
                    endPos = int.Parse(digits[1]);
                    endPos = endPos - startPos;
                    endPos = endPos < 0 ? endPos + 8 : endPos;
                    endPos = endPos > 8 ? endPos - 8 : endPos;

                    if (endPos < 4)
                    {
                        endPos = int.Parse(digits[1]);
                        return true;
                    }
                    if (endPos > 4)
                    {
                        endPos = int.Parse(digits[1]);
                        return false;
                    }
                }
                else if (content.Contains('V'))
                {
                    var str = content.Substring(0, 4);
                    var digits = str.Split('V');
                    endPos = int.Parse(digits[1][1].ToString());

                    if (NoteCreateHelper.IsRightHalf(endPos))
                        return true;
                    return false;
                }
                else if (content.Contains('w'))
                {
                    var str = content.Substring(0, 3);
                    endPos = int.Parse(str.Substring(2, 1));
                    if (NoteCreateHelper.IsUpperHalf(endPos))
                        return true;
                    return false;
                }
                else
                {
                    //int endPos;
                    if (content.Contains("qq") || content.Contains("pp"))
                        endPos = int.Parse(content.Substring(3, 1));
                    else
                        endPos = int.Parse(content.Substring(2, 1));
                    if (NoteCreateHelper.IsRightHalf(endPos))
                        return true;
                    return false;
                }
                return true;
            }
            public static (IReadOnlyDictionary<int, int>, IReadOnlyDictionary<SensorArea, SensorArea>) GenerateMappingTable()
            {
                var touchPannelMappingTable = GenerateTouchPanelMappingTable();
                var buttonRingMappingTable = GenerateButtonRingMappingTable();
                foreach (var (k, v) in buttonRingMappingTable)
                {
                    touchPannelMappingTable[(SensorArea)(k - 1)] = (SensorArea)(v - 1);
                }
                return (buttonRingMappingTable, touchPannelMappingTable);
            }
            static Dictionary<SensorArea, SensorArea> GenerateTouchPanelMappingTable()
            {
                var areas = ((SensorArea[])Enum.GetValues(typeof(SensorArea))).ToArray();
                var newAreas = new SensorArea?[33];
                var rd = new System.Random();
                var dict = new Dictionary<SensorArea, SensorArea>();

                for (var i = 0; i < 33; i++)
                {
                    var originArea = (SensorArea)i;
                    SensorArea value;
                    if (i < 8)
                    {
                        newAreas[i] = originArea;
                        continue;
                    }
                    while (true)
                    {
                        value = (SensorArea)rd.Next(0, 33);
                        if (value > SensorArea.E8 || value < SensorArea.A1)
                            continue;
                        else if (value.GetGroup() != originArea.GetGroup())
                            continue;
                        else if (!newAreas.Contains(value))
                            break;
                    }
                    newAreas[i] = value;
                }

                for (var i = 0; i < 33; i++)
                {
                    dict.Add(areas[i], (SensorArea)newAreas[i]!);
                }
                return dict;
            }
            static Dictionary<int, int> GenerateButtonRingMappingTable()
            {
                var areas = new int[8]
                {
                    1,2,3,4,5,6,7,8
                };
                var newAreas = new int?[8];
                var rd = new System.Random();
                var dict = new Dictionary<int, int>();

                for (var i = 0; i < 8; i++)
                {
                    int value;
                    do
                    {
                        value = rd.Next(1, 9);
                        if (value > 8 || value < 1)
                            continue;
                    }
                    while (newAreas.Contains(value));
                    newAreas[i] = value;
                }

                for (var i = 0; i < 8; i++)
                {
                    dict.Add(areas[i], (int)newAreas[i]!);
                }
                return dict;
            }
            static int RandomTap(int originKeyIndex, IReadOnlyDictionary<int, int> mappingTable)
            {
                return mappingTable[originKeyIndex];
            }
            static SensorArea RandomTouch(SensorArea originArea, IReadOnlyDictionary<SensorArea, SensorArea> mappingTable)
            {
                return mappingTable[originArea];
            }
            static (int, int) RandomSlide(int startPos, int endPos, IReadOnlyDictionary<int, int> mappingTable)
            {
                var diff = startPos - endPos;
                if (diff > 0)
                {
                    diff = 8 - diff;
                }
                else if (diff < 0)
                {
                    diff = Math.Abs(diff);
                }
                var newStartPos = mappingTable[startPos];
                var newEndPos = ((SensorArea)(newStartPos - 1)).Diff(diff).GetIndex();

                return (newStartPos, newEndPos);
            }
            static int RandomTap()
            {
                var rd = new System.Random();
                return rd.Next(1, 9);
            }
            static SensorArea RandomTouch()
            {
                var rd = new System.Random();
                return (SensorArea)rd.Next(0, 33);
            }
            static (int, int) RandomSlide(int startPos, int endPos)
            {
                var diff = startPos - endPos;
                if (diff > 0)
                {
                    diff = 8 - diff;
                }
                else if (diff < 0)
                {
                    diff = Math.Abs(diff);
                }
                var rd = new System.Random();
                var newStartPos = rd.Next(1, 9);
                var newEndPos = ((SensorArea)(newStartPos - 1)).Diff(diff).GetIndex();

                return (newStartPos, newEndPos);
            }
            public static void SetNewPositionIfRequested(ref int originPos,
                                                         IReadOnlyDictionary<int, int> mappingTable)
            {
                switch (MajEnv.Settings.Game.Random)
                {
                    case RandomModeOption.Disabled:
                        return;
                    case RandomModeOption.RANDOM:
                    case RandomModeOption.S_RANDOM:
                        originPos = RandomTap(originPos, mappingTable);
                        break;
                }
            }
            public static void SetNewPositionIfRequested(ref SensorArea originPos,
                                                         IReadOnlyDictionary<SensorArea, SensorArea> mappingTable)
            {
                switch (MajEnv.Settings.Game.Random)
                {
                    case RandomModeOption.Disabled:
                        return;
                    case RandomModeOption.RANDOM:
                    case RandomModeOption.S_RANDOM:
                        originPos = RandomTouch(originPos, mappingTable);
                        break;
                }
            }
            public static void SetSlideNewPositionIfRequested(ref int originStartPos,
                                                              ref int originEndPos,
                                                              IReadOnlyDictionary<int, int> mappingTable)
            {
                switch (MajEnv.Settings.Game.Random)
                {
                    case RandomModeOption.Disabled:
                        return;
                    case RandomModeOption.RANDOM:
                    case RandomModeOption.S_RANDOM:
                        (originStartPos, originEndPos) = RandomSlide(originStartPos, originEndPos, mappingTable);
                        break;
                }
            }
            public static SimaiNote[] NoteFolding(SimaiNote[] simaiNotes)
            {
                if (!USERSETTING_NOTE_FOLDING)
                {
                    return simaiNotes;
                }
                var buffer = Pool<FoldingSimaiNote>.RentArray(4);
                var buffer2 = Pool<FoldingSimaiNote>.RentArray(4);
                var buffer3 = Pool<FoldedSimaiNote>.RentArray(4);
                try
                {
                    Array.Clear(buffer, 0, buffer.Length);
                    Array.Clear(buffer2, 0, buffer2.Length);
                    Array.Clear(buffer3, 0, buffer3.Length);
                    var bufferIndex = 0;
                    var buffer2Index = 0;
                    var buffer3Index = 0;

                    foreach (var note in simaiNotes)
                    {
                        var foldingNote = new FoldingSimaiNote(note);
                        if (foldingNote.Type == SimaiNoteType.Slide)
                        {
                            BufferHelper.EnsureBufferLength(bufferIndex + 1, ref buffer);
                            buffer[bufferIndex++] = foldingNote;
                            continue;
                        }
                        else
                        {
                            BufferHelper.EnsureBufferLength(buffer2Index + 1, ref buffer2);
                            buffer2[buffer2Index++] = foldingNote;
                            continue;
                        }
                    }
                    var groupedSlides = buffer.GroupBy(x => x);
                    foreach (var slides in groupedSlides)
                    {
                        var key = slides.Key;
                        if (key.Origin == null)
                        {
                            continue;
                        }
                        BufferHelper.EnsureBufferLength(buffer3Index + 1, ref buffer3);

                        buffer3[buffer3Index++] = new()
                        {
                            Type = key.Type,
                            StartPosition = key.StartPosition,
                            HoldTime = key.HoldTime,
                            IsBreak = key.IsBreak,
                            IsEx = key.IsEx,
                            IsFakeRotate = key.IsFakeRotate,
                            IsForceStar = key.IsForceStar,
                            IsHanabi = key.IsHanabi,
                            IsSlideBreak = key.IsSlideBreak,
                            IsSlideNoHead = key.IsSlideNoHead,
                            IsMine = key.IsMine,
                            IsMineSlide = key.IsMineSlide,
                            RawContent = key.RawContent,
                            SlideStartTime = key.SlideStartTime,
                            SlideTime = key.SlideTime,
                            TouchArea = key.TouchArea,
                            Count = slides.Count()
                        };
                    }
                    var result = new SimaiNote[buffer2Index + buffer3Index];
                    var resultIndex = 0;
                    foreach (var note in buffer2.AsSpan(0, buffer2Index))
                    {
                        result[resultIndex++] = note.Origin!;
                    }
                    foreach (var note in buffer3.AsSpan(0, buffer3Index))
                    {
                        result[resultIndex++] = note;
                    }

                    return result;
                }
                finally
                {
                    Pool<FoldingSimaiNote>.ReturnArray(buffer);
                    Pool<FoldingSimaiNote>.ReturnArray(buffer2);
                    Pool<FoldedSimaiNote>.ReturnArray(buffer3);
                }
            }
        }

        readonly struct FoldingSimaiNote
        {
            public SimaiNoteType Type
            {
                get => _origin.Type;
            }
            public int StartPosition
            {
                get => _origin.StartPosition;
            }
            public double HoldTime
            {
                get => _origin.HoldTime;
            }
            public bool IsBreak
            {
                get => _origin.IsBreak;
            }
            public bool IsEx
            {
                get => _origin.IsEx;
            }
            public bool IsFakeRotate
            {
                get => _origin.IsFakeRotate;
            }
            public bool IsForceStar
            {
                get => _origin.IsForceStar;
            }
            public bool IsHanabi
            {
                get => _origin.IsHanabi;
            }
            public bool IsSlideBreak
            {
                get => _origin.IsSlideBreak;
            }
            public bool IsSlideNoHead
            {
                get => _origin.IsSlideNoHead;
            }
            public bool IsMine
            {
                get => _origin.IsMine;
            }
            public bool IsMineSlide
            {
                get => _origin.IsMineSlide;
            }
            public string RawContent
            {
                get => _origin.RawContent;
            }
            public double SlideStartTime
            {
                get => _origin.SlideStartTime;
            }
            public double SlideTime
            {
                get => _origin.SlideTime;
            }
            public char TouchArea
            {
                get => _origin.TouchArea;
            }
            public SimaiNote? Origin
            {
                get => _origin;
            }

            readonly SimaiNote? _origin;
            readonly int _hashCode;
            public FoldingSimaiNote(SimaiNote origin, bool? isSlideNoHead = null)
            {
                _origin = origin;
                var hash1 = HashCode.Combine(
                    _origin.Type,
                    _origin.StartPosition,
                    _origin.RawContent,
                    _origin.HoldTime,
                    _origin.SlideStartTime,
                    _origin.SlideTime,
                    _origin.IsBreak,
                    _origin.IsEx
                );
                var hash2 = HashCode.Combine(
                    hash1,
                    _origin.IsHanabi,
                    _origin.IsSlideBreak,
                    isSlideNoHead ?? _origin.IsSlideNoHead,
                    _origin.IsFakeRotate,
                    _origin.IsForceStar,
                    _origin.TouchArea
                );
                _hashCode = hash2;
            }
            public static implicit operator FoldingSimaiNote(SimaiNote origin)
            {
                return new(origin);
            }
            public static bool operator ==(FoldingSimaiNote left, FoldingSimaiNote right)
            {
                //return left.Type == right.Type &&
                //       left.StartPosition == right.StartPosition &&
                //       left.RawContent == right.RawContent &&
                //       left.HoldTime == right.HoldTime &&
                //       left.SlideStartTime == right.SlideStartTime &&
                //       left.SlideTime == left.SlideTime &&
                //       left.IsBreak == left.IsBreak &&
                //       left.IsEx == right.IsEx &&
                //       left.IsHanabi == right.IsHanabi &&
                //       left.IsSlideBreak == left.IsSlideBreak &&
                //       left.IsSlideNoHead == right.IsSlideNoHead &&
                //       left.IsFakeRotate == right.IsFakeRotate &&
                //       left.IsForceStar == right.IsForceStar &&
                //       left.TouchArea == right.TouchArea;
                return left._hashCode == right._hashCode;
            }
            public static bool operator !=(FoldingSimaiNote left, FoldingSimaiNote right)
            {
                return !(left == right);
            }
            public override bool Equals(object obj)
            {
                if (obj is not FoldingSimaiNote obj2)
                {
                    return false;
                }
                return this == obj2;
            }
            public override int GetHashCode()
            {
                return _hashCode;
            }
        }
        class FoldedSimaiNote : SimaiNote
        {
            public int Count { get; init; }
        }
        class SubSlideNote : SimaiNote
        {
            public SimaiNote Origin { get; set; } = new();
        }
        readonly struct CreateSlideResult<T> where T : SlideBase
        {
            public T SlideInstance { get; init; }
            public TapPoolingInfo?[] StarInfos { get; init; }
        }
    }
}
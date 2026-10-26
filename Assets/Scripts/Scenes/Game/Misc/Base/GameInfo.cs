using MajdataPlay.Extensions;
using MajdataPlay.Scenes.Game.Notes;
using MajdataPlay.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MajdataPlay.Settings;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    internal class GameInfo
    {
        public GameMode Mode { get; init; }
        public ISongDetail? Current { get; private set; } = null;
        public ChartLevel CurrentLevel { get; private set; } = ChartLevel.Easy;
        public ChartSetting ChartSettings { get; private set; }
        public bool IsDanMode => Mode == GameMode.Dan;
        public bool IsDanLifeEnabled => IsDanMode && DanInfo?.StartHP >= 0;
        public bool IsPracticeMode => Mode == GameMode.Practice;

        public ISongDetail[] Charts => _chartQueue;
        public ChartLevel[] Levels => _levels;

        // Dan Mode
        public int CurrentHP { get; set; } = 0;
        public int MaxHP { get; set; } = 0;
        public int HPRecover { get; set; } = 0;
        public bool IsForceGameover => DanInfo?.IsForceGameover ?? false;
        public DanInfo? DanInfo { get; init; } = null;
        public int PracticeCount
        {
            get => _practiceCount;
            set
            {
                if (Mode == GameMode.Practice)
                {
                    if (_index != 0)
                        throw new InvalidOperationException();
                    else if (value <= 0)
                        throw new ArgumentException();

                    Results = new GameResult[value];
                }
                _practiceCount = value;
            }
        }
        public Range<long>? ComboRange { get; set; }
        public Range<double>? TimeRange { get; set; }
        public GameResult[] Results { get; private set; }

        int _practiceCount = 1;
        int _index = 0;
        // Results has one slot per played round, while _index is the position in the chart queue, which also
        // counts skipped (null) charts.
        int _resultIndex = 0;
        ISongDetail[] _chartQueue;
        ChartLevel[] _levels;
        public GameInfo(GameMode mode, ISongDetail[] chartQueue, ChartLevel[] levels, int practiceCount)
        {
            Mode = mode;
            if (chartQueue is null)
            {
                _chartQueue = Array.Empty<ISongDetail>();
                Results = Array.Empty<GameResult>();
                _levels = Array.Empty<ChartLevel>();
                ChartSettings = new ChartSetting();
            }
            else
            {
                var count = 0;
                for (var i = 0; i < chartQueue.Length; i++)
                {
                    if (chartQueue[i] is not null)
                    {
                        count++;
                    }
                }
                switch (mode)
                {
                    case GameMode.Practice:
                        _levels = new ChartLevel[1]
                        {
                            levels[0]
                        };
                        _chartQueue = new ISongDetail[1]
                        {
                            chartQueue[0]
                        };
                        Results = new GameResult[practiceCount];
                        PracticeCount = practiceCount;
                        Current = chartQueue[0];
                        CurrentLevel = levels[0];
                        break;
                    default:
                        _levels = levels;
                        Results = new GameResult[count];
                        _chartQueue = chartQueue;
                        Current = chartQueue[0];
                        CurrentLevel = levels[0];
                        break;
                }
                ChartSettings = ChartSettingStorage.GetSetting(Current);
            }
        }
        public GameInfo(GameMode mode, ISongDetail[] chartQueue, ChartLevel[] levels) : this(mode, chartQueue, levels, 1)
        {

        }
        public void OnNoteJudged(in JudgeGrade grade,int multiple = 1)
        {
            if (!IsDanLifeEnabled || DanInfo is null)
            {
                return;
            }
            var damage = DanInfo.Damages[grade];
            CurrentHP += damage * multiple;
            CurrentHP = CurrentHP.Clamp(0, MaxHP);
        }
        public void RecordResult(in GameResult result)
        {
            Results[_resultIndex] = result;
        }
        public GameResult GetLastResult()
        {
            if (Results.Length == 0)
                return default;
            var index = _resultIndex.Clamp(0, Results.Length - 1);
            return Results[index];
        }
        public bool NextRound()
        {
            var canMoveNext = MoveNext();
            if (!canMoveNext)
            {
                return false;
            }
            ChartSettings = ChartSettingStorage.GetSetting(Current);
            switch (Mode)
            {
                case GameMode.Dan:
                    if (IsDanLifeEnabled)
                    {
                        CurrentHP += HPRecover;
                        CurrentHP = CurrentHP.Clamp(0, MaxHP);
                    }
                    break;
            }
            return true;
        }
        bool MoveNext()
        {
            switch (Mode)
            {
                case GameMode.Practice:
                    if (_index >= PracticeCount - 1)
                    {
                        return false;
                    }
                    _index++;
                    _resultIndex++;
                    return true;
                default:
                    if (_index >= _chartQueue.Length)
                    {
                        return false;
                    }

                    _index++;
                    for (; _index < _chartQueue.Length; _index++)
                    {

                        if (_chartQueue[_index] is null)
                        {
                            continue;
                        }
                        else
                        {
                            Current = _chartQueue[_index];
                            CurrentLevel = _levels[_index];
                            _resultIndex++;
                            return true;
                        }
                    }
                    break;
            }

            return false;
        }
    }
}

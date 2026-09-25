using ExCSS;
using MajdataPlay.Diagnostics;
using MajdataPlay.Utils;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.List
{
    public class ChartMetadataDisplayer : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("onlineIdText")]
        TextMeshProUGUI _onlineIdDisplayer;

        [SerializeField]
        [FormerlySerializedAs("charter")]
        TextMeshProUGUI _charter;

        [SerializeField]
        [FormerlySerializedAs("title")]
        TextMeshProUGUI _title;

        [SerializeField]
        [FormerlySerializedAs("artist")]
        TextMeshProUGUI _artist;

        [SerializeField]
        [FormerlySerializedAs("bpmDisplayer")]
        TextMeshProUGUI _bpmDisplayer;

        [SerializeField]
        [FormerlySerializedAs("durationDisplayer")]
        TextMeshProUGUI _durationDisplayer;

        float _loadTimer = 0f;
        float _loadDelayTimer = 0f;

        ISongDetail? _currentSongDetail;
        ChartLevel _currentLevel;
        Task<SimaiFile>? _maidataLoadTask;
        CancellationToken _cancellationToken = default;

        const float LOAD_DEBOUNCE_INTERVAL_SEC = 0.4f;

        void LateUpdate()
        {
            if(_currentSongDetail is null)
            {
                return;
            }
            else if (_loadTimer < LOAD_DEBOUNCE_INTERVAL_SEC)
            {
                _loadTimer += MajTimeline.DeltaTime;
                _loadDelayTimer -= MajTimeline.DeltaTime;
                return;
            }
            else if(_loadDelayTimer > 0)
            {
                _loadDelayTimer -= MajTimeline.DeltaTime;
                return;
            }
            else if (_maidataLoadTask is null)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _currentSongDetail = null;
                    _loadTimer = 0f;
                    return;
                }
                _maidataLoadTask = _currentSongDetail.GetMaidataAsync(true, token: _cancellationToken).AsTask();
                ListManager.AllBackgroundTasks.Add(_maidataLoadTask);
                return;
            }
            else if (!_maidataLoadTask.IsCompleted)
            {
                return;
            }
            try
            {
                if (_maidataLoadTask.IsCompletedSuccessfully)
                {
                    var simaiFile = _maidataLoadTask.Result;
                    var simaiChart = simaiFile.Charts[(int)_currentLevel];
                    var analyzeResult = ChartAnalyzer.AnalyzeMaidata(simaiChart);
                    if (analyzeResult.Length == TimeSpan.Zero)
                    {
                        return;
                    }
                    if (analyzeResult.MaxBPM != analyzeResult.MinBPM)
                    {
                        _bpmDisplayer.text = $"{analyzeResult.MinBPM}-{analyzeResult.MaxBPM}";
                    }
                    else
                    {
                        _bpmDisplayer.text = $"{analyzeResult.MaxBPM}";
                    }
                    _durationDisplayer.text = $"{(int)analyzeResult.Length.TotalMinutes}:{analyzeResult.Length.Seconds:00}";
                }
                else
                {
                    MajDebug.LogException(_maidataLoadTask.Exception);
                }
            }
            finally
            {
                _currentSongDetail = null;
                _maidataLoadTask = null;
                _loadTimer = 0f;
            }
        }

        public void SetMetadataFromSongDetail(ISongDetail songDetail, ChartLevel level, int loadDelayMS = 0, CancellationToken token = default)
        {
            if (songDetail is OnlineSongDetail onlineSongDetail)
            {
                _onlineIdDisplayer.text = $"ID: {onlineSongDetail.Id}";
            }
            else
            {
                _onlineIdDisplayer.text = string.Empty;
            }
            _currentSongDetail = songDetail;
            _currentLevel = level;
            _title.text = songDetail.Title;
            _artist.text = songDetail.Artist;
            _charter.text = songDetail.Designers[(int)level];

            _bpmDisplayer.text = "--";
            _durationDisplayer.text = "--:--";

            _cancellationToken = token;
            _loadDelayTimer = loadDelayMS / 1000f;
        }
    }
}

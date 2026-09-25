using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.List;
using MajdataPlay.Utils;
using MajdataPlay.Utils.ChartRadar;
using MajSimai;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay
{
    public class MajRadarDisplayer : MonoBehaviour
    {
        public RawImage MajRadarRawImage;
        private Material _radarMaterial;
        public TextMeshProUGUI EstiText;
        public Color[] RadarFillColors;
        public Color[] RadarOutlineColors;
        public void Start()
        {
            _radarMaterial = MajRadarRawImage.material;
        }
        public async UniTask Analyze(SimaiChart simaiChart, CancellationToken cts)
        {
            ChartRadarSnapshot result = await ChartRadarService.AnalyzeAsync(
                                            simaiChart,
                                            cts);
            List<float> scores = new();
            if (result.DimensionOrder.Count != 7)
            {
                MajDebug.LogError($"Unexpected dimension count: {result.DimensionOrder.Count}");
                return;
            }
            if (result.IsSuccess)
            {
                foreach (var dimension in result.DimensionOrder)
                {
                    scores.Add((float)(result.Scores[dimension] ?? 0f));
                    MajDebug.LogInfo(dimension + ": " + (result.Scores[dimension] ?? 0f));
                }
                SetRadar(scores, (float)(result.FittedConstant ?? 0f));
            }
            else if (!result.IsCancelled)
            {
                MajDebug.LogError(result.Errors);
            }
        }

        public void SetRadar(List<float> scores, float esti)
        {
            if (_radarMaterial == null)
            {
                Debug.LogError("Radar material is not initialized.");
                return;
            }
            _radarMaterial.SetFloat("_V0", scores[0]);
            _radarMaterial.SetFloat("_V1", scores[1]);
            _radarMaterial.SetFloat("_V2", scores[2]);
            _radarMaterial.SetFloat("_V3", scores[3]);
            _radarMaterial.SetFloat("_V4", scores[4]);
            _radarMaterial.SetFloat("_V5", scores[5]);
            EstiText.text = String.Format("{0:F2}", esti);
        }

        float _loadTimer = 0f;
        float _loadDelayTimer = 0f;

        ISongDetail? _currentSongDetail;
        ChartLevel _currentLevel;
        Task<SimaiFile>? _maidataLoadTask;
        CancellationToken _cancellationToken = default;

        const float LOAD_DEBOUNCE_INTERVAL_SEC = 0.4f;

        void LateUpdate()
        {
            if (_currentSongDetail is null)
            {
                return;
            }
            else if (_loadTimer < LOAD_DEBOUNCE_INTERVAL_SEC)
            {
                _loadTimer += MajTimeline.DeltaTime;
                _loadDelayTimer -= MajTimeline.DeltaTime;
                return;
            }
            else if (_loadDelayTimer > 0)
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
                    //TODO AllBackgroundTasks?
                    Analyze(simaiChart, _cancellationToken).Forget();
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

        public void SetRadarFromSongDetail(ISongDetail songDetail, ChartLevel level, int loadDelayMS = 0, CancellationToken token = default)
        {
            _currentSongDetail = songDetail;
            _currentLevel = level;

            EstiText.text = "";
            _cancellationToken = token;
            _loadDelayTimer = loadDelayMS / 1000f;
        }
    }
}

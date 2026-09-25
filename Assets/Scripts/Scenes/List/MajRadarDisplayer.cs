using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.List;
using MajdataPlay.Utils.ChartRadar;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LitMotion;

#nullable enable
namespace MajdataPlay
{
    public class MajRadarDisplayer : MonoBehaviour
    {
        public RawImage MajRadarRawImage;
        private Material _radarMaterial;
        public TextMeshProUGUI EstiText;
        public Color[] RadarFillColors = new Color[6];
        public Color[] RadarOutlineColors = new Color[6];
        private MotionHandle[] _scoreHandles = new MotionHandle[6];
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
                MajDebug.LogError($"[MajRadar] Unexpected dimension count: {result.DimensionOrder.Count}");
                return;
            }
            if (result.IsSuccess)
            {
                foreach (var dimension in result.DimensionOrder)
                {
                    scores.Add((float)(result.Scores[dimension] / 250f ?? 0f));
                    //MajDebug.LogInfo(dimension + ": " + (result.Scores[dimension] ?? 0f));
                }
                SetRadar(scores, (float)(result.FittedConstant ?? 0f));
            }
            else if (!result.IsCancelled)
            {
                MajDebug.LogError($"[MajRadar] Error while processing radar values{result.Errors}");
            }
        }

        public void SetRadar(List<float> scores, float esti)
        {
            if (_radarMaterial == null)
            {
                Debug.LogError("[MajRadar] Radar material is not initialized.");
                return;
            }

            var scoreZero = new List<float> { 0, 0, 0, 0, 0, 0, 0 };

            // 雷达图数值动画
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                _scoreHandles[index].TryCancel();
                _scoreHandles[index] = LMotion.Create(scoreZero[index], scores[index], 0.5f)
                .WithEase(Ease.OutCubic)
                .Bind(value =>
                {
                    scoreZero[index] = value;
                    _radarMaterial.SetFloat($"_V{index}", value);
                });
            }

            // 颜色切换动画
            var maxIndex = scores.IndexOf(scores.Max());

            Color startFill = _radarMaterial.GetColor("_FillColor");
            Color targetFill = RadarFillColors[maxIndex];

            LMotion.Create(startFill, targetFill, 0.5f)
            .Bind(color =>
            {
                _radarMaterial.SetColor("_FillColor", color);
            });

            Color startOutline = _radarMaterial.GetColor("_OutlineColor");
            Color targetOutline = RadarOutlineColors[maxIndex];

            LMotion.Create(startOutline, targetOutline, 0.5f)
            .Bind(color =>
            {
                _radarMaterial.SetColor("_OutlineColor", color);
            });

            // 估算值数字滚动动画
            if (esti > 0)
            {
                float currentEsti = float.TryParse(EstiText.text, out var oldValue)
                ? oldValue
                : 0f;

                LMotion.Create(currentEsti, esti, 0.5f)
                .WithEase(Ease.OutCubic)
                .Bind(value =>
                {
                    EstiText.text = value.ToString("F2");
                });
            }
            else
            {
                EstiText.text = "";
            }
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
            SetRadar(new List<float> { 0, 0, 0, 0, 0, 0, 0 }, 0);
            _cancellationToken = token;
            _loadDelayTimer = loadDelayMS / 1000f;
        }
    }
}

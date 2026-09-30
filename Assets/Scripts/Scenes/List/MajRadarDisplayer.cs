using Cysharp.Threading.Tasks;
using LitMotion;
using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.List;
using MajdataPlay.Threading;
using MajdataPlay.Utils.ChartRadar;
using MajSimai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay
{
    public class MajRadarDisplayer : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("MajRadarRawImage")]
        private RawImage _radarRawImage;

        [SerializeField]
        [FormerlySerializedAs("EstiText")]
        private TextMeshProUGUI _estiText;

        [SerializeField]
        [FormerlySerializedAs("RadarFillColors")]
        private Color[] _radarFillColors = new Color[6];

        [SerializeField]
        [FormerlySerializedAs("RadarOutlineColors")]
        private Color[] _radarOutlineColors = new Color[6];

        private Material _radarMaterial;
        private readonly MotionHandle[] _scoreHandles = new MotionHandle[6];
        private float _loadTimer = 0f;
        private float _loadDelayTimer = 0f;

        private ISongDetail? _currentSongDetail;
        private ChartLevel _currentLevel;
        private Task<SimaiFile>? _maidataLoadTask;
        private CancellationToken _cancellationToken = default;

        const float LOAD_DEBOUNCE_INTERVAL_SEC = 0.4f;




        private void Awake()
        {
            _radarMaterial = (_radarRawImage.material = new(_radarRawImage.material));
        }
        private async UniTask AnalyzeAsync(SimaiChart simaiChart, CancellationToken cts)
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
                MajDebug.LogError("[MajRadar] Radar material is not initialized.");
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
            Color targetFill = _radarFillColors[maxIndex];

            LMotion.Create(startFill, targetFill, 0.5f)
            .Bind(color =>
            {
                _radarMaterial.SetColor("_FillColor", color);
            });

            Color startOutline = _radarMaterial.GetColor("_OutlineColor");
            Color targetOutline = _radarOutlineColors[maxIndex];

            LMotion.Create(startOutline, targetOutline, 0.5f)
            .Bind(color =>
            {
                _radarMaterial.SetColor("_OutlineColor", color);
            });

            // 估算值数字滚动动画
            if (esti > 0)
            {
                float currentEsti = float.TryParse(_estiText.text, out var oldValue)
                ? oldValue
                : 0f;

                LMotion.Create(currentEsti, esti, 0.5f)
                .WithEase(Ease.OutCubic)
                .Bind(value =>
                {
                    _estiText.text = value.ToString("F2");
                });
            }
            else
            {
                _estiText.text = "";
            }
        }

        private void LateUpdate()
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
                _maidataLoadTask.Register($"[{nameof(MajRadarDisplayer)}]Load maidata");
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
                    var task = AnalyzeAsync(simaiChart, _cancellationToken).Register($"[{nameof(MajRadarDisplayer)}]Chart analyze");
                    task.Forget();
                    ListManager.AllBackgroundTasks.Add(task.AsTask());
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

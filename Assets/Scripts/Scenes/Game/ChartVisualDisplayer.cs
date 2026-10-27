using MajdataPlay.Diagnostics;
using MajdataPlay.Scenes.List;
using MajdataPlay.Threading;
using MajdataPlay.Utils;
using MajSimai;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    public class ChartVisualDisplayer : MonoBehaviour
    {
        public float LastAnalyzeBpm { get; private set; } = 0f;
        public bool LastAnalyzeIsEmpty { get; private set; }

        [SerializeField]
        private GameObject? _iconPrefab;

        private GameObject? _loadingPrefab;
        private GameObject? _errorIcon;
        private GameObject? _helpIcon;

        private RawImage _rawImage;
        private RectTransform _rectTransform;


        private Texture2D _emptyTexture;

#if UNITY_ANDROID || UNITY_IOS
        private const int MAX_CACHE_COUNT = 8;
#else
        const int MAX_CACHE_COUNT = 32;
#endif
        private int _cacheCursor = 0;

        private float _loadDelayTimer = 0f;

        private readonly LoadTask _loadTask = new();

        private void Awake()
        {
            Majdata<ChartVisualDisplayer>.Instance = this;

            if (_iconPrefab != null)
            {
                var iconTransform = _iconPrefab.transform;
                _loadingPrefab = iconTransform.GetChild(0).gameObject;
                _helpIcon = iconTransform.GetChild(1).gameObject;
                _errorIcon = iconTransform.GetChild(2).gameObject;
            }
            _rawImage = GetComponent<RawImage>();
            _rectTransform = GetComponent<RectTransform>();
            _emptyTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _emptyTexture.SetPixel(0, 0, Color.white);
            _emptyTexture.Apply();
        }
        private void OnDestroy()
        {
            Majdata<ChartVisualDisplayer>.Free();
        }
        private void LateUpdate()
        {
            if (_loadTask.IsFinished || (_loadTask.SongDetail is null && _loadTask.Chart is null))
            {
                return;
            }
            else if (_loadDelayTimer > 0)
            {
                _loadDelayTimer -= MajTimeline.DeltaTime;
                return;
            }
            var maidataLoadTask = _loadTask.MaidataLoadTask!;
            if (maidataLoadTask is null)
            {
                try
                {
                    AnalyzeMaidata(_loadTask.Chart!, _loadTask.Length);
                }
                catch (Exception e)
                {
                    MajDebug.LogException(e);
                    SetError();
                }
                _loadTask.IsFinished = true;
            }
            else if (maidataLoadTask.IsCompleted)
            {
                if (maidataLoadTask.IsCompletedSuccessfully)
                {
                    var maidata = maidataLoadTask.Result;
                    _loadTask.Maidata = maidata;
                    try
                    {
                        if (_loadTask.SongDetail is not null)
                        {
                            AnalyzeMaidata(maidata.Charts[(int)_loadTask.Level], _loadTask.Length);
                        }
                        else if (_loadTask.Chart is not null)
                        {
                            AnalyzeMaidata(_loadTask.Chart, _loadTask.Length);
                        }
                        else
                        {
                            SetError();
                        }
                    }
                    catch (Exception e)
                    {
                        MajDebug.LogException(e);
                        SetError();
                    }
                    _loadTask.IsFinished = true;
                }
                else
                {
                    SetError();
                    _loadTask.IsFinished = true;
                }
            }
        }
        public void SetSongDeatil(ISongDetail songDetail, ChartLevel chartLevel, float? length = null, int loadDelayMS = 0, CancellationToken token = default)
        {
            _loadTask.SongDetail = songDetail;
            _loadTask.Level = chartLevel;
            _loadTask.MaidataLoadTask = songDetail.GetMaidataAsync(true, token: token).AsTask();
            _loadTask.MaidataLoadTask.Register($"[{nameof(ChartVisualDisplayer)}]Load maidata");
            ListManager.AllBackgroundTasks.Add(_loadTask.MaidataLoadTask);
            _loadTask.Length = length;
            _loadTask.Maidata = null;
            _loadTask.IsFinished = false;
            _loadDelayTimer = loadDelayMS / 1000f;
            SetLoading();
        }
        public void SetSimaiChart(SimaiChart chart, float? length = null)
        {
            _loadTask.SongDetail = null;
            _loadTask.Level = default;
            _loadTask.Length = length;
            _loadTask.Chart = chart;
            _loadTask.IsFinished = false;
            _loadDelayTimer = 0;
            SetLoading();
        }
        private void AnalyzeMaidata(SimaiChart chart, float? length)
        {
            var cache = default(CachedMaidataAnalyzeResult);
            var rect = _rectTransform.rect;
            var minBpm = 0f;
            var maxBpm = 0f;
            var anaLength = TimeSpan.Zero;
            var graph = default(Texture?);
            var needRefreshCache = false;
            var graphCacheKey = $"MaidataAnalyze/{chart.Fumen}";
            if (Cache.MaidataAnalyze.TryGetValue(chart.Fumen, out cache))
            {
                minBpm = cache.MinBPM;
                maxBpm = cache.MaxBPM;
                anaLength = cache.Length;
                Cache.Texture.TryGetValue(graphCacheKey, out var graph2D);
                graph = graph2D;
            }
            if (!cache.IsValid || graph is null)
            {
                var result = ChartAnalyzer.AnalyzeMaidataWithGraph(chart, (int)rect.height, (int)rect.width, length);
                minBpm = result.MinBPM;
                maxBpm = result.MaxBPM;
                anaLength = result.Length;
                graph = result.LineGraph!;
                needRefreshCache = true;
            }
            if (anaLength == TimeSpan.Zero)
            {
                SetHelp();
                return;
            }
            else if (needRefreshCache)
            {
                Cache.Texture.Set(graphCacheKey, (Texture2D)graph);
                Cache.MaidataAnalyze.Set(chart.Fumen, new()
                {
                    IsValid = true,
                    MinBPM = minBpm,
                    MaxBPM = maxBpm,
                    Length = anaLength,
                    Timestamp = DateTime.Now
                });
            }
            SetTexture(graph);
        }

        private void SetLoading()
        {
            _errorIcon?.SetActive(false);
            _helpIcon?.SetActive(false);
            _loadingPrefab?.SetActive(true);
            _rawImage.texture = _emptyTexture;
        }
        private void SetHelp()
        {
            _errorIcon?.SetActive(false);
            _helpIcon?.SetActive(true);
            _loadingPrefab?.SetActive(false);
            _rawImage.texture = _emptyTexture;
        }
        private void SetError()
        {
            _errorIcon?.SetActive(true);
            _helpIcon?.SetActive(false);
            _loadingPrefab?.SetActive(false);
            _rawImage.texture = _emptyTexture;
        }
        private void SetTexture(Texture texture)
        {
            _errorIcon?.SetActive(false);
            _helpIcon?.SetActive(false);
            _loadingPrefab?.SetActive(false);
            _rawImage.texture = texture;
        }

        private class LoadTask
        {
            public ISongDetail? SongDetail { get; set; }
            public Task<SimaiFile>? MaidataLoadTask { get; set; }
            public float? Length { get; set; }
            public SimaiFile? Maidata { get; set; }
            public SimaiChart? Chart { get; set; }
            public ChartLevel Level { get; set; }
            public bool IsFinished { get; set; }
        }
    }
}

using Cysharp.Text;
using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Drawing;
using MajdataPlay.Scenes.List;
using MajdataPlay.Threading;
using MajdataPlay.Utils;
using MajSimai;
using Nito.AsyncEx;
using SkiaSharp;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.VisualScripting.Antlr3.Runtime;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.UIElements;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    public class ChartVisualDisplayer : MonoBehaviour
    {
        public float LastAnalyzeBpm { get; private set; } = 0f;
        public bool LastAnalyzeIsEmpty { get; private set; }

        [SerializeField]
        GameObject? _iconPrefab;

        GameObject? _loadingPrefab;
        GameObject? _errorIcon;
        GameObject? _helpIcon;

        RawImage _rawImage;
        RectTransform _rectTransform;


        Texture2D _emptyTexture;

#if UNITY_ANDROID || UNITY_IOS
        const int MAX_CACHE_COUNT = 8;
#else
        const int MAX_CACHE_COUNT = 32;
#endif
        int _cacheCursor = 0;

        float _loadDelayTimer = 0f;

        readonly LoadTask _loadTask = new();

        readonly CacheItem[] _cachedAnalyzeResults = new CacheItem[MAX_CACHE_COUNT];

        void Awake()
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
        void OnDestroy()
        {
            Majdata<ChartVisualDisplayer>.Free();
            for (var i = _cacheCursor - 1; i >= 0; i--)
            {
                ref var cachedResult = ref _cachedAnalyzeResults[i];
                if(cachedResult.AnalyzeResult is MaidataLineGraphAnalyzeResult result)
                {
                    UnityEngine.Object.DestroyImmediate(result.LineGraph, true);
                }
                cachedResult = default;
                _cacheCursor--;
            }
        }
        void LateUpdate()
        {
            if(_loadTask.IsFinished || (_loadTask.SongDetail is null && _loadTask.Chart is null))
            {
                return;
            }
            else if(_loadDelayTimer > 0)
            {
                _loadDelayTimer -= MajTimeline.DeltaTime;
                return;
            }
            var maidataLoadTask = _loadTask.MaidataLoadTask!;
            if(maidataLoadTask is null)
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
                if(maidataLoadTask.IsCompletedSuccessfully)
                {
                    var maidata = maidataLoadTask.Result;
                    _loadTask.Maidata = maidata;
                    try
                    {
                        if(_loadTask.SongDetail is not null)
                        {
                            AnalyzeMaidata(maidata.Charts[(int)_loadTask.Level], _loadTask.Length);
                        }
                        else if(_loadTask.Chart is not null)
                        {
                            AnalyzeMaidata(_loadTask.Chart, _loadTask.Length);
                        }
                        else
                        {
                            SetError();
                        }
                    }
                    catch(Exception e)
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
        void AnalyzeMaidata(SimaiChart chart, float? length)
        {
#if !UNITY_EDITOR
            ref var cachedResult = ref _cachedAnalyzeResults[0];
            if (TryGetCachedResultIndex(chart, out var index))
            {
                var a = _cachedAnalyzeResults[index];
                Array.Copy(_cachedAnalyzeResults, 0, _cachedAnalyzeResults, 1, index);
                cachedResult = a;
            }
            else
            {
                if (_cacheCursor == _cachedAnalyzeResults.Length)
                {
                    ref var last = ref _cachedAnalyzeResults[_cachedAnalyzeResults.Length - 1];
                    if (last.AnalyzeResult is MaidataLineGraphAnalyzeResult result)
                    {
                        UnityEngine.Object.DestroyImmediate(result.LineGraph, true);
                    }
                    last = default;
                    _cacheCursor--;
                }
                Array.Copy(_cachedAnalyzeResults, 0, _cachedAnalyzeResults, 1, _cacheCursor);
                cachedResult = new()
                {
                    Chart = chart,
                };
                _cacheCursor++;
            }
#else
            var cachedResult = default(CacheItem);
#endif
            var rect = _rectTransform.rect;
            var anaResult = cachedResult.AnalyzeResult ??= ChartAnalyzer.AnalyzeMaidataWithGraph(chart, (int)rect.height, (int)rect.width, length);
            if(anaResult.Length == TimeSpan.Zero)
            {
                SetHelp();
                return;
            }
            SetTexture(anaResult.LineGraph);
        }

        void SetLoading()
        {
            _errorIcon?.SetActive(false);
            _helpIcon?.SetActive(false);
            _loadingPrefab?.SetActive(true);
            _rawImage.texture = _emptyTexture;
        }
        void SetHelp()
        {
            _errorIcon?.SetActive(false);
            _helpIcon?.SetActive(true);
            _loadingPrefab?.SetActive(false);
            _rawImage.texture = _emptyTexture;
        }
        void SetError()
        {
            _errorIcon?.SetActive(true);
            _helpIcon?.SetActive(false);
            _loadingPrefab?.SetActive(false);
            _rawImage.texture = _emptyTexture;
        }
        void SetTexture(Texture texture)
        {
            _errorIcon?.SetActive(false);
            _helpIcon?.SetActive(false);
            _loadingPrefab?.SetActive(false);
            _rawImage.texture = texture;
        }
        bool TryGetCachedResultIndex(SimaiChart chart, out int index)
        {
            index = default;
            for (var i = 0; i < _cacheCursor; i++)
            {
                if (_cachedAnalyzeResults[i].Chart == chart)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        class LoadTask
        {
            public ISongDetail? SongDetail { get; set; }
            public Task<SimaiFile>? MaidataLoadTask { get; set; }
            public float? Length { get; set; }
            public SimaiFile? Maidata { get; set; }
            public SimaiChart? Chart { get; set; }
            public ChartLevel Level { get; set; }
            public bool IsFinished { get; set; }
        }
        struct CacheItem
        {
            public required SimaiChart Chart { get; init; }
            public MaidataLineGraphAnalyzeResult? AnalyzeResult { get; set; }
        }
    }
}

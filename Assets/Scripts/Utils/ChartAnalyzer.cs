using Cysharp.Threading.Tasks;
using MajdataPlay.Buffers;
using MajdataPlay.Diagnostics;
using MajdataPlay.Drawing;
using MajdataPlay.Numerics;
using MajSimai;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Utils
{
    [BurstCompile]
    internal static class ChartAnalyzer
    {
        [ThreadStatic]
        static SKPaint? s_tapPaint;
        [ThreadStatic]
        static SKPaint? s_slidePaint;
        [ThreadStatic]
        static SKPaint? s_touchPaint;
        [ThreadStatic]
        static SKPath? s_tapPath;
        [ThreadStatic]
        static SKPath? s_slidePath;
        [ThreadStatic]
        static SKPath? s_touchPath;

        [ThreadStatic]
        static SKPoint[]? s_radii;
        [ThreadStatic]
        static SKRoundRect? s_roundRect;

        readonly static Color TapColor = new Color(0.8980393f, 0.3176471f, 0.5607843f);
        readonly static Color TouchColor = new Color(0.9450981f, 0.654902f, 0.2235294f);
        readonly static Color SlideColor = new Color(0.2431373f, 0.5568628f, 0.6784314f);

        public static MaidataAnalyzeResult AnalyzeMaidata(SimaiChart data, float? chartLength = null)
        {
            if (data.NoteTimings.IsEmpty)
            {
                return default;
            }
            var noteTimings = data.NoteTimings;
            var length = chartLength ?? (float)noteTimings[noteTimings.Length - 1].Timing;
            GetBpmRange(noteTimings, out var minBpm, out var maxBpm);

            return new()
            {
                Length = TimeSpan.FromSeconds(length),
                MaxBPM = maxBpm,
                MinBPM = minBpm,
            };
        }
        public static MaidataLineGraphAnalyzeResult AnalyzeMaidataWithGraph(SimaiChart data,
                                                                            int height,
                                                                            int width,
                                                                            float? chartLength = null)
        {
            if (data.NoteTimings.IsEmpty)
            {
                return default;
            }
            var noteTimings = data.NoteTimings;
            var length = chartLength ?? (float)noteTimings[noteTimings.Length - 1].Timing;
            var bars = BuildBars(noteTimings, length);
            var graph = DrawGraph(bars, height, width);
            GetBpmRange(noteTimings, out var minBpm, out var maxBpm);

            return new()
            {
                Length = TimeSpan.FromSeconds(length),
                MaxBPM = maxBpm,
                MinBPM = minBpm,
                LineGraph = graph
            };
        }

        static void GetBpmRange(
            ReadOnlySpan<SimaiTimingPoint> data,
            out float minBpm,
            out float maxBpm)
        {
            minBpm = float.MaxValue;
            maxBpm = float.MinValue;

            for (int i = 0; i < data.Length; i++)
            {
                float bpm = data[i].Bpm;

                minBpm = Mathf.Min(minBpm, bpm);
                maxBpm = Mathf.Max(maxBpm, bpm);
            }

            if (data.Length == 0)
            {
                minBpm = 0f;
                maxBpm = 0f;
            }
        }

        static unsafe Texture DrawGraph(
                            NativeArray<GraphBar> bars,
                            int height,
                            int width)
        {
            const int BarCount = 64;

            EnsureSakaComponentIsInited();

            var imageInfo = new SKImageInfo(width, height);

            using var surface = SKSurface.Create(imageInfo);

            var canvas = surface.Canvas;
            canvas.Clear(SKColor.Empty);

            var step = (float)width / BarCount;

            var barWidth = step * 0.72f;
            var radius = barWidth * 0.5f;

            for (var i = 0; i < BarCount; i++)
            {
                var x = (i + 0.5f) * step;

                var barInfo = bars[i];

                var tapHeight = barInfo.Tap * height;
                var slideHeight = barInfo.Slide * height;
                var touchHeight = barInfo.Touch * height;

                // Touch (最底层)
                DrawRoundRectBar(
                    canvas,
                    x,
                    height,
                    tapHeight + slideHeight + touchHeight,
                    barWidth,
                    radius,
                    s_touchPaint);

                // Slide
                DrawRoundRectBar(
                    canvas,
                    x,
                    height,
                    tapHeight + slideHeight,
                    barWidth,
                    radius,
                    s_slidePaint);

                // Tap (顶部)
                DrawRoundRectBar(
                    canvas,
                    x,
                    height,
                    tapHeight,
                    barWidth,
                    radius,
                    s_tapPaint);
            }

            return surface.ToTexture2D(imageInfo);
        }
        static void DrawRoundRectBar(SKCanvas canvas,
                                     float centerX,
                                     float bottom,
                                     float barHeight,
                                     float width,
                                     float radius,
                                     SKPaint paint)
        {
            if (barHeight <= 0f)
            {
                return;
            }

            var rect = new SKRect(
                centerX - (width * 0.5f),
                bottom - barHeight,
                centerX + (width * 0.5f),
                bottom);

            // 四个角全部设置圆角
            s_radii![0] = new SKPoint(radius, radius); // 左上
            s_radii[1] = new SKPoint(radius, radius); // 右上
            s_radii[2] = new SKPoint(radius, radius); // 右下
            s_radii[3] = new SKPoint(radius, radius); // 左下

            s_roundRect!.SetRectRadii(rect, s_radii);

            canvas.DrawRoundRect(s_roundRect, paint);
        }
        static NativeArray<GraphBar> BuildBars(
            ReadOnlySpan<SimaiTimingPoint> data,
            float length,
            int barCount = 64)
        {
            var bars = new NativeArray<GraphBar>(barCount, Allocator.Temp);

            float max = 0f;

            for (int i = 0; i < data.Length; i++)
            {
                var timingPoint = data[i];

                int barIndex = (int)(timingPoint.Timing / length * barCount);

                if (barIndex < 0)
                    continue;

                if (barIndex >= barCount)
                    barIndex = barCount - 1;

                var bar = bars[barIndex];

                foreach (var note in timingPoint.Notes)
                {
                    switch (note.Type)
                    {
                        case SimaiNoteType.Tap:
                        case SimaiNoteType.Hold:
                            bar.Tap += 1;
                            break;

                        case SimaiNoteType.Slide:
                            bar.Slide += 2;
                            break;

                        case SimaiNoteType.Touch:
                        case SimaiNoteType.TouchHold:
                            bar.Touch += 1;
                            break;
                    }
                }

                bars[barIndex] = bar;
            }

            // 求最大柱高度
            for (int i = 0; i < barCount; i++)
            {
                var sum = bars[i].Tap + bars[i].Slide + bars[i].Touch;
                max = Mathf.Max(max, sum);
            }

            // 归一化
            if (max > 0)
            {
                for (int i = 0; i < barCount; i++)
                {
                    var bar = bars[i];

                    bars[i] = new GraphBar
                    {
                        Tap = bar.Tap / max,
                        Slide = bar.Slide / max,
                        Touch = bar.Touch / max
                    };
                }
            }

            return bars;
        }

        [MemberNotNull(nameof(s_tapPaint), nameof(s_slidePaint), nameof(s_touchPaint))]
        [MemberNotNull(nameof(s_tapPath), nameof(s_slidePath), nameof(s_touchPath))]
        [MemberNotNull(nameof(s_radii), nameof(s_roundRect))]
        static void EnsureSakaComponentIsInited()
        {
            if (s_tapPaint is null)
            {
                s_tapPaint = new();
                s_tapPaint.Color = TapColor.ToSkColor();
                s_tapPaint.IsAntialias = true;
                s_tapPaint.Style = SKPaintStyle.Fill;
            }
            if (s_slidePaint is null)
            {
                s_slidePaint = new();
                s_slidePaint.Color = SlideColor.ToSkColor();
                s_slidePaint.IsAntialias = true;
                s_slidePaint.Style = SKPaintStyle.Fill;
            }
            if (s_touchPaint is null)
            {
                s_touchPaint = new();
                s_touchPaint.Color = TouchColor.ToSkColor();
                s_touchPaint.IsAntialias = true;
                s_touchPaint.Style = SKPaintStyle.Fill;
            }
            if (s_tapPath is null)
            {
                s_tapPath = new();
            }
            else
            {
                s_tapPath.Rewind();
            }
            if (s_touchPath is null)
            {
                s_touchPath = new();
            }
            else
            {
                s_touchPath.Rewind();
            }
            if (s_slidePath is null)
            {
                s_slidePath = new();
            }
            else
            {
                s_slidePath.Rewind();
            }

            if (s_radii is null)
            {
                s_radii = new SKPoint[4]
                {
                    SKPoint.Empty,
                    SKPoint.Empty,
                    SKPoint.Empty,
                    SKPoint.Empty
                };
            }
            else
            {
                s_radii[0] = SKPoint.Empty;
                s_radii[1] = SKPoint.Empty;
                s_radii[2] = SKPoint.Empty;
                s_radii[3] = SKPoint.Empty;
            }
            if (s_roundRect is null)
            {
                s_roundRect = new();
            }
            else
            {
                s_roundRect.SetEmpty();
            }
        }
        struct GraphBar
        {
            public float Tap { get; set; }
            public float Slide { get; set; }
            public float Touch { get; set; }
        }
    }
}

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Utils;

namespace MajdataPlay.IO
{
    internal static class OniimaiController
    {
        private static AndroidJavaClass _bridge;
        private static bool _failed;
        private static readonly int[] LedFrame = new int[9];
        internal static bool TouchConnected { get; private set; }
        internal static bool ButtonConnected { get; private set; }
        internal static bool ExternalActive { get; private set; }
        internal static int GameFrameRate { get; private set; } = 60;
        private static bool _frameRateApplied;
        private static float _nextStats;
        private static int _artGame, _coverId, _graphId;
        private static readonly OniimaiStatsRetention<StatsFrame> Stats = new();
        [Serializable]
        private sealed class StatsFrame
        {
            public bool game, result;
            public string scene, title, artist, designer, level;
            public int levelColor;
            public float seconds, length;
            public double achievement;
            public long combo, dx, critical, perfect, great, good, miss, fast, late;
        }

        // Presentation uses the unmodified 1080x1920 cabinet canvas. Phone layout settings
        // stay saved as-is and automatically become effective again after unplugging.
        internal static bool UsePhoneLayout(bool configured) => !ExternalActive && configured;

        private static AndroidJavaClass Bridge => _bridge ??= new AndroidJavaClass(
            "net.majdata.majdataplay.oniimai.OniimaiController");

        internal static void MergeInput(Span<bool> touch, Span<bool> buttons,
                                       Span<int> touchClicks, Span<int> buttonClicks)
        {
            if (_failed) return;
            try
            {
                var frame = Bridge.CallStatic<long[]>("poll");
                if (frame == null || frame.Length != 5) return;
                TouchConnected = (frame[4] & 1) != 0;
                ButtonConnected = (frame[4] & 2) != 0;
                ExternalActive = (frame[4] & 4) != 0;
                var frameRate = (frame[4] & 16) != 0 ? 120 : 60;
                if (!_frameRateApplied || GameFrameRate != frameRate)
                {
                    GameFrameRate = frameRate;
                    Application.targetFrameRate = frameRate;
                    _frameRateApplied = true;
                    Debug.Log($"Oniimai Android FPS selected: target={frameRate}");
                }
                OniimaiFrame.Merge(frame, touch, buttons, touchClicks, buttonClicks);
            }
            catch (Exception ex) { Fail(ex); }
        }
        internal static void SubmitLights(ReadOnlySpan<Color> colors, byte cabinetBrightness)
        {
            if (_failed) return;
            try
            {
                for (var i = 0; i < 8; i++)
                {
                    Color32 c = colors[i];
                    LedFrame[i] = (c.r << 16) | (c.g << 8) | c.b;
                }
                LedFrame[8] = cabinetBrightness * 0x010101;
                Bridge.CallStatic("lights", LedFrame);
            }
            catch (Exception ex) { Fail(ex); }
        }
        internal static void SetSettingsVisible(bool visible)
        {
            if (_failed) return;
            try { Bridge.CallStatic("settingsVisible", visible); }
            catch (Exception ex) { Fail(ex); }
        }
        internal static void UpdateStats()
        {
            if (_failed) return;
            try
            {
                var scene = SceneSwitcher.CurrentScene;
                var game = Majdata<GamePlayManager>.Instance;
                var inGame = scene == MajScenes.Game;
                var inResult = scene == MajScenes.Result || scene == MajScenes.TotalResult;
                if (Stats.Advance(inGame, inResult, game != null ? game.GetInstanceID() : 0))
                {
                    ClearArtwork();
                    _nextStats = 0;
                }
                if (Time.unscaledTime < _nextStats) return;
                _nextStats = Time.unscaledTime + 0.1f;
                var counter = Majdata<ObjectCounter>.Instance;
                if (inGame && !Stats.IsFinal && counter != null && game != null)
                {
                    Stats.Capture(ReadStats(game, counter), false);
                    if (ExternalActive) UpdateArtwork(game);
                }
                var frame = Stats.Frame ?? new StatsFrame { title = "MajdataPlay" };
                frame.scene = scene.ToString();
                frame.result = inResult && Stats.IsFinal;
                // Keep refreshing the heartbeat on results, including dashboard previews.
                Bridge.CallStatic("stats", JsonUtility.ToJson(frame));
            }
            catch (Exception ex) { Debug.LogWarning("Oniimai stats: " + ex.Message); }
        }
        internal static void CaptureResult(GamePlayManager game, ObjectCounter counter, GameResult result)
        {
            if (_failed) return;
            try
            {
                if (Stats.Advance(true, false, game.GetInstanceID())) ClearArtwork();
                var frame = ReadStats(game, counter);
                var judge = MajdataPlay.Collections.JudgeDetail.UnpackJudgeRecord(result.JudgeRecord.TotalJudgeInfo);
                frame.achievement = result.Acc.DX;
                frame.dx = result.DXScore;
                frame.critical = judge.CriticalPerfect; frame.perfect = judge.Perfect;
                frame.great = judge.Great; frame.good = judge.Good; frame.miss = judge.Miss;
                frame.fast = result.Fast; frame.late = result.Late;
                Stats.Capture(frame, true);
                _nextStats = 0;
                // Copy PNGs before Unity destroys the song's textures. This also supports
                // enabling the monitor for the first time while already on results.
                UpdateArtwork(game);
            }
            catch (Exception ex) { Debug.LogWarning("Oniimai final stats: " + ex.Message); }
        }
        private static StatsFrame ReadStats(GamePlayManager game, ObjectCounter counter)
        {
            ref readonly var judge = ref counter.JudgeStats;
            Color32 color = game.OniimaiLevelColor;
            return new StatsFrame {
                game = true, title = game.OniimaiSongTitle, artist = game.OniimaiArtist,
                designer = game.OniimaiDesigner, level = game.OniimaiLevel,
                levelColor = unchecked((int)0xff000000) | color.r << 16 | color.g << 8 | color.b,
                seconds = Mathf.Clamp(game.ThisFrameSec, 0, Mathf.Max(0, game.AudioLength)), length = game.AudioLength,
                combo = counter.OniimaiCombo, dx = counter.OniimaiRemainingDxScore,
                achievement = counter.AccurateStats.Achievement_C,
                critical = judge.TotalCriticalCount, perfect = judge.TotalPerfectCount,
                great = judge.TotalGreatCount, good = judge.TotalGoodCount, miss = judge.TotalMissCount,
                fast = judge.TotalFastCount, late = judge.TotalLateCount,
            };
        }
        private static void ClearArtwork()
        {
            _artGame = _coverId = _graphId = 0;
            Bridge.CallStatic("artwork", 0, Array.Empty<byte>());
            Bridge.CallStatic("artwork", 1, Array.Empty<byte>());
        }
        private static void UpdateArtwork(GamePlayManager game)
        {
            if (_artGame != game.GetInstanceID()) {
                ClearArtwork(); _artGame = game.GetInstanceID();
            }
            var cover = game.OniimaiCover;
            if (cover != null && _coverId != cover.GetInstanceID()) {
                SendArtwork(0, cover.texture, cover.rect, 192, 192);
                _coverId = cover.GetInstanceID();
            }
            var graph = game.OniimaiGraph;
            if (graph != null && graph.width > 1 && _graphId != graph.GetInstanceID()) {
                SendArtwork(1, graph, new Rect(0, 0, graph.width, graph.height), 640, 100);
                _graphId = graph.GetInstanceID();
            }
        }
        // Small, static artwork is transferred only when the song/graph changes.
        // Live gameplay frames are never read back or copied for the phone panel.
        private static void SendArtwork(int kind, Texture source, Rect crop, int width, int height)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D image = null;
            try {
                Graphics.Blit(source, target, new Vector2(crop.width / source.width, crop.height / source.height),
                    new Vector2(crop.x / source.width, crop.y / source.height));
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                Bridge.CallStatic("artwork", kind, image.EncodeToPNG());
            }
            finally {
                RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.Destroy(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }
        private static void Fail(Exception ex)
        {
            _failed = true;
            TouchConnected = ButtonConnected = false;
            Debug.LogError("Oniimai Android bridge unavailable: " + ex);
        }
    }
}
#endif

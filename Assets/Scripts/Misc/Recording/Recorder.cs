using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using MajdataPlay.IO;
using MajdataPlay.Runtime;
using MajdataPlay.Settings;
using System;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Recording
{
    internal static class Recorder
    {
        public static bool IsRecording
        {
            get
            {
                return s_recorder?.IsRecording ?? false;
            }
        }
        public static bool IsConnected
        {
            get
            {
                return s_recorder?.IsConnected ?? false;
            }
        }
        public static bool IsEnabled
        {
            get
            {
                return MajEnv.Settings.Game.RecordMode != RecordModeOption.Disable;
            }
        }

        private static IRecorder? s_recorder;
        private static bool s_isInited = false;

        internal static void Init()
        {
            if (!s_isInited)
            {
                s_isInited = true;
                if ((PlatformInfo.IsAndroid || PlatformInfo.IsIOS) && MajEnv.Settings.Game.RecordMode == RecordModeOption.OBSTrigger)
                {
                    MajDebug.LogWarning(nameof(Recorder), "OBS Trigger recording is not supported on mobile platforms.");
                    return;
                }
                switch (MajEnv.Settings.Game.RecordMode)
                {
                    case RecordModeOption.OBSTrigger:
                        s_recorder = new OBSRecorder();
                        break;
                    case RecordModeOption.FFmpeg:
                        var capturer = SceneSwitcher.MainCamera.GetComponent<FFmpegCameraCapturer>();
                        s_recorder = new FFmpegRecorder(capturer,
                            MajInstances.AudioManager.CreateAudioRecorder(),
                            MajEnv.Settings.Encoding.ContainerFormat);
                        var options = capturer.Options;
                        var encodingSetting = MajEnv.Settings.Encoding;
                        options.Height = (int)(Screen.height * encodingSetting.ResolutionScale);
                        options.Width = (int)(Screen.width * encodingSetting.ResolutionScale);
                        options.Format = encodingSetting.EncodingFormat;
                        options.FrameRate = encodingSetting.FrameRate;
                        options.RateControlMode = encodingSetting.RateControlMode;
                        options.BitRate = encodingSetting.BitRate;
                        options.MaximumBitRate = Math.Max(encodingSetting.BitRate, encodingSetting.MaximumBitRate);
                        options.PreferredEncoderType = (VideoEncoderType)encodingSetting.PreferredEncoder;
                        break;
                    default:
                        MajDebug.LogError(nameof(Recorder), "Unsupported record mode: " + MajEnv.Settings.Game.RecordMode);
                        break;
                }
            }
            GameManager.OnAppQuit += OnApplicationQuit;
        }
        public static void StartRecord(string filename)
        {
            if (s_recorder is null || !IsEnabled || !s_recorder.IsConnected)
            {
                return;
            }
            s_recorder.SetOutputName(filename);
            s_recorder.StartRecord();
        }
        public static async Task StartRecordAsync(string filename)
        {
            if (s_recorder is null || !IsEnabled || !s_recorder.IsConnected)
            {
                return;
            }
            s_recorder.SetOutputName(filename);
            await s_recorder.StartRecordAsync();
        }
        public static void StopRecord()
        {
            if (s_recorder is null || !IsEnabled || !s_recorder.IsConnected)
            {
                return;
            }
            s_recorder.StopRecord();
        }
        public static async Task StopRecordAsync()
        {
            if (s_recorder is null || !IsEnabled || !s_recorder.IsConnected)
            {
                return;
            }
            await s_recorder.StopRecordAsync();
        }
        internal static void OnLateUpdate()
        {
            if (s_recorder is null)
            {
                return;
            }
            s_recorder.OnLateUpdate();
        }
        internal static void OnSettingsChanged()
        {
            if (s_recorder is null)
            {
                return;
            }
            s_recorder.Dispose();
            s_recorder = null;
            s_isInited = false;
            Init();
        }
        private static void OnApplicationQuit(object? sender, EventArgs? e)
        {
            if (s_recorder is null || !IsEnabled || !s_recorder.IsConnected)
            {
                return;
            }
            s_recorder.StopRecord();
            GameManager.OnAppQuit -= OnApplicationQuit;
        }
    }
}

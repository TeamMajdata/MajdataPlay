using MajdataPlay.FFmpeg;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using System;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay.Recording
{
    internal static class Recorder
    {
        public static bool IsRecording
        {
            get
            {
                return _recorder?.IsRecording ?? false;
            }
        }
        public static bool IsConnected
        {
            get
            {
                return _recorder.IsConnected;
            }
        }
        public static bool IsEnabled
        {
            get
            {
#if UNITY_STANDALONE
                return MajEnv.Settings.Game.RecordMode == RecordModeOption.OBSTrigger;
#else
                return false;
#endif
            }
        }

        private static IRecorder _recorder;
        private static int s_isInited = 0;

        internal static void Init()
        {
            if (s_isInited == 0)
            {
                _recorder = new OBSRecorder();
                //_recorder = new FFmpegRecorder(SceneSwitcher.MainCamera.GetComponent<FFmpegCameraCapturer>(), MajInstances.AudioManager.CreateAudioRecorder(), MediaContainerFormat.MP4);
                s_isInited = 1;
            }
            GameManager.OnAppQuit += OnApplicationQuit;
        }
        public static void StartRecord(string filename)
        {
            if (!IsEnabled || !_recorder.IsConnected)
            {
                return;
            }
            _recorder.SetOutputName(filename);
            _recorder.StartRecord();
        }
        public static async Task StartRecordAsync(string filename)
        {
            if (!IsEnabled || !_recorder.IsConnected)
            {
                return;
            }
            _recorder.SetOutputName(filename);
            await _recorder.StartRecordAsync();
        }
        public static void StopRecord()
        {
            if (!IsEnabled || !_recorder.IsConnected)
            {
                return;
            }
            _recorder.StopRecord();
        }
        public static async Task StopRecordAsync()
        {
            if (!IsEnabled || !_recorder.IsConnected)
            {
                return;
            }
            await _recorder.StopRecordAsync();
        }
        internal static void OnLateUpdate()
        {
            _recorder.OnLateUpdate();
        }
        private static void OnApplicationQuit(object? sender, EventArgs? e)
        {
            if (!IsEnabled || !_recorder.IsConnected)
            {
                return;
            }
            _recorder.StopRecord();
            GameManager.OnAppQuit -= OnApplicationQuit;
        }
    }
}

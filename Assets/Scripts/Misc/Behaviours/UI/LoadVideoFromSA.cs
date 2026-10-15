using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using MajdataPlay.IO.Storage;
using System;
using UnityEngine;
using UnityEngine.Serialization;

#nullable enable

namespace MajdataPlay.Behaviours.UI
{
    public class LoadVideoFromSA : MonoBehaviour
    {
        [FormerlySerializedAs("videoPlayer")]
        [SerializeField]
        private FFmpegVideoPlayer _ffmpegPlayer;

        [FormerlySerializedAs("videopath")]
        [SerializeField]
        private string _videoPath;

        [FormerlySerializedAs("LoadOnly")]
        [SerializeField]
        private bool _loadOnly;

        private void Awake()
        {
            _ffmpegPlayer = GetComponent<FFmpegVideoPlayer>();
        }
        private void Start()
        {
            DelayPlayVideo().Forget();
        }
        private async UniTask DelayPlayVideo()
        {
            while (MajEnv.Settings is null)
            {
                await UniTask.Yield();
            }
            var videoPath = _videoPath;
            if (string.IsNullOrEmpty(videoPath) || videoPath.Length == 1)
            {
                MajDebug.LogWarning(nameof(LoadVideoFromSA), $"Invalid video path: {videoPath}");
                return;
            }
            else if (videoPath[0] is '/' or '\\')
            {
                videoPath = videoPath.Substring(1);
            }
            StorageFile? videoFile = null;
            try
            {
                foreach (var ext in MajEnv.SupportedVideoFormats)
                {
                    videoFile = MajEnv.AssetsDirectory.FindFileAt(videoPath + ext);
                    if (videoFile is not null)
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                MajDebug.LogError(nameof(LoadVideoFromSA), $"Failed to resolve video: {videoPath}");
                MajDebug.LogException(e);
                return;
            }
            if (videoFile is null)
            {
                MajDebug.LogError(nameof(LoadVideoFromSA), $"Video does not exists: {videoPath}");
                return;
            }
            var path = videoFile.Location;
            MajDebug.LogInfo(nameof(LoadVideoFromSA), $"Load video from {path}");
            _ffmpegPlayer.Url = path;
            if (_loadOnly)
            {
                _ffmpegPlayer.Prepare();
            }
            else
            {
                _ffmpegPlayer.Play();
            }
        }
    }
}

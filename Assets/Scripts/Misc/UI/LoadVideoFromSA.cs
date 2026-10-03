using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Serialization;

namespace MajdataPlay.UI
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

        void Awake()
        {
            _ffmpegPlayer = GetComponent<FFmpegVideoPlayer>();
        }
        void Start()
        {
            DelayPlayVideo().Forget();
        }
        async UniTask DelayPlayVideo()
        {
            while(MajEnv.Settings is null)
            {
                await UniTask.Yield();
            }
            var videoPath = _videoPath;
            if (string.IsNullOrEmpty(videoPath) || videoPath.Length == 1)
            {
                MajDebug.LogWarning($"[{nameof(LoadVideoFromSA)}]Invalid video path: {videoPath}");
                return;
            }
            else if (videoPath[0] is '/' or '\\')
            {
                videoPath = videoPath.Substring(1);
            }
            var path = string.Empty;
            var isValid = false;
            foreach(var ext in MajEnv.SUPPORTED_VIDEO_FORMAT)
            {
                path = Path.Combine(MajEnv.AssetsPath, videoPath + ext);

                if(File.Exists(path))
                {
                    isValid = true;
                    break;
                }
            }
            if(!isValid)
            {
                MajDebug.LogError($"[{nameof(LoadVideoFromSA)}]Video does not exists: {videoPath}");
                return;
            }
            MajDebug.LogInfo($"[{nameof(LoadVideoFromSA)}]Load video from {path}");
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
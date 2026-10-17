using MajdataPlay.Diagnostics;
using UnityEngine;
using UnityEngine.Video;
#nullable enable
namespace MajdataPlay
{
    [DontDestroyOnLoad]
    public sealed class BackgroundVideoController : MajComponent
    {
        [SerializeField]
        VideoPlayer _videoPlayer = null!;
        // 背景保留普通 SpriteRenderer 绘制：不透明队列先画背景，再由 MRP 绘制 Live2D。
        // 显示/隐藏通过下面的 layer 切换完成，不再随场景切换接入 Cubism 合成 RT。
        [SerializeField]
        SpriteRenderer _videoRenderer = null!;

        protected override void Awake()
        {
            base.Awake();
            Majdata<BackgroundVideoController>.SetAsSingleton(this);
            if (_videoPlayer == null || _videoRenderer == null)
            {
                MajDebug.LogError("Global background video references are not configured.");
            }
        }

        public void Pause()
        {
            _videoPlayer.Pause();
        }

        public void Play()
        {
            _videoPlayer.Play();
        }

        public void Stop()
        {
            _videoPlayer.Stop();
        }

        public void Hide()
        {
            Pause();
            _videoRenderer.gameObject.layer = MajEnv.HIDDEN_LAYER;
        }

        public void Show()
        {
            _videoRenderer.gameObject.layer = MajEnv.DEFAULT_LAYER;
            Play();
        }
    }
}

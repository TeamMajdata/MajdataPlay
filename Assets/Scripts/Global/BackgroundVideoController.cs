using Live2D.Cubism.Rendering.URP.RenderingInterceptor;
using MajdataPlay.Diagnostics;
using MajdataPlay.FFmpeg;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
#nullable enable
namespace MajdataPlay
{
    [DontDestroyOnLoad]
    public sealed class BackgroundVideoController : MajComponent
    {
        [SerializeField]
        FFmpegVideoPlayer _videoPlayer = null!;
        [SerializeField]
        SpriteRenderer _videoRenderer = null!;

        [SerializeField]
        CubismRenderingInterceptController _renderingInterrupter;

        MajScenes[] _containsCubismComponentScenes = Array.Empty<MajScenes>();

        protected override void Awake()
        {
            base.Awake();
            Majdata<BackgroundVideoController>.SetAsSingleton(this);
            if (_videoPlayer == null || _videoRenderer == null)
            {
                MajDebug.LogError("Global background video references are not configured.");
            }
            SceneSwitcher.OnSceneChanged += OnSceneChanged;
            var scenes = (MajScenes[])Enum.GetValues(typeof(MajScenes));
            _containsCubismComponentScenes = scenes.Where(scene =>
                                                    {
                                                        var field = typeof(MajScenes).GetField(scene.ToString());
                                                        return field?.GetCustomAttribute<ContainsCubismComponentAttribute>() != null;
                                                    })
                                                    .ToArray();
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

        void OnSceneChanged(object? sender, (MajScenes NewScene, MajScenes OldScene) args)
        {
            if (_renderingInterrupter is null)
            {
                return;
            }
            var isContainsCubismComponent = Array.IndexOf(_containsCubismComponentScenes, args.NewScene) != -1;
            _renderingInterrupter.enabled = isContainsCubismComponent;
            _videoRenderer.enabled = !isContainsCubismComponent;
        }
    }
}

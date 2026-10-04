using Cysharp.Threading.Tasks;
using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.FFmpeg;
using MajdataPlay.Numerics;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    [RequireComponent(typeof(FFmpegVideoPlayer))]
    public class BGManager : MajBehaviour
    {
        public float CurrentSec
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (float)_videoPlayer.TimeSeconds;
        }
        public TimeSpan MediaLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => TimeSpan.FromSeconds(_videoPlayer.LengthSeconds);
        }
        public bool IsVideoEnded => !_usePictureAsBackground && _videoPlayer.State == VideoPlaybackState.Ended;

        [SerializeField]
        Vector3 _defaultScale;

        [SerializeField]
        Image _coverRenderer = null!;

        [SerializeField]
        RawImage _videoRenderer = null!;

        [SerializeField]
        Sprite _defaultSprite = null!;

        Material _backgroundMaterial = null!;
        FFmpegVideoPlayer _videoPlayer = null!;
        Sprite? _videoFallback;
        bool _usePictureAsBackground = true;
        int _videoLoadVersion;

        protected override void Awake()
        {
            base.Awake();
            Majdata<BGManager>.Instance = this;
            if (_defaultSprite == null)
            {
                _defaultSprite = RuntimeDatabase.Sprite.EmptySongCover;
            }
            _videoPlayer = GetComponent<FFmpegVideoPlayer>();
            _videoPlayer.Loop = false;
            _videoPlayer.TextureChanged += OnVideoTextureChanged;
            _videoPlayer.ErrorReceived += OnVideoError;
            _backgroundMaterial = new Material(_coverRenderer.material);
            _coverRenderer.material = _backgroundMaterial;
            _videoRenderer.material = _backgroundMaterial;
            _defaultScale = _coverRenderer.transform.localScale;
            DisableVideo();
        }

        void OnDestroy()
        {
            ++_videoLoadVersion;
            if (_videoPlayer != null)
            {
                _videoPlayer.TextureChanged -= OnVideoTextureChanged;
                _videoPlayer.ErrorReceived -= OnVideoError;
                _videoPlayer.Close();
            }
            Majdata<BGManager>.Free();
        }

        public void PauseVideo()
        {
            if (_usePictureAsBackground)
            {
                return;
            }
            _videoPlayer.Pause();
        }

        public void StopVideo()
        {
            if (_usePictureAsBackground)
            {
                return;
            }
            _videoPlayer.Stop();
        }

        public void PlayVideo(float time, float speed)
        {
            if (_usePictureAsBackground || !_videoPlayer.IsPrepared)
            {
                return;
            }
            _videoPlayer.SetRate(speed);
            if (_videoPlayer.IsSeekable)
            {
                _videoPlayer.TimeSeconds = time;
            }
            _videoPlayer.Play();
        }

        public void SetVideoSpeed(float speed)
        {
            if (_usePictureAsBackground)
            {
                return;
            }
            _videoPlayer.SetRate(speed);
        }

        public void SetBackgroundPic(Sprite? sprite)
        {
            DisableVideo();
            var rectTransform = _coverRenderer.rectTransform;
            if (sprite is null)
            {
                _coverRenderer.sprite = _defaultSprite;
                _coverRenderer.transform.localScale = _defaultScale;
                rectTransform.sizeDelta = new Vector2(1080, 1080);
                return;
            }
            _coverRenderer.sprite = sprite;
            var tex = sprite.texture;
            var scale = 1080f / tex.width;
            rectTransform.sizeDelta = new Vector2(tex.width, tex.height);
            _coverRenderer.transform.localScale = new Vector3(scale, scale, scale);
        }

        public void DisableVideo()
        {
            // Invalidate pending loads even when the cover is already visible.
            ++_videoLoadVersion;
            _usePictureAsBackground = true;
            _videoFallback = null;
            _videoRenderer.enabled = false;
            _videoRenderer.texture = null!;
            _coverRenderer.enabled = true;
            _videoPlayer.Close();
        }

        public void SetBrightness(float brightness)
        {
            _backgroundMaterial.SetFloat("_Brightness", brightness.Clamp(0, 1));
        }

        public async UniTask SetMovieAsync(string path, Sprite? fallback)
        {
            SetBackgroundPic(fallback);
            _videoFallback = fallback;
            var loadVersion = _videoLoadVersion;
            try
            {
                // Preload presents the first frame without advancing the playback clock.
                await _videoPlayer.PreloadAsync(path).AsUniTask()
                    .Timeout(TimeSpan.FromSeconds(15), DelayType.Realtime);
                if (loadVersion != _videoLoadVersion)
                {
                    return;
                }
                _usePictureAsBackground = false;
                _coverRenderer.enabled = false;
                OnVideoTextureChanged(_videoPlayer, _videoPlayer.Texture);
            }
            catch (Exception e)
            {
                // A newer load, a picture change, or destruction owns the background now.
                if (loadVersion != _videoLoadVersion)
                {
                    return;
                }
                if (e is TimeoutException)
                {
                    MajDebug.LogError("MAJTEXT_ERR_VIDEO_PLAYER_PREPARE_TIMEOUT".i18n());
                }
                else
                {
                    MajDebug.LogException(e);
                }
                SetBackgroundPic(fallback);
            }
        }

        void OnVideoTextureChanged(FFmpegVideoPlayer player, Texture? texture)
        {
            if (_videoRenderer == null)
            {
                return;
            }
            _videoRenderer.texture = texture!;
            _videoRenderer.enabled = !_usePictureAsBackground && texture != null;
            if (texture != null)
            {
                var scale = (float)texture.height / texture.width;
                _videoRenderer.transform.localScale = new Vector3(1f, scale, 1f);
            }
        }

        void OnVideoError(FFmpegVideoPlayer player, string error)
        {
            // Preparation failures are handled by SetMovieAsync; playback failures
            // also need to restore the cover after the preload task has completed.
            if (!_usePictureAsBackground)
            {
                SetBackgroundPic(_videoFallback);
            }
        }
    }
}

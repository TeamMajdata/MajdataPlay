#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || UNITY_ANDROID || UNITY_IOS
#define VLCUNITY_SUPPORTED
#endif
using Cysharp.Threading.Tasks;
using MajdataPlay.Extensions;
using MajdataPlay.IO;
using MajdataPlay.Scenes.View;
using MajdataPlay.Utils;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Video;
#if VLCUNITY_SUPPORTED
using LibVLCSharp;
#endif
using UnityEngine.UI;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using MajdataPlay.Diagnostics;
using MajdataPlay.Numerics;
#nullable enable
namespace MajdataPlay.Scenes.Game
{
    public class BGManager : MajBehaviour
    {
        public float CurrentSec
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if VLCUNITY_SUPPORTED
                if (_videoOutput != null)
                    return _videoPlayer.Time / 1000f;
#endif
                return _unityVideoPlayer != null ? (float)_unityVideoPlayer.time : 0;
            }
        }
        public TimeSpan MediaLength
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return TimeSpan.FromMilliseconds(_mediaLengthMs);
            }
        }
        
        [SerializeField]
        Vector3 _defaultScale;

        [SerializeField]
        Image _coverRenderer;

        [SerializeField]
        RawImage _videoRenderer;

        [SerializeField]
        Sprite _defaultSprite;

        Material _backgroundMaterial;

#if VLCUNITY_SUPPORTED
        MediaPlayer _videoPlayer;
        VlcVideoOutput? _videoOutput;
#endif
        VideoPlayer _unityVideoPlayer;

        // when copying native Texture2D textures to Unity RenderTextures, the orientation mapping is incorrect on Android, so we flip it over.
        [SerializeField]
        bool _flipTextureX = true;
        [SerializeField]
        bool _flipTextureY = true;

        bool _usePictureAsBackground = false;

        long _mediaLengthMs = 0;

        protected override void Awake()
        {
            base.Awake();
            Majdata<BGManager>.Instance = this;
            if(_defaultSprite == null)
            {
                _defaultSprite = RuntimeDatabase.Sprite.EmptySongCover;
            }
#if VLCUNITY_SUPPORTED
            if (MajEnv.VLCLibrary != null)
            {
                try
                {
                    _videoOutput = new VlcVideoOutput(MajEnv.VLCLibrary, _flipTextureX, _flipTextureY);
                    _videoPlayer = _videoOutput.Player;
                    _videoPlayer.FileCaching = 0;
                    _videoPlayer.NetworkCaching = 0;
                }
                catch (Exception error)
                {
                    MajDebug.LogWarning("[VLC] Video output unavailable; using Unity VideoPlayer. " + error.Message);
                }
            }
            if (_videoOutput == null)
#endif
            {
                _unityVideoPlayer = GetComponent<VideoPlayer>();
                if (_unityVideoPlayer == null)
                    _unityVideoPlayer = gameObject.AddComponent<VideoPlayer>();
                _unityVideoPlayer.playOnAwake = false;
                _unityVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                _unityVideoPlayer.renderMode = VideoRenderMode.APIOnly;
            }
            _backgroundMaterial = _coverRenderer.material;
            _defaultScale = transform.localScale;
        }
        void OnDestroy()
        {
#if VLCUNITY_SUPPORTED
            MajDebug.LogInfo("[VLC] DestroyMediaPlayer");
            _videoRenderer.texture = null;
            _videoOutput?.Dispose();
#endif
            Majdata<BGManager>.Free();
        }
        internal void OnLateUpdate()
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
                VLCLateUpdate();
#endif
        }

        public void PauseVideo()
        {
            if (_usePictureAsBackground)
            {
                return;
            }

#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
            {
                _videoPlayer.Pause();
                return;
            }
#endif
            _unityVideoPlayer.Pause();
        }

        public void StopVideo()
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
            {
                _videoPlayer.Stop();
                return;
            }
#endif
            _unityVideoPlayer.Stop();
        }
        public void PlayVideo(float time,float speed)
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
            {
                _videoPlayer.SetRate(speed);
                _videoPlayer.SeekTo(TimeSpan.FromSeconds(time));
                _videoPlayer.Play();
                return;
            }
#endif
            _unityVideoPlayer.playbackSpeed = speed;
            _unityVideoPlayer.time = time;
            _unityVideoPlayer.Play();
        }

        public void SetVideoSpeed(float speed)
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
            {
                _videoPlayer.SetRate(speed);
                return;
            }
#endif
            _unityVideoPlayer.playbackSpeed = speed;
        }

        public void SetBackgroundPic(Sprite? sprite)
        {
            DisableVideo();
            var rectTransform = _coverRenderer.transform.GetComponent<RectTransform>();
            if (sprite is null) 
            { 
                _coverRenderer.sprite = _defaultSprite;
                _coverRenderer.transform.localScale = _defaultScale;
                rectTransform.sizeDelta = new Vector2(1080, 1080);
                return; 
            }
            _coverRenderer.sprite = sprite;
            //todo:set correct scale
            var tex = sprite.texture;
            var scale = 1080f / tex.width;
            rectTransform.sizeDelta = new Vector2(tex.width, tex.height);
            _coverRenderer.transform.localScale = new Vector3(scale, scale, scale);
        }

        public void DisableVideo()
        {
            _usePictureAsBackground = true;
            //Disable rawimage optional
            _videoRenderer.enabled = false;
            _coverRenderer.enabled = true;
#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
            {
                _videoPlayer.Stop();
                _videoPlayer.Media = null;
                return;
            }
#endif
            _unityVideoPlayer.Stop();
            _unityVideoPlayer.url = null;
        }
        public void SetBrightness(float brightness)
        {
            _backgroundMaterial.SetFloat("_Brightness", brightness.Clamp(0, 1));
        }

        public async UniTask SetMovieAsync(string path, Sprite? fallback)
        {
#if VLCUNITY_SUPPORTED
            if (_videoOutput != null)
            {
                await SetVlcMovieAsync(path, fallback);
                return;
            }
#endif
            await SetUnityMovieAsync(path, fallback);
        }

#if VLCUNITY_SUPPORTED
        async UniTask SetVlcMovieAsync(string path, Sprite? fallback)
        {
            var cancellationToken = destroyCancellationToken;
            var output = _videoOutput!;
            try
            {
                var previousMedia = _videoPlayer.Media;
                DisableVideo();
                previousMedia?.Dispose();
                _videoRenderer.texture = null;
                var initializedAt = MajTimeline.UnscaledTime;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var state = _videoPlayer.State;
                    if (output.IsReady && (state == VLCState.NothingSpecial || state == VLCState.Stopped))
                        break;
                    if (MajTimeline.UnscaledTime - initializedAt > TimeSpan.FromSeconds(5))
                        throw new TimeoutException("VLC graphics output initialization timed out.");
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                MajDebug.LogInfo("[VLC] LoadMedia");

                var trimmedPath = path.Trim(new char[] { '"' });//Windows likes to copy paths with quotes but Uri does not like to open them
                var uri = new Uri(trimmedPath);
                MajDebug.LogInfo("[VLC] Uri: " + uri.ToString());
                using var media = new Media(uri);
                //media.AddOption(":start-paused");
                _videoPlayer.Media = media;


                MajDebug.LogInfo("[VLC] BeginParse");
                var ret = await media.ParseAsync(MajEnv.VLCLibrary!);
                cancellationToken.ThrowIfCancellationRequested();
                if(ret != MediaParsedStatus.Done)
                {
                    SetBackgroundPic(fallback);
                    return;
                }
                MajDebug.LogInfo("[VLC] " + ret);
                if (!_videoPlayer.Play())
                    throw new InvalidOperationException("VLC could not start the background video.");
                _mediaLengthMs = media.Duration;
                var preparingAt = MajTimeline.UnscaledTime;
                // Keep picture mode until a frame is available: normal LateUpdate
                // must not consume the first frame while imports/fallback initialize.
                while (!output.TryUpdateTexture() || output.Texture == null)
                {
                    if (MajTimeline.UnscaledTime - preparingAt > TimeSpan.FromSeconds(15))
                        throw new TimeoutException("VLC background video did not produce a frame.");
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var texture = output.Texture;
                _videoRenderer.texture = texture;
                _videoRenderer.gameObject.transform.localScale = new Vector3(1f, (float)texture.height / texture.width, 1f);
                _videoPlayer.SetPause(true);
                _videoPlayer.SeekTo(TimeSpan.Zero);
                _coverRenderer.enabled = false;
                _videoRenderer.enabled = true;
                _usePictureAsBackground = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // OnDestroy already owns player/texture cleanup.
            }
            catch(Exception e)
            {
                MajDebug.LogException(e);
                SetBackgroundPic(fallback);
            }
        }
#endif

        async UniTask SetUnityMovieAsync(string path, Sprite? fallback)
        {
            var cancellationToken = destroyCancellationToken;
            try
            {
                DisableVideo();
                _unityVideoPlayer.url = new Uri(System.IO.Path.GetFullPath(path.Trim('"'))).AbsoluteUri;
                _unityVideoPlayer.Prepare();
                var startAt = MajTimeline.UnscaledTime;
                while (!_unityVideoPlayer.isPrepared)
                {
                    if (MajTimeline.UnscaledTime - startAt > TimeSpan.FromSeconds(15))
                        throw new TimeoutException("MAJTEXT_ERR_VIDEO_PLAYER_PREPARE_TIMEOUT".i18n());
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                _coverRenderer.enabled = false;
                _videoRenderer.enabled = true;
                _usePictureAsBackground = false;
                _videoRenderer.texture = _unityVideoPlayer.texture;
                var scale = (float)_unityVideoPlayer.height / (float)_unityVideoPlayer.width;
                _videoRenderer.gameObject.transform.localScale = new Vector3(1f, scale, 1f);
                _mediaLengthMs = (long)(_unityVideoPlayer.length * 1000);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The VideoPlayer component is destroyed with this GameObject.
            }
            catch (Exception error)
            {
                MajDebug.LogException(error);
                SetBackgroundPic(fallback);
            }
        }

#if VLCUNITY_SUPPORTED
        void VLCLateUpdate()
        {
            if (_videoOutput is null)
            {
                return;
            }
            // Also drain the last decoded frame after pause or a seek.
            if (!_videoOutput.TryUpdateTexture())
            {
                return;
            }
            var texture = _videoOutput.Texture;
            _videoRenderer.texture = texture;
            _videoRenderer.gameObject.transform.localScale = new Vector3(1f, (float)texture.height / texture.width, 1f);
        }

        public VideoOrientation? GetVideoOrientation()
        {
            var tracks = _videoPlayer?.Tracks(TrackType.Video);

            if (tracks == null || tracks.Count == 0)
                return null;

            var orientation = tracks[0]?.Data.Video.Orientation; //At the moment we're assuming the track we're playing is the first track

            return orientation;
        }
#endif
    }
}

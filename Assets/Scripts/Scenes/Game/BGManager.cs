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
#if UNITY_STANDALONE_WIN
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
#if UNITY_STANDALONE_WIN
                return _videoPlayer.Time / 1000f;
#else
                return (float)_videoPlayer.time;
#endif
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

        // This is the texture libVLC writes to directly. It's private.
        Texture2D? _vlcTexture = null;
        // We copy it into this texture which we actually use in unity.
        [SerializeField]
        RenderTexture? _renderTexture = null;

#if UNITY_STANDALONE_WIN
        MediaPlayer _videoPlayer;
#else
        VideoPlayer _videoPlayer;
#endif

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
#if UNITY_STANDALONE_WIN
            _videoPlayer = new MediaPlayer(MajEnv.VLCLibrary)
            {
                FileCaching = 0,
                NetworkCaching = 0,
            };
#else
            _videoPlayer = GetComponent<VideoPlayer>();
#endif
            _backgroundMaterial = _coverRenderer.material;
            _defaultScale = transform.localScale;
        }
        void OnDestroy()
        {
#if UNITY_STANDALONE_WIN
            MajDebug.LogInfo("[VLC] DestroyMediaPlayer");
            _videoPlayer.Stop();
            _videoPlayer.Dispose();
#endif
            Majdata<BGManager>.Free();
        }
        [Conditional("UNITY_STANDALONE_WIN")]
        internal void OnLateUpdate()
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if UNITY_STANDALONE_WIN
            VLCLateUpdate();    
#endif
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
        public void PlayVideo(float time,float speed)
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if UNITY_STANDALONE_WIN
            _videoPlayer.SetRate(speed);
            _videoPlayer.SeekTo(TimeSpan.FromSeconds(time));
            _videoPlayer.Play();
#else
            _videoPlayer.playbackSpeed = speed;
            _videoPlayer.time = time;
            _videoPlayer.Play();
#endif
        }

        public void SetVideoSpeed(float speed)
        {
            if (_usePictureAsBackground)
            {
                return;
            }
#if UNITY_STANDALONE_WIN
            _videoPlayer.SetRate(speed);
#else
            _videoPlayer.playbackSpeed = speed;
#endif
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
            if (_usePictureAsBackground)
            {
                return;
            }
            _usePictureAsBackground = true;
            //Disable rawimage optional
            _videoRenderer.enabled = false;
#if UNITY_STANDALONE_WIN
            _videoPlayer.Media = null;
#else
            _videoPlayer.url = null;
            _videoPlayer.Stop();
#endif
        }
        public void SetBrightness(float brightness)
        {
            _backgroundMaterial.SetFloat("_Brightness", brightness.Clamp(0, 1));
        }

        public async UniTask SetMovieAsync(string path, Sprite? fallback)
        {
#if UNITY_STANDALONE_WIN // VLC Unity
            try
            {

                MajDebug.LogInfo("[VLC] LoadMedia");
                if (_videoPlayer.Media != null)
                {
                    _videoPlayer.Media.Dispose();
                }

                var trimmedPath = path.Trim(new char[] { '"' });//Windows likes to copy paths with quotes but Uri does not like to open them
                var uri = new Uri(trimmedPath);
                MajDebug.LogInfo("[VLC] Uri: " + uri.ToString());
                var media = new Media(uri);
                //media.AddOption(":start-paused");
                _videoPlayer.Media = media;


                MajDebug.LogInfo("[VLC] BeginParse");
                var ret = await _videoPlayer.Media.ParseAsync(MajEnv.VLCLibrary!);
                if(ret != MediaParsedStatus.Done)
                {
                    SetBackgroundPic(fallback);
                    return;
                }
                MajDebug.LogInfo("[VLC] " + ret);
                _coverRenderer.enabled = false;
                _usePictureAsBackground = false;
                _videoPlayer.Play();
                _mediaLengthMs = media.Duration;
                await UniTask.Delay(200);
                _videoPlayer.Pause();
                _videoPlayer.SeekTo(TimeSpan.Zero);
            }
            catch(Exception e)
            {
                MajDebug.LogException(e);
                SetBackgroundPic(fallback);
            }
#else // Unity VideoPlayer
            _videoPlayer.url = "file://" + path;
            _videoPlayer.Prepare();
            var startAt = MajTimeline.UnscaledTime;
            var timeout = TimeSpan.FromSeconds(15);
            while (true)
            {
                try
                {
                    var remainingTime = timeout - (MajTimeline.UnscaledTime - startAt);
                    if (remainingTime.TotalSeconds < 0)
                    {
                        MajDebug.LogError("MAJTEXT_ERR_VIDEO_PLAYER_PREPARE_TIMEOUT".i18n());
                        SetBackgroundPic(fallback);
                        return;
                    }
                    if (_videoPlayer.isPrepared)
                        break;
                }
                finally
                {
                    await UniTask.Yield();
                }
            }
            _coverRenderer.enabled = false;
            _videoRenderer.texture = _videoPlayer.texture;
            var scale = (float)_videoPlayer.height / (float)_videoPlayer.width;
            _videoRenderer.gameObject.transform.localScale = new Vector3(1f, scale, 1f);
            _mediaLengthMs = (long)(_videoPlayer.length * 1000);
#endif
        }

#if UNITY_STANDALONE_WIN
        void VLCLateUpdate()
        {
            if (_videoPlayer is null)
            {
                return;
            }
            if (!_videoPlayer.IsPlaying)
            {
                return;
            }

            //Get size every frame
            uint height = 0;
            uint width = 0;
            IntPtr texPtr;
            _videoPlayer.Size(0, ref width, ref height);
            //Update the vlc texture (tex)
            texPtr = _videoPlayer.GetTexture(width, height, out var isUpdated);

            if (!isUpdated)
            {
                return;
            }

            if (_vlcTexture is null || _vlcTexture.width != width || _vlcTexture.height != height)
            {
                var px = width;
                var py = height;
                //If the currently playing video uses the Bottom Right orientation, we have to do this to avoid stretching it.
                if (GetVideoOrientation() == VideoOrientation.BottomRight)
                {
                    uint swap = px;
                    px = py;
                    py = swap;
                }

                var scale = (float)py / (float)px;

                //Make a texture of the proper size for the video to output to
                _vlcTexture = Texture2D.CreateExternalTexture((int)px, (int)py, TextureFormat.RGBA32, false, true, texPtr);
                //Make a renderTexture the same size as vlctex
                _renderTexture = new RenderTexture(_vlcTexture.width, _vlcTexture.height, 0, RenderTextureFormat.ARGB32);

                _videoRenderer.texture = _renderTexture;
                _videoRenderer.gameObject.transform.localScale = new Vector3(1f, scale, 1f);
            }


            _vlcTexture.UpdateExternalTexture(texPtr);

            //Copy the vlc texture into the output texture, automatically flipped over
            var flip = new Vector2(_flipTextureX ? -1 : 1, _flipTextureY ? -1 : 1);
            //If you wanted to do post processing outside of VLC you could use a shader here.
            Graphics.Blit(_vlcTexture, _renderTexture, flip, Vector2.zero);
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

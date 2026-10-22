using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.FFmpeg;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the cover and video background handling of <see cref="BGManager"/> in Play Mode, without media files.</summary>
    public class BGManagerPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the registered background manager.</summary>
        private SingletonScope<BGManager> _slot = null!;

        /// <summary>The default cover sprite.</summary>
        private Sprite _defaultSprite = null!;

        /// <summary>A 540 by 270 cover sprite.</summary>
        private Sprite _wideSprite = null!;

        /// <summary>Creates the cover sprites.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _slot = new SingletonScope<BGManager>();
            var small = _objects.Track(new Texture2D(4, 4));
            _defaultSprite = _objects.Track(Sprite.Create(small, new Rect(0, 0, 4, 4), Vector2.zero));
            var wide = _objects.Track(new Texture2D(540, 270));
            _wideSprite = _objects.Track(Sprite.Create(wide, new Rect(0, 0, 540, 270), Vector2.zero));
        }

        /// <summary>Destroys every object and restores the registration.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _slot.Dispose();
        }

        /// <summary>Checks that Awake registers the manager, shares one private material and starts with the cover.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Awake_StartsWithTheCoverAndASharedMaterial()
        {
            var manager = CreateManager(out var cover, out var video);
            yield return null;

            Assert.That(Majdata<BGManager>.Instance, Is.SameAs(manager));
            Assert.That(cover.material, Is.SameAs(video.material));
            Assert.That(cover.enabled, Is.True);
            Assert.That(video.enabled, Is.False);
            Assert.That(manager.IsVideoEnded, Is.False);
            Assert.That(manager.CurrentSec, Is.EqualTo(0f));
            Assert.That(manager.MediaLength, Is.EqualTo(System.TimeSpan.Zero));
        }

        /// <summary>Checks that a missing cover shows the default sprite at the default scale.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetBackgroundPic_FallsBackToTheDefaultSprite()
        {
            var manager = CreateManager(out var cover, out _, coverScale: new Vector3(2, 2, 2));
            yield return null;
            manager.SetBackgroundPic(_wideSprite);
            Assert.That(cover.transform.localScale, Is.EqualTo(new Vector3(2, 2, 2)));
            cover.transform.localScale = Vector3.one;

            manager.SetBackgroundPic(null);

            Assert.That(cover.sprite, Is.SameAs(_defaultSprite));
            Assert.That(cover.rectTransform.sizeDelta, Is.EqualTo(new Vector2(1080, 1080)));
            Assert.That(cover.transform.localScale, Is.EqualTo(new Vector3(2, 2, 2)), "The scale captured in Awake is restored");
        }

        /// <summary>Checks that a cover is sized to its texture and scaled to the 1080 pixel width.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetBackgroundPic_ScalesTheCoverToTheScreenWidth()
        {
            var manager = CreateManager(out var cover, out _);
            yield return null;

            manager.SetBackgroundPic(_wideSprite);

            Assert.That(cover.sprite, Is.SameAs(_wideSprite));
            Assert.That(cover.rectTransform.sizeDelta, Is.EqualTo(new Vector2(540, 270)));
            Assert.That(cover.transform.localScale, Is.EqualTo(new Vector3(2, 2, 2)));
        }

        /// <summary>Checks that the brightness is clamped into the shader range.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetBrightness_ClampsTheValue()
        {
            var shader = Shader.Find("Maj/Common/BrightnessAdjust");
            Assume.That(shader, Is.Not.Null, "The background shader is part of the project");
            var manager = CreateManager(out var cover, out _, _objects.Track(new Material(shader)));
            yield return null;

            manager.SetBrightness(1.5f);
            Assert.That(cover.material.GetFloat("_Brightness"), Is.EqualTo(1f));

            manager.SetBrightness(-1f);
            Assert.That(cover.material.GetFloat("_Brightness"), Is.EqualTo(0f));

            manager.SetBrightness(0.25f);
            Assert.That(cover.material.GetFloat("_Brightness"), Is.EqualTo(0.25f));
        }

        /// <summary>Checks that video controls are ignored while the cover is shown.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator VideoControls_AreIgnoredWithACover()
        {
            var manager = CreateManager(out _, out _);
            var player = manager.GetComponent<FFmpegVideoPlayer>();
            yield return null;

            manager.SetVideoSpeed(2f);
            manager.PlayVideo(1f, 3f);
            manager.PauseVideo();
            manager.StopVideo();

            Assert.That(player.PlaybackRate, Is.EqualTo(1f));
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Idle));
        }

        /// <summary>Checks that video controls reach the player once a video replaced the cover.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator VideoControls_ReachThePlayerForAVideo()
        {
            var manager = CreateManager(out _, out _);
            var player = manager.GetComponent<FFmpegVideoPlayer>();
            yield return null;
            PrivateMember.SetField(manager, "_usePictureAsBackground", false);

            manager.SetVideoSpeed(2f);
            manager.PlayVideo(1f, 3f);
            manager.PauseVideo();
            Assert.That(player.PlaybackRate, Is.EqualTo(2f), "Playing an unprepared video is ignored");

            manager.StopVideo();
            Assert.That(player.State, Is.EqualTo(VideoPlaybackState.Stopped));
            Assert.That(manager.IsVideoEnded, Is.False);
        }

        /// <summary>Checks that a new video texture is shown with its aspect ratio and a missing texture hides the video.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnVideoTextureChanged_ShowsTheTexture()
        {
            var manager = CreateManager(out _, out var video);
            var player = manager.GetComponent<FFmpegVideoPlayer>();
            var frame = _objects.Track(new Texture2D(200, 100));
            yield return null;

            PrivateMember.Invoke(manager, "OnVideoTextureChanged", player, frame);
            Assert.That(video.enabled, Is.False, "The cover is still in use");

            PrivateMember.SetField(manager, "_usePictureAsBackground", false);
            PrivateMember.Invoke(manager, "OnVideoTextureChanged", player, frame);
            Assert.That(video.enabled, Is.True);
            Assert.That(video.texture, Is.SameAs(frame));
            Assert.That(video.transform.localScale, Is.EqualTo(new Vector3(1f, 0.5f, 1f)));

            PrivateMember.Invoke(manager, "OnVideoTextureChanged", player, null);
            Assert.That(video.enabled, Is.False);
        }

        /// <summary>Checks that a playback error restores the fallback cover only while a video is shown.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnVideoError_RestoresTheFallbackCover()
        {
            var manager = CreateManager(out var cover, out var video);
            var player = manager.GetComponent<FFmpegVideoPlayer>();
            yield return null;

            PrivateMember.Invoke(manager, "OnVideoError", player, "ignored");
            Assert.That(cover.sprite, Is.Null, "A cover background ignores video errors");

            PrivateMember.SetField(manager, "_usePictureAsBackground", false);
            PrivateMember.SetField(manager, "_videoFallback", _wideSprite);
            video.enabled = true;
            cover.enabled = false;
            PrivateMember.Invoke(manager, "OnVideoError", player, "decode failed");

            Assert.That(cover.sprite, Is.SameAs(_wideSprite));
            Assert.That(cover.enabled, Is.True);
            Assert.That(video.enabled, Is.False);
            Assert.That(PrivateMember.GetField<bool>(manager, "_usePictureAsBackground"), Is.True);
        }

        /// <summary>Checks that destroying the manager unregisters it, releases its material and ignores later movie requests.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_ReleasesTheManager() => UniTask.ToCoroutine(async () =>
        {
            var manager = CreateManager(out var cover, out _);
            await UniTask.Yield();
            var material = cover.material;

            UnityEngine.Object.Destroy(manager.gameObject);
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.That(Majdata<BGManager>.Instance, Is.Null);
            Assert.That(material == null, Is.True);
            Assert.That(PrivateMember.GetField<bool>(manager, "_destroyed"), Is.True);
            await manager.SetMovieAsync("missing.mp4", _wideSprite);
            PrivateMember.Invoke(manager, "OnDestroy");
        });

        /// <summary>Creates an active background manager with a cover image and a video image.</summary>
        /// <param name="cover">Receives the cover image.</param>
        /// <param name="video">Receives the video image.</param>
        /// <param name="material">An optional material for the cover, copied by the manager.</param>
        /// <param name="coverScale">An optional scale of the cover before Awake, captured as its default scale.</param>
        /// <returns>The manager, awakened by Unity.</returns>
        private BGManager CreateManager(out Image cover, out RawImage video, Material? material = null, Vector3? coverScale = null)
        {
            var root = _objects.Create("Background");
            root.SetActive(false);
            var manager = root.AddComponent<BGManager>();
            cover = _objects.Add<Image>("Cover", root.transform);
            video = _objects.Add<RawImage>("Video", root.transform);
            if (material != null)
            {
                cover.material = material;
            }
            if (coverScale is Vector3 scale)
            {
                cover.transform.localScale = scale;
            }
            PrivateMember.SetField(manager, "_coverRenderer", cover);
            PrivateMember.SetField(manager, "_videoRenderer", video);
            PrivateMember.SetField(manager, "_defaultSprite", _defaultSprite);
            root.SetActive(true);
            return manager;
        }
    }
}

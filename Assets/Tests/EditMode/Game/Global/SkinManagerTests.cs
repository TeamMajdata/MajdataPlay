using System.Collections;
using Cysharp.Threading.Tasks;
using MajdataPlay.IO.Storage;
using MajdataPlay.Scenes.Game.Notes.Skins;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Global
{
    /// <summary>Verifies skin discovery and the note skin views of <see cref="SkinManager"/> with an empty skin directory.</summary>
    public class SkinManagerTests
    {
        /// <summary>The static sprite tables restored after each test.</summary>
        private static readonly string[] s_spriteTables =
        {
            "_tapLines", "_starLines", "_holdEnds", "_touchHoldFans", "_touchHoldBreakFans", "_touchHoldMineFans", "_touchHoldBreakMineFans",
        };

        /// <summary>The copies of the sprite tables before the test.</summary>
        private readonly Hashtable _savedTables = new();

        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The installed default settings.</summary>
        private GlobalSettingsScope _settings = null!;

        /// <summary>The temporary skin directory.</summary>
        private TemporaryDirectory _temporary = null!;

        /// <summary>The skin directory before the test.</summary>
        private StorageDirectory? _savedSkinDirectory;

        /// <summary>The manager under test, created without its lifecycle.</summary>
        private SkinManager _manager = null!;

        /// <summary>Points the skin directory at an empty temporary directory and creates the manager.</summary>
        [SetUp]
        public void SetUp()
        {
            foreach (var name in s_spriteTables)
            {
                _savedTables[name] = PrivateMember.GetStaticField<Sprite[]>(typeof(SkinManager), name).Clone();
            }
            _objects = new TestObjects();
            _settings = new GlobalSettingsScope();
            _temporary = new TemporaryDirectory();
            _savedSkinDirectory = MajEnv.SkinDirectory;
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.SkinDirectory), _temporary.Directory);
            _manager = _objects.Add<SkinManager>();
        }

        /// <summary>Restores the static state.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var name in s_spriteTables)
            {
                ((Sprite[])_savedTables[name]!).CopyTo(PrivateMember.GetStaticField<Sprite[]>(typeof(SkinManager), name), 0);
            }
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.SkinDirectory), _savedSkinDirectory);
            _objects.Dispose();
            _temporary.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that an empty skin directory falls back to the built-in empty skin.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator InitAsync_EmptyDirectory_UsesTheBuiltInSkin()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Assert.That(_manager.IsInited, Is.False);

                await _manager.InitAsync();

                Assert.That(_manager.IsInited, Is.True);
                Assert.That(_manager.SelectedSkin, Is.SameAs(CustomSkin.Empty));
                Assert.That(_manager.LoadedSkins, Is.EqualTo(new[] { CustomSkin.Empty }));
            });
        }

        /// <summary>Checks that every note skin view is built from the selected skin and the shared tables.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SkinViews_UseTheSelectedSkin()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await _manager.InitAsync();
                AssertSkinViews(_manager, CustomSkin.Empty);
            });
        }

        /// <summary>Asserts that every note skin view of a manager is built from a skin and the shared tables.</summary>
        /// <param name="manager">The initialized manager.</param>
        /// <param name="skin">The selected skin.</param>
        private static void AssertSkinViews(SkinManager manager, CustomSkin skin)
        {
            var tap = manager.GetTapSkin();
            var star = manager.GetStarSkin();
            var hold = manager.GetHoldSkin();
            var slide = manager.GetSlideSkin();
            var wifi = manager.GetWifiSkin();
            var touchHold = manager.GetTouchHoldSkin();
            var touch = manager.GetTouchSkin();
            var eachLine = manager.GetEachLineSkin();
            var judge = manager.GetJudgeTextSkin();

            Assert.That(tap.Normal, Is.SameAs(skin.Tap));
            Assert.That(tap.GuideLines[0], Is.SameAs(skin.TapLine_Normal));
            Assert.That(tap.ExEffects.Length, Is.EqualTo(3));
            Assert.That(star.Normal, Is.SameAs(skin.Star));
            Assert.That(star.GuideLines[0], Is.SameAs(skin.TapLine_Slide));
            Assert.That(star.ExEffects[0], Is.EqualTo(Color.white));
            Assert.That(hold.Normal, Is.SameAs(skin.Hold));
            Assert.That(hold.Ends[0], Is.SameAs(skin.HoldEndPoint_Normal));
            Assert.That(slide.Normal, Is.SameAs(skin.Slide));
            Assert.That(slide.Star.Normal, Is.SameAs(skin.Star));
            Assert.That(wifi.Normal[0], Is.SameAs(skin.Wifi[0]));
            Assert.That(touchHold.Fans[0], Is.SameAs(skin.TouchHold[0]));
            Assert.That(touchHold.Boader, Is.SameAs(skin.TouchHold[4]));
            Assert.That(touch.Normal, Is.SameAs(skin.Touch));
            Assert.That(touch.JustBorder, Is.SameAs(skin.TouchJust));
            Assert.That(eachLine.EachGuideLines[0], Is.SameAs(skin.EachLines[0]));
            Assert.That(judge.Miss, Is.SameAs(skin.Miss));
            Assert.That(judge.CriticalPerfect.Normal, Is.SameAs(skin.CriticalPerfect));
        }

        /// <summary>Checks that selecting the already selected skin changes nothing and raises no event.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SelectedSkin_SameSkin_IsIgnored()
        {
            return UniTask.ToCoroutine(async () =>
            {
                await _manager.InitAsync();
                var changes = 0;
                _manager.OnSkinChanged += (_, _) => changes++;

                _manager.SelectedSkin = CustomSkin.Empty;

                Assert.That(changes, Is.EqualTo(0));
                Assert.That(_manager.SelectedSkin, Is.SameAs(CustomSkin.Empty));
            });
        }
    }
}

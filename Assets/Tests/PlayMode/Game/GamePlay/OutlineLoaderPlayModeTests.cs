using System;
using System.Collections;
using System.Reflection;
using MajdataPlay.Scenes.Game;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Settings;
using MajdataPlay.Tests;
using MajdataPlay.Tests.Game.GamePlay.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode
{
    /// <summary>Checks the skin outline and its classic-mode judge effect in <see cref="OutlineLoader"/> in Play Mode.</summary>
    public class OutlineLoaderPlayModeTests
    {
        /// <summary>Tracks objects created by a test.</summary>
        private TestObjects _objects = null!;

        /// <summary>Restores the settings.</summary>
        private GamePlaySettingsScope _settings = null!;

        /// <summary>Restores the registered skin manager.</summary>
        private SingletonScope<SkinManager> _skinSlot = null!;

        /// <summary>Restores the registered gameplay manager.</summary>
        private SingletonScope<GamePlayManager> _managerSlot = null!;

        /// <summary>Restores the registered outline loader.</summary>
        private SingletonScope<OutlineLoader> _outlineSlot = null!;

        /// <summary>The skin manager that provides the selected skin.</summary>
        private SkinManager _skinManager = null!;

        /// <summary>Registers a skin manager and a gameplay manager that were never awakened.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new GamePlaySettingsScope();
            _skinSlot = new SingletonScope<SkinManager>();
            _managerSlot = new SingletonScope<GamePlayManager>();
            _outlineSlot = new SingletonScope<OutlineLoader>();

            var services = _objects.Create("Services");
            services.SetActive(false);
            _skinManager = services.AddComponent<SkinManager>();
            var manager = services.AddComponent<GamePlayManager>();
            PrivateMember.SetField(manager, "_gameSettings", _settings.Settings);
            _skinSlot.Set(_skinManager);
            _managerSlot.Set(manager);
        }

        /// <summary>Destroys every object and restores the registrations and settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            _outlineSlot.Dispose();
            _managerSlot.Dispose();
            _skinSlot.Dispose();
            _settings.Dispose();
        }

        /// <summary>Checks that a skin without an outline effect shows the outline sprite and hides the effect.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_HidesTheEffectWithoutOutlineSupport()
        {
            UseSkin(CustomSkin.Empty);

            var loader = CreateLoader(out var renderer, out var effect);
            Assert.That(Majdata<OutlineLoader>.Instance, Is.SameAs(loader));
            yield return null;

            Assert.That(renderer.sprite, Is.SameAs(CustomSkin.Empty.Outline));
            Assert.That(effect.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<bool>(loader, "_effectAvailable"), Is.False);
            Assert.DoesNotThrow(() => loader.Play());
        }

        /// <summary>Checks that the effect is enabled in classic mode for a skin with an outline and colored by difficulty.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_ColorsTheEffectInClassicMode()
        {
            _settings.Settings.Judge.Mode = JudgeModeOption.Classic;
            _settings.RuntimeConfig.List.SelectedDiff = ChartLevel.Expert;
            UseSkin(CreateSkinWithOutline());

            var loader = CreateLoader(out var renderer, out var effect);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(loader, "_effectAvailable"), Is.True);
            Assert.That(effect.activeSelf, Is.True);
            Assert.That(renderer.color, Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
            Assert.DoesNotThrow(() => loader.Play());
        }

        /// <summary>Checks that the effect stays disabled in modern mode and in view mode.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator Start_DisablesTheEffectOutsideClassicPlay()
        {
            UseSkin(CreateSkinWithOutline());
            var modern = CreateLoader(out _, out var modernEffect);
            yield return null;

            _settings.Settings.Judge.Mode = JudgeModeOption.Classic;
            MajEnv.Mode = RunningMode.View;
            var view = CreateLoader(out _, out var viewEffect);
            yield return null;

            Assert.That(PrivateMember.GetField<bool>(modern, "_effectAvailable"), Is.False);
            Assert.That(modernEffect.activeSelf, Is.False);
            Assert.That(PrivateMember.GetField<bool>(view, "_effectAvailable"), Is.False);
            Assert.That(viewEffect.activeSelf, Is.False);
        }

        /// <summary>Checks the outline color of every difficulty.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SetColor_UsesTheDifficultyColor()
        {
            UseSkin(CustomSkin.Empty);
            var loader = CreateLoader(out var renderer, out _);
            yield return null;
            var expected = new (ChartLevel Level, Color32 Color)[]
            {
                (ChartLevel.Easy, new Color32(32, 63, 255, 255)),
                (ChartLevel.Basic, new Color32(75, 250, 65, 255)),
                (ChartLevel.Advance, new Color32(249, 230, 65, 255)),
                (ChartLevel.Expert, new Color32(255, 0, 0, 255)),
                (ChartLevel.Master, new Color32(119, 0, 255, 255)),
                (ChartLevel.ReMaster, new Color32(119, 0, 255, 255)),
                (ChartLevel.UTAGE, new Color32(255, 169, 218, 255)),
                ((ChartLevel)42, new Color32(255, 255, 255, 255)),
            };

            foreach (var (level, color) in expected)
            {
                _settings.RuntimeConfig.List.SelectedDiff = level;
                PrivateMember.Invoke(loader, "SetColor");
                Assert.That((Color32)renderer.color, Is.EqualTo(color), level.ToString());
            }
        }

        /// <summary>Checks that destroying the loader unregisters it.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator OnDestroy_UnregistersTheLoader()
        {
            UseSkin(CustomSkin.Empty);
            var loader = CreateLoader(out _, out _);
            yield return null;

            UnityEngine.Object.Destroy(loader.gameObject);
            yield return null;

            Assert.That(Majdata<OutlineLoader>.Instance, Is.Null);
        }

        /// <summary>Selects a skin on the registered skin manager without loading it.</summary>
        /// <param name="skin">The skin.</param>
        private void UseSkin(CustomSkin skin)
        {
            PrivateMember.SetField(_skinManager, "_selectedSkin", skin);
        }

        /// <summary>Creates an unloaded skin that reports an outline effect.</summary>
        /// <returns>The skin.</returns>
        private static CustomSkin CreateSkinWithOutline()
        {
            var skin = (CustomSkin)Activator.CreateInstance(typeof(CustomSkin), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "Outlined" }, null)!;
            PrivateMember.SetProperty(skin, nameof(CustomSkin.IsOutlineAvailable), true);
            return skin;
        }

        /// <summary>Creates an active outline loader with a sprite renderer and an effect animator.</summary>
        /// <param name="renderer">Receives the outline renderer.</param>
        /// <param name="effect">Receives the effect object.</param>
        /// <returns>The loader, awakened by Unity.</returns>
        private OutlineLoader CreateLoader(out SpriteRenderer renderer, out GameObject effect)
        {
            var root = _objects.Create("Outline");
            root.SetActive(false);
            renderer = root.AddComponent<SpriteRenderer>();
            var loader = root.AddComponent<OutlineLoader>();
            effect = _objects.Create("Effect", root.transform);
            PrivateMember.SetField(loader, "_effectAnim", effect.AddComponent<Animator>());
            root.SetActive(true);
            return loader;
        }
    }
}

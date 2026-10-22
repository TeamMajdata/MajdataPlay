using System;
using System.Collections;
using MajdataPlay.Databases;
using MajdataPlay.Scenes.Game.Notes.Skins;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Global.PlayMode
{
    /// <summary>Verifies the Play Mode lifecycle of the global managers, the frame rate monitor and the runtime information overlay.</summary>
    public class ManagersPlayModeTests
    {
        /// <summary>The created objects.</summary>
        private TestObjects _objects = null!;

        /// <summary>The installed default settings.</summary>
        private PlayModeSettingsScope _settings = null!;

        /// <summary>Creates the object tracker and settings.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
            _settings = new PlayModeSettingsScope();
        }

        /// <summary>Destroys the objects, releases the singletons and restores the settings.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
            FreeSingleton<GameManager>();
            FreeSingleton<SkinManager>();
            FreeSingleton<RuntimeInfoDisplayer>();
            _settings.Dispose();
        }

        /// <summary>Checks that Unity samples one frame time per LateUpdate.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator FPSMonitor_SamplesEveryFrame()
        {
            using var clock = new PlayModeClock();
            var monitor = _objects.Add<FPSMonitor>();

            for (var i = 0; i < 10; i++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(20));
                yield return null;
            }

            Assert.That(PrivateMember.GetField<uint>(monitor, "_avgFPSSampleCount"), Is.GreaterThanOrEqualTo(10));
            Assert.That(monitor.AverageFPS, Is.GreaterThan(40).And.LessThanOrEqualTo(51));
        }

        /// <summary>Checks that the overlay starts hidden and renders frame rate and version text once refreshed.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator RuntimeInfoDisplayer_RendersStatusText()
        {
            var root = InactiveObjects.Create(_objects, "RuntimeInfo");
            root.AddComponent<FPSMonitor>();
            var text = root.AddComponent<TextMeshProUGUI>();
            var displayer = root.AddComponent<RuntimeInfoDisplayer>();
            root.SetActive(true);

            Assert.That(displayer.enabled, Is.False);
            Assert.That(text.enabled, Is.False);
            Assert.That(MajdataPlay.Majdata<RuntimeInfoDisplayer>.Instance, Is.SameAs(displayer));

            PrivateMember.SetField(displayer, "_setting", MajEnv.Settings);
            PrivateMember.SetField(displayer, "_versionText", "9.8.7");
            PrivateMember.SetField(displayer, "_refreshTimer", 0f);
            displayer.enabled = true;
            yield return null;
            yield return null;

            Assert.That(text.enabled, Is.True);
            Assert.That(text.text, Does.Contain("FPS").And.Contain("Ver. 9.8.7").And.Contain("--.--"));

            PrivateMember.Invoke(displayer, "OnLanguageChanged", null, null);
            PrivateMember.SetField(displayer, "_hasNewVersionAvailable", true);
            PrivateMember.Invoke(displayer, "OnLanguageChanged", null, null);
            PrivateMember.SetField(displayer, "_newVersionAvailableText", "UPDATE");
            _settings.Settings.Debug.DisplayRuntimeInfo = false;
            yield return null;
            yield return null;

            Assert.That(text.text, Does.Contain("(UPDATE)"));
            Assert.That(text.enabled, Is.False);

            UnityEngine.Object.Destroy(root);
            yield return null;
            Assert.That(MajdataPlay.Majdata<RuntimeInfoDisplayer>.IsNull, Is.True, "destroying the overlay releases the singleton");
        }

        /// <summary>Checks that the game manager registers itself and the runtime database on Awake.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator GameManager_AwakeRegistersSingletons()
        {
            var root = InactiveObjects.Create(_objects, "GameManager");
            var manager = root.AddComponent<GameManager>();
            manager.enabled = false;

            root.SetActive(true);
            yield return null;

            Assert.That(MajdataPlay.Majdata<GameManager>.Instance, Is.SameAs(manager));
            Assert.That(GameRuntime.Instance, Is.Not.Null);
            Assert.That(manager.Tag, Is.EqualTo("Untagged"));
            manager.SetActive(false);
            Assert.That(root.layer, Is.EqualTo(MajEnv.HIDDEN_LAYER));
            Assert.That(manager.Active, Is.False);
            manager.SetActive(true);
            Assert.That(root.layer, Is.EqualTo(MajEnv.DEFAULT_LAYER));
        }

        /// <summary>Checks that the skin manager registers itself on Awake and serves the built-in skin once selected.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator SkinManager_AwakeRegistersSingleton()
        {
            var manager = _objects.Add<SkinManager>();
            yield return null;

            PrivateMember.SetField(manager, "_selectedSkin", CustomSkin.Empty);

            Assert.That(MajdataPlay.Majdata<SkinManager>.Instance, Is.SameAs(manager));
            Assert.That(manager.GetTouchSkin().Normal, Is.SameAs(CustomSkin.Empty.Touch));
        }

        /// <summary>Releases a singleton registered by a component under test.</summary>
        /// <typeparam name="T">The singleton type.</typeparam>
        private static void FreeSingleton<T>() where T : class
        {
            if (MajdataPlay.Majdata<T>.IsSingleton)
            {
                MajdataPlay.Majdata<T>.Free();
            }
        }
    }
}

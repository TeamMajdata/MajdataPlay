using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MajdataPlay.Scenes.Game.Notes.Skins;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>
    /// Covers <see cref="SkinEnumerator"/>: the note skin option lists the loaded skins, starts on the selected one and
    /// switches the <see cref="SkinManager"/> selection as the player cycles through it.
    /// </summary>
    public class SkinEnumeratorTests : SceneTestBase
    {
        /// <summary>A settings section holding a skin name option.</summary>
        public sealed class SkinProbe
        {
            /// <summary>The editable skin option.</summary>
            public string Skin { get; set; } = string.Empty;

            /// <summary>A skin option marked read-only.</summary>
            [ReadOnlyOption]
            public string Locked { get; set; } = string.Empty;
        }

        /// <summary>The static sprite tables the skin manager rewrites when the selection changes.</summary>
        private static readonly string[] s_spriteTables =
        {
            "_tapLines", "_starLines", "_holdEnds", "_touchHoldFans", "_touchHoldBreakFans", "_touchHoldMineFans", "_touchHoldBreakMineFans",
        };

        /// <summary>The copies of the sprite tables before the test.</summary>
        private readonly Dictionary<string, Sprite[]> _savedTables = new();

        /// <summary>The skins reported as changed by the manager, in order.</summary>
        private readonly List<CustomSkin> _changes = new();

        /// <summary>The skin manager stub the enumerator talks to.</summary>
        private SkinManager _manager = null!;

        /// <summary>Snapshots the sprite tables and installs the skin manager stub.</summary>
        [SetUp]
        public void SetUp()
        {
            foreach (var name in s_spriteTables)
            {
                _savedTables[name] = (Sprite[])PrivateMember.GetStaticField<Sprite[]>(typeof(SkinManager), name).Clone();
            }
            _changes.Clear();
            _manager = CreateSkinManagerStub();
            _manager.OnSkinChanged += (_, skin) => _changes.Add(skin);
        }

        /// <summary>Restores the sprite tables.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var name in s_spriteTables)
            {
                _savedTables[name].CopyTo(PrivateMember.GetStaticField<Sprite[]>(typeof(SkinManager), name), 0);
            }
        }

        /// <summary>
        /// Creates a named skin that, like the built-in empty skin, reports itself as loaded and refuses to unload, so
        /// selecting it or switching away from it performs no asset I/O.
        /// </summary>
        /// <param name="name">The skin name.</param>
        /// <returns>The skin.</returns>
        private static CustomSkin CreateSkin(string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var skin = (CustomSkin)Activator.CreateInstance(typeof(CustomSkin), flags, null, new object[] { name }, null)!;
            typeof(CustomSkin).GetField("<IsLoaded>k__BackingField", flags)!.SetValue(skin, true);
            PrivateMember.SetField(skin, "_canUnload", false);
            return skin;
        }

        /// <summary>Installs the loaded skins and the selected skin on the manager stub.</summary>
        /// <param name="selected">The selected skin; it is created separately when it is not one of the loaded names.</param>
        /// <param name="names">The loaded skin names.</param>
        /// <returns>The loaded skins, in order.</returns>
        private CustomSkin[] InstallSkins(string selected, params string[] names)
        {
            var skins = new CustomSkin[names.Length];
            CustomSkin? current = null;
            for (var i = 0; i < names.Length; i++)
            {
                skins[i] = CreateSkin(names[i]);
                if (names[i] == selected)
                {
                    current = skins[i];
                }
            }
            PrivateMember.SetField(_manager, "_loadedSkinArray", skins);
            PrivateMember.SetField(_manager, "_selectedSkin", current ?? CreateSkin(selected));
            return skins;
        }

        /// <summary>Creates and initializes an enumerator for the editable skin option.</summary>
        /// <param name="probe">The settings section.</param>
        /// <param name="option">The option name.</param>
        /// <returns>The initialized enumerator.</returns>
        private static SkinEnumerator Create(SkinProbe probe, string option = nameof(SkinProbe.Skin))
        {
            var enumerator = new SkinEnumerator();
            enumerator.Init(typeof(SkinProbe).GetProperty(option)!, probe);
            return enumerator;
        }

        /// <summary>Checks that initialization lists every loaded skin by name and starts on the selected one.</summary>
        [Test]
        public void Init_ListsLoadedSkinsAndSelectsCurrent()
        {
            InstallSkins("Beta", "Alpha", "Beta", "Gamma");

            var enumerator = Create(new SkinProbe());

            Assert.That(enumerator.Current, Is.EqualTo("Beta"));
            Assert.That(enumerator.ValueText, Is.EqualTo("Beta"));
            Assert.That(enumerator.LocalizedValueText, Is.EqualTo("Beta"));
            Assert.That(enumerator.LocalizedValuesForWarmup, Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }));
            Assert.That(_changes, Is.Empty);
        }

        /// <summary>Checks that a selected skin missing from the loaded list falls back to the first entry.</summary>
        [Test]
        public void Init_UnknownSelectedSkin_StartsAtFirst()
        {
            InstallSkins("Missing", "Alpha", "Beta");

            var enumerator = Create(new SkinProbe());

            Assert.That(enumerator.Current, Is.EqualTo("Alpha"));
            Assert.That(_manager.SelectedSkin.Name, Is.EqualTo("Missing"));
        }

        /// <summary>Checks that moving forward selects the next skin on the manager, writes the option and wraps around.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MoveNext_SelectsNextSkinAndWraps()
        {
            var skins = InstallSkins("Beta", "Alpha", "Beta", "Gamma");
            var probe = new SkinProbe();
            var enumerator = Create(probe);

            Assert.That(enumerator.MoveNext(), Is.True);
            yield return null;

            Assert.That(probe.Skin, Is.EqualTo("Gamma"));
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[2]));
            Assert.That(_changes, Is.EqualTo(new[] { skins[2] }));

            Assert.That(enumerator.MoveNext(), Is.True);
            yield return null;

            Assert.That(probe.Skin, Is.EqualTo("Alpha"));
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[0]));
            Assert.That(_changes, Is.EqualTo(new[] { skins[2], skins[0] }));
        }

        /// <summary>Checks that moving backward selects the previous skin and wraps to the last one.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MovePrevious_SelectsPreviousSkinAndWraps()
        {
            var skins = InstallSkins("Beta", "Alpha", "Beta", "Gamma");
            var probe = new SkinProbe();
            var enumerator = Create(probe);

            Assert.That(enumerator.MovePrevious(), Is.True);
            yield return null;

            Assert.That(probe.Skin, Is.EqualTo("Alpha"));
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[0]));

            Assert.That(enumerator.MovePrevious(), Is.True);
            yield return null;

            Assert.That(probe.Skin, Is.EqualTo("Gamma"));
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[2]));
            Assert.That(_changes, Is.EqualTo(new[] { skins[0], skins[2] }));
        }

        /// <summary>Checks that cycling a single loaded skin keeps the selection and raises no change.</summary>
        /// <returns>The test coroutine.</returns>
        [UnityTest]
        public IEnumerator MoveNext_SingleSkin_KeepsSelection()
        {
            var skins = InstallSkins("Only", "Only");
            var probe = new SkinProbe();
            var enumerator = Create(probe);

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.MovePrevious(), Is.True);
            yield return null;

            Assert.That(probe.Skin, Is.EqualTo("Only"));
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[0]));
            Assert.That(_changes, Is.Empty);
        }

        /// <summary>Checks that a read-only option neither moves nor changes the selected skin.</summary>
        [Test]
        public void ReadOnlyOption_DoesNotChangeSkin()
        {
            var skins = InstallSkins("Alpha", "Alpha", "Beta");
            var probe = new SkinProbe();
            var enumerator = Create(probe, nameof(SkinProbe.Locked));

            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.MovePrevious(), Is.False);
            Assert.That(enumerator.Current, Is.EqualTo("Alpha"));
            Assert.That(probe.Locked, Is.Empty);
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[0]));
            Assert.That(_changes, Is.Empty);
        }

        /// <summary>Checks that refreshing after the option was written elsewhere moves the cursor without touching the manager.</summary>
        [Test]
        public void Refresh_FollowsExternalValue()
        {
            var skins = InstallSkins("Alpha", "Alpha", "Beta");
            var probe = new SkinProbe();
            var enumerator = Create(probe);

            probe.Skin = "Beta";
            enumerator.Refresh();

            Assert.That(enumerator.Current, Is.EqualTo("Beta"));
            Assert.That(_manager.SelectedSkin, Is.SameAs(skins[0]));
            Assert.That(_changes, Is.Empty);
        }
    }
}

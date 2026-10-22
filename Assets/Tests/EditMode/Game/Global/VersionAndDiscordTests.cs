using System;
using MajdataPlay.Scenes.Game;
using NUnit.Framework;
using Semver;

#nullable enable
namespace MajdataPlay.Tests.Game.Global
{
    /// <summary>Verifies the offline logic of <see cref="VersionChecker"/> and <see cref="DiscordManager"/>.</summary>
    public class VersionAndDiscordTests
    {
        /// <summary>Checks that release tags are parsed with an optional v prefix and surrounding whitespace.</summary>
        /// <param name="tag">The tag name.</param>
        /// <param name="expected">The expected version text.</param>
        [TestCase("v1.2.3", "1.2.3")]
        [TestCase("V0.1.51", "0.1.51")]
        [TestCase("  2.0.0 ", "2.0.0")]
        [TestCase("1.0.0-beta.1", "1.0.0-beta.1")]
        public void TryParseVersion_AcceptsReleaseTags(string tag, string expected)
        {
            var version = PrivateMember.InvokeStatic<SemVersion?>(typeof(VersionChecker), "TryParseVersion", tag);

            Assert.That(version, Is.Not.Null);
            Assert.That(version!.ToString(), Is.EqualTo(expected));
        }

        /// <summary>Checks that empty and non-semantic tags are ignored.</summary>
        /// <param name="tag">The tag name.</param>
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("1.2")]
        [TestCase("latest")]
        [TestCase("v1.2.3.4")]
        public void TryParseVersion_RejectsOtherTags(string tag)
        {
            Assert.That(PrivateMember.InvokeStatic<SemVersion?>(typeof(VersionChecker), "TryParseVersion", tag), Is.Null);
        }

        /// <summary>Checks that versions are ordered by major, minor and patch only.</summary>
        /// <param name="left">The first version.</param>
        /// <param name="right">The second version.</param>
        /// <param name="expectedSign">The expected comparison sign.</param>
        [TestCase("2.0.0", "1.9.9", 1)]
        [TestCase("1.2.0", "1.10.0", -1)]
        [TestCase("1.2.4", "1.2.3", 1)]
        [TestCase("1.2.3", "1.2.3", 0)]
        [TestCase("1.2.3-alpha", "1.2.3", 0)]
        public void CompareMajorMinorPatch_IgnoresPrereleases(string left, string right, int expectedSign)
        {
            var comparison = PrivateMember.InvokeStatic<int>(typeof(VersionChecker), "CompareMajorMinorPatch",
                SemVersion.Parse(left, SemVersionStyles.Strict), SemVersion.Parse(right, SemVersionStyles.Strict));

            Assert.That(Math.Sign(comparison), Is.EqualTo(expectedSign));
        }

        /// <summary>Checks the presence text of every scene, including the fallback for scenes without a description.</summary>
        [Test]
        public void GetSceneInfo_DescribesEveryScene()
        {
            var expected = new (MajScenes Scene, string Details, string State, string Small, string SmallText)[]
            {
                (MajScenes.Title, "Knocking the Door", "Title", "Idle", "Title"),
                (MajScenes.Login, "Fighting Network", "Login", "Idle", "Login"),
                (MajScenes.List, "What to Eat?", "Song Select", "Idle", "Song Select"),
                (MajScenes.Game, "Working Out", "In Game", "Playing", "Playing"),
                (MajScenes.Result, "Lying on the Cold Hard Ground", "Result", "Result", "Result"),
                (MajScenes.Setting, "Model Fine-tuning", "Settings", "Idle", "Settings"),
                (MajScenes.SortFind, "Yeah, What to Eat?", "Sort & Find", "Idle", "Sort & Find"),
                (MajScenes.TotalResult, "Lying on the Cold Hard Ground", "Results", "Result", "Total Result"),
                (MajScenes.Parctice, "Involuting", "Practice Mode", "Playing", "Practice"),
                (MajScenes.View, "Appreciating", "Chart View", "Idle", "View"),
                (MajScenes.Init, "Idle", "Idle", "Idle", "Idle"),
                (MajScenes.Test, "Idle", "Idle", "Idle", "Idle"),
            };

            foreach (var item in expected)
            {
                var info = PrivateMember.InvokeStatic<(string, string, string, string)>(typeof(DiscordManager), "GetSceneInfo", item.Scene);
                Assert.That(info, Is.EqualTo((item.Details, item.State, item.Small, item.SmallText)), item.Scene.ToString());
            }
        }

        /// <summary>Checks that every presence update is ignored while no Discord client is connected.</summary>
        [Test]
        public void PresenceUpdates_WithoutClient_AreIgnored()
        {
            Assert.That(PrivateMember.GetStaticField<object?>(typeof(DiscordManager), "_client"), Is.Null);

            Assert.DoesNotThrow(() =>
            {
                DiscordManager.UpdatePresence(MajScenes.Game);
                DiscordManager.UpdateDetails("details", true);
                DiscordManager.UpdateState("state", true);
                DiscordManager.UpdateButtons(Array.Empty<DiscordRPC.Button>(), true);
                DiscordManager.UpdateParty(new DiscordRPC.Party(), true);
                PrivateMember.InvokeStatic(typeof(DiscordManager), "OnSceneChanged", null, (MajScenes.Title, MajScenes.Init));
                PrivateMember.InvokeStatic(typeof(DiscordManager), "OnAppQuit", null, null);
            });
            Assert.That(PrivateMember.GetStaticField<object?>(typeof(DiscordManager), "_client"), Is.Null);
        }
    }
}

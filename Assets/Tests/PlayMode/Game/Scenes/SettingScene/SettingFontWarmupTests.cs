using System.Collections.Generic;
using MajdataPlay.Scenes.Setting;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;
using NUnit.Framework;
using TMPro;

#nullable enable
namespace MajdataPlay.Tests.Game.Scenes.PlayMode.SettingScene
{
    /// <summary>Covers glyph request collection and dynamic atlas warmup of the setting scene.</summary>
    public class SettingFontWarmupTests : SceneTestBase
    {
        /// <summary>Gets the pending character requests of a warmup batch.</summary>
        /// <param name="warmup">The warmup batch.</param>
        /// <returns>The requests by font.</returns>
        private static Dictionary<TMP_FontAsset, HashSet<uint>> Requests(SettingFontWarmup warmup)
        {
            return PrivateMember.GetField<Dictionary<TMP_FontAsset, HashSet<uint>>>(warmup, "_requests");
        }

        /// <summary>Checks that missing fonts and empty text create no request.</summary>
        [Test]
        public void AddText_IgnoresMissingFontAndEmptyText()
        {
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();

            warmup.AddText(null!, "abc");
            warmup.AddText(font, string.Empty);
            warmup.AddText(font, null!);

            Assert.That(Requests(warmup), Is.Empty);
        }

        /// <summary>Checks that a request always includes TMP's implicit glyphs and the text's characters.</summary>
        [Test]
        public void AddText_RequestsImplicitAndTextCharacters()
        {
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();

            warmup.AddText(font, "Ab");
            warmup.AddText(font, "b");

            var characters = Requests(warmup)[font];
            Assert.That(characters, Does.Contain(0x20u).And.Contains(0x5Fu).And.Contains(0x2026u).And.Contains(0x25A1u));
            Assert.That(characters, Does.Contain((uint)'A').And.Contains((uint)'b'));
        }

        /// <summary>Checks that surrogate pairs become one code point and invalid or control characters are skipped.</summary>
        [Test]
        public void AddText_DecodesSurrogatesAndSkipsInvalidCharacters()
        {
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();

            warmup.AddText(font, "😀");
            warmup.AddText(font, "x\uD83D");
            warmup.AddText(font, "\uDE00y");
            warmup.AddText(font, "\uD83Dz");
            warmup.AddText(font, "\n\t");

            var characters = Requests(warmup)[font];
            Assert.That(characters, Does.Contain(0x1F600u));
            Assert.That(characters, Does.Contain((uint)'x').And.Contains((uint)'y').And.Contains((uint)'z'));
            Assert.That(characters, Has.No.Member(0xD83Du));
            Assert.That(characters, Has.No.Member(0xDE00u));
            Assert.That(characters, Has.No.Member((uint)'\n'));
            Assert.That(characters, Has.No.Member((uint)'\t'));
        }

        /// <summary>Checks that warming up adds requested glyphs to a dynamic atlas and clears the batch.</summary>
        [Test]
        public void Warmup_AddsGlyphsToDynamicFontAndClearsRequests()
        {
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();

            warmup.AddText(font, "QWE");
            warmup.Warmup();

            Assert.That(font.characterLookupTable.ContainsKey('Q'), Is.True);
            Assert.That(font.characterLookupTable.ContainsKey('W'), Is.True);
            Assert.That(Requests(warmup), Is.Empty);
        }

        /// <summary>Checks that warmup follows fallback chains, including cyclic ones, without looping.</summary>
        [Test]
        public void Warmup_FollowsCyclicFallbacksOnce()
        {
            var warmup = new SettingFontWarmup();
            var primary = CreateDynamicFont();
            var fallback = CreateDynamicFont();
            primary.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            fallback.fallbackFontAssetTable = new List<TMP_FontAsset> { primary };

            warmup.AddText(primary, "R");
            warmup.Warmup();

            Assert.That(primary.characterLookupTable.ContainsKey('R'), Is.True);
            Assert.That(primary.characterLookupTable.ContainsKey(0xE123u), Is.False);
            Assert.That(fallback.characterLookupTable.ContainsKey(0xE123u), Is.False);
        }

        /// <summary>Checks that a static font is queried but never asked to add glyphs.</summary>
        [Test]
        public void Warmup_DoesNotPopulateStaticFonts()
        {
            var warmup = new SettingFontWarmup();
            var font = CreateDynamicFont();
            font.atlasPopulationMode = AtlasPopulationMode.Static;

            warmup.AddText(font, "K");
            warmup.Warmup();

            Assert.That(font.characterLookupTable.ContainsKey('K'), Is.False);
            Assert.That(Requests(warmup), Is.Empty);
        }
    }
}

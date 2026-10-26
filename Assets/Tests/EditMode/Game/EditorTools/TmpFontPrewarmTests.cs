using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers the character set parser, the font prewarmer and the prewarm window's input model.</summary>
    public class TmpFontPrewarmTests
    {
        /// <summary>A Latin-only source font shipped with TextMesh Pro.</summary>
        private const string LatinFontPath = "Assets/TextMesh Pro/Fonts/LiberationSans.ttf";

        /// <summary>A Korean source font of the project that has Hangul syllables.</summary>
        private const string HangulFontPath = "Assets/Fonts/General/Jua-Regular.ttf";

        /// <summary>HANGUL SYLLABLE GA, present in the Korean font only.</summary>
        private const uint HangulGa = 0xAC00;

        /// <summary>The in-memory font assets created by a test, destroyed with their atlases and materials.</summary>
        private readonly List<TMP_FontAsset> _createdAssets = new();

        /// <summary>The prewarm window created by a test.</summary>
        private TmpFontPrewarmWindow? _window;

        /// <summary>Destroys the test's in-memory font assets and window.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _createdAssets)
            {
                DestroyFontAsset(asset);
            }
            _createdAssets.Clear();
            if (_window != null)
            {
                Object.DestroyImmediate(_window);
                _window = null;
            }
        }

        /// <summary>Checks that literal text yields distinct code points, pairs surrogates and drops control characters and the BOM.</summary>
        [Test]
        public void AddCharacters_CombinesSurrogatePairsAndSkipsControls()
        {
            var codePoints = new SortedSet<uint>();

            var added = TmpCharacterSetParser.AddCharacters("A\u3042\U0001F600\n\tA\uFEFF", codePoints);

            Assert.That(added, Is.EqualTo(3));
            Assert.That(codePoints, Is.EqualTo(new uint[] { 0x41, 0x3042, 0x1F600 }));
        }

        /// <summary>Checks that an unpaired surrogate is ignored instead of becoming a code point.</summary>
        [Test]
        public void AddCharacters_SkipsLoneSurrogates()
        {
            var codePoints = new SortedSet<uint>();

            TmpCharacterSetParser.AddCharacters("\uD800B\uDC00", codePoints);

            Assert.That(codePoints, Is.EqualTo(new uint[] { 0x42 }));
        }

        /// <summary>Checks that ranges accept the U+ and 0x prefixes and every separator.</summary>
        [Test]
        public void AddUnicodeRanges_AcceptsPrefixesAndSeparators()
        {
            var codePoints = new SortedSet<uint>();

            var added = TmpCharacterSetParser.AddUnicodeRanges("0041-0043, U+3042;0x4E00\n4E01-u+4E02", codePoints);

            Assert.That(added, Is.EqualTo(7));
            Assert.That(codePoints, Is.EqualTo(new uint[] { 0x41, 0x42, 0x43, 0x3042, 0x4E00, 0x4E01, 0x4E02 }));
        }

        /// <summary>Checks that ranges skip control characters and surrogates.</summary>
        [Test]
        public void AddUnicodeRanges_SkipsSurrogatesAndControls()
        {
            var codePoints = new SortedSet<uint>();

            TmpCharacterSetParser.AddUnicodeRanges("001E-0021, D7FF-E000", codePoints);

            Assert.That(codePoints, Is.EqualTo(new uint[] { 0x20, 0x21, 0xD7FF, 0xE000 }));
        }

        /// <summary>Checks that only code points not yet in the set are counted.</summary>
        [Test]
        public void AddUnicodeRanges_CountsOnlyNewCodePoints()
        {
            var codePoints = new SortedSet<uint> { 0x41 };

            var added = TmpCharacterSetParser.AddUnicodeRanges("0041-0043", codePoints);

            Assert.That(added, Is.EqualTo(2));
        }

        /// <summary>Checks that the last code point of Unicode is accepted without overflowing the range loop.</summary>
        [Test]
        public void AddUnicodeRanges_AcceptsLastCodePoint()
        {
            var codePoints = new SortedSet<uint>();

            TmpCharacterSetParser.AddUnicodeRanges("10FFFE-10FFFF", codePoints);

            Assert.That(codePoints, Is.EqualTo(new uint[] { 0x10FFFE, 0x10FFFF }));
        }

        /// <summary>Checks that malformed, reversed and out-of-range entries are rejected.</summary>
        /// <param name="ranges">The invalid range list.</param>
        [TestCase("XYZ")]
        [TestCase("0043-0041")]
        [TestCase("110000")]
        [TestCase("-0041")]
        [TestCase("0041-")]
        [TestCase("0041-0042-0043")]
        [TestCase("U+")]
        [TestCase("1234567")]
        public void AddUnicodeRanges_RejectsInvalidEntries(string ranges)
        {
            Assert.Throws<FormatException>(() => TmpCharacterSetParser.AddUnicodeRanges(ranges, new SortedSet<uint>()));
        }

        /// <summary>Checks that every built-in preset is a valid, non-empty range list.</summary>
        [Test]
        public void Presets_AreValidRanges()
        {
            foreach (var preset in TmpCharacterSetParser.Presets)
            {
                var codePoints = new SortedSet<uint>();

                var added = TmpCharacterSetParser.AddUnicodeRanges(preset.Ranges, codePoints);

                Assert.That(added, Is.GreaterThan(0), preset.Name);
            }
        }

        /// <summary>Checks that the lookup chain is depth first like TMP's search and survives fallback cycles.</summary>
        [Test]
        public void GetLookupChain_IsDepthFirstAndIgnoresCycles()
        {
            var a = CreateBareFontAsset("A");
            var b = CreateBareFontAsset("B");
            var c = CreateBareFontAsset("C");
            var d = CreateBareFontAsset("D");
            a.fallbackFontAssetTable = new List<TMP_FontAsset> { b, c };
            b.fallbackFontAssetTable = new List<TMP_FontAsset> { d, a };

            var chain = TmpFontPrewarmer.GetLookupChain(a, true);
            var withoutFallbacks = TmpFontPrewarmer.GetLookupChain(a, false);

            Assert.That(chain, Is.EqualTo(new[] { a, b, d, c }));
            Assert.That(withoutFallbacks, Is.EqualTo(new[] { a }));
        }

        /// <summary>Checks that missing characters are added to a dynamic font asset.</summary>
        [Test]
        public void Prewarm_AddsMissingCharactersToDynamicAsset()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("ABC"), InMemoryOptions());

            Assert.That(result.RequestedCount, Is.EqualTo(3));
            Assert.That(result.AlreadyPresentCount, Is.Zero);
            Assert.That(result.AddedCount, Is.EqualTo(3));
            Assert.That(result.AddedPerAsset[latin], Is.EqualTo(3));
            Assert.That(result.UnsupportedCodePoints, Is.Empty);
            Assert.That(latin.HasCharacter('A') && latin.HasCharacter('B') && latin.HasCharacter('C'), Is.True);
        }

        /// <summary>Checks that a second run counts the characters as present and adds nothing.</summary>
        [Test]
        public void Prewarm_CountsExistingCharactersAsPresent()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            TmpFontPrewarmer.Prewarm(latin, CodePoints("ABC"), InMemoryOptions());

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("ABC"), InMemoryOptions());

            Assert.That(result.AlreadyPresentCount, Is.EqualTo(3));
            Assert.That(result.AddedCount, Is.Zero);
            Assert.That(result.AddedPerAsset, Is.Empty);
            Assert.That(TmpFontPrewarmer.CountPresent(latin, CodePoints("ABCD"), true), Is.EqualTo(3));
        }

        /// <summary>Checks that a character no font of the chain has is reported as unsupported.</summary>
        [Test]
        public void Prewarm_ReportsCharactersMissingFromEveryFont()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);

            var result = TmpFontPrewarmer.Prewarm(latin, new[] { 0x41u, HangulGa }, InMemoryOptions());

            Assert.That(result.AddedCount, Is.EqualTo(1));
            Assert.That(result.UnsupportedCodePoints, Is.EqualTo(new[] { HangulGa }));
            Assert.That(latin.HasCharacter((int)HangulGa), Is.False);
        }

        /// <summary>Checks that characters missing from the primary source font land in the fallback that has them.</summary>
        [Test]
        public void Prewarm_FillsFallbackForCharactersMissingFromPrimary()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var hangul = CreateDynamicFontAsset(HangulFontPath);
            latin.fallbackFontAssetTable = new List<TMP_FontAsset> { hangul };

            var result = TmpFontPrewarmer.Prewarm(latin, new[] { 0x41u, HangulGa }, InMemoryOptions());

            Assert.That(result.AddedCount, Is.EqualTo(2));
            Assert.That(result.AddedPerAsset[latin], Is.EqualTo(1));
            Assert.That(result.AddedPerAsset[hangul], Is.EqualTo(1));
            Assert.That(latin.HasCharacter('A'), Is.True);
            Assert.That(hangul.HasCharacter((int)HangulGa), Is.True);
            Assert.That(hangul.HasCharacter('A'), Is.False, "The primary font already supplies 'A', so the fallback must not receive it.");
            Assert.That(result.UnsupportedCodePoints, Is.Empty);
        }

        /// <summary>Checks that disabling fallbacks leaves the fallback untouched and reports its characters as unsupported.</summary>
        [Test]
        public void Prewarm_WithoutFallbacks_LeavesFallbackUntouched()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var hangul = CreateDynamicFontAsset(HangulFontPath);
            latin.fallbackFontAssetTable = new List<TMP_FontAsset> { hangul };
            var options = InMemoryOptions();
            options.IncludeFallbacks = false;

            var result = TmpFontPrewarmer.Prewarm(latin, new[] { HangulGa }, options);

            Assert.That(result.UnsupportedCodePoints, Is.EqualTo(new[] { HangulGa }));
            Assert.That(hangul.HasCharacter((int)HangulGa), Is.False);
        }

        /// <summary>Checks that a static asset is skipped without TMP's warning while its fallbacks are still filled.</summary>
        [Test]
        public void Prewarm_SkipsStaticAssetButFillsItsFallbacks()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var hangul = CreateDynamicFontAsset(HangulFontPath);
            latin.atlasPopulationMode = AtlasPopulationMode.Static;
            latin.fallbackFontAssetTable = new List<TMP_FontAsset> { hangul };

            var result = TmpFontPrewarmer.Prewarm(latin, new[] { HangulGa }, InMemoryOptions());

            LogAssert.NoUnexpectedReceived();
            Assert.That(result.AddedPerAsset.ContainsKey(latin), Is.False);
            Assert.That(latin.characterTable, Is.Empty);
            Assert.That(hangul.HasCharacter((int)HangulGa), Is.True);
        }

        /// <summary>Checks that baking adds characters to a static asset and leaves it static, non-readable and without a runtime source font.</summary>
        [Test]
        public void Prewarm_BakesIntoStaticAssetAndKeepsItStatic()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            latin.TryAddCharacters(new[] { 0x41u }, out _, false);
            MakeStatic(latin);

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("BC"), StaticBakeOptions());

            LogAssert.NoUnexpectedReceived();
            Assert.That(result.AddedPerAsset[latin], Is.EqualTo(2));
            Assert.That(latin.HasCharacter('A') && latin.HasCharacter('B') && latin.HasCharacter('C'), Is.True);
            Assert.That(latin.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(latin.atlasTexture.isReadable, Is.False);
            Assert.That(latin.sourceFontFile, Is.Null);
            Assert.That(result.GrownAtlasSizes, Is.Empty);
        }

        /// <summary>Checks that a static primary is filled before its dynamic same-face fallback, which then receives nothing.</summary>
        [Test]
        public void Prewarm_FillsStaticPrimaryBeforeDynamicFallback()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var latinDynamic = CreateDynamicFontAsset(LatinFontPath);
            MakeStatic(latin);
            latin.fallbackFontAssetTable = new List<TMP_FontAsset> { latinDynamic };

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("XY"), StaticBakeOptions());

            Assert.That(result.AddedPerAsset[latin], Is.EqualTo(2));
            Assert.That(result.AddedPerAsset.ContainsKey(latinDynamic), Is.False);
            Assert.That(latinDynamic.characterTable, Is.Empty);
        }

        /// <summary>Checks that a full single-atlas static asset is rebuilt at a larger size that keeps its characters and fits the new ones.</summary>
        [Test]
        public void Prewarm_GrowsFullStaticAtlas()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath, 64);
            latin.isMultiAtlasTexturesEnabled = false;
            latin.TryAddCharacters(new[] { 0x41u }, out _, false);
            MakeStatic(latin);
            const string added = "BCDEFGHIJKLMNOP";

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints(added), StaticBakeOptions());

            Assert.That(result.UnsupportedCodePoints, Is.Empty);
            Assert.That(result.AddedPerAsset[latin], Is.EqualTo(added.Length));
            Assert.That(latin.HasCharacter('A'), Is.True, "Characters baked before the rebuild must survive it.");
            Assert.That(added.All(c => latin.HasCharacter(c)), Is.True);
            Assert.That(latin.atlasWidth * latin.atlasHeight, Is.GreaterThan(64 * 64));
            Assert.That(result.GrownAtlasSizes[latin], Is.EqualTo(new Vector2Int(latin.atlasWidth, latin.atlasHeight)));
            Assert.That(latin.atlasTexture.width, Is.EqualTo(latin.atlasWidth));
            Assert.That(latin.material.GetFloat(ShaderUtilities.ID_TextureWidth), Is.EqualTo(latin.atlasWidth));
            Assert.That(latin.material.GetFloat(ShaderUtilities.ID_TextureHeight), Is.EqualTo(latin.atlasHeight));
            Assert.That(latin.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(latin.atlasTexture.isReadable, Is.False);
        }

        /// <summary>Checks that the size limit stops growth and the characters that still do not fit are reported as unsupported.</summary>
        [Test]
        public void Prewarm_StaticAtlasGrowthStopsAtLimit()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath, 64);
            latin.isMultiAtlasTexturesEnabled = false;
            MakeStatic(latin);
            var options = StaticBakeOptions();
            options.MaxStaticAtlasSize = 128;

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"), options);

            Assert.That(latin.atlasWidth, Is.EqualTo(128));
            Assert.That(latin.atlasHeight, Is.EqualTo(128));
            Assert.That(result.UnsupportedCodePoints, Is.Not.Empty);
            Assert.That(result.AddedCount + result.UnsupportedCodePoints.Count, Is.EqualTo(result.RequestedCount));
        }

        /// <summary>Checks that without growth a full static atlas keeps its size and the overflow is reported as unsupported.</summary>
        [Test]
        public void Prewarm_WithoutGrowth_ReportsStaticOverflowAsUnsupported()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath, 64);
            latin.isMultiAtlasTexturesEnabled = false;
            MakeStatic(latin);
            var options = StaticBakeOptions();
            options.GrowStaticAtlases = false;

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("ABCDEFGHIJKLMNOP"), options);

            Assert.That(latin.atlasWidth, Is.EqualTo(64));
            Assert.That(latin.atlasHeight, Is.EqualTo(64));
            Assert.That(result.GrownAtlasSizes, Is.Empty);
            Assert.That(result.UnsupportedCodePoints, Is.Not.Empty);
            Assert.That(latin.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
        }

        /// <summary>Checks that a static asset without a source font is reported and skipped while its fallbacks are still filled.</summary>
        [Test]
        public void Prewarm_SkipsStaticAssetWithoutSourceFont()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var hangul = CreateDynamicFontAsset(HangulFontPath);
            MakeStatic(latin);
            using (var serializedObject = new SerializedObject(latin))
            {
                serializedObject.FindProperty("m_SourceFontFileGUID").stringValue = string.Empty;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
            latin.fallbackFontAssetTable = new List<TMP_FontAsset> { hangul };

            var result = TmpFontPrewarmer.Prewarm(latin, new[] { HangulGa }, StaticBakeOptions());

            Assert.That(TmpFontPrewarmer.GetSourceFont(latin), Is.Null);
            Assert.That(result.StaticAssetsWithoutSourceFont, Is.EqualTo(new[] { latin }));
            Assert.That(latin.characterTable, Is.Empty);
            Assert.That(hangul.HasCharacter((int)HangulGa), Is.True);
        }

        /// <summary>Checks that a static asset finds its source font through the GUID TMP keeps after dropping the font reference.</summary>
        [Test]
        public void GetSourceFont_ResolvesStaticAssetThroughGuid()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            MakeStatic(latin);

            var font = TmpFontPrewarmer.GetSourceFont(latin);

            Assert.That(latin.sourceFontFile, Is.Null);
            Assert.That(font, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Font>(LatinFontPath)));
        }

        /// <summary>Checks that a baked persistent static asset is written to disk as static with a non-readable atlas and the new characters.</summary>
        [Test]
        public void Prewarm_SavesBakedStaticAsset()
        {
            using var folder = new TemporaryAssetFolder();
            var asset = CreatePersistentFontAsset(folder, "Static.asset");
            MakeStatic(asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            var options = StaticBakeOptions();
            options.SaveAssets = true;

            var result = TmpFontPrewarmer.Prewarm(asset, CodePoints("AB"), options);

            Assert.That(result.AddedCount, Is.EqualTo(2));
            var text = folder.ReadText("Static.asset");
            StringAssert.Contains("m_AtlasPopulationMode: 0", text);
            StringAssert.Contains("m_IsReadable: 0", text);
            StringAssert.Contains("m_Unicode: 65", text);
            StringAssert.Contains("m_Unicode: 66", text);
        }

        /// <summary>Checks the window's description of a static asset for each option combination.</summary>
        [Test]
        public void DescribeStaticHandling_FollowsOptions()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            latin.isMultiAtlasTexturesEnabled = false;
            MakeStatic(latin);
            _window = ScriptableObject.CreateInstance<TmpFontPrewarmWindow>();

            var growing = _window.DescribeStaticHandling(latin);
            _window.Options.GrowStaticAtlases = false;
            var fixedSize = _window.DescribeStaticHandling(latin);
            _window.Options.IncludeStaticAssets = false;
            var skipped = _window.DescribeStaticHandling(latin);

            Assert.That(growing, Does.Contain("grows up to 4096"));
            Assert.That(fixedSize, Does.Contain("go to its fallbacks"));
            Assert.That(skipped, Does.Contain("baking into static assets is off"));
        }

        /// <summary>Checks that the summary names grown atlases and static assets without a source font.</summary>
        [Test]
        public void Describe_ListsGrownAtlasesAndStaticAssetsWithoutSource()
        {
            var latin = CreateBareFontAsset("Latin");
            var result = new TmpFontPrewarmResult(latin)
            {
                RequestedCount = 1,
                AddedCount = 1
            };
            result.AddedPerAsset[latin] = 1;
            result.GrownAtlasSizes[latin] = new Vector2Int(128, 64);
            result.StaticAssetsWithoutSourceFont.Add(latin);

            var line = TmpFontPrewarmWindow.Describe(result);

            Assert.That(line, Is.EqualTo("Latin: requested 1, already present 0, added 1 (Latin: 1), grew atlas Latin: 128x64, static without source font: Latin"));
        }

        /// <summary>
        /// Checks that a character a later static asset already has is still added to an earlier dynamic asset whose source font has it,
        /// because TMP generates it there at runtime before reaching the static asset.
        /// </summary>
        [Test]
        public void Prewarm_AddsCharacterThatOnlyALaterStaticAssetHas()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var baked = CreateDynamicFontAsset(LatinFontPath);
            baked.TryAddCharacters(new[] { 0x41u }, out _, false);
            MakeStatic(baked);
            latin.fallbackFontAssetTable = new List<TMP_FontAsset> { baked };

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("A"), InMemoryOptions());

            Assert.That(result.AlreadyPresentCount, Is.Zero);
            Assert.That(result.AddedPerAsset[latin], Is.EqualTo(1));
            Assert.That(latin.HasCharacter('A'), Is.True);
        }

        /// <summary>Checks that a character an earlier static asset has counts as present and is not added to a later dynamic asset.</summary>
        [Test]
        public void Prewarm_CountsCharacterOfEarlierStaticAssetAsPresent()
        {
            var baked = CreateDynamicFontAsset(LatinFontPath);
            var latin = CreateDynamicFontAsset(LatinFontPath);
            baked.TryAddCharacters(new[] { 0x41u }, out _, false);
            MakeStatic(baked);
            baked.fallbackFontAssetTable = new List<TMP_FontAsset> { latin };

            var result = TmpFontPrewarmer.Prewarm(baked, CodePoints("A"), InMemoryOptions());

            Assert.That(result.AlreadyPresentCount, Is.EqualTo(1));
            Assert.That(latin.characterTable, Is.Empty);
        }

        /// <summary>Checks that canceling stops before the next batch and reports the rest as unprocessed, not unsupported.</summary>
        [Test]
        public void Prewarm_CancelStopsAndReportsUnprocessed()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var options = InMemoryOptions();
            options.BatchSize = 1;
            var progressCalls = 0;

            var result = TmpFontPrewarmer.Prewarm(latin, CodePoints("ABC"), options, (_, _) =>
            {
                progressCalls++;
                return progressCalls > 1;
            });

            Assert.That(result.IsCanceled, Is.True);
            Assert.That(result.AddedCount, Is.EqualTo(1));
            Assert.That(result.UnprocessedCount, Is.EqualTo(2));
            Assert.That(result.UnsupportedCodePoints, Is.Empty);
            Assert.That(latin.HasCharacter('B'), Is.False);
        }

        /// <summary>Checks that progress is reported before every batch with fractions in [0, 1).</summary>
        [Test]
        public void Prewarm_ReportsProgressPerBatch()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            var options = InMemoryOptions();
            options.BatchSize = 2;
            var fractions = new List<float>();

            TmpFontPrewarmer.Prewarm(latin, CodePoints("ABCDE"), options, (progress, _) =>
            {
                fractions.Add(progress);
                return false;
            });

            Assert.That(fractions.Count, Is.EqualTo(3));
            Assert.That(fractions, Is.Ordered);
            Assert.That(fractions.All(fraction => fraction >= 0f && fraction < 1f), Is.True);
        }

        /// <summary>Checks that the internal Clear Dynamic Data On Build flag can be read and written.</summary>
        [Test]
        public void ClearDynamicDataOnBuild_RoundTrips()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);

            TmpFontPrewarmer.SetClearDynamicDataOnBuild(latin, true);
            var afterOn = TmpFontPrewarmer.GetClearDynamicDataOnBuild(latin);
            TmpFontPrewarmer.SetClearDynamicDataOnBuild(latin, false);
            var afterOff = TmpFontPrewarmer.GetClearDynamicDataOnBuild(latin);

            Assert.That(afterOn, Is.True);
            Assert.That(afterOff, Is.False);
        }

        /// <summary>Checks that a persistent font asset is written to disk with the new characters.</summary>
        [Test]
        public void Prewarm_SavesPersistentAsset()
        {
            using var folder = new TemporaryAssetFolder();
            var asset = CreatePersistentFontAsset(folder, "Prewarm.asset");

            var result = TmpFontPrewarmer.Prewarm(asset, CodePoints("AB"), new TmpFontPrewarmOptions());

            Assert.That(result.AddedCount, Is.EqualTo(2));
            Assert.That(EditorUtility.IsDirty(asset), Is.False);
            var text = folder.ReadText("Prewarm.asset");
            StringAssert.Contains("m_Unicode: 65", text);
            StringAssert.Contains("m_Unicode: 66", text);
        }

        /// <summary>Checks that the window adds font assets from folders and ignores duplicates and other objects.</summary>
        [Test]
        public void AddFontAssets_ExpandsFoldersAndSkipsDuplicates()
        {
            using var folder = new TemporaryAssetFolder();
            var asset = CreatePersistentFontAsset(folder, "Folder.asset");
            var folderObject = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder.Path);
            _window = ScriptableObject.CreateInstance<TmpFontPrewarmWindow>();

            var firstAdded = _window.AddFontAssets(new Object[] { folderObject, asset.material });
            var secondAdded = _window.AddFontAssets(new Object[] { asset });

            Assert.That(firstAdded, Is.EqualTo(1));
            Assert.That(secondAdded, Is.Zero);
            Assert.That(_window.FontAssets, Is.EqualTo(new[] { asset }));
        }

        /// <summary>Checks that the window merges characters, files and ranges and surfaces range errors.</summary>
        [Test]
        public void CodePoints_MergeAllSourcesAndReportRangeErrors()
        {
            _window = ScriptableObject.CreateInstance<TmpFontPrewarmWindow>();
            var file = new TextAsset("BC\r\n");

            _window.Characters = "AB";
            _window.AddCharacterFiles(new Object[] { file, _window });
            _window.UnicodeRanges = "0044";
            var merged = _window.CodePoints.ToArray();
            var errorBefore = _window.ParseError;
            _window.UnicodeRanges = "zz";
            var errorAfter = _window.ParseError;

            Assert.That(merged, Is.EqualTo(new uint[] { 0x41, 0x42, 0x43, 0x44 }));
            Assert.That(errorBefore, Is.Null);
            Assert.That(errorAfter, Does.Contain("zz"));
            Object.DestroyImmediate(file);
        }

        /// <summary>Checks the summary line, including the per-asset counts and the unsupported sample.</summary>
        [Test]
        public void Describe_ListsCountsAndUnsupportedSample()
        {
            var latin = CreateDynamicFontAsset(LatinFontPath);
            latin.name = "Latin";

            var result = TmpFontPrewarmer.Prewarm(latin, new[] { 0x41u, HangulGa }, InMemoryOptions());
            var line = TmpFontPrewarmWindow.Describe(result);

            Assert.That(line, Is.EqualTo("Latin: requested 2, already present 0, added 1 (Latin: 1), unsupported 1 [U+AC00]"));
        }

        /// <summary>Gets the code points of literal text.</summary>
        /// <param name="text">The characters.</param>
        /// <returns>The distinct code points in ascending order.</returns>
        private static SortedSet<uint> CodePoints(string text)
        {
            var codePoints = new SortedSet<uint>();
            TmpCharacterSetParser.AddCharacters(text, codePoints);
            return codePoints;
        }

        /// <summary>Gets options that do not touch the asset database.</summary>
        /// <returns>Default options with saving disabled.</returns>
        private static TmpFontPrewarmOptions InMemoryOptions()
        {
            return new TmpFontPrewarmOptions
            {
                SaveAssets = false
            };
        }

        /// <summary>Gets in-memory options that bake static assets.</summary>
        /// <returns>Default options with saving disabled and static baking enabled.</returns>
        private static TmpFontPrewarmOptions StaticBakeOptions()
        {
            var options = InMemoryOptions();
            options.IncludeStaticAssets = true;
            return options;
        }

        /// <summary>Turns a font asset static the way TMP's inspector does: no runtime source font and non-readable atlases.</summary>
        /// <param name="asset">The font asset.</param>
        private static void MakeStatic(TMP_FontAsset asset)
        {
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            TmpFontPrewarmer.SetAtlasTexturesReadable(asset, false);
            Assert.That(asset.atlasTexture.isReadable, Is.False);
        }

        /// <summary>Creates a small in-memory dynamic font asset that the test destroys.</summary>
        /// <param name="fontPath">The project path of the source font.</param>
        /// <param name="atlasSize">The atlas width and height.</param>
        /// <returns>The font asset.</returns>
        private TMP_FontAsset CreateDynamicFontAsset(string fontPath, int atlasSize = 256)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            Assert.That(font, Is.Not.Null, fontPath);
            var asset = TMP_FontAsset.CreateFontAsset(font, 24, 3, GlyphRenderMode.SDFAA, atlasSize, atlasSize);
            _createdAssets.Add(asset);
            return asset;
        }

        /// <summary>Creates an in-memory font asset without a source font, for lookup chain tests.</summary>
        /// <param name="name">The asset name.</param>
        /// <returns>The font asset.</returns>
        private TMP_FontAsset CreateBareFontAsset(string name)
        {
            var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
            asset.name = name;
            _createdAssets.Add(asset);
            return asset;
        }

        /// <summary>Creates a dynamic font asset saved in a temporary folder, with its atlas and material as sub-assets.</summary>
        /// <param name="folder">The temporary folder, deleted by the test.</param>
        /// <param name="fileName">The asset file name.</param>
        /// <returns>The persistent font asset.</returns>
        private static TMP_FontAsset CreatePersistentFontAsset(TemporaryAssetFolder folder, string fileName)
        {
            AssetDatabase.CreateFolder("Assets", folder.Path.Substring("Assets/".Length));
            var font = AssetDatabase.LoadAssetAtPath<Font>(LatinFontPath);
            var asset = TMP_FontAsset.CreateFontAsset(font, 24, 3, GlyphRenderMode.SDFAA, 256, 256);
            AssetDatabase.CreateAsset(asset, folder.Combine(fileName));
            asset.atlasTexture.name = "Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            asset.material.name = "Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Destroys an in-memory font asset with its atlas textures and material.</summary>
        /// <param name="asset">The font asset.</param>
        private static void DestroyFontAsset(TMP_FontAsset asset)
        {
            if (asset == null)
            {
                return;
            }
            if (asset.atlasTextures is not null)
            {
                foreach (var texture in asset.atlasTextures)
                {
                    if (texture != null)
                    {
                        Object.DestroyImmediate(texture);
                    }
                }
            }
            if (asset.material != null)
            {
                Object.DestroyImmediate(asset.material);
            }
            Object.DestroyImmediate(asset);
        }
    }
}

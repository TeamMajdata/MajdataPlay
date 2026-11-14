using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEditor;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Protects shared backdrop resource bindings and explicitly authored large cream glass panels.</summary>
    public sealed class UiPvBackdropAssetTests
    {
        /// <summary>Matches complete YAML components without instantiating scene managers or video decoders.</summary>
        private static readonly Regex s_components = new Regex(
            @"^--- !u!114 &-?\d+[^\r\n]*\r?\n(?<body>.*?)(?=^--- |\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        /// <summary>Checks templates are build-reachable and animated PV globals cannot become stale material properties.</summary>
        [Test]
        public void Resources_KeepProductionFilterAndGlassTemplatesReachable()
        {
            var resources = UnityEngine.Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme");
            Assert.That(resources, Is.Not.Null);
            Assert.That(resources.PvBlurMaterial, Is.Not.Null);
            Assert.That(resources.PvGlassMaterial, Is.Not.Null);
            Assert.That(resources.PvBlurMaterial!.shader.name, Is.EqualTo("Hidden/Maj/UI PV Gaussian"));
            Assert.That(resources.PvBlurMaterial.passCount, Is.EqualTo(4));
            Assert.That(resources.PvGlassMaterial!.shader.name, Is.EqualTo("Maj/UI/PV Glass"));
            Assert.That(resources.PvGlassMaterial.HasProperty("_PanelBaseTex"), Is.True);
            Assert.That(resources.PvGlassMaterial.HasProperty("_FillAlpha"), Is.True);
            Assert.That(resources.PvGlassMaterial.HasProperty("_MajPvBlurReady"), Is.False);
            Assert.That(resources.PvGlassMaterial.HasProperty("_MajPvBlurTexture"), Is.False);
            Assert.That(resources.PvGlassMaterial.HasProperty("_MajPvWorldToUV"), Is.False);
            Assert.That(resources.PvGlassMaterial.HasProperty("_MajUiPanelBlurTexture"), Is.False);
            Assert.That(resources.PvGlassMaterial.HasProperty("_MajUiPanelBlurReady"), Is.False);
            Assert.That(resources.PvGlassMaterial.HasProperty("_BackdropUsesPanelBlur"), Is.True);
        }

        /// <summary>Checks only the existing large List cover joins the capture prefix while retaining its shader and original binding.</summary>
        [Test]
        public void ListBackgroundCover_OptsIntoCaptureWithoutChangingItsAuthoredMaterial()
        {
            var text = FileSystem.File.Open("Assets/Prefabs/List/Main_Display_ribbon.prefab").ReadAllText();
            var image = Regex.Match(text, @"^--- !u!114 &579586896977130437\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
            Assert.That(image, Does.Contain("_captureBeforeBackdrop: 1"));
            Assert.That(Regex.Matches(text, @"^  _captureBeforeBackdrop: 1\r?$", RegexOptions.Multiline).Count, Is.EqualTo(1));
            Assert.That(image, Does.Contain("m_Material: {fileID: 2100000, guid: da09b782efbceae43a763c31ce1e1db9, type: 2}"));
            Assert.That(image, Does.Contain("m_Sprite: {fileID: 21300000, guid: 30663c3fa610ab742aa9f7e584136105, type: 3}"));
            Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 1}"));
            Assert.That(image, Does.Contain("m_PreserveAspect: 1"));
            Assert.That(text, Does.Contain("_bgSongCoverDisplayer: {fileID: 579586896977130437}"));
            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Rendering/Materials/UI/[UI]BlurCircleFade.mat");
            Assert.That(source, Is.Not.Null);
            Assert.That(source!.shader.name, Is.EqualTo("Maj/UI/BlurCircleFade_Gaussian_Fixed"));
            Assert.That(source.renderQueue, Is.EqualTo(3000), "Only the opted-in runtime Image may change its queue.");
        }

        /// <summary>Checks only the intended large Ribbon panels opt into PV glass and keep their authored alpha Images.</summary>
        /// <param name="path">The authored scene or prefab path.</param>
        /// <param name="expectedCount">The exact number of opt-in panels in this source.</param>
        /// <param name="backgroundImageId">The explicitly bound blue ring's existing Image component.</param>
        [TestCase("Assets/Scenes/Setting.unity", 1, "912764832")]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", 1, "5136493199664605945")]
        [TestCase("Assets/Scenes/SortFind.unity", 1, "1662593950")]
        public void AuthoredOptIns_PreserveOriginalQualityImageAndAuthoredOpacity(string path, int expectedCount,
            string backgroundImageId)
        {
            var panelGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/UiBackdropPanel.cs");
            var imageGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/QualityUiImage.cs");
            Assert.That(panelGuid, Is.Not.Empty);
            var text = FileSystem.File.Open(path).ReadAllText();
            var count = 0;
            foreach (Match component in s_components.Matches(text))
            {
                var body = component.Groups["body"].Value;
                if (!body.Contains("guid: " + panelGuid))
                {
                    continue;
                }
                if (body.Contains("_isCategory: 1") || body.Contains("_isButton: 1") || body.Contains("_liquidOnly: 1")
                    || body.Contains("_backgroundImage: {fileID: 0}"))
                {
                    continue;
                }
                Assert.That(body, Does.Contain("_backgroundImage: {fileID: " + backgroundImageId + "}"),
                    "The cream board must retain its original blue-ring binding; the separate white Board needs no ring.");
                var gameObject = Regex.Match(body, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
                Assert.That(gameObject, Is.Not.Empty);
                var imageFound = false;
                foreach (Match candidate in s_components.Matches(text))
                {
                    var image = candidate.Groups["body"].Value;
                    if (image.Contains("guid: " + imageGuid) && image.Contains("m_GameObject: {fileID: " + gameObject + "}"))
                    {
                        Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 0.85}"));
                        Assert.That(image, Does.Contain("guid: 04e00e0b72a3d284ebec3f7160996aa1"));
                        Assert.That(image, Does.Contain("m_Sprite: {fileID: 936887466,"),
                            "Only the large cream panel uses the PV glass effect.");
                        imageFound = true;
                        break;
                    }
                }
                Assert.That(imageFound, Is.True, path + ": glass must reuse its existing quality-aware Image.");
                count++;
            }
            Assert.That(count, Is.EqualTo(expectedCount));
        }

        /// <summary>Checks the large glass panels normalize their authored resting fade while ordinary Alpha remains unchanged.</summary>
        /// <param name="path">The authored scene or prefab containing the existing panel component.</param>
        /// <param name="panelId">The exact opt-in component whose reference must match its Image.</param>
        [TestCase("Assets/Scenes/Setting.unity", "1053645856")]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", "5019321880153334813")]
        [TestCase("Assets/Scenes/SortFind.unity", "1660259408")]
        public void LargeGlassRestOpacity_MatchesAuthoredImageWithoutChangingOrdinaryAlpha(string path, string panelId)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var panel = Regex.Match(text, @"^--- !u!114 &" + panelId +
                @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
                .Groups["body"].Value;
            Assert.That(panel, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
            Assert.That(panel, Does.Contain("_referenceOpacity: 0.85"));
            var gameObject = Regex.Match(panel, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
            Assert.That(gameObject, Is.Not.Empty);
            var images = 0;
            foreach (Match component in s_components.Matches(text))
            {
                var body = component.Groups["body"].Value;
                if (body.Contains("guid: 7c51cc521e1747f0ba99ae9b65785bd5") &&
                    body.Contains("m_GameObject: {fileID: " + gameObject + "}"))
                {
                    Assert.That(body, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 0.85}"));
                    images++;
                }
            }
            Assert.That(images, Is.EqualTo(1));
        }

        /// <summary>Checks large panels store only scalar color policy, keeping the palette in their artwork.</summary>
        /// <param name="path">The actual scene or prefab containing the large glass component.</param>
        /// <param name="panelId">The unchanged local identifier of the target normal-role panel.</param>
        [TestCase("Assets/Scenes/Setting.unity", "1053645856")]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", "5019321880153334813")]
        [TestCase("Assets/Scenes/SortFind.unity", "1660259408")]
        public void LargeGlassTint_UsesArtworkDrivenScalarPolicyWithoutSceneInstantiation(string path, string panelId)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var body = Regex.Match(text, @"^--- !u!114 &" + panelId +
                @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
                .Groups["body"].Value;
            Assert.That(body, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
            // Inspect this exact component without starting unrelated scene managers or requiring
            // dependencies outside this narrow rendering fixture.
            Assert.That(body, Does.Not.Contain("_glassTint:"));
            Assert.That(body, Does.Not.Contain("_fillColor:"));
            Assert.That(body, Does.Not.Contain("_useGraphicTint:"));
            Assert.That(body, Does.Contain("_referenceOpacity: 0.85"));
            foreach (var policy in new[] { ("_tintStrength", 0.4f), ("_frostStrength", 0.08f) })
            {
                var scalar = Regex.Match(body, @"^  " + policy.Item1 + @": (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                Assert.That(scalar.Success, Is.True, "The scalar policy must be explicit in this exact component.");
                Assert.That(float.Parse(scalar.Groups["value"].Value, CultureInfo.InvariantCulture),
                    Is.EqualTo(policy.Item2).Within(0.00001f));
            }
        }

        /// <summary>Checks all seven plates use the authored Liquid tint and RGB artwork-share policy.</summary>
        /// <param name="path">The exact authored scene or prefab.</param>
        /// <param name="spriteId">The original Sprite identifier selecting this plate without loading its scene.</param>
        [TestCase("Assets/Scenes/Setting.unity", "936887466")]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", "936887466")]
        [TestCase("Assets/Scenes/SortFind.unity", "936887466")]
        [TestCase("Assets/Scenes/Calibrator.unity", "936887466")]
        [TestCase("Assets/Scenes/Practice.unity", "936887466")]
        [TestCase("Assets/Prefabs/UI/UserInfo.prefab", "2110853651")]
        [TestCase("Assets/Prefabs/UI/ChartVisualDisplayer.prefab", "1073801904")]
        public void LiquidLayerTint_LargeAndWhitePlatesKeepExplicitTextureDerivedStrength(string path, string spriteId)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var panelGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/UiBackdropPanel.cs");
            var count = 0;
            foreach (Match imageComponent in s_components.Matches(text))
            {
                var image = imageComponent.Groups["body"].Value;
                if (!image.Contains("guid: 7c51cc521e1747f0ba99ae9b65785bd5") ||
                    !image.Contains("m_Sprite: {fileID: " + spriteId + ","))
                {
                    continue;
                }
                var owner = Regex.Match(image, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
                Assert.That(owner, Is.Not.Empty);
                foreach (Match candidate in s_components.Matches(text))
                {
                    var panel = candidate.Groups["body"].Value;
                    if (!panel.Contains("guid: " + panelGuid) || !panel.Contains("m_GameObject: {fileID: " + owner + "}"))
                    {
                        continue;
                    }
                    var strength = Regex.Match(panel, @"^  _liquidTintStrength: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                    Assert.That(strength.Success, Is.True, "Liquid color policy must be explicit on this exact authored plate.");
                    var isCream = spriteId == "936887466";
                    var calibratedBoard = path == "Assets/Scenes/Setting.unity" || path == "Assets/Scenes/SortFind.unity"
                        || path == "Assets/Scenes/Calibrator.unity" || path == "Assets/Scenes/Practice.unity"
                        || path == "Assets/Prefabs/List/Main_Display_ribbon.prefab";
                    Assert.That(float.Parse(strength.Groups["value"].Value, CultureInfo.InvariantCulture),
                        Is.EqualTo(calibratedBoard ? 0f : isCream ? 0.23f : 0.42f).Within(0.00001f));
                    var artworkShare = Regex.Match(panel, @"^  _liquidArtworkBlend: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                    if (artworkShare.Success)
                    {
                        Assert.That(float.Parse(artworkShare.Groups["value"].Value, CultureInfo.InvariantCulture),
                            Is.EqualTo(calibratedBoard ? 0.4233333f : 0.25f));
                    }
                    else
                    {
                        Assert.That(isCream, Is.True, "White top cards must explicitly author their original-artwork share.");
                    }
                    Assert.That(panel, Does.Not.Contain("_liquidFrostStrength:"));
                    Assert.That(panel, Does.Not.Contain("_liquidTintColor:"));
                    count++;
                }
            }
            Assert.That(count, Is.EqualTo(1), path + ": exactly one matching plate must carry this policy.");
        }

        /// <summary>Checks every authored glass component explicitly records its color and artwork-share policy after default changes.</summary>
        [Test]
        public void LiquidArtworkMix_AllAuthoredPanelsKeepExplicitRolePolicies()
        {
            var paths = new Dictionary<string, int>
            {
                { "Assets/Scenes/Calibrator.unity", 3 },
                { "Assets/Scenes/Login.unity", 5 },
                { "Assets/Scenes/Title.unity", 1 },
                { "Assets/Scenes/Practice.unity", 8 },
                { "Assets/Scenes/SortFind.unity", 3 },
                { "Assets/Scenes/Setting.unity", 5 },
                { "Assets/Prefabs/UI/Buttons.prefab", 8 },
                { "Assets/Prefabs/UI/UserInfo.prefab", 1 },
                { "Assets/Prefabs/UI/ChartVisualDisplayer.prefab", 1 },
                { "Assets/Prefabs/Setting/MenuTitleDisplayer.prefab", 1 },
                { "Assets/Prefabs/List/Model/CollectionDisplayer.prefab", 1 },
                { "Assets/Prefabs/List/Main_Display_ribbon.prefab", 1 }
            };
            var creamIds = new HashSet<string> { "5019321880153334813", "1053645856", "1660259408",
                "900000000000000018", "900000000000000019" };
            var topIds = new HashSet<string> { "4988950831640157352", "7983312345234581847", "900000000000000022" };
            var calibratedIds = new HashSet<string> { "5019321880153334813", "1053645856", "1660259408",
                "900000000000000018", "900000000000000019" };
            var strongIds = new HashSet<string> { "5019321880153334813", "1053645856", "1660259408",
                "900000000000000017", "4988950831640157352", "7983312345234581847",
                "900000000000000020", "900000000000000021", "900000000000000018",
                "900000000000000019", "900000000000000022" };
            var panelGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/UiBackdropPanel.cs");
            var total = 0;
            var creams = 0;
            var tops = 0;
            var strongPanels = 0;
            foreach (var entry in paths)
            {
                var text = FileSystem.File.Open(entry.Key).ReadAllText();
                var inFile = 0;
                foreach (Match component in s_components.Matches(text))
                {
                    var panel = component.Groups["body"].Value;
                    if (!panel.Contains("guid: " + panelGuid))
                    {
                        continue;
                    }
                    var id = Regex.Match(component.Value, @"^--- !u!114 &(?<id>-?\d+)").Groups["id"].Value;
                    var isCream = creamIds.Contains(id);
                    var isTop = topIds.Contains(id);
                    var isTitle = id == "900000000000000020";
                    var calibratedBoard = calibratedIds.Contains(id);
                    var tint = Regex.Matches(panel, @"^  _liquidTintStrength: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                    var artwork = Regex.Matches(panel, @"^  _liquidArtworkBlend: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                    Assert.That(tint.Count, Is.EqualTo(1), entry.Key + ": explicit tint must not depend on a prior serialized class default.");
                    Assert.That(artwork.Count, Is.EqualTo(1), entry.Key + ": the RGB artwork share must be explicit and unique.");
                    Assert.That(float.Parse(tint[0].Groups["value"].Value, CultureInfo.InvariantCulture),
                        Is.EqualTo(calibratedBoard || isTitle ? 0f : isCream ? 0.23f : isTop ? 0.42f : 0.9f));
                    Assert.That(float.Parse(artwork[0].Groups["value"].Value, CultureInfo.InvariantCulture),
                        Is.EqualTo(calibratedBoard || isTitle ? 0.4233333f : 0.25f));
                    Assert.That(panel, Does.Not.Contain("_liquidFrostStrength:"));
                    var strongRoute = Regex.Matches(panel, @"^  _useStrongBlur: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                    if (strongIds.Contains(id))
                    {
                        Assert.That(strongRoute.Count, Is.EqualTo(1));
                        Assert.That(strongRoute[0].Groups["value"].Value, Is.EqualTo("1"), "Large boards, chart strips and startup plates must use the drawn-background RT1.");
                        strongPanels++;
                    }
                    else
                    {
                        Assert.That(strongRoute.Count, Is.Zero, "Categories and peripheral buttons must not sample PV-only RT1.");
                    }
                    creams += isCream ? 1 : 0;
                    tops += isTop ? 1 : 0;
                    inFile++;
                }
                Assert.That(inFile, Is.EqualTo(entry.Value), entry.Key + ": preserve this asset's original number of glass components.");
                total += inFile;
            }
            Assert.That(total, Is.EqualTo(38));
            Assert.That(creams, Is.EqualTo(5));
            Assert.That(tops, Is.EqualTo(3));
            Assert.That(strongPanels, Is.EqualTo(11));
        }

        /// <summary>Checks the loading plate uses one sliced optical surface and the calibrated large-board color policy.</summary>
        [Test]
        public void TitleLoadingPlate_UsesSlicedCalibratedBoardPolicyWithoutMovingItsLayout()
        {
            var text = FileSystem.File.Open("Assets/Scenes/Title.unity").ReadAllText();
            var panel = Regex.Match(text, @"^--- !u!114 &900000000000000020\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            var image = Regex.Match(text, @"^--- !u!114 &1514694040\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(image, Does.Contain("m_Sprite: {fileID: -547999258, guid: 04e00e0b72a3d284ebec3f7160996aa1"));
            Assert.That(image, Does.Contain("  m_Type: 1"), "Tiled UV repetition must not repeat the optical contour.");
            Assert.That(image, Does.Contain("  m_PixelsPerUnitMultiplier: 3.2"));
            var rect = Regex.Match(text, @"^--- !u!224 &1514694039\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(rect, Does.Contain("m_AnchoredPosition: {x: 188, y: -303}"));
            Assert.That(rect, Does.Contain("m_SizeDelta: {x: 470.79, y: 50.62}"));
            foreach (var expected in new Dictionary<string, float>
            {
                { "_liquidTintStrength", 0f }, { "_liquidArtworkBlend", 0.4233333f },
                { "_liquidRefractionWidthScale", 2f }, { "_useStrongBlur", 1f },
                { "_liquidCapsule", 0f }, { "_isButton", 0f }, { "_isCategory", 0f }
            })
            {
                var match = Regex.Match(panel, @"^  " + expected.Key + @": (?<v>[^\r\n]+)", RegexOptions.Multiline);
                Assert.That(match.Success, Is.True, expected.Key);
                Assert.That(float.Parse(match.Groups["v"].Value, CultureInfo.InvariantCulture), Is.EqualTo(expected.Value));
            }
            var insets = Regex.Match(panel, @"^  _liquidBodyInsets: (?<v>[^\r\n]+)", RegexOptions.Multiline);
            var components = Regex.Matches(insets.Groups["v"].Value, @": (?<v>[^,}]+)");
            Assert.That(components.Count, Is.EqualTo(4));
            for (var index = 0; index < components.Count; index++)
            {
                Assert.That(float.Parse(components[index].Groups["v"].Value, CultureInfo.InvariantCulture),
                    Is.EqualTo(index % 2 == 0 ? 4f / 203f : 4f / 164f).Within(0.0000001f));
            }
        }

        /// <summary>Checks Setting's dark and white inner plates reference the real large panel instead of a fixed yellow palette.</summary>
        /// <param name="panelId">The exact existing inner-plate component identifier.</param>
        [TestCase("900000000000000016")]
        [TestCase("900000000000000017")]
        public void LiquidLayerTint_SettingInnerPlatesInheritTheirLargePanel(string panelId)
        {
            var text = FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
            var panels = Regex.Matches(text, @"^--- !u!114 &" + panelId +
                @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.That(panels.Count, Is.EqualTo(1), "The actual inner plate must not gain duplicate component identifiers.");
            var panel = panels[0].Groups["body"].Value;
            Assert.That(panel, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
            Assert.That(panel, Does.Contain("_liquidTintSource: {fileID: 1053645856}"));
            var strength = Regex.Match(panel, @"^  _liquidSourceTintStrength: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
            Assert.That(strength.Success, Is.True);
            Assert.That(float.Parse(strength.Groups["value"].Value, CultureInfo.InvariantCulture), Is.EqualTo(0.14f).Within(0.00001f));
            var ownStrength = Regex.Match(panel, @"^  _liquidTintStrength: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
            if (ownStrength.Success)
            {
                Assert.That(float.Parse(ownStrength.Groups["value"].Value, CultureInfo.InvariantCulture), Is.EqualTo(0.9f));
            }
            Assert.That(panel, Does.Not.Contain("_liquidTintColor:"));
        }

        /// <summary>Checks width multipliers remain confined to the authored cards and adjustment capsules.</summary>
        /// <param name="path">The exact authored scene or prefab.</param>
        /// <param name="panelId">The requested large-card component in this asset.</param>
        /// <param name="expectedScale">The requested multiplier over the existing role-specific width.</param>
        [TestCase("Assets/Scenes/Setting.unity", "1053645856", 2f)]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", "5019321880153334813", 2f)]
        [TestCase("Assets/Scenes/SortFind.unity", "1660259408", 2f)]
        [TestCase("Assets/Prefabs/UI/UserInfo.prefab", "4988950831640157352", 1.5f)]
        [TestCase("Assets/Prefabs/UI/ChartVisualDisplayer.prefab", "7983312345234581847", 1.5f)]
        [TestCase("Assets/Scenes/Practice.unity", "900000000000000019", 2f)]
        [TestCase("Assets/Scenes/Practice.unity", "900000000000000022", 1.5f)]
        public void LiquidRefractionWidth_OnlyRequestedCardsOverrideTheirExistingBand(string path, string panelId, float expectedScale)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var matches = Regex.Matches(text, @"^--- !u!114 &" + panelId +
                @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.That(matches.Count, Is.EqualTo(1), "The authorized component identifier must remain unique.");
            var panel = matches[0].Groups["body"].Value;
            Assert.That(panel, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
            var scale = Regex.Match(panel, @"^  _liquidRefractionWidthScale: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
            Assert.That(scale.Success, Is.True);
            Assert.That(float.Parse(scale.Groups["value"].Value, CultureInfo.InvariantCulture), Is.EqualTo(expectedScale));
            var expectedOverrideCount = 1;
            if (path == "Assets/Scenes/Setting.unity")
            {
                foreach (var adjustmentId in new[] { "900000000000000014", "900000000000000015" })
                {
                    var adjustments = Regex.Matches(text, @"^--- !u!114 &" + adjustmentId +
                        @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
                    Assert.That(adjustments.Count, Is.EqualTo(1), "Each authorized adjustment component must remain unique.");
                    var adjustment = adjustments[0].Groups["body"].Value;
                    Assert.That(adjustment, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
                    var adjustmentScale = Regex.Match(adjustment, @"^  _liquidRefractionWidthScale: (?<value>[^\r\n]+)\r?$", RegexOptions.Multiline);
                    Assert.That(adjustmentScale.Success, Is.True);
                    Assert.That(float.Parse(adjustmentScale.Groups["value"].Value, CultureInfo.InvariantCulture), Is.EqualTo(0.75f));
                }
                expectedOverrideCount += 2;
            }
            else if (path == "Assets/Scenes/Practice.unity")
            {
                expectedOverrideCount = 8;
            }
            Assert.That(Regex.Matches(text, @"^  _liquidRefractionWidthScale:", RegexOptions.Multiline).Count, Is.EqualTo(expectedOverrideCount),
                "Only the explicitly authored cards and adjustment capsules may override their widths.");
        }

        /// <summary>Checks the peripheral ring and unrelated calibration panels retain their unmodified widths.</summary>
        /// <param name="path">The authored asset excluded from the requested width change.</param>
        [TestCase("Assets/Prefabs/UI/Buttons.prefab")]
        [TestCase("Assets/Scenes/Calibrator.unity")]
        public void LiquidRefractionWidth_ExcludedAssetsKeepTheirDefaultBand(string path)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            Assert.That(text, Does.Not.Contain("_liquidRefractionWidthScale:"),
                "The card-specific widths must not change the outer eight buttons or unrelated calibration panels.");
        }

        /// <summary>Checks only the two visible submonitor white plates reuse an existing panel capture without starting one.</summary>
        /// <param name="path">The shared UI prefab containing the existing white plate and clear foreground.</param>
        /// <param name="spriteId">The original atlas slice's unchanged local identifier.</param>
        [TestCase("Assets/Prefabs/UI/UserInfo.prefab", 2110853651L)]
        [TestCase("Assets/Prefabs/UI/ChartVisualDisplayer.prefab", 1073801904L)]
        public void SubmonitorWhitePlates_ReuseFirstCaptureWithoutMovingOrBlurringForeground(string path, long spriteId)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var image = prefab!.GetComponent<QualityUiImage>();
            var panel = prefab.GetComponent<UiBackdropPanel>();
            Assert.That(image, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<UiBackdropPanel>(true).Length, Is.EqualTo(1),
                "Names, chart contents and the avatar keep their original clear native rendering.");
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image.sprite, out var guid, out long id), Is.True);
            Assert.That(guid, Is.EqualTo("04e00e0b72a3d284ebec3f7160996aa1"));
            Assert.That(id, Is.EqualTo(spriteId));
            Assert.That(image.type, Is.EqualTo(UnityEngine.UI.Image.Type.Sliced));
            Assert.That(image.color.a, Is.EqualTo(1f));
            var properties = new SerializedObject(panel);
            Assert.That(properties.FindProperty("_reusePanelCaptureOnly").boolValue, Is.True);
            Assert.That(properties.FindProperty("_isButton").boolValue, Is.False);
            Assert.That(properties.FindProperty("_isCategory").boolValue, Is.False);
            Assert.That(properties.FindProperty("_backgroundImage").objectReferenceValue, Is.Null);
            Assert.That(properties.FindProperty("_buttonForegroundRoot").objectReferenceValue, Is.Null);
            Assert.That(properties.FindProperty("_fillAlpha").floatValue, Is.EqualTo(1f));
            Assert.That(properties.FindProperty("_referenceOpacity").floatValue, Is.EqualTo(1f));
            Assert.That(properties.FindProperty("_tintStrength").floatValue, Is.EqualTo(1f));
            Assert.That(properties.FindProperty("_frostStrength").floatValue, Is.EqualTo(0.02f));
            Assert.That(properties.FindProperty("_liquidArtworkBlend").floatValue, Is.EqualTo(0.25f),
                "The two submonitor plates retain 25 percent artwork and expose 75 percent transmitted background.");
            Assert.That(properties.FindProperty("_liquidFrostStrength"), Is.Null, "The superseded neutral-white veil must not survive beside the RGB artwork mix.");
            var clear = prefab.transform.Find(path.Contains("UserInfo") ? "UserName" : "Graph");
            Assert.That(clear, Is.Not.Null);
            Assert.That(clear.GetComponent<UiBackdropPanel>(), Is.Null);
            if (path.Contains("UserInfo"))
            {
                var avatar = prefab.transform.Find("UserIconBg/UserIcon");
                Assert.That(avatar, Is.Not.Null);
                Assert.That(avatar.GetComponent<UiBackdropPanel>(), Is.Null);
                var protectedStrip = properties.FindProperty("_liquidOriginalArtworkLeft").vector2Value;
                Assert.That(protectedStrip.x, Is.EqualTo(0.1406141269f).Within(0.0000001f));
                Assert.That(protectedStrip.y, Is.EqualTo(0.0098103336f).Within(0.0000001f));
            }
            else
            {
                Assert.That(properties.FindProperty("_liquidOriginalArtworkLeft").vector2Value, Is.EqualTo(UnityEngine.Vector2.zero));
            }
        }

        /// <summary>Checks the legacy chart implementation remains outside the new shared-UI white-plate opt-ins.</summary>
        [Test]
        public void LegacyChartPrefab_DoesNotGainAnActiveBackdropConsumer()
        {
            var text = FileSystem.File.Open("Assets/Prefabs/ChartVisualDisplayer.prefab").ReadAllText();
            Assert.That(text, Does.Not.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
        }

        /// <summary>Checks the real List instances retain both shared top-card glass components without disabling overrides.</summary>
        /// <param name="guid">The shared top-card prefab's persistent GUID.</param>
        /// <param name="panelId">The shared glass component's persistent identifier.</param>
        /// <param name="imageId">The shared quality-aware background Image identifier.</param>
        [TestCase("92067dc9350e4724081ee085aa8273f1", "4988950831640157352", "4988950831640157351")]
        [TestCase("baccd8ec487f66549b861e1dd324baf6", "7983312345234581847", "7983312345234581846")]
        public void ListTopCards_DoNotOverrideOrRemoveTheirSharedGlass(string guid, string panelId, string imageId)
        {
            var text = FileSystem.File.Open("Assets/Scenes/List.unity").ReadAllText();
            var count = 0;
            foreach (Match instance in Regex.Matches(text, @"^--- !u!1001 &-?\d+\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline))
            {
                var body = instance.Groups["body"].Value;
                if (!body.Contains("m_SourcePrefab: {fileID: 100100000, guid: " + guid))
                {
                    continue;
                }
                Assert.That(body, Does.Contain("m_RemovedComponents: []"));
                Assert.That(body, Does.Not.Contain("target: {fileID: " + panelId + ", guid: " + guid));
                Assert.That(body, Does.Not.Contain("target: {fileID: " + imageId + ", guid: " + guid));
                count++;
            }
            Assert.That(count, Is.EqualTo(1));
        }

        /// <summary>Checks every authored large or small menu plate becomes lighter without adding new blur consumers.</summary>
        /// <param name="path">The scene or prefab containing the original plate.</param>
        /// <param name="imageId">The unchanged quality-aware Image identifier.</param>
        /// <param name="spriteId">The original large cream or small white atlas slice.</param>
        [TestCase("Assets/Scenes/Setting.unity", "1053645854", "936887466")]
        [TestCase("Assets/Scenes/Setting.unity", "489720307", "660075131")]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", "5019321880153334812", "936887466")]
        [TestCase("Assets/Scenes/SortFind.unity", "1660259406", "936887466")]
        [TestCase("Assets/Scenes/Practice.unity", "325391450", "936887466")]
        [TestCase("Assets/Scenes/Calibrator.unity", "325391450", "936887466")]
        public void MenuPlateOpacity_KeepsOriginalArtworkAndReducesOnlyVertexAlpha(string path, string imageId,
            string spriteId)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var image = Regex.Match(text, @"^--- !u!114 &" + imageId +
                @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
                .Groups["body"].Value;
            Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
            Assert.That(image, Does.Contain("m_Sprite: {fileID: " + spriteId +
                ", guid: 04e00e0b72a3d284ebec3f7160996aa1, type: 3}"));
            Assert.That(image, Does.Contain("m_Material: {fileID: 2100000, guid: 78a3c5eb06624f448a0238577d1de902, type: 2}"));
            Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 0.85}"));
        }

        /// <summary>Checks the inner plus and minus capsules keep their original opacity rather than inheriting plate fading.</summary>
        [Test]
        public void SettingCapsules_RetainOriginalFullOpacityAndButtonArtwork()
        {
            var text = FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
            foreach (var imageId in new[] { "1623762598", "2074874836" })
            {
                var image = Regex.Match(text, @"^--- !u!114 &" + imageId +
                    @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
                    .Groups["body"].Value;
                Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 1}"));
                Assert.That(image, Does.Contain("guid: 1b12dc9204227dc42bb2fc6a3075a021"));
            }
        }

        /// <summary>Checks original menu layering remains PV, blue ring, large panel and existing foreground.</summary>
        /// <param name="path">The scene or prefab containing the authored background hierarchy.</param>
        /// <param name="parentId">The background RectTransform whose sibling order controls transparent composition.</param>
        /// <param name="glassId">The large cream glass panel's RectTransform.</param>
        /// <param name="whitePanelId">The optional original alpha-only white panel's RectTransform, or an empty string.</param>
        /// <param name="ringId">The blue ring's RectTransform.</param>
        /// <param name="finalForegroundId">The originally last foreground that must remain after the panels.</param>
        [TestCase("Assets/Scenes/Setting.unity", "1254486400", "1053645853", "489720306", "912764831", "1339324174")]
        [TestCase("Assets/Prefabs/List/Main_Display_ribbon.prefab", "347761307061707918", "8383740590471529115", "", "3424592043163546147", "1162168240910878356")]
        [TestCase("Assets/Scenes/SortFind.unity", "1127180454", "1660259405", "", "1662593949", "1688309037")]
        public void MenuLayers_KeepBlueRingBeforeLargeGlassAndExistingForeground(string path, string parentId,
            string glassId, string whitePanelId, string ringId, string finalForegroundId)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var parent = Regex.Match(text, @"^--- !u!224 &" + parentId +
                @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline);
            Assert.That(parent.Success, Is.True, path + ": authored background transform must exist.");
            var children = Regex.Match(parent.Groups["body"].Value,
                @"m_Children:\r?\n(?<children>(?:  - \{fileID: -?\d+\}\r?\n)+)").Groups["children"].Value;
            Assert.That(children, Is.Not.Empty);
            var glass = children.IndexOf("{fileID: " + glassId + "}", System.StringComparison.Ordinal);
            var ring = children.IndexOf("{fileID: " + ringId + "}", System.StringComparison.Ordinal);
            var foreground = children.IndexOf("{fileID: " + finalForegroundId + "}", System.StringComparison.Ordinal);
            Assert.That(ring, Is.GreaterThanOrEqualTo(0));
            Assert.That(glass, Is.GreaterThan(ring), "Keep the original blue ring behind the glass panel.");
            Assert.That(foreground, Is.GreaterThan(glass), "Keep the original final foreground after the panel.");
            if (whitePanelId.Length != 0)
            {
                var whitePanel = children.IndexOf("{fileID: " + whitePanelId + "}", System.StringComparison.Ordinal);
                Assert.That(whitePanel, Is.GreaterThan(glass));
                Assert.That(foreground, Is.GreaterThan(whitePanel));
            }
        }

        /// <summary>Checks the small white Setting panel preserves its original Gaussian-mode Alpha while opting into Liquid Glass.</summary>
        [Test]
        public void SettingWhitePanel_RetainsArtworkAndUsesBoardSourceInBothStyles()
        {
            var text = FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
            var image = Regex.Match(text, @"^--- !u!114 &489720307\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
            Assert.That(image, Does.Contain("m_Material: {fileID: 2100000, guid: 78a3c5eb06624f448a0238577d1de902, type: 2}"));
            Assert.That(image, Does.Contain("m_Sprite: {fileID: 660075131, guid: 04e00e0b72a3d284ebec3f7160996aa1, type: 3}"));
            Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 0.85}"));
            var panelGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/UiBackdropPanel.cs");
            var count = 0;
            foreach (Match component in s_components.Matches(text))
            {
                var body = component.Groups["body"].Value;
                if (body.Contains("m_GameObject: {fileID: 489720305}") && body.Contains("guid: " + panelGuid))
                {
                    Assert.That(body, Does.Contain("_liquidOnly: 0"));
                    Assert.That(body, Does.Contain("_referenceOpacity: 0.85"));
                    count++;
                }
            }
            Assert.That(count, Is.EqualTo(1));
        }

        /// <summary>Checks Search authors only Practice-style A4/A5 controls and preserves the board and navigation capture policies.</summary>
        [Test]
        public void SortFind_UsesPracticePeripheralConfirmationAndCancelWithoutChangingNavigation()
        {
            var text = FileSystem.File.Open("Assets/Scenes/SortFind.unity").ReadAllText();
            var backgroundBindings = Regex.Matches(text, @"_backgroundImage: \{fileID: (?<id>-?[1-9]\d*)\}");
            Assert.That(backgroundBindings.Count, Is.EqualTo(1));
            Assert.That(backgroundBindings[0].Groups["id"].Value, Is.EqualTo("1662593950"));
            var panelGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/UiBackdropPanel.cs");
            foreach (Match component in s_components.Matches(text))
            {
                var body = component.Groups["body"].Value;
                if (body.Contains("guid: " + panelGuid) && !body.Contains("_liquidOnly: 1"))
                {
                    Assert.That(body, Does.Contain("m_GameObject: {fileID: 1660259404}"),
                        "The new peripheral bodies inherit their existing shared-prefab policies.");
                }
            }
            var instance = Regex.Match(text, @"^--- !u!1001 &362213379\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(instance, Does.Contain("m_SourcePrefab: {fileID: 100100000, guid: 246320a2741b25248ab6e94bc499d92a, type: 3}"));
            Assert.That(instance, Does.Contain("m_TransformParent: {fileID: 1127180454}"));
            var removed = Regex.Match(instance, @"m_RemovedGameObjects:(?<body>.*?)    m_AddedGameObjects:",
                RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(Regex.Matches(removed, @"fileID:").Count, Is.EqualTo(6));
            foreach (var removedId in new[] { "6765661420530731545", "1494683800915637128", "3383897936242902499",
                "504485935048825702", "6380678675710360550", "162027289803103054" })
            {
                Assert.That(removed, Does.Contain("fileID: " + removedId + ","));
            }
            var main = Regex.Match(text, @"^--- !u!224 &1127180454\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(main, Does.Contain("{fileID: 362213380}"));
            Assert.That(text, Does.Not.Contain("m_Name: OkButton"));
            Assert.That(text, Does.Not.Contain("BindSensor: 15000000"), "The obsolete D5 confirmation graphic must be removed.");
            Assert.That(text, Does.Not.Contain("{fileID: 1237316694}"));
            var buttons = FileSystem.File.Open("Assets/Prefabs/UI/Buttons.prefab").ReadAllText();
            foreach (var control in new[]
            {
                ("5317505558870171490", "03000000", "8983832068243600665", "8647141364730774082", "846669334", "846669335",
                    "21300000, guid: 5b820f37f0cf96e4eb82a0384a4ae8d4"),
                ("7142667378218051414", "04000000", "7024678684790401055", "7303350980742751906", "1380209031", "1380209032",
                    "1951789352, guid: 736513dcbc18dec438227ea2c0cb9831")
            })
            {
                var binding = Regex.Match(buttons, @"^--- !u!114 &" + control.Item1 + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(binding, Does.Contain("BindButton: " + control.Item2));
                var panel = Regex.Match(buttons, @"^--- !u!114 &" + control.Item3 + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(panel, Does.Contain("_isButton: 1"));
                Assert.That(panel, Does.Not.Contain("_liquidOnly: 1"), "Both Gaussian and Liquid reuse the standard peripheral source.");
                Assert.That(panel, Does.Contain("_buttonForegroundRoot: {fileID: " + control.Item4 + "}"));
                Assert.That(instance, Does.Contain("targetCorrespondingSourceObject: {fileID: " + control.Item4 + ","));
                Assert.That(instance, Does.Contain("addedObject: {fileID: " + control.Item5 + "}"));
                var icon = Regex.Match(text, @"^--- !u!114 &" + control.Item6 + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(icon, Does.Contain("guid: fe87c0e1cc204ed48ad3b37840f39efc"));
                Assert.That(icon, Does.Contain("m_Sprite: {fileID: " + control.Item7 + ", type: 3}"));
                Assert.That(icon, Does.Contain("m_Color: {r: 0.85882354, g: 0.7529412, b: 0.52156866, a: 1}"));
            }
            foreach (var componentId in new[] { "1688309038", "2122532829" })
            {
                var body = Regex.Match(text, @"^--- !u!114 &" + componentId +
                    @"\r?\n(?<body>.*?)(?=^--- |\z)", RegexOptions.Multiline | RegexOptions.Singleline)
                    .Groups["body"].Value;
                Assert.That(body, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
                var materialGuid = Regex.Match(body,
                    @"m_Material: \{fileID: 2100000, guid: (?<guid>[a-f0-9]+), type: 2\}")
                    .Groups["guid"].Value;
                Assert.That(materialGuid, Is.Not.Empty);
                var material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(AssetDatabase.GUIDToAssetPath(materialGuid));
                Assert.That(material, Is.Not.Null);
                Assert.That(material!.renderQueue, Is.GreaterThan(MajUiBackdropBridge.BackgroundQueueUpperBound),
                    "Search navigation controls must remain after the background capture.");
            }
        }

        /// <summary>Checks the Search input's own nested Canvas has a raycaster without changing its keyboard settings.</summary>
        [Test]
        public void SortFind_MainDisplayRaycasterKeepsNestedSearchInputClickable()
        {
            var text = FileSystem.File.Open("Assets/Scenes/SortFind.unity").ReadAllText();
            var main = Regex.Match(text, @"^--- !u!1 &1127180453\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(main, Does.Contain("component: {fileID: 1127180458}"));
            Assert.That(main, Does.Contain("component: {fileID: 900000000000000120}"));
            var raycaster = Regex.Match(text, @"^--- !u!114 &900000000000000120\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(raycaster, Does.Contain("m_GameObject: {fileID: 1127180453}"));
            Assert.That(raycaster, Does.Contain("guid: dc42784cf147c0c48a680349fa168899"));
            Assert.That(raycaster, Does.Contain("m_Enabled: 1"));
            Assert.That(raycaster, Does.Contain("m_BlockingObjects: 0"));
            Assert.That(text, Does.Contain("m_TargetGraphic: {fileID: 658895492}"));
            Assert.That(text, Does.Contain("m_HideMobileInput: 1"));
            Assert.That(text, Does.Contain("m_HideSoftKeyboard: 0"));
            Assert.That(text, Does.Contain("m_DeselectOnBackgroundClick: 1"));
        }
        /// <summary>Checks calibration reuses the established Board/Peripheral policies without replacing its existing artwork or geometry.</summary>
        /// <param name="panelId">The existing glass component identifier.</param>
        /// <param name="ownerId">The unchanged scene GameObject identifier.</param>
        /// <param name="foregroundId">The existing child symbol transform, or zero for the cream board.</param>
        [TestCase("900000000000000018", "325391448", "0")]
        [TestCase("900000000000000001", "1556681020", "1924603244")]
        [TestCase("900000000000000002", "585776999", "806350225")]
        public void CalibratorGlass_UsesStrongBoardAndLatePeripheralSnapshots(string panelId, string ownerId, string foregroundId)
        {
            var text = FileSystem.File.Open("Assets/Scenes/Calibrator.unity").ReadAllText();
            var panel = Regex.Match(text, @"^--- !u!114 &" + panelId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(panel, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
            Assert.That(panel, Does.Contain("m_GameObject: {fileID: " + ownerId + "}"));
            Assert.That(panel, Does.Contain("_liquidOnly: 0"), "Both backdrop styles must select the same scene-source role.");
            Assert.That(panel, Does.Contain("_isCategory: 0"));
            Assert.That(panel, Does.Contain("_backgroundImage: {fileID: 0}"), "This scene has no separate blue-ring Image to bind.");
            Assert.That(panel, Does.Contain("_buttonForegroundRoot: {fileID: " + foregroundId + "}"));
            if (foregroundId == "0")
            {
                Assert.That(panel, Does.Contain("_useStrongBlur: 1"), "The cream plate must use the same RT1 strong source as Setting.");
                Assert.That(panel, Does.Contain("_liquidClippedCircle: 1"));
                Assert.That(panel, Does.Contain("_isButton: 0"));
                Assert.That(panel, Does.Contain("_liquidTintStrength: 0"));
                Assert.That(panel, Does.Contain("_liquidArtworkBlend: 0.4233333"));
            }
            else
            {
                Assert.That(panel, Does.Contain("_isButton: 1"), "The outer capsule must capture after the completed UI at RT3.");
                Assert.That(panel, Does.Contain("_liquidEllipse: 0"), "Peripheral policy already supplies its ellipse without native wide-bevel policy.");
                Assert.That(panel, Does.Not.Contain("_useStrongBlur:"));
                var foreground = Regex.Match(text, @"^--- !u!224 &" + foregroundId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(foreground, Is.Not.Empty, "Bind the existing symbol transform rather than creating another Image.");
            }
        }

        /// <summary>Checks the existing comment bubble and text share one explicit liquid-only foreground policy.</summary>
        [Test]
        public void LiquidOrdering_BindsCommentForeground()
        {
            var list = FileSystem.File.Open("Assets/Prefabs/List/Main_Display_ribbon.prefab").ReadAllText();
            var board = Regex.Match(list, @"^--- !u!114 &5019321880153334813\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(board, Does.Contain("_liquidForegroundRoot: {fileID: 3374659970506328570}"));
            var comment = Regex.Match(list, @"^--- !u!224 &3374659970506328570\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(comment, Does.Contain("m_GameObject: {fileID: 5216211417724343022}"));
            Assert.That(comment, Does.Contain("{fileID: 6427247033723599783}"), "The same group includes the spoken text and its bubble.");

        }

        /// <summary>Checks QR title and container stay ordinary white panels and the QR remains a native RawImage.</summary>
        [Test]
        public void LoginQrGraphics_KeepOriginalWhitePanelsAndNativeRawImage()
        {
            var login = FileSystem.File.Open("Assets/Scenes/Login.unity").ReadAllText();
            var panelGuid = AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/UiBackdropPanel.cs");
            Assert.That(panelGuid, Is.Not.Empty);
            foreach (var imageId in new[] { "583075179", "640682228" })
            {
                var image = Regex.Match(login, @"^--- !u!114 &" + imageId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
                Assert.That(image, Does.Contain("m_Material: {fileID: 2100000, guid: 78a3c5eb06624f448a0238577d1de902, type: 2}"));
                Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 1}"));
                Assert.That(image, Does.Contain("m_Type: 1"), "Preserve the original sliced white artwork.");
                Assert.That(image, Does.Contain("m_Sprite: {fileID: -547999258, guid: 04e00e0b72a3d284ebec3f7160996aa1"));
                var owner = Regex.Match(image, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
                Assert.That(owner, Is.Not.Empty);
                foreach (Match component in s_components.Matches(login))
                {
                    var body = component.Groups["body"].Value;
                    Assert.That(body.Contains("guid: " + panelGuid) && body.Contains("m_GameObject: {fileID: " + owner + "}"),
                        Is.False, "Neither QR backing plate may opt into either glass style.");
                }
            }
            var qr = Regex.Match(login, @"^--- !u!114 &1359979665\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(qr, Does.Contain("m_Script: {fileID: 11500000, guid: 1344c3c82d62a2a41a3576d8abb8e3ea, type: 3}"));
            Assert.That(qr, Does.Contain("m_Material: {fileID: 2100000, guid: 78a3c5eb06624f448a0238577d1de902, type: 2}"),
                "All presets retain the original black-and-white QR material.");
            Assert.That(qr, Does.Not.Contain("_liquidGlassMaterial:"));
            Assert.That(login, Does.Not.Contain("_liquidCaptureSource:"));
        }

        /// <summary>Checks login plates retain native nine-slice geometry while the two circular keys use the final capture.</summary>
        /// <param name="imageId">The existing quality-aware image identifier.</param>
        /// <param name="panelId">The statically authored glass component identifier.</param>
        /// <param name="peripheral">Whether this is a circular external key rather than a native input plate.</param>
        [TestCase("1998670342", "900000000000000101", false)]
        [TestCase("486500992", "900000000000000102", false)]
        [TestCase("86439850", "900000000000000003", true)]
        [TestCase("256638719", "900000000000000004", true)]
        public void LoginSurfaces_UseBothStylesWithoutChangingInputGraphics(string imageId, string panelId, bool peripheral)
        {
            var text = FileSystem.File.Open("Assets/Scenes/Login.unity").ReadAllText();
            var image = Regex.Match(text, @"^--- !u!114 &" + imageId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            var panel = Regex.Match(text, @"^--- !u!114 &" + panelId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            var ownerId = Regex.Match(image, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
            var owner = Regex.Match(text, @"^--- !u!1 &" + ownerId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
            Assert.That(image, Does.Contain("guid: 78a3c5eb06624f448a0238577d1de902"), "Ordinary presets retain the original source material.");
            Assert.That(image, Does.Contain("m_Type: " + (peripheral ? "0" : "1")));
            Assert.That(owner, Does.Contain("component: {fileID: " + panelId + "}"));
            Assert.That(panel, Does.Contain("m_GameObject: {fileID: " + ownerId + "}"));
            Assert.That(panel, Does.Contain("_liquidOnly: 0"));
            Assert.That(panel, Does.Contain("_isButton: " + (peripheral ? "1" : "0")));
            Assert.That(panel, Does.Contain("_isCategory: 0"), "Native nine-slice corners are independent of carousel capsule geometry.");
            Assert.That(panel, Does.Not.Contain("_useStrongBlur: 1"));
            Assert.That(panel, Does.Contain("_fillAlpha: 1"));
            if (peripheral)
            {
                var rectId = imageId == "86439850" ? "86439849" : "256638718";
                Assert.That(panel, Does.Contain("_buttonForegroundRoot: {fileID: " + rectId + "}"));
            }
            else
            {
                Assert.That(panel, Does.Contain("_buttonForegroundRoot: {fileID: 0}"));
                Assert.That(panel, Does.Contain("_liquidBodyInsets:"));
            }
            if (imageId == "1998670342" || imageId == "486500992")
            {
                Assert.That(image, Does.Contain("m_Sprite: {fileID: 2079732121"));
                var inputId = imageId == "1998670342" ? "1998670344" : "486500994";
                var input = Regex.Match(text, @"^--- !u!114 &" + inputId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(input, Does.Contain("m_TargetGraphic: {fileID: " + imageId + "}"));
                Assert.That(input, Does.Contain("m_SelectedSprite: {fileID: 739469296"));
            }
        }

        /// <summary>Checks Practice boards and adjustment arrows share their established both-style role policies.</summary>
        [Test]
        public void PracticeSurfaces_ReuseListSettingAndChartPoliciesWithoutReplacingGraphics()
        {
            var text = FileSystem.File.Open("Assets/Scenes/Practice.unity").ReadAllText();
            var controls = new[]
            {
                (Image: "325391450", Panel: "900000000000000019", Sprite: "936887466"),
                (Image: "1426382402", Panel: "900000000000000005", Sprite: "200455113"),
                (Image: "2027712564", Panel: "900000000000000006", Sprite: "170564351"),
                (Image: "1244187158", Panel: "900000000000000007", Sprite: "200455113"),
                (Image: "1472799756", Panel: "900000000000000008", Sprite: "200455113"),
                (Image: "189428624", Panel: "900000000000000009", Sprite: "170564351"),
                (Image: "158197088", Panel: "900000000000000010", Sprite: "170564351"),
                (Image: "659019541", Panel: "900000000000000022", Sprite: "1073801904")
            };
            foreach (var control in controls)
            {
                var board = control.Image == "325391450";
                var chart = control.Image == "659019541";
                var panel = Regex.Match(text, @"^--- !u!114 &" + control.Panel + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                var image = Regex.Match(text, @"^--- !u!114 &" + control.Image + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(panel, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
                Assert.That(panel, Does.Contain("_liquidOnly: 0"), "Both Gaussian and liquid glass must activate this role.");
                Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
                Assert.That(image, Does.Contain("m_Material: {fileID: 2100000, guid: 78a3c5eb06624f448a0238577d1de902, type: 2}"));
                Assert.That(image, Does.Contain("m_Sprite: {fileID: " + control.Sprite + ", guid: 04e00e0b72a3d284ebec3f7160996aa1, type: 3}"));
                Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: " + (board ? "0.85" : "1") + "}"));
                var ownerId = Regex.Match(image, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
                Assert.That(ownerId, Is.Not.Empty);
                Assert.That(panel, Does.Contain("m_GameObject: {fileID: " + ownerId + "}"));
                var owner = Regex.Match(text, @"^--- !u!1 &" + ownerId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(owner, Does.Contain("component: {fileID: " + control.Panel + "}"));
                var baselinePath = board ? "Assets/Prefabs/List/Main_Display_ribbon.prefab"
                    : chart ? "Assets/Prefabs/UI/ChartVisualDisplayer.prefab" : "Assets/Scenes/Setting.unity";
                var baselineId = board ? "5019321880153334813" : chart ? "7983312345234581847" : "900000000000000014";
                var baselineText = FileSystem.File.Open(baselinePath).ReadAllText();
                var baseline = Regex.Match(baselineText, @"^--- !u!114 &" + baselineId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                foreach (var field in new[] { "_fillAlpha", "_tintStrength", "_liquidTintStrength", "_liquidArtworkBlend",
                    "_liquidRefractionWidthScale", "_frostStrength", "_referenceOpacity" })
                {
                    var expected = Regex.Match(baseline, @"^  " + field + @": (?<value>[^\r\n]+)", RegexOptions.Multiline);
                    var actual = Regex.Match(panel, @"^  " + field + @": (?<value>[^\r\n]+)", RegexOptions.Multiline);
                    Assert.That(expected.Success && actual.Success, Is.True, control.Panel + ": " + field);
                    Assert.That(actual.Groups["value"].Value, Is.EqualTo(expected.Groups["value"].Value), control.Panel + ": " + field);
                }
                if (board || chart)
                {
                    Assert.That(panel, Does.Contain("_useStrongBlur: 1"));
                    Assert.That(panel, Does.Contain("_reusePanelCaptureOnly: " + (chart ? "1" : "0")));
                }
                else
                {
                    Assert.That(panel, Does.Contain("_usePeripheralBackdrop: 1"));
                    Assert.That(panel, Does.Contain("_liquidCapsule: 1"));
                    Assert.That(panel, Does.Contain("_fillSamplePosition: {x: 0.25, y: 0.5}"));
                    Assert.That(panel, Does.Not.Contain("_useStrongBlur: 1"));
                }
                if (chart)
                {
                    foreach (var field in new[] { "_liquidBodyInsets", "_liquidCornerRadius" })
                    {
                        var expected = Regex.Match(baseline, @"^  " + field + @": (?<value>[^\r\n]+)", RegexOptions.Multiline);
                        Assert.That(panel, Does.Contain(field + ": " + expected.Groups["value"].Value));
                    }
                }
            }
        }

        /// <summary>Checks all explicitly identified menu buttons and remaining plates opt into Liquid without altering Gaussian coverage.</summary>
        /// <param name="path">The scene containing the authored controls.</param>
        /// <param name="imageIds">The exact preserved quality-aware Image identifiers that gain Liquid Glass.</param>
        [TestCase("Assets/Scenes/SortFind.unity", "2122532829,1688309038")]
        public void LiquidOnlyControls_KeepNativeOrderAndSampleUndecoratedFill(string path, string imageIds)
        {
            var text = FileSystem.File.Open(path).ReadAllText();
            var actualImages = new HashSet<string>();
            foreach (Match component in s_components.Matches(text))
            {
                var panel = component.Groups["body"].Value;
                if (!panel.Contains("guid: ef06d39c7c35439cafc6637c942934d4") || !panel.Contains("_liquidOnly: 1"))
                {
                    continue;
                }
                var gameObject = Regex.Match(panel, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
                var panelId = Regex.Match(component.Value, @"^--- !u!114 &(?<id>-?\d+)").Groups["id"].Value;
                var owner = Regex.Match(text, @"^--- !u!1 &" + gameObject + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                    RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
                Assert.That(owner, Does.Contain("component: {fileID: " + panelId + "}"));
                Assert.That(panel, Does.Contain("_isButton: 0"));
                Assert.That(panel, Does.Contain("_isCategory: 0"));
                Assert.That(panel, Does.Contain("_backgroundImage: {fileID: 0}"));
                Assert.That(panel, Does.Contain("_buttonForegroundRoot: {fileID: 0}"));

                var matches = 0;
                foreach (Match candidate in s_components.Matches(text))
                {
                    var image = candidate.Groups["body"].Value;
                    if (!image.Contains("guid: 7c51cc521e1747f0ba99ae9b65785bd5") ||
                        !image.Contains("m_GameObject: {fileID: " + gameObject + "}"))
                    {
                        continue;
                    }
                    matches++;
                    var id = Regex.Match(candidate.Value, @"^--- !u!114 &(?<id>-?\d+)").Groups["id"].Value;
                    Assert.That(actualImages.Add(id), Is.True);
                    var sprite = Regex.Match(image, @"m_Sprite: \{fileID: (?<id>-?\d+), guid: 04e00e0b72a3d284ebec3f7160996aa1").Groups["id"].Value;
                    Assert.That(sprite, Is.Not.Empty);
                    var capsule = sprite == "1269494906" || sprite == "87351688" || sprite == "170564351" || sprite == "200455113";
                    var ellipse = sprite == "-1089058292";
                    Assert.That(panel, Does.Contain("_liquidCapsule: " + (capsule ? "1" : "0")));
                    Assert.That(panel, Does.Contain("_liquidEllipse: " + (ellipse ? "1" : "0")));
                    Assert.That(panel, Does.Contain("_fillSamplePosition: {x: " + (capsule ? "0.25" : "0.5") + ", y: 0.5}"));
                    var originalAlpha = Regex.Match(image, @"m_Color: \{r: [^,]+, g: [^,]+, b: [^,]+, a: (?<a>[^}]+)\}").Groups["a"].Value;
                    Assert.That(panel, Does.Contain("_referenceOpacity: " + originalAlpha));
                    var fillAlpha = sprite == "660075131" ? "0.4980392" : sprite == "936887466" ? "0.5019608" : "1";
                    Assert.That(panel, Does.Contain("_fillAlpha: " + fillAlpha));
                }
                Assert.That(matches, Is.EqualTo(1), "Each opt-in reuses one original QualityUiImage.");
            }
            Assert.That(actualImages, Is.EquivalentTo(imageIds.Split(',')), "Do not omit controls or opt icons/text fields into glass.");
        }

        /// <summary>Checks the Setting value plate and adjustment capsules enable both styles without replacing their authored images.</summary>
        /// <param name="panelId">The existing backdrop component identifier.</param>
        /// <param name="imageId">The unchanged quality-aware Image component identifier.</param>
        /// <param name="spriteId">The actual dark plate or adjustment capsule atlas slice.</param>
        /// <param name="peripheral">Whether the capsule samples the last peripheral prefix instead of the category prefix.</param>
        [TestCase("900000000000000014", "2074874836", "1269494906", true)]
        [TestCase("900000000000000015", "1623762598", "87351688", true)]
        [TestCase("900000000000000016", "1339324175", "-784170209", false)]
        public void SettingValueAndAdjustmentPlates_EnableBothStylesAndKeepArtwork(string panelId, string imageId, string spriteId, bool peripheral)
        {
            var text = FileSystem.File.Open("Assets/Scenes/Setting.unity").ReadAllText();
            var panel = Regex.Match(text, @"^--- !u!114 &" + panelId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            var image = Regex.Match(text, @"^--- !u!114 &" + imageId + @"\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(panel, Does.Contain("guid: ef06d39c7c35439cafc6637c942934d4"));
            Assert.That(panel, Does.Contain("_liquidOnly: 0"), "Gaussian must use the existing captured category source as well as Liquid.");
            Assert.That(panel, Does.Contain("_isButton: 0"));
            Assert.That(panel, Does.Contain("_isCategory: 0"));
            Assert.That(panel.Contains("_usePeripheralBackdrop: 1"), Is.EqualTo(peripheral), "Source selection must not turn the capsules into elliptical outer buttons.");
            Assert.That(panel, Does.Contain("_liquidCapsule: " + (peripheral ? "1" : "0")));
            if (peripheral)
            {
                Assert.That(panel, Does.Contain("_liquidRefractionWidthScale: 0.75"), "Adjustment capsules match the smaller category lens rather than inheriting the full-height cap.");
            }
            Assert.That(panel, Does.Not.Contain("_useStrongBlur: 1"), "These foreground controls must not sample PV-only RT1.");
            Assert.That(image, Does.Contain("guid: 7c51cc521e1747f0ba99ae9b65785bd5"));
            Assert.That(image, Does.Contain("m_Sprite: {fileID: " + spriteId + ", guid: 04e00e0b72a3d284ebec3f7160996aa1, type: 3}"));
            Assert.That(image, Does.Contain("m_Material: {fileID: 2100000, guid: 1b12dc9204227dc42bb2fc6a3075a021, type: 2}"));
            Assert.That(image, Does.Contain("m_Color: {r: 1, g: 1, b: 1, a: 1}"));
            var owner = Regex.Match(image, @"m_GameObject: \{fileID: (?<id>-?\d+)\}").Groups["id"].Value;
            Assert.That(panel, Does.Contain("m_GameObject: {fileID: " + owner + "}"));
        }
        /// <summary>Checks the eight shared ring bodies opt into the second capture with their exact original fill palette.</summary>
        [Test]
        public void RingButtonBodies_KeepAuthoredFillAndExplicitForegroundRoots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/UI/Buttons.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab!.GetComponentsInChildren<UiBackdropPanel>(true).Length, Is.EqualTo(8));
            for (var index = 1; index <= 8; index++)
            {
                var button = prefab.transform.Find("Button" + index);
                Assert.That(button, Is.Not.Null);
                var background = button.Find("BG");
                Assert.That(background, Is.Not.Null);
                var image = background.GetComponent<QualityUiImage>();
                var panel = background.GetComponent<UiBackdropPanel>();
                Assert.That(image, Is.Not.Null);
                Assert.That(panel, Is.Not.Null);
                Assert.That(background.GetSiblingIndex(), Is.Zero);
                Assert.That(background.localPosition.z, Is.EqualTo(1f));
                Assert.That(image.sprite, Is.Not.Null);
                var dark = index == 3 || index == 4 || index == 6;
                Assert.That(image.sprite.name, Is.EqualTo(dark ? "newui@3x_20" : "newui@3x_21"));
                Assert.That(image.SourceMaterial.shader.name, Is.EqualTo("Maj/Common/Opaque"));
                Assert.That(image.type, Is.EqualTo(UnityEngine.UI.Image.Type.Simple));
                Assert.That(image.preserveAspect, Is.True);
                Assert.That(image.color.a, Is.EqualTo(1f));
                var properties = new SerializedObject(panel);
                Assert.That(properties.FindProperty("_isButton").boolValue, Is.True);
                Assert.That(properties.FindProperty("_buttonForegroundRoot").objectReferenceValue, Is.SameAs(button));
                Assert.That(properties.FindProperty("_backgroundImage").objectReferenceValue, Is.Null);
                Assert.That(properties.FindProperty("_fillAlpha").floatValue, Is.EqualTo(1f));
                Assert.That(properties.FindProperty("_referenceOpacity").floatValue, Is.EqualTo(1f));
                Assert.That(properties.FindProperty("_tintStrength").floatValue, Is.EqualTo(1f));
                Assert.That(properties.FindProperty("_frostStrength").floatValue, Is.Zero);
                Assert.That(properties.FindProperty("_liquidArtworkBlend").floatValue, Is.EqualTo(0.25f));
                Assert.That(properties.FindProperty("_liquidTintStrength").floatValue, Is.EqualTo(0.9f));
            }
            var disabledRootImage = prefab.transform.Find("Button1").GetComponent<QualityUiImage>();
            Assert.That(disabledRootImage.enabled, Is.False);
            Assert.That(disabledRootImage.GetComponent<UiBackdropPanel>(), Is.Null);
        }

        /// <summary>Checks each category uses one fixed-cap body and shares the existing category snapshot.</summary>
        /// <param name="path">The shared category prefab.</param>
        /// <param name="bodyPath">The body child path, or an empty string for its root Image.</param>
        /// <param name="foregroundPath">The explicit sharp hierarchy, or an empty string for the complete root.</param>
        [TestCase("Assets/Prefabs/Setting/MenuTitleDisplayer.prefab", "", "TitleViewport")]
        [TestCase("Assets/Prefabs/List/Model/CollectionDisplayer.prefab", "BG", "")]
        public void CategoryBackgrounds_KeepWholeGroupsTogetherWhileReusingThePanelSnapshot(string path, string bodyPath,
            string foregroundPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab!.GetComponentsInChildren<UiBackdropPanel>(true).Length, Is.EqualTo(1));
            Assert.That(prefab.transform.Find("SelectedBackground"), Is.Null,
                "Selection must resize and tint the existing body instead of crossfading two backgrounds.");
            var body = bodyPath.Length == 0 ? prefab.transform : prefab.transform.Find(bodyPath);
            Assert.That(body, Is.Not.Null);
            var image = body.GetComponent<QualityUiImage>();
            var panel = body.GetComponent<UiBackdropPanel>();
            Assert.That(image, Is.Not.Null);
            Assert.That(panel, Is.Not.Null);
            Assert.That(image.color.a, Is.EqualTo(1f));
            Assert.That(image.SourceMaterial.shader.name, Is.EqualTo("Maj/UI/Rounded Rect"));
            Assert.That(image.sprite, Is.Null);
            Assert.That(image.type, Is.EqualTo(UnityEngine.UI.Image.Type.Simple));
            var properties = new SerializedObject(panel);
            Assert.That(properties.FindProperty("_isButton").boolValue, Is.False);
            Assert.That(properties.FindProperty("_isCategory").boolValue, Is.True);
            Assert.That(properties.FindProperty("_backgroundImage").objectReferenceValue, Is.Null);
            Assert.That(properties.FindProperty("_fillAlpha").floatValue, Is.EqualTo(1f));
            Assert.That(properties.FindProperty("_referenceOpacity").floatValue, Is.EqualTo(1f));
            Assert.That(properties.FindProperty("_tintStrength").floatValue, Is.EqualTo(1f));
            Assert.That(properties.FindProperty("_frostStrength").floatValue, Is.Zero);
            Assert.That(properties.FindProperty("_liquidOnly").boolValue, Is.False);
            Assert.That(properties.FindProperty("_liquidTintStrength").floatValue, Is.EqualTo(0.9f));
            Assert.That(properties.FindProperty("_liquidArtworkBlend").floatValue, Is.EqualTo(0.25f));
            var insets = properties.FindProperty("_liquidBodyInsets").vector4Value;
            Assert.That(insets, Is.EqualTo(UnityEngine.Vector4.zero),
                "The native body has no transparent atlas padding that could shift its optical contour.");
            var expectedRoot = foregroundPath.Length == 0 ? prefab.transform : prefab.transform.Find(foregroundPath);
            Assert.That(properties.FindProperty("_buttonForegroundRoot").objectReferenceValue, Is.SameAs(expectedRoot));
            var yaml = FileSystem.File.Open(path).ReadAllText();
            var title = Regex.Match(yaml, @"^--- !u!114 &857430918264730004\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(title, Does.Contain("guid: 64704fb03abf4531a01420ed392c5264"));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image, out _, out long imageId), Is.True);
            Assert.That(title, Does.Contain("_background: {fileID: " + imageId + "}"));
            Assert.That(title, Does.Contain("_selectedWidth: 254.69"));
            Assert.That(title, Does.Contain("_selectedBackgroundColor: {r: 0.62352943, g: 0.6901961, b: 0.7372549, a: 1}"));
        }

        /// <summary>Checks category titles have an authored clipping viewport and never shrink or ellipsize selected text.</summary>
        /// <param name="path">The shared category prefab.</param>
        /// <param name="titleName">The original title child's name.</param>
        /// <param name="hasIcon">Whether this category has a separate icon that rises above its selected body.</param>
        [TestCase("Assets/Prefabs/Setting/MenuTitleDisplayer.prefab", "Title", false)]
        [TestCase("Assets/Prefabs/List/Model/CollectionDisplayer.prefab", "Name", true)]
        public void CategoryTitles_KeepFixedFontSizesAndAnAuthoredMarqueeViewport(string path, string titleName, bool hasIcon)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var viewport = prefab!.transform.Find("TitleViewport");
            Assert.That(viewport, Is.Not.Null);
            Assert.That(viewport.childCount, Is.EqualTo(1), "One existing text moves inside its static viewport.");
            Assert.That(viewport.GetComponent<UnityEngine.UI.RectMask2D>(), Is.Not.Null);
            var text = viewport.Find(titleName).GetComponent<TMPro.TextMeshProUGUI>();
            Assert.That(text, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Length, Is.EqualTo(1));
            Assert.That(text.fontSize, Is.EqualTo(22f));
            Assert.That(text.enableAutoSizing, Is.False);
            Assert.That(text.overflowMode, Is.EqualTo(TMPro.TextOverflowModes.Overflow));
            Assert.That(text.rectTransform.localScale, Is.EqualTo(UnityEngine.Vector3.one));
            var yaml = FileSystem.File.Open(path).ReadAllText();
            var presentation = Regex.Match(yaml, @"^--- !u!114 &857430918264730004\r?\n(?<body>.*?)(?=^--- |\z)",
                RegexOptions.Multiline | RegexOptions.Singleline).Groups["body"].Value;
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(text, out _, out long textId), Is.True);
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(viewport, out _, out long viewportId), Is.True);
            Assert.That(presentation, Does.Contain("_titleDisplayer: {fileID: " + textId + "}"));
            Assert.That(presentation, Does.Contain("_titleViewport: {fileID: " + viewportId + "}"));
            Assert.That(presentation, Does.Contain("_selectedFontSize: 32"));
            Assert.That(presentation, Does.Contain("_unselectedTitleInset: 4"));
            Assert.That(presentation, Does.Contain("_selectedTitleInset: 49.345"));
            Assert.That(((UnityEngine.RectTransform)viewport).rect.width, Is.EqualTo(139.83f).Within(0.001f));
            Assert.That(text.rectTransform.rect.width, Is.EqualTo(((UnityEngine.RectTransform)viewport).rect.width));
            var selectedWidth = float.Parse(Regex.Match(presentation, @"_selectedWidth: (?<value>[^\r\n]+)")
                .Groups["value"].Value, CultureInfo.InvariantCulture);
            var selectedInset = float.Parse(Regex.Match(presentation, @"_selectedTitleInset: (?<value>[^\r\n]+)")
                .Groups["value"].Value, CultureInfo.InvariantCulture);
            Assert.That(selectedWidth - 2f * selectedInset, Is.EqualTo(156f).Within(0.001f));
            Assert.That((selectedWidth - 2f * selectedInset) * 0.5f, Is.LessThanOrEqualTo(80f - 2f),
                "Both category rows retain two design units between the text viewport and the existing arrows' inner edges.");
            if (hasIcon)
            {
                var iconRect = (UnityEngine.RectTransform)prefab.transform.Find("Icon");
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(iconRect, out _, out long iconId), Is.True);
                Assert.That(presentation, Does.Contain("_iconTransform: {fileID: " + iconId + "}"));
                Assert.That(iconRect.sizeDelta, Is.EqualTo(new UnityEngine.Vector2(24f, 24f)));
                Assert.That(presentation, Does.Contain("_selectedIconSizeMultiplier: 2"));
                var selectedIconY = Regex.Match(presentation, @"_selectedIconPosition: \{x: [^,]+, y: (?<value>[^}]+)\}");
                Assert.That(selectedIconY.Success, Is.True);
                var selectedIconBottom = float.Parse(selectedIconY.Groups["value"].Value, CultureInfo.InvariantCulture)
                    - iconRect.rect.height;
                Assert.That(selectedIconBottom,
                    Is.GreaterThanOrEqualTo(((UnityEngine.RectTransform)prefab.transform).rect.height * 0.5f - 1f),
                    "The doubled icon rests at the unchanged body's upper edge without moving the category row.");
            }
            else
            {
                Assert.That(presentation, Does.Contain("_iconTransform: {fileID: 0}"));
            }
        }

        /// <summary>Checks the real List carousel owns its selected style without a fixed duplicate name, icon, bar or shader-mask binding.</summary>
        [Test]
        public void ListCategories_UseRealMovingTabsWithoutAFixedSelectedOverlay()
        {
            var path = "Assets/Prefabs/List/Main_Display_ribbon.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var group = prefab!.transform.Find("CollectionList");
            Assert.That(group, Is.Not.Null);
            var pool = group.Find("Collections");
            Assert.That(pool, Is.Not.Null);
            Assert.That(pool.childCount, Is.EqualTo(9));
            Assert.That(group.Find("Displayer"), Is.Null, "No fixed central background, duplicate title or icon subtree remains.");
            foreach (UnityEngine.Transform row in pool)
            {
                Assert.That(((UnityEngine.RectTransform)row).sizeDelta, Is.EqualTo(new UnityEngine.Vector2(147.83f, 76.8f)));
                var normal = row.Find("BG").GetComponent<QualityUiImage>();
                Assert.That(normal.SourceMaterial.shader.name, Is.EqualTo("Maj/UI/Rounded Rect"));
                Assert.That(normal.sprite, Is.Null);
                Assert.That(row.Find("SelectedBackground"), Is.Null);
                Assert.That(row.Find("TitleViewport/Name"), Is.Not.Null);
                Assert.That(row.Find("Icon"), Is.Not.Null);
                Assert.That(row.GetComponentsInChildren<UiBackdropPanel>(true).Length, Is.EqualTo(1));
            }
            var yaml = FileSystem.File.Open(path).ReadAllText();
            Assert.That(yaml, Does.Not.Contain("_selectedCollectionBar"));
            Assert.That(yaml, Does.Not.Contain("_collectionOcclusion"));
            var source = FileSystem.File.Open("Assets/Prefabs/List/Model/CollectionDisplayer.prefab").ReadAllText();
            Assert.That(source, Does.Contain("_tabDisplayer: {fileID: 857430918264730004}"));
        }

        /// <summary>Checks independent navigation arrows stay above moving tabs without restoring the old central overlay.</summary>
        [Test]
        public void ListCategoryNavigation_KeepsNativeArrowsAndSensorBindingsAboveMovingTabs()
        {
            var path = "Assets/Prefabs/List/Main_Display_ribbon.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var group = prefab!.transform.Find("CollectionList");
            Assert.That(group, Is.Not.Null);
            Assert.That(group.Find("Displayer"), Is.Null);
            var navigation = group.Find("Navigation");
            Assert.That(navigation, Is.Not.Null);
            Assert.That(navigation.childCount, Is.EqualTo(2));
            Assert.That(navigation.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length, Is.EqualTo(2));
            Assert.That(navigation.GetComponentsInChildren<UiBackdropPanel>(true), Is.Empty);
            var arrows = navigation.GetComponent<UnityEngine.CanvasGroup>();
            Assert.That(arrows, Is.Not.Null, "Only the two navigation arrows share the category transition fade.");
            Assert.That(arrows.alpha, Is.EqualTo(1f));
            Assert.That(navigation.GetComponentsInChildren<UnityEngine.CanvasGroup>(true).Length, Is.EqualTo(1));
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(arrows, out _, out long arrowsId), Is.True);
            var yaml = FileSystem.File.Open(path).ReadAllText();
            Assert.That(yaml, Does.Contain("_navigationArrows: {fileID: " + arrowsId + "}"));
            foreach (UnityEngine.Transform arrow in navigation)
            {
                Assert.That(arrow.name, Is.EqualTo("L").Or.EqualTo("R"));
                var rect = (UnityEngine.RectTransform)arrow;
                Assert.That(rect.anchoredPosition, Is.EqualTo(new UnityEngine.Vector2(arrow.name == "L" ? -96f : 96f, 0f)));
                Assert.That(rect.sizeDelta, Is.EqualTo(new UnityEngine.Vector2(32f, 32f)));
                var image = arrow.GetComponent<UnityEngine.UI.Image>();
                Assert.That(image, Is.TypeOf<UnityEngine.UI.Image>(), "Native Images retain their explicit queue in every UI theme.");
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(image.material.shader.name, Is.EqualTo("Maj/UI/Default"));
                Assert.That(image.material.renderQueue, Is.EqualTo(3301));
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(arrow.gameObject, out _, out long gameObjectId), Is.True);
                var animationCount = 0;
                foreach (Match component in s_components.Matches(yaml))
                {
                    var body = component.Groups["body"].Value;
                    if (body.Contains("m_GameObject: {fileID: " + gameObjectId + "}") &&
                        body.Contains("guid: 3ae676a105b153c449b10f977d8a884e"))
                    {
                        Assert.That(body, Does.Contain("BindSensor: " + (arrow.name == "L" ? "0f000000" : "08000000")));
                        animationCount++;
                    }
                }
                Assert.That(animationCount, Is.EqualTo(1), "Each visible arrow retains its original input animation component.");
            }
        }
        /// <summary>Checks the actual nested List symbols and radial hold progress remain sharp foreground rather than blurred bodies.</summary>
        [Test]
        public void RingButtonForegrounds_KeepSymbolsAndHoldProgressOutsideBlur()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/List/Main_Display_ribbon.prefab");
            Assert.That(prefab, Is.Not.Null);
            var buttons = prefab!.transform.Find("Buttons");
            Assert.That(buttons, Is.Not.Null);
            var progressCount = 0;
            var foregroundCount = 0;
            for (var index = 1; index <= 8; index++)
            {
                var button = buttons.Find("Button" + index);
                var background = button.Find("BG");
                Assert.That(background.GetComponent<UiBackdropPanel>(), Is.Not.Null);
                Assert.That(button.GetComponentsInChildren<UiBackdropPanel>(true).Length, Is.EqualTo(1));
                foreach (UnityEngine.Transform child in button)
                {
                    if (child == background)
                    {
                        continue;
                    }
                    Assert.That(child.GetComponentsInChildren<UiBackdropPanel>(true), Is.Empty);
                    foreach (var image in child.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    {
                        foregroundCount++;
                        if (image.type == UnityEngine.UI.Image.Type.Filled)
                        {
                            Assert.That(image.fillMethod, Is.EqualTo(UnityEngine.UI.Image.FillMethod.Radial360));
                            progressCount++;
                        }
                    }
                }
            }
            Assert.That(foregroundCount, Is.EqualTo(9));
            Assert.That(progressCount, Is.EqualTo(1));
            var holdPrefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/UI/ButtonHoldCircle.prefab");
            Assert.That(holdPrefab, Is.Not.Null);
            Assert.That(holdPrefab!.GetComponentsInChildren<UiBackdropPanel>(true), Is.Empty);
        }

    }
}

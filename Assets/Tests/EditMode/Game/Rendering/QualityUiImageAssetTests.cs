using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MajdataPlay.IO.Storage;
using MajdataPlay.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

#nullable enable
namespace MajdataPlay.Tests.Game.Rendering
{
    /// <summary>Protects the serialized UI bindings used by the quality-dependent Ribbon atlas.</summary>
    public sealed class QualityUiImageAssetTests
    {
        /// <summary>Identifies the existing atlas whose Sprite references must remain unchanged.</summary>
        private const string AtlasGuid = "04e00e0b72a3d284ebec3f7160996aa1";

        /// <summary>Identifies the quality-aware Image script without changing component file identifiers.</summary>
        private const string QualityImageGuid = "7c51cc521e1747f0ba99ae9b65785bd5";

        /// <summary>Identifies Unity's ordinary Image, which cannot respond to UI outline changes.</summary>
        private const string StandardImageGuid = "fe87c0e1cc204ed48ad3b37840f39efc";

        /// <summary>Matches complete Unity YAML component records while retaining their local identifiers.</summary>
        private static readonly Regex s_documents = new Regex(
            @"^--- !u!(?<kind>\d+) &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- |\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        /// <summary>Checks every authored Image that directly uses a Ribbon Sprite.</summary>
        [Test]
        public void RibbonImages_UseQualityAwareScriptAndRectangularGeometry()
        {
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/Sprites/Ribbon/newui@3x.png"), Is.EqualTo(AtlasGuid));
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/Scripts/Misc/Rendering/QualityUiImage.cs"), Is.EqualTo(QualityImageGuid));
            var count = 0;
            foreach (var (path, text) in EnumerateAuthoredAssets())
            {
                foreach (Match record in s_documents.Matches(text))
                {
                    if (record.Groups["kind"].Value != "114" || !record.Value.Contains(AtlasGuid))
                    {
                        continue;
                    }
                    var script = ScriptGuid(record.Value);
                    if (script != StandardImageGuid && script != QualityImageGuid)
                    {
                        // Selectable SpriteState stores the same atlas; its target is checked separately.
                        continue;
                    }
                    Assert.That(script, Is.EqualTo(QualityImageGuid), $"{path}, Image {record.Groups["id"].Value}");
                    Assert.That(record.Value, Does.Contain("m_EditorClassIdentifier: MajdataPlay.Core::MajdataPlay.Rendering.QualityUiImage"), $"{path}: the serialized type identifier must match the new script.");
                    Assert.That(record.Value, Does.Contain("m_UseSpriteMesh: 0"), $"{path}: atlas replacement requires unchanged rectangular UVs.");
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0), "The validation must inspect real authored Ribbon images.");
        }

        /// <summary>Checks the lazy registry keeps the paired artwork in Ribbon and the template in Materials.</summary>
        [Test]
        public void OutlineResources_ReferenceAuthoredAtlasAndAlphaTemplate()
        {
            var resources = Resources.Load<UiOutlineResources>("Rendering/UI/OutlineTheme");

            Assert.That(resources, Is.Not.Null);
            Assert.That(resources.OutlinedSprites.Count, Is.EqualTo(34));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sprite in resources.OutlinedSprites)
            {
                Assert.That(sprite, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(sprite), Is.EqualTo("Assets/Sprites/Ribbon/newui-outlined@3x.png"));
                Assert.That(names.Add(sprite.name), Is.True, "Outlined slices must have unique logical names.");
            }
            Assert.That(resources.AlphaMaterial, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(resources.AlphaMaterial), Is.EqualTo("Assets/Rendering/Materials/UI/AtlasAlpha.mat"));
            Assert.That(resources.AlphaMaterial!.shader.name, Is.EqualTo("Maj/UI/Atlas Alpha"));
            Assert.That(resources.CoverAlphaMaterial, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(resources.CoverAlphaMaterial), Is.EqualTo("Assets/Rendering/Materials/UI/List/CoverAlpha.mat"));
            Assert.That(resources.CoverAlphaMaterial!.shader.name, Is.EqualTo("Maj/UI/List/Cover Alpha"));
            Assert.That(resources.BackgroundCutoffMaterial, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(resources.BackgroundCutoffMaterial), Is.EqualTo("Assets/Rendering/Materials/UI/AtlasCutoff.mat"));
            Assert.That(resources.BackgroundCutoffMaterial!.shader.name, Is.EqualTo("Maj/UI/Atlas Cutoff"));
        }

        /// <summary>Checks that buttons using Sprite Swap render through the same quality-aware Image.</summary>
        [Test]
        public void RibbonSpriteStates_TargetQualityAwareImages()
        {
            var count = 0;
            foreach (var (path, text) in EnumerateAuthoredAssets())
            {
                var records = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match record in s_documents.Matches(text))
                {
                    records.Add(record.Groups["id"].Value, record.Value);
                }
                foreach (var record in records.Values)
                {
                    if (!record.Contains(AtlasGuid) || !record.Contains("m_SpriteState:"))
                    {
                        continue;
                    }
                    var target = Regex.Match(record, @"m_TargetGraphic: \{fileID: (?<id>-?\d+)\}");
                    Assert.That(target.Success, Is.True, path);
                    Assert.That(records.TryGetValue(target.Groups["id"].Value, out var targetRecord), Is.True, path);
                    Assert.That(ScriptGuid(targetRecord!), Is.EqualTo(QualityImageGuid), $"{path}: Sprite Swap must not bypass the outline texture.");
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0), "The validation must inspect real Sprite Swap controls.");
        }

        /// <summary>Checks real prefab deserialization preserves Unity Image fields across the script substitution.</summary>
        [Test]
        public void SlicedTitleBackground_PreservesImageTypeAndLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Setting/MenuTitleDisplayer.prefab");
            Assert.That(prefab, Is.Not.Null);
            var found = false;
            foreach (var image in prefab.GetComponentsInChildren<Image>(true))
            {
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image, out var guid, out long fileId) || fileId != 857430918264730003L)
                {
                    continue;
                }
                Assert.That(image.GetType().FullName, Is.EqualTo("MajdataPlay.Rendering.QualityUiImage"));
                Assert.That(image.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(image.pixelsPerUnitMultiplier, Is.EqualTo(3f));
                Assert.That(image.color, Is.EqualTo(Color.white));
                Assert.That(image.raycastTarget, Is.False);
                Assert.That(image.sprite, Is.Not.Null);
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(image.sprite)), Is.EqualTo(AtlasGuid));
                found = true;
            }
            Assert.That(found, Is.True, "The original sliced Image fileID must survive the migration.");
        }

        /// <summary>Reads tracked scene and prefab YAML without loading or resaving scenes.</summary>
        /// <returns>The asset path and original serialized text for each relevant scene or prefab.</returns>
        private static IEnumerable<(string Path, string Text)> EnumerateAuthoredAssets()
        {
            var projectPrefix = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if ((path.StartsWith("Assets/Scenes/", StringComparison.Ordinal) && path.EndsWith(".unity", StringComparison.Ordinal)) ||
                    (path.StartsWith("Assets/Prefabs/", StringComparison.Ordinal) && path.EndsWith(".prefab", StringComparison.Ordinal)))
                {
                    yield return (path, FileSystem.File.Open(projectPrefix + path).ReadAllText());
                }
            }
        }

        /// <summary>Extracts a MonoBehaviour script GUID without interpreting unrelated serialized fields.</summary>
        /// <param name="record">The component's YAML document.</param>
        /// <returns>The script GUID, or an empty string for other document types.</returns>
        private static string ScriptGuid(string record)
        {
            return Regex.Match(record, @"m_Script: \{fileID: 11500000, guid: (?<guid>[0-9a-f]+), type: 3\}").Groups["guid"].Value;
        }
    }
}

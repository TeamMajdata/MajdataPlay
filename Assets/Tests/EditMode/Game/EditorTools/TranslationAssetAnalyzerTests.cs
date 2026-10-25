using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers <see cref="TranslationAssetAnalyzer"/>, which reads LocalizeTMP keys from scene and prefab YAML.</summary>
    public class TranslationAssetAnalyzerTests
    {
        /// <summary>A scene fragment with direct, quoted, empty and overridden keys.</summary>
        private const string SceneText =
            "%YAML 1.1\r\n" +
            "--- !u!114 &1\r\n" +
            "MonoBehaviour:\r\n" +
            "  local_key: MAJTEXT_PLAIN\r\n" +
            "--- !u!114 &2\r\n" +
            "MonoBehaviour:\r\n" +
            "  local_key: 'MAJTEXT_IT''S'\r\n" +
            "--- !u!114 &3\r\n" +
            "MonoBehaviour:\r\n" +
            "  local_key: \r\n" +
            "--- !u!1001 &4\r\n" +
            "PrefabInstance:\r\n" +
            "    - target: {fileID: 1, guid: 0, type: 3}\r\n" +
            "      propertyPath: local_key\r\n" +
            "      value: MAJTEXT_OVERRIDE\r\n" +
            "      objectReference: {fileID: 0}\r\n";

        /// <summary>Checks that direct fields and prefab overrides are collected with their sources.</summary>
        [Test]
        public void CollectText_FindsFieldsAndOverrides()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            TranslationAssetAnalyzer.CollectText(SceneText, "Assets/Fixture.unity", keys);

            Assert.That(keys.Keys, Is.EquivalentTo(new[] { "MAJTEXT_PLAIN", "MAJTEXT_IT'S", "MAJTEXT_OVERRIDE" }));
            Assert.That(keys["MAJTEXT_PLAIN"], Does.Contain("Assets/Fixture.unity (LocalizeTMP)"));
            Assert.That(keys["MAJTEXT_OVERRIDE"], Does.Contain("Assets/Fixture.unity (LocalizeTMP prefab override)"));
        }

        /// <summary>Checks the YAML scalar unquoting.</summary>
        [Test]
        public void Unquote_HandlesYamlScalars()
        {
            Assert.That(TranslationAssetAnalyzer.Unquote("PLAIN"), Is.EqualTo("PLAIN"));
            Assert.That(TranslationAssetAnalyzer.Unquote("'a''b'"), Is.EqualTo("a'b"));
            Assert.That(TranslationAssetAnalyzer.Unquote("\"a\\\"b\""), Is.EqualTo("a\"b"));
            Assert.That(TranslationAssetAnalyzer.Unquote("'"), Is.EqualTo("'"));
        }

        /// <summary>Checks that the project's scenes yield their LocalizeTMP keys and that unreadable assets are reported.</summary>
        [Test]
        public void Collect_ReadsProjectScenes()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            TranslationAssetAnalyzer.Collect(new[] { "Assets/Scenes/List.unity", "Assets/Missing/Nothing.unity" }, keys, diagnostics);

            Assert.That(keys["MAJTEXT_LIST_INPUT_HINT"].Single(), Is.EqualTo("Assets/Scenes/List.unity (LocalizeTMP)"));
            Assert.That(diagnostics.Single(), Does.Contain("Assets/Missing/Nothing.unity"));
        }

        /// <summary>Checks that an asset that is not text-serialized is reported instead of being searched.</summary>
        [Test]
        public void Collect_ReportsBinaryAssets()
        {
            using var folder = new TemporaryAssetFolder();
            var path = folder.Combine("Binary.prefab");
            TranslationDocument.WriteIfUnchanged(path, string.Empty, "local_key: MAJTEXT_HIDDEN");
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            TranslationAssetAnalyzer.Collect(new[] { path }, keys, diagnostics);

            Assert.That(keys, Is.Empty);
            Assert.That(diagnostics.Single(), Does.Contain("not text-serialized"));
            Assert.Throws<ArgumentNullException>(() => TranslationAssetAnalyzer.Collect(null!, keys, diagnostics));
            Assert.Throws<ArgumentNullException>(() => TranslationAssetAnalyzer.Collect(Array.Empty<string>(), null!, diagnostics));
            Assert.Throws<ArgumentNullException>(() => TranslationAssetAnalyzer.Collect(Array.Empty<string>(), keys, null!));
        }
    }
}

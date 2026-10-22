using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// Covers the translation editor window's editing model without showing it: resource loading, key synchronization,
    /// metadata edits, analysis and saving to temporary assets.
    /// </summary>
    public class TranslationManagerWindowTests
    {
        /// <summary>The window under test.</summary>
        private TranslationManagerWindow _window = null!;

        /// <summary>Creates the window, which loads the language resources, and builds its UI tree.</summary>
        [SetUp]
        public void SetUp()
        {
            _window = ScriptableObject.CreateInstance<TranslationManagerWindow>();
            PrivateMember.Invoke(_window, "CreateGUI");
        }

        /// <summary>Discards edits and destroys the window.</summary>
        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(_window);
            Object.DestroyImmediate(_window);
        }

        /// <summary>Gets the template keys.</summary>
        private List<string> Template => PrivateMember.GetField<List<string>>(_window, "_templateContent");

        /// <summary>Gets the language documents.</summary>
        private List<TranslationDocument> Languages => PrivateMember.GetField<List<TranslationDocument>>(_window, "_languages");

        /// <summary>Checks that loading reads the template and every language resource sorted by code.</summary>
        [Test]
        public void OnEnable_LoadsTemplateAndLanguages()
        {
            Assert.That(Template, Is.Not.Empty);
            Assert.That(Languages.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(Languages.Select(document => document.Code), Is.Ordered.Using((IComparer<string>)System.StringComparer.Ordinal));
            Assert.That(PrivateMember.GetField<string>(_window, "_templatePath"), Does.EndWith("template.tpl.json"));
            Assert.That(PrivateMember.GetField<int>(_window, "_selectedLanguageIndex"), Is.EqualTo(0));
            Assert.That(_window.hasUnsavedChanges, Is.False);
        }

        /// <summary>Checks that the UI tree has three tabs and switching shows only the selected one.</summary>
        [Test]
        public void CreateGUI_BuildsTabsAndSwitches()
        {
            var pages = PrivateMember.GetField<List<VisualElement>>(_window, "_tabPages");

            PrivateMember.Invoke(_window, "SwitchTab", 1);
            PrivateMember.Invoke(_window, "SwitchTab", 7);

            Assert.That(pages.Count, Is.EqualTo(3));
            Assert.That(pages[1].style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(pages[0].style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(PrivateMember.GetField<int>(_window, "_activeTab"), Is.EqualTo(1));
        }

        /// <summary>Checks that manual keys are trimmed, deduplicated and mark the template dirty.</summary>
        [Test]
        public void AddManualKey_AddsTrimmedUniqueKeys()
        {
            var field = PrivateMember.GetField<TextField>(_window, "_newKeyField");
            var count = Template.Count;

            field.value = "  MajTestManual.Key  ";
            PrivateMember.Invoke(_window, "AddManualKey");
            field.value = "MajTestManual.Key";
            PrivateMember.Invoke(_window, "AddManualKey");
            field.value = "   ";
            PrivateMember.Invoke(_window, "AddManualKey");

            Assert.That(Template.Count, Is.EqualTo(count + 1));
            Assert.That(Template.Last(), Is.EqualTo("MajTestManual.Key"));
            Assert.That(field.value, Is.EqualTo("   "));
            Assert.That(_window.hasUnsavedChanges, Is.True);
        }

        /// <summary>Checks that analysis discovers keys, adds missing ones to the template once and explains sources.</summary>
        [Test]
        public void AnalyzeAndAddFixedKeys_SynchronizesTemplate()
        {
            PrivateMember.Invoke(_window, "AnalyzeAssembly");
            var fixedKeys = PrivateMember.GetField<Dictionary<string, HashSet<string>>>(_window, "_fixedKeys");

            Assert.That(fixedKeys, Is.Not.Empty);
            Assert.That(PrivateMember.GetField<string>(_window, "_analysisStatus"), Does.Contain("fixed keys"));
            Assert.That(PrivateMember.GetField<int>(_window, "_activeTab"), Is.EqualTo(2));

            PrivateMember.Invoke(_window, "AddFixedKeys");
            var count = Template.Count;
            PrivateMember.Invoke(_window, "AddFixedKeys");

            Assert.That(Template.Count, Is.EqualTo(count));
            Assert.That(fixedKeys.Keys.All(key => Template.Contains(key)), Is.True);
            Assert.That(PrivateMember.Invoke<string>(_window, "GetSources", "MAJTEXT_LIST_EMPTY_COLLECTION"), Does.Contain("CoverListManager"));
            Assert.That(PrivateMember.Invoke<string>(_window, "GetSources", "MajTestManual.Unknown"), Does.StartWith("Not proven"));
        }

        /// <summary>Checks that missing template keys are added to the selected language once, preserving existing text.</summary>
        [Test]
        public void AddMissingTranslations_AddsTemplateKeysToSelection()
        {
            PrivateMember.SetField(_window, "_selectedLanguageIndex", -1);
            PrivateMember.Invoke(_window, "AddMissingTranslations");
            Assert.That(Languages.Any(document => document.IsDirty), Is.False);

            PrivateMember.SetField(_window, "_selectedLanguageIndex", 0);
            Template.Add("MajTestManual.Missing");
            var document = Languages[0];
            var existing = document.Entries.Where(entry => entry.Text.Length > 0).ToDictionary(entry => entry.Key, entry => entry.Text);

            PrivateMember.Invoke(_window, "AddMissingTranslations");
            var count = document.Entries.Count;
            PrivateMember.Invoke(_window, "AddMissingTranslations");

            Assert.That(document.Entries.Count, Is.EqualTo(count));
            Assert.That(document.Entries.Single(entry => entry.Key == "MajTestManual.Missing").Text, Is.Empty);
            Assert.That(existing.All(pair => document.Entries.Single(entry => entry.Key == pair.Key).Text == pair.Value), Is.True);
            Assert.That(document.IsDirty, Is.True);
        }

        /// <summary>Checks that metadata edits apply to the selected language only.</summary>
        [Test]
        public void UpdateLanguageMetadata_EditsSelectedDocument()
        {
            PrivateMember.SetField(_window, "_selectedLanguageIndex", 99);
            PrivateMember.Invoke(_window, "UpdateLanguageMetadata", true, "ignored");
            Assert.That(Languages.Any(document => document.Code == "ignored"), Is.False);

            PrivateMember.SetField(_window, "_selectedLanguageIndex", 1);
            PrivateMember.Invoke(_window, "UpdateLanguageMetadata", true, "xx-XX");
            PrivateMember.Invoke(_window, "UpdateLanguageMetadata", false, "Tester");
            PrivateMember.Invoke(_window, "RefreshViews");

            Assert.That(Languages[1].Code, Is.EqualTo("xx-XX"));
            Assert.That(Languages[1].Author, Is.EqualTo("Tester"));
            Assert.That(PrivateMember.GetField<TextField>(_window, "_codeField").value, Is.EqualTo("xx-XX"));
            Assert.That(PrivateMember.GetField<Label>(_window, "_languageSummary").text, Does.Contain("entries"));
            Assert.That(_window.hasUnsavedChanges, Is.True);
        }

        /// <summary>Checks that discarding reloads the resources and clears the unsaved state.</summary>
        [Test]
        public void DiscardChanges_ReloadsResources()
        {
            Template.Add("MajTestManual.Discarded");
            PrivateMember.SetField(_window, "_templateDirty", true);

            _window.DiscardChanges();

            Assert.That(Template, Does.Not.Contain("MajTestManual.Discarded"));
            Assert.That(_window.hasUnsavedChanges, Is.False);
        }

        /// <summary>Checks that the template and a language save to their asset paths and reset their dirty state.</summary>
        [Test]
        public void SaveChanges_WritesTemplateAndDirtyLanguages()
        {
            using var folder = new TemporaryAssetFolder();
            PrivateMember.SetField(_window, "_templatePath", folder.Combine("template.tpl.json"));
            PrivateMember.SetField(_window, "_templateSource", string.Empty);
            PrivateMember.SetField(_window, "_templateContent", new List<string> { "B", "A" });
            PrivateMember.SetField(_window, "_templateDirty", true);
            const string source = "{\"Code\":\"fx\",\"Translations\":{\"A\":\"a\"}}";
            TranslationDocument.WriteIfUnchanged(folder.Combine("fx-FX.json"), string.Empty, source);
            var document = TranslationDocument.Read(folder.Combine("fx-FX.json"), source);
            document.Code = "fx-FX";
            document.IsDirty = true;
            Languages.Clear();
            Languages.Add(document);

            _window.SaveChanges();

            var template = JArray.Parse(folder.ReadText("template.tpl.json"));
            Assert.That(template.Values<string>(), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(PrivateMember.GetField<bool>(_window, "_templateDirty"), Is.False);
            Assert.That(document.IsDirty, Is.False);
            Assert.That(JObject.Parse(folder.ReadText("fx-FX.json")).Value<string>("Code"), Is.EqualTo("fx-FX"));
            Assert.That(_window.hasUnsavedChanges, Is.False);
        }

        /// <summary>Checks the case-insensitive search filter.</summary>
        [Test]
        public void MatchesFilter_IsCaseInsensitive()
        {
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(TranslationManagerWindow), "MatchesFilter", "MAJTEXT_SAY", "say"), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(TranslationManagerWindow), "MatchesFilter", "MAJTEXT_SAY", string.Empty), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(TranslationManagerWindow), "MatchesFilter", "MAJTEXT_SAY", "hello"), Is.False);
        }
    }
}

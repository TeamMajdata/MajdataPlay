using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>Checks production editor controls and document persistence in an isolated Unity project.</summary>
    internal static class TranslationEditorValidation
    {
        /// <summary>The number of assertions completed by this validation run.</summary>
        private static int s_assertions;


        /// <summary>The production window owned by the asynchronous layout validation.</summary>
        private static TranslationManagerWindow? s_window;

        /// <summary>The current resize-and-tab scenario.</summary>
        private static int s_layoutCase;

        /// <summary>The remaining editor frames before checking settled layout geometry.</summary>
        private static int s_layoutFrames;

        /// <summary>Whether the current scenario has been configured.</summary>
        private static bool s_layoutPrepared;

        /// <summary>The deadline that prevents an unavailable UI panel from hanging validation.</summary>
        private static double s_layoutDeadline;

        /// <summary>The window sizes used for minimum, wide, and narrow dock-like layouts.</summary>
        private static readonly Vector2[] s_layoutSizes =
        {
            new Vector2(760, 420),
            new Vector2(1200, 760),
            new Vector2(480, 360),
            new Vector2(960, 320)
        };

        /// <summary>Runs the isolated editor validation from Unity batch mode.</summary>
        public static void Run()
        {
            var handedOff = false;
            TranslationManagerWindow? window = null;
            try
            {
                Assert(Application.unityVersion == "6000.3.17f1", "The required Unity version is used.");
                var project = Path.GetDirectoryName(Application.dataPath)!;
                Assert(Path.GetFileName(project) == "TranslationEditorValidation", "Writes are confined to the isolated project.");
                Assert(Path.GetFullPath(Environment.CurrentDirectory) == Path.GetFullPath(project), "Relative resource paths resolve inside the isolated project.");
                ValidateDocuments();
                window = EditorWindow.GetWindow<TranslationManagerWindow>();
                Invoke(window, "CreateGUI");
                Invoke(window, "CreateGUI");
                Assert(GetField<IList>(window, "_tabPages").Count == 3, "UI recreation does not duplicate tab pages.");
                var serialized = new SerializedObject(window);
                Assert(serialized.FindProperty("_languages").arraySize == Resources.LoadAll<TextAsset>("Langs").Count(asset => !asset.name.EndsWith(".tpl", StringComparison.Ordinal)), "All language documents are serializable and loaded.");
                var languages = GetField<List<TranslationDocument>>(window, "_languages");
                Assert(languages.All(document => document.Entries.Count > 0), "Dictionary-backed languages have editable entries.");
                Assert(GetField<List<string>>(window, "_templateContent").Count > 100, "The template resource is loaded.");
                ValidateAnalysis(window);
                ValidateEditing(window);
                StartLayoutValidation(window);
                handedOff = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }
            finally
            {
                if (!handedOff && window != null)
                {
                    window.DiscardChanges();
                    window.Close();
                }
            }
        }

        /// <summary>Starts real-panel layout checks after the editor has had time to arrange its visual tree.</summary>
        /// <param name="window">The production window to resize and inspect.</param>
        private static void StartLayoutValidation(TranslationManagerWindow window)
        {
            s_window = window;
            s_layoutCase = 0;
            s_layoutPrepared = false;
            s_layoutDeadline = EditorApplication.timeSinceStartup + 60;
            // Docked windows can become smaller than EditorWindow.minSize; emulate that available space.
            window.minSize = new Vector2(320, 240);
            var document = GetField<List<TranslationDocument>>(window, "_languages")[0];
            document.Entries.Add(new TranslationEntry
            {
                Key = "0_LAYOUT_LONG_KEY_" + new string('K', 120),
                Text = "First line\n" + new string('W', 180) + "\n第三行：多行翻译\nLast line"
            });
            Invoke(window, "RefreshViews");
            EditorApplication.update += OnLayoutUpdate;
        }

        /// <summary>Waits for layout and checks all tabs at several actual editor-window sizes.</summary>
        private static void OnLayoutUpdate()
        {
            try
            {
                var window = s_window;
                if (window == null || EditorApplication.timeSinceStartup > s_layoutDeadline)
                {
                    throw new InvalidOperationException("The layout validation window or its UI panel became unavailable.");
                }
                if (!s_layoutPrepared)
                {
                    var size = s_layoutSizes[s_layoutCase / 3];
                    window.position = new Rect(40, 40, size.x, size.y);
                    Invoke(window, "SwitchTab", s_layoutCase % 3);
                    window.Repaint();
                    s_layoutFrames = 12;
                    s_layoutPrepared = true;
                    return;
                }
                window.Repaint();
                if (--s_layoutFrames > 0)
                {
                    return;
                }
                ValidateLayout(window, s_layoutCase % 3);
                s_layoutCase++;
                s_layoutPrepared = false;
                if (s_layoutCase == s_layoutSizes.Length * 3)
                {
                    FinishLayoutValidation(null);
                }
            }
            catch (Exception exception)
            {
                FinishLayoutValidation(exception);
            }
        }

        /// <summary>Checks settled viewport bounds, toolbar controls, list sizes and editable row columns.</summary>
        /// <param name="window">The production window.</param>
        /// <param name="tab">The visible tab index.</param>
        private static void ValidateLayout(TranslationManagerWindow window, int tab)
        {
            var root = window.rootVisualElement;
            var page = GetField<List<VisualElement>>(window, "_tabPages")[tab];
            Assert(root.panel is not null && root.worldBound.width > 300 && root.worldBound.height > 240, "The actual editor panel has nonzero layout dimensions.");
            AssertInside(page, root, "The visible tab stays inside the window.");
            foreach (var toolbar in root.Query<UnityEditor.UIElements.Toolbar>().ToList())
            {
                if (toolbar.resolvedStyle.display == DisplayStyle.None || toolbar.worldBound.height == 0)
                {
                    continue;
                }
                AssertInside(toolbar, root, "Toolbars remain inside the window after wrapping.");
                foreach (var search in toolbar.Query<UnityEditor.UIElements.ToolbarSearchField>().ToList())
                {
                    Assert(search.worldBound.width >= 80, "Search inputs retain a usable width.");
                    AssertInside(search, toolbar, "Search inputs remain inside their toolbar.");
                }
                foreach (var button in toolbar.Query<UnityEditor.UIElements.ToolbarButton>().ToList())
                {
                    Assert(button.worldBound.height >= 16, "Toolbar buttons keep a usable height.");
                    AssertInside(button, toolbar, "Toolbar buttons are not clipped or pushed outside the toolbar.");
                }
            }
            var summaryName = tab == 0 ? "_templateSummary" : tab == 1 ? "_languageSummary" : "_analysisSummary";
            AssertInside(GetField<Label>(window, summaryName), page, "Summary labels wrap within their tab.");
            var listNames = tab == 0 ? new[] { "_templateList" } :
                tab == 1 ? new[] { "_languageList", "_translationList" } : new[] { "_analysisList", "_diagnosticList" };
            foreach (var field in listNames)
            {
                var list = GetField<ListView>(window, field);
                Debug.Log($"TRANSLATION_EDITOR_LAYOUT case={s_layoutCase} root={root.worldBound} tab={page.worldBound} {field}={list.worldBound}");
                Assert(list.worldBound.height >= 36, $"{field} retains a usable scrolling viewport.");
                AssertInside(list, page, $"{field} stays inside its tab instead of expanding to content size.");
            }
            if (tab == 0)
            {
                var manual = GetField<TextField>(window, "_newKeyField");
                AssertInside(manual, page, "Manual-key controls stay visible below the template list.");
                foreach (var field in GetField<ListView>(window, "_templateList").Query<TextField>("key").ToList().Where(field => field.parent.resolvedStyle.display != DisplayStyle.None))
                {
                    var remove = field.parent.Q<Button>();
                    AssertInside(remove, field.parent, "Template remove buttons remain inside their row.");
                    Assert(remove.worldBound.width >= 40, "Template remove buttons retain a usable width.");
                    Assert(field.worldBound.xMax <= remove.worldBound.xMin + 2, "Template inputs leave space for the remove button.");
                    AssertInside(field, field.parent, "Template inputs stay inside their row.");
                    AssertInside(field.parent, page, "Template rows stay within the horizontal viewport.", false);
                }
            }
            else if (tab == 1)
            {
                var translations = GetField<ListView>(window, "_translationList");
                // Dynamic-height virtualization keeps hidden recycled rows in the visual tree.
                var fields = translations.Query<TextField>("text").ToList().Where(field => field.parent.resolvedStyle.display != DisplayStyle.None).ToList();
                Assert(fields.Count > 0, "Translation rows are realized after the panel layout settles.");
                foreach (var field in fields)
                {
                    Assert(field.worldBound.width >= 80, $"Translation inputs have enough space for editing. input={field.worldBound}, row={field.parent.worldBound}, display={field.parent.resolvedStyle.display}");
                    AssertInside(field, field.parent, "Localized-text fields do not overflow their key column.");
                    AssertInside(field.parent, translations, "Translation rows stay within the horizontal viewport.", false);
                    var key = field.parent.Q<Label>("key");
                    Assert(key.worldBound.xMax <= field.worldBound.xMin + 2, "Key and localized-text columns do not overlap.");
                }
            }
        }

        /// <summary>Checks that a laid-out child is contained by its expected viewport.</summary>
        /// <param name="child">The arranged element to inspect.</param>
        /// <param name="parent">The expected containing viewport.</param>
        /// <param name="message">The behavior to report if bounds are invalid.</param>
        /// <param name="checkHeight">Whether to require vertical containment; scrolling rows only require horizontal containment.</param>
        private static void AssertInside(VisualElement child, VisualElement parent, string message, bool checkHeight = true)
        {
            var bounds = child.worldBound;
            var viewport = parent.worldBound;
            var horizontal = bounds.xMin >= viewport.xMin - 2 && bounds.xMax <= viewport.xMax + 2;
            var vertical = !checkHeight || (bounds.yMin >= viewport.yMin - 2 && bounds.yMax <= viewport.yMax + 2);
            Assert(horizontal && vertical, $"{message} child={bounds}, viewport={viewport}");
        }

        /// <summary>Closes only the owned window and reports layout validation success or failure.</summary>
        /// <param name="exception">The validation failure, or null when every scenario passed.</param>
        private static void FinishLayoutValidation(Exception? exception)
        {
            EditorApplication.update -= OnLayoutUpdate;
            if (s_window != null)
            {
                s_window.DiscardChanges();
                s_window.Close();
                s_window = null;
            }
            if (exception is not null)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"TRANSLATION_EDITOR_VALIDATION_PASSED assertions={s_assertions} layoutCases={s_layoutCase}");
            EditorApplication.Exit(0);
        }

        /// <summary>Checks static analysis through the actual loaded runtime/editor assembly boundary.</summary>
        /// <param name="window">The production editor window.</param>
        private static void ValidateAnalysis(TranslationManagerWindow window)
        {
            Invoke(window, "AnalyzeAssembly");
            var keys = GetField<Dictionary<string, HashSet<string>>>(window, "_fixedKeys");
            Assert(keys.Keys.Any(key => key.StartsWith("Call.", StringComparison.Ordinal)), "Localization calls in Assembly-CSharp are analyzed.");
            Assert(keys.ContainsKey("MAJSETTING_CATEGORY_ChartSetting"), "Setting keys cross the runtime/editor assembly boundary.");
            Assert(keys.ContainsKey("MAJSETTING_GENERAL_OPTION_Inner"), "Custom string-array options are inferred without initialization.");
            Assert(GetField<List<string>>(window, "_diagnostics").Count > 0, "Dynamic calls and values have diagnostics.");
            var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Assembly-CSharp");
            var guard = runtime.GetType("MajdataPlay.TranslationValidation.ExecutionGuard", true)!;
            var count = guard.GetProperty("ExecutionCount", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert((int)count.GetValue(null)! == 0, "No fixture method, accessor, constructor or initializer was executed.");
            var template = GetField<List<string>>(window, "_templateContent");
            var original = new HashSet<string>(template, StringComparer.Ordinal);
            Invoke(window, "AddFixedKeys");
            template = GetField<List<string>>(window, "_templateContent");
            Assert(original.IsSubsetOf(template), "Additive analysis preserves all preexisting template keys.");
            Assert(keys.Keys.All(key => template.Contains(key, StringComparer.Ordinal)), "All fixed keys are added to the template.");
            var size = template.Count;
            Invoke(window, "AddFixedKeys");
            Assert(template.Count == size, "Repeated synchronization does not duplicate keys.");
            Assert((bool)Invoke(window, "SaveTemplate")!, "Template saving succeeds in the isolated project.");
            Assert(JArray.Parse(File.ReadAllText("Assets/Resources/Langs/template.tpl.json")).Count == size, "The saved template contains every retained key.");
        }

        /// <summary>Checks selection, recycled fields, additive synchronization and serialized editing state.</summary>
        /// <param name="window">The production editor window.</param>
        private static void ValidateEditing(TranslationManagerWindow window)
        {
            var document = GetField<List<TranslationDocument>>(window, "_languages")[0];
            var existing = document.Entries.ToDictionary(entry => entry.Key, entry => entry.Text, StringComparer.Ordinal);
            Invoke(window, "AddMissingTranslations");
            Assert(existing.All(pair => document.Entries.Single(entry => entry.Key == pair.Key).Text == pair.Value), "Adding missing keys preserves existing translations.");
            var list = GetField<ListView>(window, "_translationList");
            var row = list.makeItem();
            window.rootVisualElement.Add(row);
            list.bindItem(row, 0);
            var entry = (TranslationEntry)row.userData;
            var value = entry.Text + " [editor validation]";
            row.Q<TextField>("text").value = value;
            Assert(entry.Text == value && document.IsDirty, $"Recycled translation fields edit the correct document (panel={row.panel is not null}, dirty={document.IsDirty}, actual={entry.Text}).");
            row.RemoveFromHierarchy();
            Assert(window.hasUnsavedChanges, "The window warns about unsaved language edits.");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            var originalCode = document.Code;
            var code = GetField<TextField>(window, "_codeField");
            code.value = "validation-language";
            Assert(document.Code == "validation-language", "Metadata fields edit the selected document.");
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            document = GetField<List<TranslationDocument>>(window, "_languages")[0];
            Assert(document.Code == originalCode, "Undo restores the selected document metadata.");
            Undo.PerformRedo();
            document = GetField<List<TranslationDocument>>(window, "_languages")[0];
            Assert(document.Code == "validation-language", "Redo restores metadata and rebinds the restored document.");
            var selected = GetField<ListView>(window, "_languageList");
            Assert(selected.selectedIndex == 0, "Editing fields does not clear the language selection.");
            foreach (var translation in document.Entries)
            {
                if (translation.Text.Length == 0)
                {
                    translation.Text = translation.Key;
                }
            }
            Assert((bool)Invoke(window, "SaveSelectedLanguage")!, "Saving the selected language succeeds.");
            Assert(JObject.Parse(File.ReadAllText(document.AssetPath)).Value<string>("Code") == "validation-language", "Saved JSON reflects edited metadata.");
            Assert(!window.hasUnsavedChanges, "The unsaved-change state clears after saving all edits.");
            var snapshot = EditorJsonUtility.ToJson(window);
            var restored = ScriptableObject.CreateInstance<TranslationManagerWindow>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(snapshot, restored);
                var restoredDocument = GetField<List<TranslationDocument>>(restored, "_languages")[0];
                Assert(restoredDocument.Entries.Any(item => item.Key == entry.Key && item.Text == value), "Translation entries survive Unity serialization.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restored);
            }
        }

        /// <summary>Checks both language schemas, extra metadata, key validation and external-edit protection.</summary>
        private static void ValidateDocuments()
        {
            const string DictionarySource = "{\"Code\":\"test\",\"Author\":\"author\",\"Extra\":42,\"Translations\":{\"A\":\"one\"}}";
            const string MappingSource = "{\"Code\":\"test\",\"Author\":\"author\",\"Extra\":42,\"MappingTable\":[{\"Origin\":\"A\",\"Content\":\"first\"},{\"Origin\":\"A\",\"Content\":\"last\"}]}";
            var directory = "Temp/TranslationDocumentValidation";
            Directory.CreateDirectory(directory);
            foreach (var source in new[] { DictionarySource, MappingSource })
            {
                var assetPath = Path.Combine(directory, source == DictionarySource ? "dictionary.json" : "mapping.json");
                File.WriteAllText(assetPath, source);
                var document = TranslationDocument.Read(assetPath, source);
                Assert(document.Entries.Count == 1, "Runtime duplicate mapping semantics are preserved.");
                Assert(document.Entries[0].Text == (source == DictionarySource ? "one" : "last"), "The correct translation schema is read.");
                document.Entries[0].Text = "changed";
                document.IsDirty = true;
                document.Save();
                var saved = JObject.Parse(File.ReadAllText(assetPath));
                Assert(saved.Value<int>("Extra") == 42, "Saving retains unrelated JSON metadata.");
                Assert((saved["MappingTable"] is not null) == document.UsesMappingTable, "Saving retains the language's original schema.");
                Assert(!document.IsDirty && TranslationDocument.Read(assetPath, document.SourceJson).Entries[0].Text == "changed", "The saved document round-trips.");
                File.AppendAllText(assetPath, " ");
                AssertThrows<IOException>(document.Save, "External changes prevent stale overwrites.");
            }
            AssertThrows<InvalidOperationException>(() => TranslationDocument.ValidateKeys(new[] { "A", "A" }), "Duplicate keys are rejected.");
            AssertThrows<InvalidOperationException>(() => TranslationDocument.ValidateKeys(new[] { " " }), "Empty keys are rejected.");
        }

        /// <summary>Reads one private window field for white-box editor validation.</summary>
        /// <typeparam name="T">The expected field type.</typeparam>
        /// <param name="window">The window instance.</param>
        /// <param name="name">The production field name.</param>
        /// <returns>The field's current value.</returns>
        private static T GetField<T>(TranslationManagerWindow window, string name)
        {
            return (T)typeof(TranslationManagerWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        }

        /// <summary>Invokes a private editor action without simulating mouse input.</summary>
        /// <param name="window">The window instance.</param>
        /// <param name="name">The production method name.</param>
        /// <param name="arguments">The arguments passed to the editor action.</param>
        /// <returns>The action's return value, if any.</returns>
        private static object? Invoke(TranslationManagerWindow window, string name, params object[] arguments)
        {
            return typeof(TranslationManagerWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, arguments);
        }

        /// <summary>Records a validation assertion.</summary>
        /// <param name="condition">The expected true condition.</param>
        /// <param name="message">The behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The condition is false.</exception>
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            s_assertions++;
        }

        /// <summary>Checks that an operation throws a specific exception.</summary>
        /// <typeparam name="T">The expected exception type.</typeparam>
        /// <param name="action">The operation to check.</param>
        /// <param name="message">The behavior being checked.</param>
        /// <exception cref="InvalidOperationException">The expected exception was not thrown.</exception>
        private static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                s_assertions++;
                return;
            }
            throw new InvalidOperationException(message);
        }
    }
}

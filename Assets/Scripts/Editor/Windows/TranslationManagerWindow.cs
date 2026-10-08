using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>Edits language resources and statically discovers fixed localization keys.</summary>
    public sealed class TranslationManagerWindow : EditorWindow
    {
        /// <summary>The fallback path when a translation template does not exist yet.</summary>
        private const string TemplatePath = "Assets/Resources/Langs/template.tpl.json";

        /// <summary>The editable template, including manually maintained keys.</summary>
        [SerializeField]
        private List<string> _templateContent = new();

        /// <summary>The editable documents retained across assembly reloads.</summary>
        [SerializeField]
        private List<TranslationDocument> _languages = new();

        /// <summary>The selected document index, or minus one for no selection.</summary>
        [SerializeField]
        private int _selectedLanguageIndex = -1;

        /// <summary>The active tab index.</summary>
        [SerializeField]
        private int _activeTab;

        /// <summary>Whether resource data is already present in the serialized editing state.</summary>
        [SerializeField]
        private bool _isLoaded;

        /// <summary>Whether template edits need saving.</summary>
        [SerializeField]
        private bool _templateDirty;

        /// <summary>The actual template asset path.</summary>
        [SerializeField]
        private string _templatePath = TemplatePath;

        /// <summary>The template text at load or last save, for external-edit detection.</summary>
        [SerializeField]
        private string _templateSource = string.Empty;

        /// <summary>The discovered keys and their method or setting sources.</summary>
        private readonly Dictionary<string, HashSet<string>> _fixedKeys = new(StringComparer.Ordinal);

        /// <summary>The dynamic calls and settings requiring manual review.</summary>
        private readonly List<string> _diagnostics = new();

        /// <summary>The template indices matching the current search.</summary>
        private readonly List<int> _visibleTemplateIndices = new();

        /// <summary>The translations matching the current search.</summary>
        private readonly List<TranslationEntry> _visibleTranslations = new();

        /// <summary>The sorted fixed-key report.</summary>
        private readonly List<string> _visibleFixedKeys = new();

        /// <summary>The tab pages in the current UI tree.</summary>
        private readonly List<VisualElement> _tabPages = new();

        /// <summary>The template list.</summary>
        private ListView? _templateList;

        /// <summary>The language selection list.</summary>
        private ListView? _languageList;

        /// <summary>The editable translation list.</summary>
        private ListView? _translationList;

        /// <summary>The fixed-key report list.</summary>
        private ListView? _analysisList;

        /// <summary>The unresolved-call report list.</summary>
        private ListView? _diagnosticList;

        /// <summary>The template summary.</summary>
        private Label? _templateSummary;

        /// <summary>The selected document summary.</summary>
        private Label? _languageSummary;

        /// <summary>The analysis status.</summary>
        private Label? _analysisSummary;

        /// <summary>The selected language's editor controls.</summary>
        private VisualElement? _languageEditor;

        /// <summary>The language code field.</summary>
        private TextField? _codeField;

        /// <summary>The language author field.</summary>
        private TextField? _authorField;

        /// <summary>The input for a manually maintained template key.</summary>
        private TextField? _newKeyField;

        /// <summary>The template filter.</summary>
        private string _templateFilter = string.Empty;

        /// <summary>The translation filter.</summary>
        private string _translationFilter = string.Empty;

        /// <summary>The last analysis result.</summary>
        private string _analysisStatus = "Analyze Assembly-CSharp to discover fixed keys.";

        private void OnEnable()
        {
            if (!_isLoaded)
            {
                LoadResources();
            }
            Undo.undoRedoPerformed += OnUndoRedo;
            UpdateUnsavedState();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            _tabPages.Clear();
            rootVisualElement.style.overflow = Overflow.Hidden;
            minSize = new Vector2(760, 420);
            var toolbar = CreateWrappingToolbar();
            toolbar.Add(CreateToolbarButton("Template", () => SwitchTab(0)));
            toolbar.Add(CreateToolbarButton("Translations", () => SwitchTab(1)));
            toolbar.Add(CreateToolbarButton("Analysis", () => SwitchTab(2)));
            toolbar.Add(CreateToolbarButton("Analyze Assembly-CSharp", AnalyzeAssembly));
            toolbar.Add(CreateToolbarButton("Reload resources", ReloadResources));
            rootVisualElement.Add(toolbar);
            for (var i = 0; i < 3; i++)
            {
                var page = new VisualElement();
                ConfigureFlexibleContent(page);
                _tabPages.Add(page);
                rootVisualElement.Add(page);
            }
            CreateTemplateEditorTab(_tabPages[0]);
            CreateTranslationEditorTab(_tabPages[1]);
            CreateAnalysisTab(_tabPages[2]);
            RefreshViews();
            SwitchTab(_activeTab);
        }

        /// <summary>Lets content fill only the available space instead of growing to its intrinsic size.</summary>
        /// <param name="element">The tab, pane, list, or input whose flex dimensions are constrained.</param>
        private static void ConfigureFlexibleContent(VisualElement element)
        {
            element.style.flexGrow = 1;
            element.style.flexShrink = 1;
            element.style.flexBasis = 0;
            element.style.minWidth = 0;
            element.style.minHeight = 0;
        }

        /// <summary>Creates a toolbar that keeps its controls accessible in narrow panes.</summary>
        /// <returns>A non-shrinking toolbar whose height follows its wrapped rows.</returns>
        private static Toolbar CreateWrappingToolbar()
        {
            var toolbar = new Toolbar();
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.style.height = StyleKeyword.Auto;
            toolbar.style.minHeight = 22;
            toolbar.style.flexShrink = 0;
            return toolbar;
        }

        /// <summary>Creates a toolbar action with a height independent of its wrapped parent.</summary>
        /// <param name="text">The action's visible caption.</param>
        /// <param name="clicked">The action to invoke when the button is clicked.</param>
        /// <returns>A button that retains its natural width and usable height.</returns>
        private static ToolbarButton CreateToolbarButton(string text, Action clicked)
        {
            var button = new ToolbarButton(clicked) { text = text };
            button.style.height = 20;
            button.style.flexShrink = 0;
            return button;
        }

        /// <summary>Creates a search input that shares available toolbar width with its buttons.</summary>
        /// <returns>A flexible search input with a usable minimum width.</returns>
        private static ToolbarSearchField CreateSearchField()
        {
            var search = new ToolbarSearchField();
            search.style.height = 20;
            search.style.flexGrow = 1;
            search.style.flexShrink = 1;
            search.style.flexBasis = 140;
            search.style.minWidth = 100;
            return search;
        }

        /// <summary>Creates a summary whose text wraps inside the current pane.</summary>
        /// <returns>A non-shrinking label that reserves enough height for its text.</returns>
        private static Label CreateSummaryLabel()
        {
            var label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexShrink = 0;
            return label;
        }

        /// <summary>Creates template editing and additive key synchronization controls.</summary>
        /// <param name="rootElement">The tab's root.</param>
        private void CreateTemplateEditorTab(VisualElement rootElement)
        {
            var toolbar = CreateWrappingToolbar();
            toolbar.Add(CreateToolbarButton("Add discovered keys", AddFixedKeys));
            toolbar.Add(CreateToolbarButton("Save template", () => SaveTemplate()));
            var search = CreateSearchField();
            search.RegisterValueChangedCallback(evt =>
            {
                _templateFilter = evt.newValue;
                RefreshTemplateView();
            });
            toolbar.Add(search);
            rootElement.Add(toolbar);
            rootElement.Add(new HelpBox("Analysis is additive. Manually maintained, scene/prefab and other-platform keys are not removed automatically.", HelpBoxMessageType.Info));
            _templateSummary = CreateSummaryLabel();
            rootElement.Add(_templateSummary);
            _templateList = new ListView(_visibleTemplateIndices, 24, MakeTemplateRow, BindTemplateRow);
            ConfigureFlexibleContent(_templateList);
            _templateList.selectionType = SelectionType.None;
            rootElement.Add(_templateList);
            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.flexShrink = 0;
            _newKeyField = new TextField("Manual key");
            ConfigureFlexibleContent(_newKeyField);
            addRow.Add(_newKeyField);
            addRow.Add(new Button(AddManualKey) { text = "Add" });
            rootElement.Add(addRow);
        }

        /// <summary>Creates a recycled template row.</summary>
        /// <returns>A key editor and remove button.</returns>
        private VisualElement MakeTemplateRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var field = new TextField { name = "key" };
            ConfigureFlexibleContent(field);
            field.RegisterValueChangedCallback(evt =>
            {
                if (row.userData is not int index || index < 0 || index >= _templateContent.Count)
                {
                    return;
                }
                Undo.RecordObject(this, "Edit translation template key");
                _templateContent[index] = evt.newValue;
                _templateDirty = true;
                UpdateUnsavedState();
            });
            row.Add(field);
            row.Add(new Button(() =>
            {
                if (row.userData is not int index || index < 0 || index >= _templateContent.Count)
                {
                    return;
                }
                Undo.RecordObject(this, "Remove translation template key");
                _templateContent.RemoveAt(index);
                _templateDirty = true;
                RefreshViews();
            }) { text = "Remove" });
            return row;
        }

        /// <summary>Binds a recycled row to its current template index.</summary>
        /// <param name="element">The row to bind.</param>
        /// <param name="index">The index in the filtered list.</param>
        private void BindTemplateRow(VisualElement element, int index)
        {
            var templateIndex = _visibleTemplateIndices[index];
            element.userData = templateIndex;
            element.Q<TextField>("key").SetValueWithoutNotify(_templateContent[templateIndex]);
            element.tooltip = GetSources(_templateContent[templateIndex]);
        }

        /// <summary>Creates the language selection list and translation editor.</summary>
        /// <param name="rootElement">The tab's root.</param>
        private void CreateTranslationEditorTab(VisualElement rootElement)
        {
            var split = new TwoPaneSplitView(0, 180, TwoPaneSplitViewOrientation.Horizontal);
            ConfigureFlexibleContent(split);
            rootElement.Add(split);
            _languageList = new ListView(_languages, 24, () => new Label(), (element, index) =>
            {
                var document = _languages[index];
                ((Label)element).text = document.Code + (document.IsDirty ? " *" : string.Empty);
                element.tooltip = document.AssetPath;
            });
            _languageList.style.minHeight = 0;
            _languageList.style.minWidth = 120;
            _languageList.selectionType = SelectionType.Single;
            _languageList.selectionChanged += OnSelectionChanged;
            split.Add(_languageList);
            var rightRoot = new VisualElement();
            ConfigureFlexibleContent(rightRoot);
            rightRoot.style.minWidth = 240;
            split.Add(rightRoot);
            _languageSummary = CreateSummaryLabel();
            rightRoot.Add(_languageSummary);
            _languageEditor = new VisualElement();
            ConfigureFlexibleContent(_languageEditor);
            rightRoot.Add(_languageEditor);
            var toolbar = CreateWrappingToolbar();
            toolbar.Add(CreateToolbarButton("Add missing template keys", AddMissingTranslations));
            toolbar.Add(CreateToolbarButton("Save language", () => SaveSelectedLanguage()));
            var search = CreateSearchField();
            search.RegisterValueChangedCallback(evt =>
            {
                _translationFilter = evt.newValue;
                RefreshTranslationView();
            });
            toolbar.Add(search);
            _languageEditor.Add(toolbar);
            _codeField = new TextField("Code");
            _codeField.RegisterValueChangedCallback(evt => UpdateLanguageMetadata(true, evt.newValue));
            _languageEditor.Add(_codeField);
            _authorField = new TextField("Author");
            _authorField.RegisterValueChangedCallback(evt => UpdateLanguageMetadata(false, evt.newValue));
            _languageEditor.Add(_authorField);
            _translationList = new ListView(_visibleTranslations, 42, MakeTranslationRow, BindTranslationRow);
            _translationList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            _translationList.selectionType = SelectionType.None;
            ConfigureFlexibleContent(_translationList);
            _languageEditor.Add(_translationList);
        }

        /// <summary>Creates a recycled translation row.</summary>
        /// <returns>A read-only key and editable localized text.</returns>
        private VisualElement MakeTranslationRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var key = new Label { name = "key" };
            key.style.width = Length.Percent(45);
            key.style.flexShrink = 0;
            key.style.whiteSpace = WhiteSpace.Normal;
            row.Add(key);
            var text = new TextField { name = "text", multiline = true };
            ConfigureFlexibleContent(text);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.RegisterValueChangedCallback(evt =>
            {
                var document = GetSelectedLanguage();
                if (document is null || row.userData is not TranslationEntry entry)
                {
                    return;
                }
                Undo.RecordObject(this, "Edit translated text");
                entry.Text = evt.newValue;
                document.IsDirty = true;
                UpdateUnsavedState();
                RefreshLanguageSummary();
            });
            row.Add(text);
            return row;
        }

        /// <summary>Binds a recycled translation row.</summary>
        /// <param name="element">The row to bind.</param>
        /// <param name="index">The index in the filtered list.</param>
        private void BindTranslationRow(VisualElement element, int index)
        {
            var entry = _visibleTranslations[index];
            element.userData = entry;
            element.Q<Label>("key").text = entry.Key;
            element.Q<TextField>("text").SetValueWithoutNotify(entry.Text);
            element.tooltip = GetSources(entry.Key);
        }

        /// <summary>Creates reports of inferred keys and unresolved dynamic calls.</summary>
        /// <param name="rootElement">The tab's root.</param>
        private void CreateAnalysisTab(VisualElement rootElement)
        {
            rootElement.Add(new HelpBox("Only the currently compiled Assembly-CSharp is analyzed. Inactive platform branches and dynamic runtime data require manual review. Hover over a key to inspect its sources.", HelpBoxMessageType.Info));
            _analysisSummary = CreateSummaryLabel();
            rootElement.Add(_analysisSummary);
            _analysisList = new ListView(_visibleFixedKeys, 22, () => new Label(), (element, index) =>
            {
                var key = _visibleFixedKeys[index];
                ((Label)element).text = key;
                element.tooltip = GetSources(key);
            });
            ConfigureFlexibleContent(_analysisList);
            rootElement.Add(_analysisList);
            rootElement.Add(new Label("Unresolved / dynamic calls and settings"));
            _diagnosticList = new ListView(_diagnostics, 36, () =>
            {
                var label = new Label();
                label.style.whiteSpace = WhiteSpace.Normal;
                return label;
            }, (element, index) =>
            {
                ((Label)element).text = _diagnostics[index];
            });
            _diagnosticList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            ConfigureFlexibleContent(_diagnosticList);
            rootElement.Add(_diagnosticList);
        }

        /// <summary>Analyzes the loaded assembly without executing game or setting initialization.</summary>
        private void AnalyzeAssembly()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _analysisStatus = "Wait for Unity to finish importing and compiling before analyzing.";
                RefreshAnalysisView();
                return;
            }
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.GetName().Name == "Assembly-CSharp");
            if (assembly is null)
            {
                _analysisStatus = "Assembly-CSharp is not loaded. Resolve script compilation errors first.";
                RefreshAnalysisView();
                return;
            }
            _fixedKeys.Clear();
            _diagnostics.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("Translation analysis", "Reading method bodies and setting metadata...", 0.5f);
                TranslationCallAnalyzer.Collect(assembly, _fixedKeys, _diagnostics);
                TranslationSettingAnalyzer.Collect(assembly, _fixedKeys, _diagnostics);
                _analysisStatus = $"{_fixedKeys.Count} fixed keys; {_diagnostics.Count} diagnostics. Current compiled platform only.";
            }
            catch (Exception exception)
            {
                _fixedKeys.Clear();
                _diagnostics.Clear();
                _analysisStatus = "Analysis failed; partial results were discarded. See the Console.";
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            RefreshViews();
            SwitchTab(2);
        }

        /// <summary>Adds discovered keys without deleting manually maintained template entries.</summary>
        private void AddFixedKeys()
        {
            var existing = new HashSet<string>(_templateContent, StringComparer.Ordinal);
            var additions = _fixedKeys.Keys.Where(key => !existing.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            if (additions.Length == 0)
            {
                return;
            }
            Undo.RecordObject(this, "Add discovered translation keys");
            _templateContent.AddRange(additions);
            _templateDirty = true;
            RefreshViews();
        }

        /// <summary>Adds a nonempty, unique manually maintained template key.</summary>
        private void AddManualKey()
        {
            var key = _newKeyField?.value.Trim() ?? string.Empty;
            if (key.Length == 0 || _templateContent.Contains(key, StringComparer.Ordinal))
            {
                return;
            }
            Undo.RecordObject(this, "Add manual translation key");
            _templateContent.Add(key);
            _templateDirty = true;
            _newKeyField?.SetValueWithoutNotify(string.Empty);
            RefreshViews();
        }

        /// <summary>Adds missing template entries while preserving existing translation text.</summary>
        private void AddMissingTranslations()
        {
            var document = GetSelectedLanguage();
            if (document is null)
            {
                return;
            }
            var existing = new HashSet<string>(document.Entries.Select(entry => entry.Key), StringComparer.Ordinal);
            var additions = _templateContent.Where(key => !string.IsNullOrWhiteSpace(key) && existing.Add(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            if (additions.Length == 0)
            {
                return;
            }
            Undo.RecordObject(this, "Add missing language keys");
            foreach (var key in additions)
            {
                document.Entries.Add(new TranslationEntry { Key = key });
            }
            document.IsDirty = true;
            RefreshViews();
        }

        /// <summary>Updates document metadata and records the edit for undo.</summary>
        /// <param name="isCode">Whether to edit the code instead of the author.</param>
        /// <param name="value">The new metadata text.</param>
        private void UpdateLanguageMetadata(bool isCode, string value)
        {
            var document = GetSelectedLanguage();
            if (document is null)
            {
                return;
            }
            Undo.RecordObject(this, "Edit language metadata");
            if (isCode)
            {
                document.Code = value;
            }
            else
            {
                document.Author = value;
            }
            document.IsDirty = true;
            UpdateUnsavedState();
        }

        /// <summary>Loads resources from actual asset paths without initializing runtime localization.</summary>
        private void LoadResources()
        {
            _templateContent.Clear();
            _languages.Clear();
            _templatePath = TemplatePath;
            _templateSource = string.Empty;
            foreach (var asset in Resources.LoadAll<TextAsset>("Langs"))
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                try
                {
                    var source = File.ReadAllText(assetPath);
                    if (asset.name == "template.tpl")
                    {
                        _templatePath = assetPath;
                        _templateSource = source;
                        var keys = JsonConvert.DeserializeObject<List<string>>(source);
                        if (keys is null)
                        {
                            throw new FormatException("The template must be an array of keys.");
                        }
                        TranslationDocument.ValidateKeys(keys);
                        _templateContent.AddRange(keys);
                    }
                    else if (!asset.name.EndsWith(".tpl", StringComparison.Ordinal))
                    {
                        _languages.Add(TranslationDocument.Read(assetPath, source));
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Translation editor] Cannot load {assetPath}: {exception.Message}");
                }
            }
            _languages.Sort((left, right) => StringComparer.Ordinal.Compare(left.Code, right.Code));
            _templateDirty = false;
            _selectedLanguageIndex = _languages.Count == 0 ? -1 : Math.Clamp(_selectedLanguageIndex, 0, _languages.Count - 1);
            _isLoaded = true;
        }

        /// <summary>Reloads resource files after confirming that unsaved changes may be discarded.</summary>
        private void ReloadResources()
        {
            if (hasUnsavedChanges && !EditorUtility.DisplayDialog("Reload translations", "Discard all unsaved template and language changes?", "Discard and reload", "Cancel"))
            {
                return;
            }
            Undo.ClearUndo(this);
            LoadResources();
            RefreshViews();
        }

        /// <summary>Saves a validated, deterministically ordered template.</summary>
        /// <returns>Whether saving succeeded.</returns>
        private bool SaveTemplate()
        {
            try
            {
                TranslationDocument.ValidateKeys(_templateContent);
                var keys = _templateContent.OrderBy(key => key, StringComparer.Ordinal).ToArray();
                var source = JsonConvert.SerializeObject(keys, Formatting.Indented) + Environment.NewLine;
                TranslationDocument.WriteIfUnchanged(_templatePath, _templateSource, source);
                AssetDatabase.ImportAsset(_templatePath);
                _templateSource = source;
                _templateContent = new List<string>(keys);
                _templateDirty = false;
                Undo.ClearUndo(this);
                RefreshViews();
                return true;
            }
            catch (Exception exception)
            {
                ReportSaveError(exception);
                return false;
            }
        }

        /// <summary>Saves the selected language, if any.</summary>
        /// <returns>Whether a language was selected and saved.</returns>
        private bool SaveSelectedLanguage()
        {
            var document = GetSelectedLanguage();
            return document is not null && SaveLanguage(document);
        }

        /// <summary>Saves a language document and reimports its resource without changing its GUID.</summary>
        /// <param name="document">The document to save.</param>
        /// <returns>Whether saving succeeded.</returns>
        private bool SaveLanguage(TranslationDocument document)
        {
            var emptyCount = document.Entries.Count(entry => entry.Text.Length == 0);
            if (emptyCount != 0 && !EditorUtility.DisplayDialog("Empty translations", $"{emptyCount} translations are empty and will display as blank in the game. Save anyway?", "Save", "Cancel"))
            {
                return false;
            }
            try
            {
                document.Save();
                AssetDatabase.ImportAsset(document.AssetPath);
                Undo.ClearUndo(this);
                RefreshViews();
                return true;
            }
            catch (Exception exception)
            {
                ReportSaveError(exception);
                return false;
            }
        }

        /// <summary>Reports a cancelled or failed save without discarding editing state.</summary>
        /// <param name="exception">The validation, conflict, or I/O error.</param>
        private static void ReportSaveError(Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Translations were not saved", exception.Message, "OK");
        }

        /// <summary>Updates selection without clearing it when focus moves into translation fields.</summary>
        /// <param name="selectedItems">The language documents selected by the list.</param>
        private void OnSelectionChanged(IEnumerable<object> selectedItems)
        {
            _selectedLanguageIndex = _languageList?.selectedIndex ?? -1;
            RefreshTranslationView();
        }

        /// <summary>Returns the selected editing document.</summary>
        /// <returns>The selected language, or null when selection is invalid.</returns>
        private TranslationDocument? GetSelectedLanguage()
        {
            if (_selectedLanguageIndex < 0 || _selectedLanguageIndex >= _languages.Count)
            {
                return null;
            }
            return _languages[_selectedLanguageIndex];
        }

        /// <summary>Displays one tab.</summary>
        /// <param name="index">The zero-based tab index.</param>
        private void SwitchTab(int index)
        {
            if (index < 0 || index >= _tabPages.Count)
            {
                return;
            }
            _activeTab = index;
            for (var i = 0; i < _tabPages.Count; i++)
            {
                _tabPages[i].style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Rebinds the lists after loading, analysis, saving, or undo.</summary>
        private void RefreshViews()
        {
            if (_languageList is not null)
            {
                var selectedIndex = _selectedLanguageIndex;
                _languageList.itemsSource = _languages;
                _languageList.RefreshItems();
                _selectedLanguageIndex = selectedIndex;
                _languageList.SetSelectionWithoutNotify(_selectedLanguageIndex < 0 ? Array.Empty<int>() : new[] { _selectedLanguageIndex });
            }
            RefreshTemplateView();
            RefreshTranslationView();
            RefreshAnalysisView();
            UpdateUnsavedState();
        }

        /// <summary>Refreshes the template filter and missing-key count.</summary>
        private void RefreshTemplateView()
        {
            _visibleTemplateIndices.Clear();
            for (var i = 0; i < _templateContent.Count; i++)
            {
                if (MatchesFilter(_templateContent[i], _templateFilter))
                {
                    _visibleTemplateIndices.Add(i);
                }
            }
            _templateList?.RefreshItems();
            if (_templateSummary is not null)
            {
                var missing = _fixedKeys.Keys.Count(key => !_templateContent.Contains(key, StringComparer.Ordinal));
                _templateSummary.text = $"{_templateContent.Count} template keys; {missing} discovered keys not in template. {_templatePath}";
            }
        }

        /// <summary>Refreshes metadata and the filtered translation list.</summary>
        private void RefreshTranslationView()
        {
            var document = GetSelectedLanguage();
            _visibleTranslations.Clear();
            if (document is not null)
            {
                _visibleTranslations.AddRange(document.Entries.Where(entry => MatchesFilter(entry.Key, _translationFilter) || MatchesFilter(entry.Text, _translationFilter)).OrderBy(entry => entry.Key, StringComparer.Ordinal));
            }
            _codeField?.SetValueWithoutNotify(document?.Code ?? string.Empty);
            _authorField?.SetValueWithoutNotify(document?.Author ?? string.Empty);
            _languageEditor?.SetEnabled(document is not null);
            _translationList?.RefreshItems();
            RefreshLanguageSummary();
        }

        /// <summary>Reports missing and empty translations.</summary>
        private void RefreshLanguageSummary()
        {
            if (_languageSummary is null)
            {
                return;
            }
            var document = GetSelectedLanguage();
            if (document is null)
            {
                _languageSummary.text = "Select a language resource to edit its translations.";
                return;
            }
            var present = new HashSet<string>(document.Entries.Select(entry => entry.Key), StringComparer.Ordinal);
            var missing = _templateContent.Count(key => !present.Contains(key));
            var empty = document.Entries.Count(entry => entry.Text.Length == 0);
            _languageSummary.text = $"{document.Entries.Count} entries; {missing} missing template keys; {empty} empty translations. {document.AssetPath}";
        }

        /// <summary>Refreshes the sorted key and dynamic-call reports.</summary>
        private void RefreshAnalysisView()
        {
            _visibleFixedKeys.Clear();
            _visibleFixedKeys.AddRange(_fixedKeys.Keys.OrderBy(key => key, StringComparer.Ordinal));
            _analysisList?.RefreshItems();
            _diagnosticList?.RefreshItems();
            if (_analysisSummary is not null)
            {
                _analysisSummary.text = _analysisStatus;
            }
        }

        /// <summary>Formats a key's origins for the editor tooltip.</summary>
        /// <param name="key">The key to inspect.</param>
        /// <returns>Sorted sources, or a notice that the key is manually maintained.</returns>
        private string GetSources(string key)
        {
            return _fixedKeys.TryGetValue(key, out var sources)
                ? string.Join(Environment.NewLine, sources.OrderBy(source => source, StringComparer.Ordinal))
                : "Not proven by the current assembly analysis; retained as a manually maintained key.";
        }

        /// <summary>Tests whether text matches an editor search.</summary>
        /// <param name="text">The text to search.</param>
        /// <param name="filter">The case-insensitive filter.</param>
        /// <returns>Whether the text should be visible.</returns>
        private static bool MatchesFilter(string text, string filter)
        {
            return text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Updates the close prompt and language dirty indicators.</summary>
        private void UpdateUnsavedState()
        {
            hasUnsavedChanges = _templateDirty || _languages.Any(document => document.IsDirty);
            saveChangesMessage = "Save the template and edited language resources before closing?";
            _languageList?.RefreshItems();
        }

        /// <summary>Refreshes references after Unity restores serialized editing state.</summary>
        private void OnUndoRedo()
        {
            RefreshViews();
        }

        /// <summary>Saves all dirty documents when Unity requests saving before close.</summary>
        public override void SaveChanges()
        {
            if (_templateDirty && !SaveTemplate())
            {
                return;
            }
            foreach (var document in _languages)
            {
                if (document.IsDirty && !SaveLanguage(document))
                {
                    return;
                }
            }
            base.SaveChanges();
        }

        /// <summary>Discards editing state and restores the current resource files.</summary>
        public override void DiscardChanges()
        {
            Undo.ClearUndo(this);
            LoadResources();
            RefreshViews();
            base.DiscardChanges();
        }

        /// <summary>Opens the translation editor from Unity's Window menu.</summary>
        [MenuItem("Window/Manage translations")]
        public static void ShowWindow()
        {
            var window = GetWindow<TranslationManagerWindow>();
            window.titleContent = new GUIContent("Translation editor");
        }
    }
}

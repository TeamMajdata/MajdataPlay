using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>Edits language resources and statically discovers fixed localization keys.</summary>
    /// <remarks>
    /// A scan never edits the template directly: it proposes additions and removals in the Review tab, and only the
    /// changes the user checks are applied.
    /// </remarks>
    public sealed class TranslationManagerWindow : EditorWindow
    {
        /// <summary>The fallback path when a translation template does not exist yet.</summary>
        private const string TemplatePath = "Assets/Resources/Langs/template.tpl.json";

        /// <summary>The name of the runtime assembly whose localization calls and settings are analyzed.</summary>
        private const string RuntimeAssemblyName = "MajdataPlay.Core";

        /// <summary>The index of the template tab.</summary>
        private const int TemplateTab = 0;

        /// <summary>The index of the translation editing tab.</summary>
        private const int TranslationsTab = 1;

        /// <summary>The index of the key review tab.</summary>
        private const int ReviewTab = 2;

        /// <summary>The index of the analysis report tab.</summary>
        private const int AnalysisTab = 3;

        /// <summary>The number of tabs.</summary>
        private const int TabCount = 4;

        /// <summary>Selects which review candidates are listed.</summary>
        private enum ReviewFilter
        {
            /// <summary>Lists every candidate.</summary>
            All,

            /// <summary>Lists proposed additions only.</summary>
            Additions,

            /// <summary>Lists proposed removals only.</summary>
            Removals,
        }

        /// <summary>The proposed template changes of the last scan, retained across assembly reloads.</summary>
        [SerializeField]
        private List<TranslationKeyCandidate> _candidates = new();

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

        /// <summary>The review candidates matching the current review filter and search.</summary>
        private readonly List<TranslationKeyCandidate> _visibleCandidates = new();

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

        /// <summary>The review candidate list.</summary>
        private ListView? _candidateList;

        /// <summary>The review summary.</summary>
        private Label? _reviewSummary;

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

        /// <summary>The review search filter.</summary>
        private string _reviewSearch = string.Empty;

        /// <summary>The change type listed in the review tab.</summary>
        private ReviewFilter _reviewFilter = ReviewFilter.All;

        /// <summary>Whether a scan has completed since the window was enabled, so <see cref="_fixedKeys"/> is current.</summary>
        private bool _hasAnalysis;

        /// <summary>The last analysis result.</summary>
        private string _analysisStatus = $"Analyze {RuntimeAssemblyName} to discover fixed keys.";

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
            toolbar.Add(CreateToolbarButton("Template", () => SwitchTab(TemplateTab)));
            toolbar.Add(CreateToolbarButton("Translations", () => SwitchTab(TranslationsTab)));
            toolbar.Add(CreateToolbarButton("Review", () => SwitchTab(ReviewTab)));
            toolbar.Add(CreateToolbarButton("Analysis", () => SwitchTab(AnalysisTab)));
            toolbar.Add(CreateToolbarButton($"Analyze {RuntimeAssemblyName}", AnalyzeAssembly));
            toolbar.Add(CreateToolbarButton("Save all", () => SaveAll()));
            toolbar.Add(CreateToolbarButton("Reload resources", ReloadResources));
            rootVisualElement.Add(toolbar);
            for (var i = 0; i < TabCount; i++)
            {
                var page = new VisualElement();
                ConfigureFlexibleContent(page);
                _tabPages.Add(page);
                rootVisualElement.Add(page);
            }
            CreateTemplateEditorTab(_tabPages[TemplateTab]);
            CreateTranslationEditorTab(_tabPages[TranslationsTab]);
            CreateReviewTab(_tabPages[ReviewTab]);
            CreateAnalysisTab(_tabPages[AnalysisTab]);
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
            toolbar.Add(CreateToolbarButton("Review discovered keys", OpenReview));
            toolbar.Add(CreateToolbarButton("Remove filtered keys", ConfirmRemoveFilteredTemplateKeys));
            toolbar.Add(CreateToolbarButton("Save template", () => SaveTemplate()));
            var search = CreateSearchField();
            search.RegisterValueChangedCallback(evt =>
            {
                _templateFilter = evt.newValue;
                RefreshTemplateView();
            });
            toolbar.Add(search);
            rootElement.Add(toolbar);
            rootElement.Add(new HelpBox("A scan never edits the template directly. Review the proposed additions and removals in the Review tab and apply the ones you check. \"Remove filtered keys\" removes every key matching the search.", HelpBoxMessageType.Info));
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
            toolbar.Add(CreateToolbarButton("Add missing keys to all languages", AddMissingTranslationsToAll));
            toolbar.Add(CreateToolbarButton("Remove keys not in template", ConfirmRemoveObsoleteTranslations));
            toolbar.Add(CreateToolbarButton("Remove keys not in template from all languages", ConfirmRemoveObsoleteTranslationsFromAll));
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

        /// <summary>Creates the review of the template changes proposed by the last scan.</summary>
        /// <param name="rootElement">The tab's root.</param>
        private void CreateReviewTab(VisualElement rootElement)
        {
            rootElement.Add(new HelpBox(
                "+ Add: found by the scan but missing from the template. - Remove: in the template but not found by the scan. " +
                "Check the changes to keep and apply them; nothing is written until the template is saved. Optional additions are " +
                "numeric option values, which display the number when untranslated. Removals start unchecked because keys used by " +
                "dynamic code, other platforms or external data cannot be proven. Hover over a key to see its sources.",
                HelpBoxMessageType.Info));
            var toolbar = CreateWrappingToolbar();
            var filter = new EnumField(_reviewFilter);
            filter.style.flexShrink = 0;
            filter.style.minWidth = 90;
            filter.RegisterValueChangedCallback(evt =>
            {
                _reviewFilter = (ReviewFilter)evt.newValue;
                RefreshReviewView();
            });
            toolbar.Add(filter);
            toolbar.Add(CreateToolbarButton("Check listed", () => SetListedCandidatesChecked(true)));
            toolbar.Add(CreateToolbarButton("Uncheck listed", () => SetListedCandidatesChecked(false)));
            toolbar.Add(CreateToolbarButton("Check required additions", CheckRequiredAdditions));
            toolbar.Add(CreateToolbarButton("Apply checked", ApplyCheckedCandidates));
            var search = CreateSearchField();
            search.RegisterValueChangedCallback(evt =>
            {
                _reviewSearch = evt.newValue;
                RefreshReviewView();
            });
            toolbar.Add(search);
            rootElement.Add(toolbar);
            _reviewSummary = CreateSummaryLabel();
            rootElement.Add(_reviewSummary);
            _candidateList = new ListView(_visibleCandidates, 22, MakeCandidateRow, BindCandidateRow);
            _candidateList.selectionType = SelectionType.None;
            ConfigureFlexibleContent(_candidateList);
            rootElement.Add(_candidateList);
        }

        /// <summary>Creates a recycled review row.</summary>
        /// <returns>A check box, the proposed change, the key and a note.</returns>
        private VisualElement MakeCandidateRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var toggle = new Toggle { name = "checked" };
            toggle.style.flexShrink = 0;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (row.userData is not TranslationKeyCandidate candidate)
                {
                    return;
                }
                Undo.RecordObject(this, "Check translation key change");
                candidate.IsChecked = evt.newValue;
                RefreshReviewSummary();
            });
            row.Add(toggle);
            var change = new Label { name = "change" };
            change.style.width = 70;
            change.style.flexShrink = 0;
            row.Add(change);
            var key = new Label { name = "key" };
            ConfigureFlexibleContent(key);
            key.style.overflow = Overflow.Hidden;
            row.Add(key);
            var note = new Label { name = "note" };
            note.style.flexShrink = 0;
            note.style.unityFontStyleAndWeight = FontStyle.Italic;
            row.Add(note);
            return row;
        }

        /// <summary>Binds a recycled review row.</summary>
        /// <param name="element">The row to bind.</param>
        /// <param name="index">The index in the filtered list.</param>
        private void BindCandidateRow(VisualElement element, int index)
        {
            var candidate = _visibleCandidates[index];
            element.userData = candidate;
            element.tooltip = candidate.Details;
            element.Q<Toggle>("checked").SetValueWithoutNotify(candidate.IsChecked);
            element.Q<Label>("change").text = candidate.Change == TranslationKeyChange.Add ? "+ Add" : "- Remove";
            element.Q<Label>("key").text = candidate.Key;
            element.Q<Label>("note").text = candidate.Change == TranslationKeyChange.Remove
                ? "not found by scan"
                : candidate.IsOptional ? "optional numeric value" : string.Empty;
        }

        /// <summary>Creates reports of inferred keys and unresolved dynamic calls.</summary>
        /// <param name="rootElement">The tab's root.</param>
        private void CreateAnalysisTab(VisualElement rootElement)
        {
            rootElement.Add(new HelpBox($"Only the currently compiled {RuntimeAssemblyName} is analyzed. Inactive platform branches and dynamic runtime data require manual review. Hover over a key to inspect its sources.", HelpBoxMessageType.Info));
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

        /// <summary>
        /// Scans the loaded assembly, the setting metadata and the scenes and prefabs without executing game code, and
        /// proposes the resulting template changes for review.
        /// </summary>
        /// <remarks>
        /// The scan writes into fresh collections and replaces the previous results only when it completes, so a
        /// cancelled or failed scan keeps them.
        /// </remarks>
        private void AnalyzeAssembly()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                _analysisStatus = "Wait for Unity to finish importing and compiling before analyzing.";
                RefreshAnalysisView();
                return;
            }
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.GetName().Name == RuntimeAssemblyName);
            if (assembly is null)
            {
                _analysisStatus = $"{RuntimeAssemblyName} is not loaded. Resolve script compilation errors first.";
                RefreshAnalysisView();
                return;
            }
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var cancellation = new CancellationTokenSource();
            try
            {
                TranslationCallAnalyzer.Collect(assembly, keys, diagnostics,
                    new CancelableProgressBar("Reading method bodies...", 0f, 0.8f, cancellation), cancellation.Token);
                new CancelableProgressBar("Reading setting metadata...", 0.8f, 0.9f, cancellation).Report(0f);
                cancellation.Token.ThrowIfCancellationRequested();
                TranslationSettingAnalyzer.Collect(assembly, keys, diagnostics);
                new CancelableProgressBar("Reading scenes and prefabs...", 0.9f, 1f, cancellation).Report(0f);
                cancellation.Token.ThrowIfCancellationRequested();
                TranslationAssetAnalyzer.Collect(FindSceneAndPrefabPaths(), keys, diagnostics);
            }
            catch (OperationCanceledException)
            {
                _analysisStatus = "Analysis cancelled; the previous results were kept.";
                RefreshAnalysisView();
                return;
            }
            catch (Exception exception)
            {
                _analysisStatus = "Analysis failed; the previous results were kept. See the Console.";
                Debug.LogException(exception);
                RefreshAnalysisView();
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.RecordObject(this, "Analyze translation keys");
            _fixedKeys.Clear();
            foreach (var pair in keys)
            {
                _fixedKeys.Add(pair.Key, pair.Value);
            }
            _diagnostics.Clear();
            _diagnostics.AddRange(diagnostics);
            _candidates = TranslationKeyReview.Build(_fixedKeys, _templateContent);
            _hasAnalysis = true;
            var additions = _candidates.Count(candidate => candidate.Change == TranslationKeyChange.Add);
            _analysisStatus = $"{_fixedKeys.Count} fixed keys in {stopwatch.ElapsedMilliseconds} ms; {additions} additions and " +
                $"{_candidates.Count - additions} removals to review; {_diagnostics.Count} diagnostics. Current compiled platform only.";
            RefreshViews();
            SwitchTab(ReviewTab);
        }

        /// <summary>Lists the scenes and prefabs under Assets whose <c>LocalizeTMP</c> keys are scanned.</summary>
        /// <returns>The distinct asset paths.</returns>
        private static IEnumerable<string> FindSceneAndPrefabPaths()
        {
            var folders = new[] { "Assets" };
            return AssetDatabase.FindAssets("t:Scene", folders)
                .Concat(AssetDatabase.FindAssets("t:Prefab", folders))
                .Distinct(StringComparer.Ordinal)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Length != 0);
        }

        /// <summary>Shows the review of the last scan, scanning first when no scan has completed yet.</summary>
        private void OpenReview()
        {
            if (!_hasAnalysis)
            {
                AnalyzeAssembly();
                return;
            }
            SwitchTab(ReviewTab);
        }

        /// <summary>Checks or unchecks every candidate listed by the current review filter and search.</summary>
        /// <param name="isChecked">The new checked state.</param>
        private void SetListedCandidatesChecked(bool isChecked)
        {
            Undo.RecordObject(this, isChecked ? "Check listed translation key changes" : "Uncheck listed translation key changes");
            TranslationKeyReview.SetChecked(_visibleCandidates, isChecked);
            RefreshReviewView();
        }

        /// <summary>Checks every addition that is not optional, whatever the current filter.</summary>
        private void CheckRequiredAdditions()
        {
            Undo.RecordObject(this, "Check required translation key additions");
            TranslationKeyReview.SetChecked(_candidates.Where(candidate => candidate.Change == TranslationKeyChange.Add && !candidate.IsOptional), true);
            RefreshReviewView();
        }

        /// <summary>Applies the checked review candidates to the template, which then needs saving.</summary>
        private void ApplyCheckedCandidates()
        {
            if (!_candidates.Any(candidate => candidate.IsChecked))
            {
                return;
            }
            Undo.RecordObject(this, "Apply translation key changes");
            var (added, removed) = TranslationKeyReview.Apply(_templateContent, _candidates);
            if (added != 0 || removed != 0)
            {
                _templateDirty = true;
            }
            _analysisStatus = $"Applied {added} additions and {removed} removals to the template; save the template to keep them.";
            RefreshViews();
        }

        /// <summary>Removes every template key matching the search after confirmation.</summary>
        private void ConfirmRemoveFilteredTemplateKeys()
        {
            if (_templateFilter.Length == 0 || _visibleTemplateIndices.Count == 0)
            {
                EditorUtility.DisplayDialog("Remove filtered keys", "Enter a search that matches the keys to remove first.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("Remove filtered keys", $"Remove the {_visibleTemplateIndices.Count} template keys matching \"{_templateFilter}\"? Language files are not changed.", "Remove", "Cancel"))
            {
                RemoveTemplateKeys(_visibleTemplateIndices.ToArray());
            }
        }

        /// <summary>Removes several template keys in one undoable step.</summary>
        /// <param name="indices">The indices into the template of the keys to remove.</param>
        /// <returns>The number of removed keys.</returns>
        private int RemoveTemplateKeys(IReadOnlyCollection<int> indices)
        {
            var removals = new HashSet<int>(indices.Where(index => index >= 0 && index < _templateContent.Count));
            if (removals.Count == 0)
            {
                return 0;
            }
            Undo.RecordObject(this, "Remove translation template keys");
            var kept = new List<string>(_templateContent.Count - removals.Count);
            for (var i = 0; i < _templateContent.Count; i++)
            {
                if (!removals.Contains(i))
                {
                    kept.Add(_templateContent[i]);
                }
            }
            _templateContent = kept;
            _templateDirty = true;
            RefreshViews();
            return removals.Count;
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

        /// <summary>Adds missing template entries to the selected language while preserving existing translation text.</summary>
        private void AddMissingTranslations()
        {
            var document = GetSelectedLanguage();
            if (document is null)
            {
                return;
            }
            AddMissingKeys(new[] { document });
        }

        /// <summary>Adds missing template entries to every language while preserving existing translation text.</summary>
        private void AddMissingTranslationsToAll()
        {
            AddMissingKeys(_languages);
        }

        /// <summary>Adds the template keys that several languages lack as empty entries, in one undoable step.</summary>
        /// <param name="documents">The languages to complete.</param>
        /// <returns>The total number of added entries.</returns>
        private int AddMissingKeys(IReadOnlyList<TranslationDocument> documents)
        {
            var templateKeys = _templateContent.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var undoRecorded = false;
            var total = 0;
            foreach (var document in documents)
            {
                var existing = new HashSet<string>(document.Entries.Select(entry => entry.Key), StringComparer.Ordinal);
                var additions = templateKeys.Where(key => !existing.Contains(key)).ToArray();
                if (additions.Length == 0)
                {
                    continue;
                }
                if (!undoRecorded)
                {
                    Undo.RecordObject(this, "Add missing language keys");
                    undoRecorded = true;
                }
                foreach (var key in additions)
                {
                    document.Entries.Add(new TranslationEntry { Key = key });
                }
                document.IsDirty = true;
                total += additions.Length;
            }
            if (total != 0)
            {
                RefreshViews();
            }
            return total;
        }

        /// <summary>Removes the selected language's entries whose keys are not in the template, after confirmation.</summary>
        private void ConfirmRemoveObsoleteTranslations()
        {
            var document = GetSelectedLanguage();
            if (document is not null)
            {
                ConfirmRemoveObsoleteKeys(new[] { document });
            }
        }

        /// <summary>Removes every language's entries whose keys are not in the template, after confirmation.</summary>
        private void ConfirmRemoveObsoleteTranslationsFromAll()
        {
            ConfirmRemoveObsoleteKeys(_languages);
        }

        /// <summary>Asks before removing the entries whose keys are not in the template, since their texts are lost.</summary>
        /// <param name="documents">The languages to clean up.</param>
        private void ConfirmRemoveObsoleteKeys(IReadOnlyList<TranslationDocument> documents)
        {
            var templateKeys = new HashSet<string>(_templateContent, StringComparer.Ordinal);
            var count = documents.Sum(document => document.Entries.Count(entry => !templateKeys.Contains(entry.Key)));
            if (count == 0)
            {
                EditorUtility.DisplayDialog("Remove keys not in template", "Every entry is in the template.", "OK");
                return;
            }
            var languages = string.Join(", ", documents.Select(document => document.Code));
            if (EditorUtility.DisplayDialog("Remove keys not in template", $"Remove {count} entries whose keys are not in the template from {languages}? Their translated texts are discarded when the languages are saved.", "Remove", "Cancel"))
            {
                RemoveObsoleteKeys(documents);
            }
        }

        /// <summary>Removes the entries whose keys are not in the template from several languages, in one undoable step.</summary>
        /// <param name="documents">The languages to clean up.</param>
        /// <returns>The total number of removed entries.</returns>
        private int RemoveObsoleteKeys(IReadOnlyList<TranslationDocument> documents)
        {
            var templateKeys = new HashSet<string>(_templateContent, StringComparer.Ordinal);
            if (!documents.Any(document => document.Entries.Exists(entry => !templateKeys.Contains(entry.Key))))
            {
                return 0;
            }
            Undo.RecordObject(this, "Remove language keys not in template");
            var total = 0;
            foreach (var document in documents)
            {
                var removed = document.Entries.RemoveAll(entry => !templateKeys.Contains(entry.Key));
                if (removed != 0)
                {
                    document.IsDirty = true;
                    total += removed;
                }
            }
            RefreshViews();
            return total;
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
            if (_hasAnalysis)
            {
                // The reloaded template may differ from the one the last scan was compared with.
                _candidates = TranslationKeyReview.Build(_fixedKeys, _templateContent);
            }
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
            RefreshReviewView();
            RefreshAnalysisView();
            UpdateUnsavedState();
        }

        /// <summary>Refreshes the filtered review list and its summary.</summary>
        private void RefreshReviewView()
        {
            _visibleCandidates.Clear();
            foreach (var candidate in _candidates)
            {
                var matchesChange = _reviewFilter switch
                {
                    ReviewFilter.Additions => candidate.Change == TranslationKeyChange.Add,
                    ReviewFilter.Removals => candidate.Change == TranslationKeyChange.Remove,
                    _ => true,
                };
                if (matchesChange && MatchesFilter(candidate.Key, _reviewSearch))
                {
                    _visibleCandidates.Add(candidate);
                }
            }
            _candidateList?.RefreshItems();
            RefreshReviewSummary();
        }

        /// <summary>Reports how many changes are proposed and checked.</summary>
        private void RefreshReviewSummary()
        {
            if (_reviewSummary is null)
            {
                return;
            }
            if (_candidates.Count == 0)
            {
                _reviewSummary.text = _hasAnalysis
                    ? "The template matches the last scan; there is nothing to review."
                    : $"Analyze {RuntimeAssemblyName} to propose template changes.";
                return;
            }
            var additions = _candidates.Count(candidate => candidate.Change == TranslationKeyChange.Add);
            var checkedAdditions = _candidates.Count(candidate => candidate.Change == TranslationKeyChange.Add && candidate.IsChecked);
            var optionalAdditions = _candidates.Count(candidate => candidate.Change == TranslationKeyChange.Add && candidate.IsOptional);
            var removals = _candidates.Count - additions;
            var checkedRemovals = _candidates.Count(candidate => candidate.Change == TranslationKeyChange.Remove && candidate.IsChecked);
            _reviewSummary.text = $"{additions} additions ({checkedAdditions} checked, {optionalAdditions} optional); " +
                $"{removals} removals ({checkedRemovals} checked). {_visibleCandidates.Count} listed.";
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
                var additions = _candidates.Count(candidate => candidate.Change == TranslationKeyChange.Add);
                _templateSummary.text = $"{_templateContent.Count} template keys; {additions} additions and {_candidates.Count - additions} removals pending review. {_templatePath}";
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

        /// <summary>Saves the template and every edited language, stopping at the first failure or cancellation.</summary>
        /// <returns>Whether everything was saved.</returns>
        private bool SaveAll()
        {
            if (_templateDirty && !SaveTemplate())
            {
                return false;
            }
            foreach (var document in _languages)
            {
                if (document.IsDirty && !SaveLanguage(document))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Saves all dirty documents when Unity requests saving before close.</summary>
        public override void SaveChanges()
        {
            if (SaveAll())
            {
                base.SaveChanges();
            }
        }

        /// <summary>Discards editing state and restores the current resource files.</summary>
        public override void DiscardChanges()
        {
            Undo.ClearUndo(this);
            LoadResources();
            RefreshViews();
            base.DiscardChanges();
        }

        /// <summary>Shows a cancelable progress bar for one stage of the scan and cancels the scan when the user asks.</summary>
        private sealed class CancelableProgressBar : IProgress<float>
        {
            /// <summary>The stage description shown under the title.</summary>
            private readonly string _info;

            /// <summary>The overall progress at the start of the stage.</summary>
            private readonly float _start;

            /// <summary>The overall progress at the end of the stage.</summary>
            private readonly float _end;

            /// <summary>The source cancelled when the user presses Cancel.</summary>
            private readonly CancellationTokenSource _cancellation;

            /// <summary>Initializes a new instance of the <see cref="CancelableProgressBar"/> class.</summary>
            /// <param name="info">The stage description shown under the title.</param>
            /// <param name="start">The overall progress at the start of the stage, from 0 to 1.</param>
            /// <param name="end">The overall progress at the end of the stage, from 0 to 1.</param>
            /// <param name="cancellation">The source cancelled when the user presses Cancel.</param>
            internal CancelableProgressBar(string info, float start, float end, CancellationTokenSource cancellation)
            {
                _info = info;
                _start = start;
                _end = end;
                _cancellation = cancellation;
            }

            /// <summary>Shows the progress of the stage and requests cancellation when the user presses Cancel.</summary>
            /// <param name="value">The progress of the stage, from 0 to 1.</param>
            public void Report(float value)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Translation analysis", _info, Mathf.Lerp(_start, _end, value)))
                {
                    _cancellation.Cancel();
                }
            }
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

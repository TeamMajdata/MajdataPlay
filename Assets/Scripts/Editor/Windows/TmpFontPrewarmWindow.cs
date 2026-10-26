using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>
    /// Editor window that adds characters to selected or dropped TMP font assets ahead of use, from literal characters,
    /// character set files and Unicode ranges, so the glyphs are not rasterized at runtime. Dynamic assets receive the glyphs
    /// directly; static assets can be baked in place and stay static.
    /// </summary>
    internal sealed class TmpFontPrewarmWindow : EditorWindow
    {
        /// <summary>The window title.</summary>
        private const string WindowTitle = "TMP Font Prewarmer";

        /// <summary>The number of unsupported code points listed in a result line.</summary>
        private const int UnsupportedSampleCount = 16;

        /// <summary>The atlas size limits offered for growing static atlases.</summary>
        private static readonly int[] MaxStaticAtlasSizes = { 1024, 2048, 4096, 8192 };

        /// <summary>The labels of <see cref="MaxStaticAtlasSizes"/>.</summary>
        private static readonly GUIContent[] MaxStaticAtlasSizeLabels =
        {
            new("1024"), new("2048"), new("4096"), new("8192")
        };

        /// <summary>The font assets to prewarm, in the order they are processed.</summary>
        private readonly List<TMP_FontAsset> _fontAssets = new();

        /// <summary>The character set files whose text is added as literal characters.</summary>
        private readonly List<TextAsset?> _characterFiles = new();

        /// <summary>The summaries of the last run, one per font asset.</summary>
        private readonly List<string> _resultLines = new();

        /// <summary>How glyphs are added and saved; the window bakes static assets by default.</summary>
        private readonly TmpFontPrewarmOptions _options = new()
        {
            IncludeStaticAssets = true
        };

        /// <summary>The literal characters to add.</summary>
        private string _characters = string.Empty;

        /// <summary>The Unicode range list to add.</summary>
        private string _unicodeRanges = string.Empty;

        /// <summary>The union of all character sources, rebuilt when an input changes.</summary>
        private SortedSet<uint> _codePoints = new();

        /// <summary>The range list error of the last rebuild, or null when the inputs are valid.</summary>
        private string? _parseError;

        /// <summary>Whether an input changed since <see cref="_codePoints"/> was rebuilt.</summary>
        private bool _isCodePointsDirty = true;

        /// <summary>The scroll position of the window content.</summary>
        private Vector2 _scroll;

        /// <summary>The font assets to prewarm.</summary>
        internal IReadOnlyList<TMP_FontAsset> FontAssets => _fontAssets;

        /// <summary>How glyphs are added and saved.</summary>
        internal TmpFontPrewarmOptions Options => _options;

        /// <summary>The literal characters to add; setting it marks the code points for rebuilding.</summary>
        internal string Characters
        {
            get => _characters;
            set
            {
                _characters = value ?? string.Empty;
                _isCodePointsDirty = true;
            }
        }

        /// <summary>The Unicode range list to add; setting it marks the code points for rebuilding.</summary>
        internal string UnicodeRanges
        {
            get => _unicodeRanges;
            set
            {
                _unicodeRanges = value ?? string.Empty;
                _isCodePointsDirty = true;
            }
        }

        /// <summary>The union of all character sources.</summary>
        internal IReadOnlyCollection<uint> CodePoints
        {
            get
            {
                RefreshCodePoints();
                return _codePoints;
            }
        }

        /// <summary>The range list error, or null when the inputs are valid.</summary>
        internal string? ParseError
        {
            get
            {
                RefreshCodePoints();
                return _parseError;
            }
        }

        /// <summary>Opens the window.</summary>
        [MenuItem("Window/MajdataPlay/" + WindowTitle)]
        public static void Open()
        {
            GetWindow<TmpFontPrewarmWindow>(WindowTitle);
        }

        /// <summary>Opens the window with the TMP font assets selected in the Project window.</summary>
        [MenuItem("Assets/Prewarm TMP Font Asset...", false, 2000)]
        private static void OpenWithSelection()
        {
            var window = GetWindow<TmpFontPrewarmWindow>(WindowTitle);
            window.AddFontAssets(Selection.objects);
        }

        /// <summary>Enables the Project window menu item only when TMP font assets are selected.</summary>
        /// <returns>True when the selection contains a TMP font asset.</returns>
        [MenuItem("Assets/Prewarm TMP Font Asset...", true)]
        private static bool ValidateOpenWithSelection()
        {
            return Selection.GetFiltered<TMP_FontAsset>(SelectionMode.Assets).Length > 0;
        }

        /// <summary>Adds TMP font assets, including those inside folders, skipping duplicates.</summary>
        /// <param name="objects">Font assets, folders or other objects; other objects are ignored.</param>
        /// <returns>The number of font assets added.</returns>
        internal int AddFontAssets(IEnumerable<Object> objects)
        {
            var addedCount = 0;
            foreach (var obj in objects)
            {
                if (obj is TMP_FontAsset fontAsset)
                {
                    addedCount += AddFontAsset(fontAsset) ? 1 : 0;
                    continue;
                }
                var path = obj == null ? null : AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }
                foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { path }))
                {
                    var folderAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (folderAsset != null)
                    {
                        addedCount += AddFontAsset(folderAsset) ? 1 : 0;
                    }
                }
            }
            if (addedCount > 0)
            {
                Repaint();
            }
            return addedCount;
        }

        /// <summary>Adds character set files, skipping duplicates and objects that are not text assets.</summary>
        /// <param name="objects">The dropped or selected objects.</param>
        /// <returns>The number of files added.</returns>
        internal int AddCharacterFiles(IEnumerable<Object> objects)
        {
            var addedCount = 0;
            foreach (var obj in objects)
            {
                if (obj is TextAsset textAsset && !_characterFiles.Contains(textAsset))
                {
                    _characterFiles.Add(textAsset);
                    addedCount++;
                }
            }
            if (addedCount > 0)
            {
                _isCodePointsDirty = true;
                Repaint();
            }
            return addedCount;
        }

        /// <summary>Draws the window.</summary>
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawFontAssets();
            EditorGUILayout.Space();
            DrawCharacterSources();
            EditorGUILayout.Space();
            DrawOptions();
            EditorGUILayout.Space();
            DrawActions();
            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Adds one font asset unless it is already listed.</summary>
        /// <param name="fontAsset">The font asset to add.</param>
        /// <returns>True when the asset was added.</returns>
        private bool AddFontAsset(TMP_FontAsset fontAsset)
        {
            if (_fontAssets.Contains(fontAsset))
            {
                return false;
            }
            _fontAssets.Add(fontAsset);
            return true;
        }

        /// <summary>Draws the font asset list, its drop area and per-asset warnings.</summary>
        private void DrawFontAssets()
        {
            EditorGUILayout.LabelField("Font Assets", EditorStyles.boldLabel);
            var dropArea = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop TMP font assets or folders here", EditorStyles.helpBox);
            HandleDragAndDrop(dropArea, objects => AddFontAssets(objects));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Selected"))
            {
                AddFontAssets(Selection.objects);
            }
            using (new EditorGUI.DisabledScope(_fontAssets.Count == 0))
            {
                if (GUILayout.Button("Clear"))
                {
                    _fontAssets.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();

            var removeIndex = -1;
            for (var i = 0; i < _fontAssets.Count; i++)
            {
                var fontAsset = _fontAssets[i];
                if (fontAsset == null)
                {
                    removeIndex = i;
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(fontAsset, typeof(TMP_FontAsset), false);
                }
                GUILayout.Label($"{fontAsset.atlasPopulationMode}, {fontAsset.characterTable.Count} chars", GUILayout.Width(160f));
                if (GUILayout.Button("X", GUILayout.Width(22f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
                DrawFontAssetWarning(fontAsset);
            }
            if (removeIndex >= 0)
            {
                _fontAssets.RemoveAt(removeIndex);
            }
        }

        /// <summary>Explains how a static asset is handled and warns about population mode settings that defeat or limit prewarming.</summary>
        /// <param name="fontAsset">The listed font asset.</param>
        private void DrawFontAssetWarning(TMP_FontAsset fontAsset)
        {
            if (!TmpFontPrewarmer.IsDynamic(fontAsset))
            {
                EditorGUILayout.HelpBox(DescribeStaticHandling(fontAsset), TmpFontPrewarmer.GetSourceFont(fontAsset) == null && _options.IncludeStaticAssets
                                            ? MessageType.Warning
                                            : MessageType.Info);
                return;
            }
            if (!TmpFontPrewarmer.GetClearDynamicDataOnBuild(fontAsset))
            {
                return;
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox("Clear Dynamic Data On Build is on: TMP removes prewarmed glyphs before every build and when the Editor quits.", MessageType.Warning);
            if (GUILayout.Button("Turn Off", GUILayout.Width(70f), GUILayout.Height(38f)))
            {
                TmpFontPrewarmer.SetClearDynamicDataOnBuild(fontAsset, false);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Describes what a run does to a static asset with the current options.</summary>
        /// <param name="fontAsset">The static font asset.</param>
        /// <returns>The help text shown under the asset.</returns>
        internal string DescribeStaticHandling(TMP_FontAsset fontAsset)
        {
            if (!_options.IncludeStaticAssets)
            {
                return "Static: baking into static assets is off, so no glyphs are added to this asset. Its fallbacks are still prewarmed.";
            }
            if (TmpFontPrewarmer.GetSourceFont(fontAsset) == null)
            {
                return "Static without a source font reference: characters cannot be baked into it. Its fallbacks are still prewarmed.";
            }
            var growth = fontAsset.isMultiAtlasTexturesEnabled
                ? "new atlas pages are added when it is full"
                : _options.GrowStaticAtlases
                    ? $"the atlas grows up to {_options.MaxStaticAtlasSize} when full"
                    : "characters that do not fit go to its fallbacks";
            return $"Static {fontAsset.atlasWidth}x{fontAsset.atlasHeight}: characters its source font has are baked into the atlas " +
                   $"({growth}); it stays static with a non-readable atlas.";
        }

        /// <summary>Draws the literal characters, character set files and Unicode range inputs.</summary>
        private void DrawCharacterSources()
        {
            EditorGUILayout.LabelField("Characters", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var characters = EditorGUILayout.TextArea(_characters, GUILayout.MinHeight(48f));
            if (EditorGUI.EndChangeCheck())
            {
                Characters = characters;
            }

            EditorGUILayout.LabelField("Character Set Files", EditorStyles.boldLabel);
            var dropArea = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop text files here (their characters are added literally)", EditorStyles.helpBox);
            HandleDragAndDrop(dropArea, objects => AddCharacterFiles(objects));
            var removeIndex = -1;
            for (var i = 0; i < _characterFiles.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var file = (TextAsset?)EditorGUILayout.ObjectField(_characterFiles[i], typeof(TextAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    _characterFiles[i] = file;
                    _isCodePointsDirty = true;
                }
                if (GUILayout.Button("X", GUILayout.Width(22f)))
                {
                    removeIndex = i;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (removeIndex >= 0)
            {
                _characterFiles.RemoveAt(removeIndex);
                _isCodePointsDirty = true;
            }
            if (GUILayout.Button("Add File Slot"))
            {
                _characterFiles.Add(null);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Unicode Ranges", EditorStyles.boldLabel);
            if (EditorGUILayout.DropdownButton(new GUIContent("Presets"), FocusType.Passive, GUILayout.Width(80f)))
            {
                ShowPresetMenu();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Hexadecimal, e.g. 0020-007E, U+3042, 0x4E00-0x9FFF", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            var ranges = EditorGUILayout.TextArea(_unicodeRanges, GUILayout.MinHeight(36f));
            if (EditorGUI.EndChangeCheck())
            {
                UnicodeRanges = ranges;
            }

            RefreshCodePoints();
            if (_parseError is not null)
            {
                EditorGUILayout.HelpBox(_parseError, MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox($"{_codePoints.Count} distinct code points.", MessageType.None);
            }
        }

        /// <summary>Shows the Unicode range presets; choosing one appends its ranges.</summary>
        private void ShowPresetMenu()
        {
            var menu = new GenericMenu();
            foreach (var preset in TmpCharacterSetParser.Presets)
            {
                var ranges = preset.Ranges;
                menu.AddItem(new GUIContent($"{preset.Name} ({preset.Ranges})"), false, () =>
                {
                    UnicodeRanges = string.IsNullOrWhiteSpace(_unicodeRanges) ? ranges : $"{_unicodeRanges.TrimEnd()}, {ranges}";
                    GUI.FocusControl(null);
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        /// <summary>Draws the prewarm options.</summary>
        private void DrawOptions()
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            _options.IncludeFallbacks = EditorGUILayout.ToggleLeft(
                new GUIContent("Include fallback font assets", "Characters missing from a font's source file go to its fallbacks, in TMP's lookup order."),
                _options.IncludeFallbacks);
            _options.IncludeFontFeatures = EditorGUILayout.ToggleLeft(
                new GUIContent("Include font features", "Reads ligature and kerning data for the new glyphs. Slower; rarely needed for CJK."),
                _options.IncludeFontFeatures);
            _options.IncludeStaticAssets = EditorGUILayout.ToggleLeft(
                new GUIContent("Bake into static font assets",
                               "Static assets in the chain receive the characters their source font has, then stay static with a non-readable atlas. " +
                               "Their own font feature setting is used so the kerning stays consistent."),
                _options.IncludeStaticAssets);
            using (new EditorGUI.DisabledScope(!_options.IncludeStaticAssets))
            {
                EditorGUI.indentLevel++;
                _options.GrowStaticAtlases = EditorGUILayout.ToggleLeft(
                    new GUIContent("Grow full static atlases",
                                   "Rebuilds a full single-atlas static asset at the next power-of-two size with its existing characters plus the new ones, " +
                                   "and updates the atlas size on its material presets."),
                    _options.GrowStaticAtlases);
                using (new EditorGUI.DisabledScope(!_options.GrowStaticAtlases))
                {
                    _options.MaxStaticAtlasSize = EditorGUILayout.IntPopup(
                        new GUIContent("Max static atlas size", "The largest width or height a static atlas may grow to."),
                        _options.MaxStaticAtlasSize,
                        MaxStaticAtlasSizeLabels,
                        MaxStaticAtlasSizes);
                }
                EditorGUI.indentLevel--;
            }
            _options.BatchSize = Mathf.Clamp(EditorGUILayout.IntField(
                new GUIContent("Batch size", "Characters per TryAddCharacters call."),
                _options.BatchSize), 1, 4096);
        }

        /// <summary>Draws the prewarm button and runs it when clicked.</summary>
        private void DrawActions()
        {
            RefreshCodePoints();
            var canRun = _fontAssets.Count > 0 && _codePoints.Count > 0 && _parseError is null;
            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("Prewarm", GUILayout.Height(28f)))
                {
                    RunPrewarm();
                }
            }
        }

        /// <summary>Draws the summaries of the last run.</summary>
        private void DrawResults()
        {
            if (_resultLines.Count == 0)
            {
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Last Run", EditorStyles.boldLabel);
            foreach (var line in _resultLines)
            {
                EditorGUILayout.HelpBox(line, MessageType.None);
            }
        }

        /// <summary>Asks for confirmation, then prewarms every listed font asset with a cancelable progress bar.</summary>
        private void RunPrewarm()
        {
            var fallbackNote = _options.IncludeFallbacks ? " and their fallbacks" : string.Empty;
            var staticNote = _options.IncludeStaticAssets
                ? _options.GrowStaticAtlases
                    ? " Static assets are baked in place and their atlases may be rebuilt at a larger size."
                    : " Static assets are baked in place."
                : string.Empty;
            var message = $"Add up to {_codePoints.Count} characters to {_fontAssets.Count} font asset(s){fallbackNote}?{staticNote}\n\n" +
                          "Modified assets are saved to disk and the change cannot be undone.";
            if (!EditorUtility.DisplayDialog(WindowTitle, message, "Prewarm", "Cancel"))
            {
                return;
            }
            _resultLines.Clear();
            try
            {
                var fontAssets = _fontAssets.ToArray();
                for (var i = 0; i < fontAssets.Length; i++)
                {
                    var fontAsset = fontAssets[i];
                    if (fontAsset == null)
                    {
                        continue;
                    }
                    var assetIndex = i;
                    var result = TmpFontPrewarmer.Prewarm(fontAsset, _codePoints, _options, (progress, info) =>
                    {
                        return EditorUtility.DisplayCancelableProgressBar(WindowTitle,
                                                                          $"[{assetIndex + 1}/{fontAssets.Length}] {info}",
                                                                          (assetIndex + progress) / fontAssets.Length);
                    });
                    var line = Describe(result);
                    _resultLines.Add(line);
                    Debug.Log($"[{nameof(TmpFontPrewarmWindow)}] {line}", fontAsset);
                    if (result.IsCanceled)
                    {
                        break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Rebuilds the code point set from all sources when an input changed.</summary>
        private void RefreshCodePoints()
        {
            if (!_isCodePointsDirty)
            {
                return;
            }
            _isCodePointsDirty = false;
            _parseError = null;
            var codePoints = new SortedSet<uint>();
            TmpCharacterSetParser.AddCharacters(_characters, codePoints);
            foreach (var file in _characterFiles)
            {
                if (file != null)
                {
                    TmpCharacterSetParser.AddCharacters(file.text, codePoints);
                }
            }
            try
            {
                TmpCharacterSetParser.AddUnicodeRanges(_unicodeRanges, codePoints);
            }
            catch (FormatException e)
            {
                _parseError = e.Message;
            }
            _codePoints = codePoints;
        }

        /// <summary>Calls the handler with the objects dropped on an area and shows the copy cursor while dragging over it.</summary>
        /// <param name="area">The drop area.</param>
        /// <param name="onDrop">Receives the dropped objects.</param>
        private static void HandleDragAndDrop(Rect area, Action<Object[]> onDrop)
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return;
            }
            if (!area.Contains(currentEvent.mousePosition))
            {
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                onDrop(DragAndDrop.objectReferences);
            }
            currentEvent.Use();
        }

        /// <summary>Formats a prewarm result as one summary line.</summary>
        /// <param name="result">The result to describe.</param>
        /// <returns>The summary, listing up to <see cref="UnsupportedSampleCount"/> unsupported code points.</returns>
        internal static string Describe(TmpFontPrewarmResult result)
        {
            var builder = new StringBuilder();
            builder.Append($"{result.FontAsset.name}: requested {result.RequestedCount}, already present {result.AlreadyPresentCount}, added {result.AddedCount}");
            if (result.AddedPerAsset.Count > 0)
            {
                builder.Append(" (");
                var isFirst = true;
                foreach (var pair in result.AddedPerAsset)
                {
                    if (!isFirst)
                    {
                        builder.Append(", ");
                    }
                    builder.Append($"{pair.Key.name}: {pair.Value}");
                    isFirst = false;
                }
                builder.Append(')');
            }
            foreach (var pair in result.GrownAtlasSizes)
            {
                builder.Append($", grew atlas {pair.Key.name}: {pair.Value.x}x{pair.Value.y}");
            }
            foreach (var asset in result.StaticAssetsWithoutSourceFont)
            {
                builder.Append($", static without source font: {asset.name}");
            }
            if (result.UnsupportedCodePoints.Count > 0)
            {
                builder.Append($", unsupported {result.UnsupportedCodePoints.Count} [");
                var sampleCount = Math.Min(UnsupportedSampleCount, result.UnsupportedCodePoints.Count);
                for (var i = 0; i < sampleCount; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(' ');
                    }
                    builder.Append($"U+{result.UnsupportedCodePoints[i]:X4}");
                }
                if (sampleCount < result.UnsupportedCodePoints.Count)
                {
                    builder.Append(" ...");
                }
                builder.Append(']');
            }
            if (result.IsCanceled)
            {
                builder.Append($", canceled with {result.UnprocessedCount} unprocessed");
            }
            return builder.ToString();
        }
    }
}

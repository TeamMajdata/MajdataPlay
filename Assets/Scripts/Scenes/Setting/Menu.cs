using LitMotion;
using MajdataPlay.Editor;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    public class Menu : MonoBehaviour
    {
        private const float OPTION_MOVE_DURATION = 0.18f;
        private const int VISIBLE_OPTION_RADIUS = 1;
        private const int RENDERING_ADVANCED_CLICK_COUNT = 10;
        // Three resting views plus one incoming/outgoing view during a slide.
        internal const int OPTION_POOL_CAPACITY = (VISIBLE_OPTION_RADIUS * 2) + 2;
        private static readonly Vector3 s_unselectedOptionScale = Vector3.one * 0.6f;
        private static readonly Color s_selectedOptionColor = new(0.8823529f, 0.8078431f, 0.6392157f, 1f);
        private static readonly Color s_unselectedOptionColor = new(0.3607843f, 0.3098039f, 0.2862745f, 1f);

        [field: SerializeField, ReadOnlyField]
        public string Name { get; set; } = string.Empty;

        [field: SerializeField, ReadOnlyField]
        public int SelectedIndex { get; private set; }
        /// <summary>
        /// Option对象<para>e.g. GameSetting.Game</para>
        /// </summary>
        public object Instance { get; set; } = null!;
        /// <summary>
        /// Gets or sets how the menu title is localized. Without one the default <c>MAJSETTING_CATEGORY_{Name}</c> key is used.
        /// </summary>
        internal SettingTranslation? TitleTranslation { get; set; }
        /// <summary>
        /// Gets or sets when the menu itself is listed; <see cref="SettingManager"/> evaluates it against the setting root.
        /// </summary>
        internal SettingVisibility Visibility { get; set; } = SettingVisibility.Always;

        [SerializeField]
        [FormerlySerializedAs("optionPrefab")]
        private GameObject _optionPrefab = null!;

        private SettingManager _manager = null!;

        [SerializeField, ReadOnlyField]
        private float _listCursorPos = 0;

        private PropertyInfo[] _properties = Array.Empty<PropertyInfo>();
        private OptionData[] _optionData = Array.Empty<OptionData>();
        private SettingVisibility[] _optionVisibilities = Array.Empty<SettingVisibility>();
        // The ShowInSettingUIWhen result of each property, as of the last evaluation.
        private bool[] _optionConditionsMet = Array.Empty<bool>();
        private int[] _displayedPropertyIndices = Array.Empty<int>();
        private int _displayedOptionCount;
        private RenderingOptions? _renderingOptions;
        // The rendering preset as of the last visibility update; changing it restarts the hidden gesture.
        private RenderingQualityOption _renderingQuality;
        private int _renderingAdvancedClickCount;
        private readonly Option?[] _visibleOptions = new Option?[OPTION_POOL_CAPACITY];
        private readonly int[] _visibleIndices = { -1, -1, -1, -1 };

        private MotionHandle _optionAnim;
        private bool _isInitialized = false;
        private bool _needsLayout;

        private readonly SettingConfig _settingConfig = MajEnv.RuntimeConfig?.Setting ?? new();
        private void Awake()
        {
            _manager = FindAnyObjectByType<SettingManager>();
        }
        public void Init()
        {
            if (_isInitialized)
            {
                return;
            }

            _properties = SettingReflectionCache.GetVisibleProperties(Instance.GetType());
            _optionData = new OptionData[_properties.Length];
            _optionVisibilities = new SettingVisibility[_properties.Length];
            _optionConditionsMet = new bool[_properties.Length];
            // Prepare every model while entering Setting, never inside a scrolling callback.
            for (var i = 0; i < _properties.Length; i++)
            {
                _optionData[i] = new OptionData(_properties[i], Instance);
                _optionVisibilities[i] = SettingReflectionCache.GetVisibility(_properties[i]);
            }
            _displayedPropertyIndices = new int[_properties.Length];
            _renderingOptions = Instance as RenderingOptions;
            UpdateOptionVisibility(true);
            _manager.InitializeOptionPool(_optionPrefab);
            _isInitialized = true;
        }
        private void OnEnable()
        {
            // Do not reparent pooled children while Unity is activating this hierarchy.
            _needsLayout = _isInitialized && FindVisibleOption(SelectedIndex) is null;
        }
        private void LateUpdate()
        {
            // Options edited during Update show or hide their dependent options before this frame renders.
            UpdateOptionVisibility();
            if (_needsLayout)
            {
                SnapDisplayerTo(SelectedIndex);
            }
        }
        private void OnDisable()
        {
            ResetRenderingAdvancedInput();
            _needsLayout = false;
            _optionAnim.TryCancel();
            ReleaseVisibleOptions();
        }
        private void OnDestroy()
        {
            _optionAnim.TryCancel();
            foreach (var data in _optionData)
            {
                data?.Dispose();
            }
        }
        internal void SwitchOption(int direction)
        {
            if (!_isInitialized)
            {
                return;
            }
            MoveOption(direction);
        }
        internal void HandleInput()
        {
            if (!_isInitialized || _displayedOptionCount == 0)
            {
                return;
            }

            if (UpdateOptionVisibility())
            {
                SnapDisplayerTo(SelectedIndex);
            }
            FindVisibleOption(SelectedIndex)?.HandleInput();
            // An edited option, such as the rendering compatibility switch, shows or hides its dependent options at once.
            if (UpdateOptionVisibility())
            {
                SnapDisplayerTo(SelectedIndex);
            }
        }
        /// <summary>A8 on the rendering quality row belongs to the advanced-settings gesture.</summary>
        internal bool TryHandleRenderingAdvancedInput(bool clicked)
        {
            if (!_isInitialized || _renderingOptions is null || !isActiveAndEnabled)
            {
                ResetRenderingAdvancedInput();
                return false;
            }
            if (UpdateOptionVisibility())
            {
                SnapDisplayerTo(SelectedIndex);
            }
            if (_renderingOptions.CompatibilityMode || _displayedOptionCount == 0 ||
                GetDisplayedProperty(SelectedIndex).Name != nameof(RenderingOptions.RenderQuality))
            {
                ResetRenderingAdvancedInput();
                return false;
            }

            // Consume A8 on this row even after unlocking, without repeatedly applying or saving.
            if (!clicked || _renderingOptions.RenderQuality == RenderingQualityOption.Custom)
            {
                return true;
            }
            _renderingAdvancedClickCount++;
            if (_renderingAdvancedClickCount < RENDERING_ADVANCED_CLICK_COUNT)
            {
                return true;
            }

            ResetRenderingAdvancedInput();
            if (_renderingOptions.EnterCustomMode())
            {
                GameManager.ApplyRenderingSettings();
                GameManager.RequestSave(this);
                UpdateOptionVisibility();
                SnapDisplayerTo(SelectedIndex);
            }
            return true;
        }

        internal void ResetRenderingAdvancedInput() => _renderingAdvancedClickCount = 0;
        internal void RefreshVisibleEnumerators()
        {
            if (!_isInitialized)
            {
                return;
            }

            foreach (var option in _visibleOptions)
            {
                option?.RefreshEnumerator();
            }
        }
        internal void CollectWarmupText(SettingFontWarmup warmup, IEnumerable<TMP_FontAsset> optionFonts, TMP_FontAsset descriptionFont)
        {
            foreach (var data in _optionData)
            {
                data.CollectWarmupText(warmup, optionFonts, descriptionFont);
            }
        }
        /// <summary>
        /// Localizes the menu title for the current language.
        /// </summary>
        /// <returns>The translated title, or its primary key when no translation exists.</returns>
        internal string GetLocalizedTitle()
        {
            TitleTranslation ??= SettingReflectionCache.GetMenuTranslation(null, Name);
            return TitleTranslation.Localize(Name);
        }
        private void MoveOption(int direction)
        {
            UpdateOptionVisibility();
            var targetIndex = SelectedIndex + direction;
            if (targetIndex < 0)
            {
                _manager.PreviousMenu();
                return;
            }
            if (targetIndex >= _displayedOptionCount)
            {
                _manager.NextMenu();
                return;
            }

            SelectOption(targetIndex, true);
        }

        private void DisplayerMoveTo(float targetPos, float duration)
        {
            _optionAnim.TryCancel();
            // Keep the selected view in the bounded window even after rapid repeated input.
            _listCursorPos = Mathf.Clamp(_listCursorPos, targetPos - 1, targetPos + 1);
            UpdateDisplayerPosition();
            _optionAnim = LMotion.Create(_listCursorPos, targetPos, duration)
                                     .WithScheduler(MotionScheduler.PostLateUpdate)
                                     .WithEase(Ease.OutQuad)
                                     .WithOnComplete(CompleteOptionMove)
                                     .Bind(this, static (x, menu) =>
                                     {
                                         menu._listCursorPos = x;
                                         menu.UpdateDisplayerPosition();
                                     });
        }
        private void CompleteOptionMove()
        {
            _listCursorPos = SelectedIndex;
            UpdateDisplayerPosition();
        }
        private void UpdateDisplayerPosition()
        {
            _needsLayout = false;
            if (_displayedOptionCount == 0)
            {
                _manager.SetDescriptionText(string.Empty);
                return;
            }

            var visibleStart = Mathf.Max(0, Mathf.FloorToInt(_listCursorPos) - VISIBLE_OPTION_RADIUS);
            var visibleEnd = Mathf.Min(_displayedOptionCount - 1, Mathf.CeilToInt(_listCursorPos) + VISIBLE_OPTION_RADIUS);
            // Release first, so every incoming view can reuse an outgoing view.
            for (var slot = 0; slot < _visibleOptions.Length; slot++)
            {
                var index = _visibleIndices[slot];
                if (index >= 0 && (index < visibleStart || index > visibleEnd))
                {
                    ReleaseOption(slot);
                }
            }

            for (var i = visibleStart; i <= visibleEnd; i++)
            {
                var distance = i - _listCursorPos;
                var optionDisplayer = EnsureOption(i);
                optionDisplayer.SetSelected(i == SelectedIndex);
                optionDisplayer.transform.localPosition = GetOptionTransformPosition(distance);
                optionDisplayer.transform.localScale = GetOptionTransformScale(distance);
                optionDisplayer.SetTextColor(GetOptionTextColor(distance));
                if (!optionDisplayer.gameObject.activeSelf)
                {
                    optionDisplayer.gameObject.SetActive(true);
                }
            }
        }
        private Option? FindVisibleOption(int index)
        {
            for (var slot = 0; slot < _visibleOptions.Length; slot++)
            {
                if (_visibleIndices[slot] == index)
                {
                    return _visibleOptions[slot];
                }
            }
            return null;
        }

        internal void ToTail()
        {
            if (!_isInitialized || _displayedOptionCount == 0)
            {
                return;
            }
            UpdateOptionVisibility();
            SelectOption(_displayedOptionCount - 1, false);
        }
        internal void ToHead()
        {
            if (!_isInitialized || _displayedOptionCount == 0)
            {
                return;
            }
            UpdateOptionVisibility();
            SelectOption(0, false);
        }

        internal void ToOption(string optionName)
        {
            if (!_isInitialized || _displayedOptionCount == 0)
            {
                return;
            }
            UpdateOptionVisibility();
            var index = 0;
            if (!string.IsNullOrEmpty(optionName))
            {
                for (var i = 0; i < _displayedOptionCount; i++)
                {
                    if (GetDisplayedProperty(i).Name == optionName)
                    {
                        index = i;
                        break;
                    }
                }
            }
            SelectOption(index, false);
        }
        private void SelectOption(int index, bool useAnimation)
        {
            var nextIndex = index.Clamp(0, _displayedOptionCount - 1);
            if (SelectedIndex != nextIndex)
            {
                ResetRenderingAdvancedInput();
            }
            SelectedIndex = nextIndex;
            _settingConfig.SelectedOption = GetDisplayedProperty(SelectedIndex).Name;
            if (useAnimation)
            {
                DisplayerMoveTo(SelectedIndex, OPTION_MOVE_DURATION);
            }
            else
            {
                SnapDisplayerTo(SelectedIndex);
            }
        }
        private Option EnsureOption(int index)
        {
            var option = FindVisibleOption(index);
            if (option is not null)
            {
                return option;
            }

            var slot = Array.IndexOf(_visibleIndices, -1);
            var data = _optionData[_displayedPropertyIndices[index]];
            option = _manager.RentOption(transform);
            _visibleIndices[slot] = index;
            _visibleOptions[slot] = option;
            option.Bind(_manager, data);
            return option;
        }
        private void ReleaseOption(int slot)
        {
            var option = _visibleOptions[slot];
            _visibleOptions[slot] = null;
            _visibleIndices[slot] = -1;
            if (option != null && _manager != null)
            {
                _manager.ReturnOption(option);
            }
        }
        /// <summary>
        /// Stops the option slide and returns every option view of this menu to the shared pool.
        /// </summary>
        /// <remarks>
        /// <see cref="SettingManager"/> calls it for menus it deselects: an inactive menu can still hold views, and
        /// deactivating it again does not run <see cref="OnDisable"/>.
        /// </remarks>
        internal void ReleaseVisibleOptions()
        {
            _optionAnim.TryCancel();
            for (var slot = 0; slot < _visibleOptions.Length; slot++)
            {
                ReleaseOption(slot);
            }
        }
        private void SnapDisplayerTo(float targetPos)
        {
            _optionAnim.TryCancel();
            _listCursorPos = targetPos;
            UpdateDisplayerPosition();
        }

        private PropertyInfo GetDisplayedProperty(int index) => _properties[_displayedPropertyIndices[index]];

        /// <summary>
        /// Re-evaluates the <see cref="ShowInSettingUIWhenAttribute"/> conditions and rebuilds the displayed option list when any of them changed.
        /// </summary>
        /// <remarks>
        /// The selected option stays selected while it is displayed; otherwise the first displayed option is selected.
        /// Rendering options declare their preset, custom and compatibility visibility with the same attribute.
        /// </remarks>
        /// <param name="force">Rebuilds the list even when no condition changed, as during initialization.</param>
        /// <returns>True when the displayed list was rebuilt.</returns>
        private bool UpdateOptionVisibility(bool force = false)
        {
            if (_renderingOptions is not null && _renderingQuality != _renderingOptions.RenderQuality)
            {
                ResetRenderingAdvancedInput();
                _renderingQuality = _renderingOptions.RenderQuality;
            }
            var conditionsChanged = UpdateOptionConditions();
            if (!conditionsChanged && !force)
            {
                return false;
            }

            var selectedProperty = _displayedOptionCount > 0
                ? GetDisplayedProperty(SelectedIndex).Name
                : string.Empty;
            ResetRenderingAdvancedInput();
            _optionAnim.TryCancel();
            // Models and enumerators stay alive, including hidden labels needed for font warmup.
            // Rebuild only when a ShowInSettingUIWhen condition changes.
            ReleaseVisibleOptions();
            _displayedOptionCount = 0;
            for (var i = 0; i < _properties.Length; i++)
            {
                if (_optionConditionsMet[i])
                {
                    _displayedPropertyIndices[_displayedOptionCount++] = i;
                }
            }

            SelectedIndex = 0;
            for (var i = 0; i < _displayedOptionCount; i++)
            {
                if (GetDisplayedProperty(i).Name == selectedProperty)
                {
                    SelectedIndex = i;
                    break;
                }
            }
            _listCursorPos = SelectedIndex;
            _needsLayout = _isInitialized && isActiveAndEnabled;
            if (_needsLayout && _displayedOptionCount > 0)
            {
                _settingConfig.SelectedOption = GetDisplayedProperty(SelectedIndex).Name;
            }
            return true;
        }

        /// <summary>
        /// Re-evaluates the <see cref="ShowInSettingUIWhenAttribute"/> conditions of every option against the menu instance.
        /// </summary>
        /// <remarks>Options without conditions are not read, so only conditional options cost a property read per call.</remarks>
        /// <returns>True when any option became shown or hidden since the last evaluation.</returns>
        private bool UpdateOptionConditions()
        {
            var hasChanged = false;
            for (var i = 0; i < _optionVisibilities.Length; i++)
            {
                var isMet = _optionVisibilities[i].IsVisible(Instance);
                if (_optionConditionsMet[i] != isMet)
                {
                    _optionConditionsMet[i] = isMet;
                    hasChanged = true;
                }
            }
            return hasChanged;
        }

        private static Vector3 GetOptionTransformScale(float diff)
        {
            return Vector3.Lerp(Vector3.one, s_unselectedOptionScale, Mathf.Clamp01(Mathf.Abs(diff)));
        }
        private static Vector3 GetOptionTransformPosition(float diff)
        {
            return new Vector3(380 * diff, -220, 0);
        }
        private static Color GetOptionTextColor(float diff)
        {
            return Color.Lerp(s_selectedOptionColor, s_unselectedOptionColor, Mathf.Clamp01(Mathf.Abs(diff)));
        }
    }
}

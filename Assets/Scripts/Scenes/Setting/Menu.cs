using LitMotion;
using MajdataPlay.Editor;
using MajdataPlay.Numerics;
using MajdataPlay.Settings;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Utils;
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
        const float OPTION_MOVE_DURATION = 0.18f;
        const int VISIBLE_OPTION_RADIUS = 1;
        // Three resting views plus one incoming/outgoing view during a slide.
        internal const int OPTION_POOL_CAPACITY = VISIBLE_OPTION_RADIUS * 2 + 2;
        static readonly Vector3 UNSELECTED_OPTION_SCALE = Vector3.one * 0.6f;
        static readonly Color SELECTED_OPTION_COLOR = new(0.8823529f, 0.8078431f, 0.6392157f, 1f);
        static readonly Color UNSELECTED_OPTION_COLOR = new(0.3607843f, 0.3098039f, 0.2862745f, 1f);

        [field: SerializeField, ReadOnlyField]
        public string Name { get; set; } = string.Empty;

        [field: SerializeField, ReadOnlyField]
        public int SelectedIndex { get; private set; }
        /// <summary>
        /// Option对象<para>e.g. GameSetting.Game</para>
        /// </summary>
        public object Instance { get; set; } = null!;

        [SerializeField]
        [FormerlySerializedAs("optionPrefab")]
        GameObject _optionPrefab = null!;

        SettingManager _manager = null!;

        [SerializeField, ReadOnlyField]
        float _listCursorPos = 0;

        PropertyInfo[] _properties = Array.Empty<PropertyInfo>();
        OptionData[] _optionData = Array.Empty<OptionData>();
        readonly Option?[] _visibleOptions = new Option?[OPTION_POOL_CAPACITY];
        readonly int[] _visibleIndices = { -1, -1, -1, -1 };

        MotionHandle _optionAnim;
        bool _isInitialized = false;
        bool _needsLayout;

        readonly SettingConfig _settingConfig = MajEnv.RuntimeConfig?.Setting ?? new();
        void Awake()
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
            // Prepare every model while entering Setting, never inside a scrolling callback.
            for (var i = 0; i < _properties.Length; i++)
            {
                _optionData[i] = new OptionData(_properties[i], Instance);
            }
            _manager.InitializeOptionPool(_optionPrefab);
            _isInitialized = true;
        }
        void OnEnable()
        {
            // Do not reparent pooled children while Unity is activating this hierarchy.
            _needsLayout = _isInitialized && FindVisibleOption(SelectedIndex) is null;
        }
        void LateUpdate()
        {
            if (_needsLayout)
            {
                SnapDisplayerTo(SelectedIndex);
            }
        }
        void OnDisable()
        {
            _needsLayout = false;
            _optionAnim.TryCancel();
            ReleaseVisibleOptions();
        }
        void OnDestroy()
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
            if (!_isInitialized || _properties.Length == 0)
            {
                return;
            }

            FindVisibleOption(SelectedIndex)?.HandleInput();
        }
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
        void MoveOption(int direction)
        {
            var targetIndex = SelectedIndex + direction;
            if (targetIndex < 0)
            {
                _manager.PreviousMenu();
                return;
            }
            if (targetIndex >= _properties.Length)
            {
                _manager.NextMenu();
                return;
            }

            SelectOption(targetIndex, true);
        }

        void DisplayerMoveTo(float targetPos, float duration)
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
        void CompleteOptionMove()
        {
            _listCursorPos = SelectedIndex;
            UpdateDisplayerPosition();
        }
        void UpdateDisplayerPosition()
        {
            _needsLayout = false;
            if (_properties.Length == 0)
            {
                _manager.SetDescriptionText(string.Empty);
                return;
            }

            var visibleStart = Mathf.Max(0, Mathf.FloorToInt(_listCursorPos) - VISIBLE_OPTION_RADIUS);
            var visibleEnd = Mathf.Min(_properties.Length - 1, Mathf.CeilToInt(_listCursorPos) + VISIBLE_OPTION_RADIUS);
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
        Option? FindVisibleOption(int index)
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
            if (!_isInitialized || _properties.Length == 0)
            {
                return;
            }
            SelectOption(_properties.Length - 1, false);
        }
        internal void ToHead()
        {
            if (!_isInitialized || _properties.Length == 0)
            {
                return;
            }
            SelectOption(0, false);
        }

        internal void ToOption(string optionName)
        {
            if (!_isInitialized || _properties.Length == 0)
            {
                return;
            }
            var index = string.IsNullOrEmpty(optionName)
                ? 0
                : Array.FindIndex(_properties, x => x.Name == optionName);
            SelectOption(index, false);
        }
        void SelectOption(int index, bool useAnimation)
        {
            SelectedIndex = index.Clamp(0, _properties.Length - 1);
            _settingConfig.SelectedOption = _properties[SelectedIndex].Name;
            if (useAnimation)
            {
                DisplayerMoveTo(SelectedIndex, OPTION_MOVE_DURATION);
            }
            else
            {
                SnapDisplayerTo(SelectedIndex);
            }
        }
        Option EnsureOption(int index)
        {
            var option = FindVisibleOption(index);
            if (option is not null)
            {
                return option;
            }

            var slot = Array.IndexOf(_visibleIndices, -1);
            var data = _optionData[index];
            option = _manager.RentOption(transform);
            _visibleIndices[slot] = index;
            _visibleOptions[slot] = option;
            option.Bind(_manager, data);
            return option;
        }
        void ReleaseOption(int slot)
        {
            var option = _visibleOptions[slot];
            _visibleOptions[slot] = null;
            _visibleIndices[slot] = -1;
            if (option != null && _manager != null)
            {
                _manager.ReturnOption(option);
            }
        }
        void ReleaseVisibleOptions()
        {
            for (var slot = 0; slot < _visibleOptions.Length; slot++)
            {
                ReleaseOption(slot);
            }
        }
        void SnapDisplayerTo(float targetPos)
        {
            _optionAnim.TryCancel();
            _listCursorPos = targetPos;
            UpdateDisplayerPosition();
        }

        static Vector3 GetOptionTransformScale(float diff)
        {
            return Vector3.Lerp(Vector3.one, UNSELECTED_OPTION_SCALE, Mathf.Clamp01(Mathf.Abs(diff)));
        }
        static Vector3 GetOptionTransformPosition(float diff)
        {
            return new Vector3(380 * diff, -220, 0);
        }
        static Color GetOptionTextColor(float diff)
        {
            return Color.Lerp(SELECTED_OPTION_COLOR, UNSELECTED_OPTION_COLOR, Mathf.Clamp01(Mathf.Abs(diff)));
        }
    }
}

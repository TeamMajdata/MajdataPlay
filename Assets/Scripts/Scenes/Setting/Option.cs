using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
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
    public class Option : MonoBehaviour
    {
        [SerializeField]
        [FormerlySerializedAs("nameText")]
        TextMeshProUGUI _nameTextDisplayer = null!;

        [SerializeField]
        [FormerlySerializedAs("valueText")]
        TextMeshProUGUI _valueTextDisplayer = null!;

        bool _isSelected;
        bool _isLocalizationSubscribed;
        string? _displayedName;
        string? _displayedValue;
        SettingTextTint? _nameTextTint;
        SettingTextTint? _valueTextTint;
        OptionData? _data;
        SettingManager _manager = null!;
        InputRepeatState _inputRepeat;
        OffsetUnitOption _descriptionOffsetUnit;

        internal void Bind(SettingManager manager, OptionData data)
        {
            Unbind();
            _manager = manager;
            _data = data;
            _nameTextTint ??= new SettingTextTint(_nameTextDisplayer);
            _valueTextTint ??= new SettingTextTint(_valueTextDisplayer);
            data.Refresh();
            data.RefreshLocalization();
            RefreshTexts();
            SubscribeLocalization();
        }

        internal void Unbind()
        {
            UnsubscribeLocalization();
            _isSelected = false;
            _inputRepeat.SuppressUntilRelease();
            _data = null;
        }

        internal void SetSelected(bool isSelected)
        {
            if (_isSelected == isSelected)
            {
                return;
            }
            _inputRepeat.SuppressUntilRelease();
            _isSelected = isSelected;
            if (isSelected)
            {
                SetDescriptionText();
            }
        }

        internal void SetTextColor(Color newColor)
        {
            _nameTextTint?.SetColor(newColor);
            _valueTextTint?.SetColor(newColor);
        }

        internal void CollectFontAssets(HashSet<TMP_FontAsset> fonts)
        {
            if (_nameTextDisplayer.font != null)
            {
                fonts.Add(_nameTextDisplayer.font);
            }
            if (_valueTextDisplayer.font != null)
            {
                fonts.Add(_valueTextDisplayer.font);
            }
        }

        internal void HandleInput()
        {
            if (!_isSelected || _data is null)
            {
                return;
            }

            // Synchronize before editing, too, so input starts from the latest setting.
            RefreshIfChanged();
            var moveNextPressed =
                InputManager.CheckSensorStatusInThisFrame(SensorArea.E4, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B4, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B3, SwitchStatus.On);
            var movePreviousPressed =
                InputManager.CheckSensorStatusInThisFrame(SensorArea.E6, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B5, SwitchStatus.On) ||
                InputManager.CheckSensorStatusInThisFrame(SensorArea.B6, SwitchStatus.On);

            if (_inputRepeat.Update(
                moveNextPressed,
                movePreviousPressed,
                MajTimeline.DeltaTime,
                0.4f,
                GetRepeatInterval(),
                out var direction))
            {
                var hasChanged = direction > 0 ? _data.MoveNext() : _data.MovePrevious();
                if (hasChanged)
                {
                    RefreshTexts();
                }
            }
        }

        internal void RefreshEnumerator()
        {
            if (_data is null)
            {
                return;
            }
            _data.Refresh();
            RefreshTexts();
            RefreshDescriptionUnit();
        }

        void LateUpdate()
        {
            RefreshIfChanged();
            RefreshDescriptionUnit();
        }

        void RefreshIfChanged()
        {
            if (_data is not null && _data.RefreshIfChanged())
            {
                RefreshTexts();
            }
        }

        void RefreshDescriptionUnit()
        {
            if (_isSelected && _data is not null && _data.IsOffsetOption &&
                _descriptionOffsetUnit != MajEnv.Settings.Debug.OffsetUnit)
            {
                SetDescriptionText();
            }
        }

        void OnLangChanged(object? sender, Language newLanguage)
        {
            if (_data is null)
            {
                return;
            }
            _data.RefreshLocalization();
            RefreshTexts();
            if (_isSelected)
            {
                SetDescriptionText();
            }
        }

        static float GetRepeatInterval()
        {
            var iterationSpeed = MajEnv.Settings.Debug.MenuOptionIterationSpeed;
            return 1f / (iterationSpeed is 0 ? 15 : iterationSpeed);
        }

        void SetDescriptionText()
        {
            if (_data is null)
            {
                return;
            }
            _descriptionOffsetUnit = MajEnv.Settings.Debug.OffsetUnit;
            if (!_data.HasDescription)
            {
                _manager.SetDescriptionText(string.Empty);
                return;
            }

            var description = _data.DescriptionKey.i18n();
            if (_data.IsOffsetOption)
            {
                description += $"\n{$"MAJTEXT_SETTING_OFFSETUNIT_{_descriptionOffsetUnit}".i18n()}";
            }
            _manager.SetDescriptionText(description);
        }

        void RefreshTexts()
        {
            if (_data is null)
            {
                return;
            }
            var name = _data.LocalizedName;
            if (_displayedName != name)
            {
                _nameTextDisplayer.text = name;
                _displayedName = name;
            }
            var value = _data.LocalizedValueText;
            if (_displayedValue != value)
            {
                _valueTextDisplayer.text = value;
                _displayedValue = value;
            }
        }

        void OnDestroy()
        {
            UnsubscribeLocalization();
            _nameTextTint?.Dispose();
            _valueTextTint?.Dispose();
        }

        void OnEnable()
        {
            if (_data is null)
            {
                return;
            }
            SubscribeLocalization();
            _data.RefreshIfChanged();
            _data.RefreshLocalization();
            RefreshTexts();
        }

        void OnDisable()
        {
            UnsubscribeLocalization();
            _inputRepeat.SuppressUntilRelease();
        }

        void SubscribeLocalization()
        {
            if (_isLocalizationSubscribed || _data is null || !isActiveAndEnabled)
            {
                return;
            }
            Localization.OnLanguageChanged += OnLangChanged;
            _isLocalizationSubscribed = true;
        }

        void UnsubscribeLocalization()
        {
            if (!_isLocalizationSubscribed)
            {
                return;
            }
            Localization.OnLanguageChanged -= OnLangChanged;
            _isLocalizationSubscribed = false;
        }
    }
}

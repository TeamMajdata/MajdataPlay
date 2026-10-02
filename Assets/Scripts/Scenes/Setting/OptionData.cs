using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.i18n;
using MajdataPlay.IO;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Utils;
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    // The menu owns this state so scrolling can reuse views without rebuilding enumerators.
    internal sealed class OptionData : IDisposable
    {
        internal PropertyInfo PropertyInfo { get; }
        internal bool HasDescription { get; }
        internal bool IsOffsetOption { get; }
        internal string DescriptionKey { get; }
        internal string LocalizedName { get; private set; }
        internal string LocalizedValueText => _enumerator.LocalizedValueText;

        readonly object _menuInstance;
        readonly string _nameKey;
        readonly IOptionEnumerator _enumerator;
        object? _lastValue;
        Language _language;

        internal OptionData(PropertyInfo propertyInfo, object menuInstance)
        {
            var metadata = SettingReflectionCache.GetOption(propertyInfo);
            PropertyInfo = propertyInfo;
            _menuInstance = menuInstance;
            HasDescription = metadata.HasDescription;
            _nameKey = metadata.NameKey;
            DescriptionKey = metadata.DescriptionKey;
            IsOffsetOption = metadata.IsOffsetOption;
            _enumerator = metadata.CreateEnumerator();
            _enumerator.Init(propertyInfo, menuInstance);
            _lastValue = propertyInfo.GetValue(menuInstance);
            _language = Localization.Current;
            LocalizedName = _nameKey.i18n();
        }

        internal bool RefreshIfChanged()
        {
            // Read the setting itself: another enumerator may have changed this property.
            var value = PropertyInfo.GetValue(_menuInstance);
            if (Equals(_lastValue, value))
            {
                return false;
            }

            Refresh();
            return true;
        }

        internal void Refresh()
        {
            _enumerator.Refresh();
            _lastValue = PropertyInfo.GetValue(_menuInstance);
        }

        internal void RefreshLocalization()
        {
            var language = Localization.Current;
            if (ReferenceEquals(_language, language))
            {
                return;
            }

            _enumerator.RefreshLocalization();
            LocalizedName = _nameKey.i18n();
            _language = language;
        }

        internal bool MoveNext()
        {
            return Move(true);
        }

        internal bool MovePrevious()
        {
            return Move(false);
        }

        bool Move(bool moveNext)
        {
            var hasChanged = moveNext ? _enumerator.MoveNext() : _enumerator.MovePrevious();
            if (hasChanged)
            {
                Refresh();
            }
            return hasChanged;
        }

        public void Dispose()
        {
            _enumerator.Dispose();
        }
    }
}

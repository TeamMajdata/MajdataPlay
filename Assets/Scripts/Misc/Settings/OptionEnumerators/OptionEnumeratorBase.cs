using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Reflection;
#nullable enable
namespace MajdataPlay.Settings.OptionEnumerators
{
    public abstract class OptionEnumeratorBase : IDisposable
    {
        protected const int FLAG_FIELD_MODE = 1;
        protected const int FLAG_PROPERTY_MODE = 2;

        public string Name { get; private set; } = string.Empty;
        public object? Current 
        { 
            get
            {
                if(OptionValues.Length == 0)
                {
                    return default;
                }
                return OptionValues[ValueIndex];
            }
        }
        public string ValueText 
        { 
            get
            {
                return ValueTexts[ValueIndex];
            }
        }
        public string LocalizedValueText 
        { 
            get
            {
                return LocalizedValueTexts[ValueIndex];
            }
        }
        // Setting preloads the finite value labels before navigation starts.
        internal IReadOnlyList<string> LocalizedValuesForWarmup => LocalizedValueTexts;
        protected int ModeFlag = 0;

        protected object Value
        {
            get
            {
                switch (ModeFlag)
                {
                    case FLAG_FIELD_MODE:
                        return FieldInfo!.GetValue(Target);
                    case FLAG_PROPERTY_MODE:
                        return PropertyInfo!.GetValue(Target);
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (ModeFlag)
                {
                    case FLAG_FIELD_MODE:
                        FieldInfo!.SetValue(Target, value);
                        break;
                    case FLAG_PROPERTY_MODE:
                        PropertyInfo!.SetValue(Target, value);
                        break;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }

        protected object Target = null!;

        protected Type Type
        {
            get
            {
                return _memberType;
            }
        }
        protected bool IsIntType { get; private set; }
        protected bool IsFloatType { get; private set; }
        protected bool IsOptional { get; private set; }
        protected bool IsReadOnly { get; set; }

        protected object[] OptionValues = Array.Empty<object>();
        protected string[] ValueTexts = Array.Empty<string>();
        protected string[] LocalizedValueTexts = Array.Empty<string>();
        protected int ValueIndex = 0;

        protected FieldInfo? FieldInfo;
        protected PropertyInfo? PropertyInfo;

        /// <summary>
        /// Gets how the value texts are localized, as selected by the translation attributes of the member.
        /// </summary>
        private protected SettingTranslation ValueTranslation { get; private set; } = SettingTranslation.DefaultOptionValue;

        /// <summary>
        /// Gets how the <c>UNSET</c> and <c>NULL</c> placeholders of a missing value are localized.
        /// </summary>
        /// <remarks>
        /// The placeholders are words rather than values of the member type, so they always fall back to the general table
        /// after the keys of the option (see <see cref="SettingTranslation.ResolvePlaceholder"/>).
        /// </remarks>
        private protected SettingTranslation PlaceholderTranslation { get; private set; } = SettingTranslation.DefaultOptionValue;

        /// <summary>
        /// Gets the values that <see cref="TranslateValuesAttribute"/> translates while the other option values stay untranslated.
        /// </summary>
        private protected SettingValueSelection SelectedValues { get; private set; } = SettingValueSelection.Empty;

        /// <summary>
        /// Gets the values that <see cref="DisableValuesAttribute"/> removes from the choices on the current platform.
        /// </summary>
        private protected SettingDisabledValues DisabledValues { get; private set; } = SettingDisabledValues.None;

        Type _memberType = null!;
        Attribute[] _attributes = Array.Empty<Attribute>();
        // True for numeric members including nullable ones; their values are only translated on explicit request.
        bool _hasNumericValues;
        // True for numeric members marked with FoldNumberAttribute; their untranslated values are shown folded.
        bool _foldsNumbers;

        public void Init(FieldInfo fieldInfo, object field)
        {
            if (fieldInfo is null)
            {
                throw new ArgumentNullException(nameof(fieldInfo));
            }
            if (field is null)
            {
                throw new ArgumentNullException(nameof(field));
            }
            FieldInfo = fieldInfo;
            Target = field;
            ModeFlag = FLAG_FIELD_MODE;
            _memberType = fieldInfo.FieldType;
            _attributes = SettingReflectionCache.GetAttributes(fieldInfo);
            IsFloatType = _memberType == typeof(float) || _memberType == typeof(double) ||
                          _memberType == typeof(decimal);
            IsIntType = _memberType == typeof(int) || _memberType == typeof(long) ||
                        _memberType == typeof(short) || _memberType == typeof(byte) ||
                        _memberType == typeof(uint) || _memberType == typeof(ulong) ||
                        _memberType == typeof(ushort) || _memberType == typeof(sbyte);
            IsOptional = GetCustomAttribute<OptionalAttribute>() != null;
            IsReadOnly = GetCustomAttribute<ReadOnlyOptionAttribute>() != null || FieldInfo.IsInitOnly;
            Name = fieldInfo.Name;
            InitTranslation();
            DisabledValues = SettingDisabledValues.Resolve(_attributes, Name, _memberType);
            InitInternal();
        }
        public void Init(PropertyInfo propertyInfo, object property)
        {
            if (propertyInfo is null)
            {
                throw new ArgumentNullException(nameof(propertyInfo));
            }
            if (property is null)
            {
                throw new ArgumentNullException(nameof(property));
            }
            PropertyInfo = propertyInfo;
            Target = property;
            ModeFlag = FLAG_PROPERTY_MODE;
            _memberType = propertyInfo.PropertyType;
            _attributes = SettingReflectionCache.GetAttributes(propertyInfo);
            IsFloatType = _memberType == typeof(float) || _memberType == typeof(double) ||
                          _memberType == typeof(decimal);
            IsIntType = _memberType == typeof(int) || _memberType == typeof(long) ||
                        _memberType == typeof(short) || _memberType == typeof(byte) ||
                        _memberType == typeof(uint) || _memberType == typeof(ulong) ||
                        _memberType == typeof(ushort) || _memberType == typeof(sbyte);
            IsOptional = GetCustomAttribute<OptionalAttribute>() != null;
            IsReadOnly = GetCustomAttribute<ReadOnlyOptionAttribute>() != null || !PropertyInfo.CanWrite;
            Name = propertyInfo.Name;
            InitTranslation();
            DisabledValues = SettingDisabledValues.Resolve(_attributes, Name, _memberType);
            InitInternal();
        }

        public virtual bool MoveNext()
        {
            if (IsReadOnly)
            {
                return false;
            }
            var nextIndex = ValueIndex + 1;
            if (nextIndex >= OptionValues.Length)
            {
                nextIndex = 0;
            }
            ValueIndex = nextIndex;
            var newValue = OptionValues[ValueIndex];
            Value = newValue;

            return true;
        }
        public virtual bool MovePrevious()
        {
            if (IsReadOnly)
            {
                return false;
            }
            var nextIndex = ValueIndex - 1;
            if (nextIndex < 0)
            {
                nextIndex = OptionValues.Length - 1;
            }
            ValueIndex = nextIndex;
            var newValue = OptionValues[ValueIndex];
            Value = newValue;

            return true;
        }
        public virtual void Refresh()
        {
            var value = Value;
            for (var i = 0; i < OptionValues.Length; i++)
            {
                if (Equals(OptionValues[i], value))
                {
                    ValueIndex = i;
                    return;
                }
            }
        }

        protected abstract void InitInternal();
        protected virtual void InitValueTexts()
        {
            ValueTexts = new string[OptionValues.Length];
            LocalizedValueTexts = new string[OptionValues.Length];
            for (int i = 0; i < OptionValues.Length; i++)
            {
                ValueTexts[i] = OptionValues[i]?.ToString() ??
                    (IsOptional ? "UNSET" : "NULL");
                LocalizedValueTexts[i] = LocalizeValueText(OptionValues[i], ValueTexts[i]);
            }
        }
        public virtual void RefreshLocalization()
        {
            for (var i = 0; i < OptionValues.Length; i++)
            {
                LocalizedValueTexts[i] = LocalizeValueText(OptionValues[i], ValueTexts[i]);
            }
        }
        /// <summary>
        /// Localizes the display text of one option value according to <see cref="ValueTranslation"/>.
        /// </summary>
        /// <remarks>
        /// Numeric values are not translated unless the member opts in with <see cref="UseTranslationAttribute"/> or
        /// <see cref="UseGeneralTranslationAttribute"/>; the <c>UNSET</c> and <c>NULL</c> placeholders of a missing
        /// value are words and use <see cref="PlaceholderTranslation"/>. While the values are untranslated, either by default or
        /// through <see cref="NoTranslationAttribute"/>, only the values listed by <see cref="TranslateValuesAttribute"/> are
        /// translated (see <see cref="SelectedValues"/>). Values with explicit attributes use exactly their keys; without
        /// attributes boolean values only use the general table and enum values only their option-specific key
        /// (see <see cref="SettingTranslation.Resolve(Attribute[], TranslationTarget, string, Type)"/>).
        /// </remarks>
        /// <param name="value">The option value, or null for a missing value.</param>
        /// <param name="valueText">The untranslated display text of <paramref name="value"/>.</param>
        /// <returns>
        /// The localized text. When it is not translated or has no translation, the folded number of an option marked with
        /// <see cref="FoldNumberAttribute"/>, or otherwise <paramref name="valueText"/>.
        /// </returns>
        protected string LocalizeValueText(object? value, string valueText)
        {
            if (value is null)
            {
                return PlaceholderTranslation.Localize(valueText);
            }
            string localizedText;
            if (!ValueTranslation.IsTranslated || (_hasNumericValues && !ValueTranslation.IsExplicit))
            {
                localizedText = SelectedValues.Localize(value, valueText);
            }
            else
            {
                localizedText = ValueTranslation.Localize(valueText, _hasNumericValues);
            }
            return FoldUntranslatedNumber(value, valueText, localizedText);
        }
        /// <summary>
        /// Replaces an untranslated numeric value text with its folded form when the option is marked with
        /// <see cref="FoldNumberAttribute"/>.
        /// </summary>
        /// <remarks>
        /// The translation lookup keeps using the unfolded <paramref name="valueText"/>, so a translated value is shown as
        /// translated and only the raw number falls back to the folded text.
        /// </remarks>
        /// <param name="value">The option value.</param>
        /// <param name="valueText">The untranslated display text of <paramref name="value"/>.</param>
        /// <param name="localizedText">The result of the translation lookup for <paramref name="valueText"/>.</param>
        /// <returns>The folded text when it applies; otherwise <paramref name="localizedText"/>.</returns>
        private string FoldUntranslatedNumber(object value, string valueText, string localizedText)
        {
            if (!_foldsNumbers || !string.Equals(localizedText, valueText, StringComparison.Ordinal))
            {
                return localizedText;
            }
            return SettingNumberFolding.TryFold(value, out var foldedText) ? foldedText : localizedText;
        }
        private void InitTranslation()
        {
            var valueType = Nullable.GetUnderlyingType(_memberType) ?? _memberType;
            _hasNumericValues = valueType.IsIntType() || valueType.IsFloatType();
            _foldsNumbers = _hasNumericValues && GetCustomAttribute<FoldNumberAttribute>() != null;
            ValueTranslation = SettingTranslation.Resolve(_attributes, TranslationTarget.OptionValue, Name, _memberType);
            PlaceholderTranslation = SettingTranslation.ResolvePlaceholder(_attributes, Name);
            SelectedValues = SettingValueSelection.Resolve(_attributes, Name, _memberType);
        }
        public virtual void Dispose()
        {
        }
        protected T? GetCustomAttribute<T>() where T : Attribute
        {
            foreach (var attribute in _attributes)
            {
                if (attribute is T result)
                {
                    return result;
                }
            }
            return null;
        }
        protected IEnumerable<T> GetCustomAttributes<T>() where T : Attribute
        {
            foreach (var attribute in _attributes)
            {
                if (attribute is T result)
                {
                    yield return result;
                }
            }
        }
    }
}

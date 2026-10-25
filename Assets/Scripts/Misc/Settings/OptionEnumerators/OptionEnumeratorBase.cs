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
        /// The placeholders are words rather than values of the member type, so an enum member, whose values do not fall
        /// back to the general table by default, still looks them up there.
        /// </remarks>
        private protected SettingTranslation PlaceholderTranslation { get; private set; } = SettingTranslation.DefaultOptionValue;

        Type _memberType = null!;
        Attribute[] _attributes = Array.Empty<Attribute>();
        // True for numeric members including nullable ones; their values are only translated on explicit request.
        bool _hasNumericValues;

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
        /// value are words and use <see cref="PlaceholderTranslation"/>. Opted-in numeric values use exactly the keys of
        /// their attributes, enum values only use the general table when requested, and boolean values that look up an
        /// option-specific key fall back to the general table afterwards
        /// (see <see cref="SettingTranslation.Resolve(Attribute[], TranslationTarget, string, Type)"/>).
        /// </remarks>
        /// <param name="value">The option value, or null for a missing value.</param>
        /// <param name="valueText">The untranslated display text of <paramref name="value"/>.</param>
        /// <returns>The localized text, or <paramref name="valueText"/> when it is not translated or has no translation.</returns>
        protected string LocalizeValueText(object? value, string valueText)
        {
            if (value is null)
            {
                return PlaceholderTranslation.Localize(valueText);
            }
            if (_hasNumericValues && !ValueTranslation.IsExplicit)
            {
                return valueText;
            }
            return ValueTranslation.Localize(valueText, _hasNumericValues);
        }
        private void InitTranslation()
        {
            var valueType = Nullable.GetUnderlyingType(_memberType) ?? _memberType;
            _hasNumericValues = valueType.IsIntType() || valueType.IsFloatType();
            ValueTranslation = SettingTranslation.Resolve(_attributes, TranslationTarget.OptionValue, Name, _memberType);
            // Resolving without the value type keeps the default general fallback that enum values lose.
            PlaceholderTranslation = valueType.IsEnum
                ? SettingTranslation.Resolve(_attributes, TranslationTarget.OptionValue, Name)
                : ValueTranslation;
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

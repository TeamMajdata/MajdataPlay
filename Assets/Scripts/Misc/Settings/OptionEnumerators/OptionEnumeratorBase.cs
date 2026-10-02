using Cysharp.Text;
using MajdataPlay.i18n;
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

        protected readonly Utf16PreparedFormat<string, object> ValueTextLocalizationTemplate = ZString.PrepareUtf16<string, object>("MAJSETTING_PROPERTY_{0}_OPTION_{1}");
        protected readonly Utf16PreparedFormat<object> GeneralValueTextLocalizationTemplate = ZString.PrepareUtf16<object>("MAJSETTING_GENERAL_OPTION_{0}");


        Type _memberType = null!;
        Attribute[] _attributes = Array.Empty<Attribute>();

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
                if (!ValueTextLocalizationTemplate.Format(Name, ValueTexts[i]).Tryi18n(out var localizedText) &&
                    !GeneralValueTextLocalizationTemplate.Format(ValueTexts[i]).Tryi18n(out localizedText))
                {
                    localizedText = ValueTexts[i];
                }
                LocalizedValueTexts[i] = localizedText;
            }
        }
        public virtual void RefreshLocalization()
        {
            for (var i = 0; i < OptionValues.Length; i++)
            {
                if (!ValueTextLocalizationTemplate.Format(Name, ValueTexts[i]).Tryi18n(out var localizedText) &&
                    !GeneralValueTextLocalizationTemplate.Format(ValueTexts[i]).Tryi18n(out localizedText))
                {
                    localizedText = ValueTexts[i];
                }
                LocalizedValueTexts[i] = localizedText;
            }
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

using MajdataPlay.Diagnostics;
using MajdataPlay.Extensions;
using MajdataPlay.Settings.OptionEnumerators;
using System;
using System.Collections.Generic;
using System.Reflection;
#nullable enable
namespace MajdataPlay.Settings
{
    // Contains type metadata only; setting instances and enumerators belong to the scene.
    internal static class SettingReflectionCache
    {
        static readonly object _sync = new();
        static readonly Dictionary<Type, PropertyInfo[]> _properties = new();
        static readonly Dictionary<Type, PropertyInfo[]> _visibleProperties = new();
        static readonly Dictionary<MemberInfo, Attribute[]> _attributes = new();
        static readonly Dictionary<PropertyInfo, SettingOptionMetadata> _options = new();
        static readonly Dictionary<Type, object[]> _enumValues = new();
        static readonly Dictionary<Type, Func<IOptionEnumerator>> _enumeratorFactories = new();

        // Returned metadata arrays are shared and must not be modified by callers.
        internal static PropertyInfo[] GetVisibleProperties(Type type)
        {
            lock (_sync)
            {
                if (_visibleProperties.TryGetValue(type, out var visible))
                {
                    return visible;
                }

                var properties = GetProperties(type);
                var result = new List<PropertyInfo>(properties.Length);
                foreach (var property in properties)
                {
                    var hidden = false;
                    foreach (var attribute in GetAttributes(property))
                    {
                        if (attribute is HideInSettingUIAttribute)
                        {
                            hidden = true;
                            break;
                        }
                    }
                    if (!hidden)
                    {
                        // Warm attributes, enum constants, and factory lookup without
                        // constructing an enumerator or retaining a setting instance.
                        GetOption(property);
                        result.Add(property);
                    }
                }

                visible = result.ToArray();
                _visibleProperties.Add(type, visible);
                return visible;
            }
        }

        internal static PropertyInfo GetProperty(Type type, string name)
        {
            lock (_sync)
            {
                PropertyInfo? result = null;
                foreach (var property in GetProperties(type))
                {
                    if (property.Name != name)
                    {
                        continue;
                    }
                    if (result is not null)
                    {
                        throw new AmbiguousMatchException($"Multiple properties named {name} exist on {type}.");
                    }
                    result = property;
                }
                return result ?? throw new MissingMemberException(type.FullName, name);
            }
        }

        internal static SettingOptionMetadata GetOption(PropertyInfo property)
        {
            lock (_sync)
            {
                if (!_options.TryGetValue(property, out var metadata))
                {
                    metadata = new SettingOptionMetadata(property, GetAttributes(property));
                    _options.Add(property, metadata);
                }
                return metadata;
            }
        }

        internal static Attribute[] GetAttributes(MemberInfo member)
        {
            lock (_sync)
            {
                if (!_attributes.TryGetValue(member, out var attributes))
                {
                    attributes = Attribute.GetCustomAttributes(member, true);
                    _attributes.Add(member, attributes);
                }
                return attributes;
            }
        }

        internal static object[] GetEnumValues(Type type)
        {
            lock (_sync)
            {
                if (!_enumValues.TryGetValue(type, out var values))
                {
                    var constants = Enum.GetValues(type);
                    values = new object[constants.Length];
                    for (var i = 0; i < constants.Length; i++)
                    {
                        values[i] = constants.GetValue(i)!;
                    }
                    _enumValues.Add(type, values);
                }
                return values;
            }
        }

        internal static Func<IOptionEnumerator> GetEnumeratorFactory(Type type)
        {
            lock (_sync)
            {
                if (!_enumeratorFactories.TryGetValue(type, out var factory))
                {
                    var constructor = type.GetConstructor(Type.EmptyTypes);
                    if (constructor is not null)
                    {
                        factory = () => (IOptionEnumerator)constructor.Invoke(null);
                    }
                    else if (type.IsValueType)
                    {
                        factory = () => (IOptionEnumerator)Activator.CreateInstance(type)!;
                    }
                    else
                    {
                        factory = () => throw new MissingMethodException(type.FullName, ".ctor()");
                    }
                    _enumeratorFactories.Add(type, factory);
                }
                return factory;
            }
        }

        static PropertyInfo[] GetProperties(Type type)
        {
            if (!_properties.TryGetValue(type, out var properties))
            {
                properties = type.GetProperties();
                _properties.Add(type, properties);
            }
            return properties;
        }
    }

    internal sealed class SettingOptionMetadata
    {
        internal PropertyInfo PropertyInfo { get; }
        internal bool HasDescription { get; }
        internal bool IsOffsetOption { get; }
        internal string NameKey { get; }
        internal string DescriptionKey { get; }

        readonly Func<IOptionEnumerator> _createEnumerator;
        readonly Type? _customEnumeratorType;

        internal SettingOptionMetadata(PropertyInfo property, Attribute[] attributes)
        {
            PropertyInfo = property;
            HasDescription = true;
            NameKey = $"MAJSETTING_PROPERTY_{property.Name}";
            DescriptionKey = $"MAJSETTING_PROPERTY_{property.Name}_DESC";
            foreach (var attribute in attributes)
            {
                switch (attribute)
                {
                    case NoDescriptionAttribute:
                        HasDescription = false;
                        break;
                    case OptionNameAttribute name:
                        NameKey = name.Name;
                        break;
                    case DescriptionAttribute description:
                        DescriptionKey = description.Text;
                        break;
                    case OptionEnumeratorAttribute enumerator:
                        _customEnumeratorType = enumerator.EnumeratorType;
                        break;
                }
            }
            IsOffsetOption = property.Name is
                "SlideFadeInOffset" or
                "AudioOffset" or
                "JudgeOffset" or
                "AnswerOffset" or
                "TouchPanelOffset" or
                "DisplayOffset";

            var type = property.PropertyType;
            if (type.IsEnum)
            {
                SettingReflectionCache.GetEnumValues(type);
            }
            if (_customEnumeratorType is not null)
            {
                _createEnumerator = SettingReflectionCache.GetEnumeratorFactory(_customEnumeratorType);
            }
            else if (type.IsEnum)
            {
                _createEnumerator = static () => new DefaultEnumEnumerator();
            }
            else if (type == typeof(bool) || type == typeof(bool?))
            {
                _createEnumerator = static () => new DefaultBooleanEnumerator();
            }
            else if (type.IsIntType() || type.IsFloatType())
            {
                _createEnumerator = static () => new DefaultNumberEnumerator();
            }
            else
            {
                _createEnumerator = static () => new DefaultReadOnlyEnumerator();
            }
        }

        internal IOptionEnumerator CreateEnumerator()
        {
            if (_customEnumeratorType is null)
            {
                return _createEnumerator();
            }
            try
            {
                var enumerator = _createEnumerator();
                if (enumerator is not null)
                {
                    return enumerator;
                }
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"[SettingUI]Failed to instantiate IOptionEnumerator specified by Attribute\nType: {_customEnumeratorType}\nException: {e}");
            }
            MajDebug.LogWarning($"[SettingUI]Failed to instantiate IOptionEnumerator specified by Attribute\nType: {_customEnumeratorType}");
            return new DefaultReadOnlyEnumerator();
        }
    }
}

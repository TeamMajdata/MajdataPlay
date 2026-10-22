using MajdataPlay.Diagnostics;
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
        private static readonly object s_sync = new();
        private static readonly Dictionary<Type, PropertyInfo[]> s_properties = new();
        private static readonly Dictionary<Type, PropertyInfo[]> s_visibleProperties = new();
        private static readonly Dictionary<MemberInfo, Attribute[]> s_attributes = new();
        private static readonly Dictionary<PropertyInfo, SettingOptionMetadata> s_options = new();
        private static readonly Dictionary<Type, object[]> s_enumValues = new();
        private static readonly Dictionary<Type, Func<IOptionEnumerator>> s_enumeratorFactories = new();

        // Returned metadata arrays are shared and must not be modified by callers.
        internal static PropertyInfo[] GetVisibleProperties(Type type)
        {
            lock (s_sync)
            {
                if (s_visibleProperties.TryGetValue(type, out var visible))
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
                s_visibleProperties.Add(type, visible);
                return visible;
            }
        }

        internal static PropertyInfo GetProperty(Type type, string name)
        {
            lock (s_sync)
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
            lock (s_sync)
            {
                if (!s_options.TryGetValue(property, out var metadata))
                {
                    metadata = new SettingOptionMetadata(property, GetAttributes(property));
                    s_options.Add(property, metadata);
                }
                return metadata;
            }
        }

        internal static Attribute[] GetAttributes(MemberInfo member)
        {
            lock (s_sync)
            {
                if (!s_attributes.TryGetValue(member, out var attributes))
                {
                    attributes = Attribute.GetCustomAttributes(member, true);
                    s_attributes.Add(member, attributes);
                }
                return attributes;
            }
        }

        internal static object[] GetEnumValues(Type type)
        {
            lock (s_sync)
            {
                if (!s_enumValues.TryGetValue(type, out var values))
                {
                    var constants = Enum.GetValues(type);
                    values = new object[constants.Length];
                    for (var i = 0; i < constants.Length; i++)
                    {
                        values[i] = constants.GetValue(i)!;
                    }
                    s_enumValues.Add(type, values);
                }
                return values;
            }
        }

        internal static Func<IOptionEnumerator> GetEnumeratorFactory(Type type)
        {
            lock (s_sync)
            {
                if (!s_enumeratorFactories.TryGetValue(type, out var factory))
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
                    s_enumeratorFactories.Add(type, factory);
                }
                return factory;
            }
        }

        private static PropertyInfo[] GetProperties(Type type)
        {
            if (!s_properties.TryGetValue(type, out var properties))
            {
                properties = type.GetProperties();
                s_properties.Add(type, properties);
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

        private readonly Func<IOptionEnumerator> _createEnumerator;
        private readonly Type? _customEnumeratorType;

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

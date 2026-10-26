using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Describes when a setting option or a setting menu is shown in the setting UI, as declared by
    /// <see cref="ShowInSettingUIWhenAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Member paths are resolved and condition values are converted once, when the visibility is resolved; evaluating it
    /// only reads the controlling settings. Instances are immutable and may be shared.
    /// </remarks>
    internal sealed class SettingVisibility
    {
        /// <summary>
        /// Gets the visibility of an entry without conditions, which is always shown.
        /// </summary>
        internal static SettingVisibility Always { get; } = new(Array.Empty<Condition>());

        private readonly Condition[] _conditions;

        private SettingVisibility(Condition[] conditions)
        {
            _conditions = conditions;
        }

        /// <summary>
        /// Resolves the visibility of a setting property from its <see cref="ShowInSettingUIWhenAttribute"/> attributes.
        /// </summary>
        /// <param name="ownerType">The type that declares the annotated property; every member path starts at this type.</param>
        /// <param name="memberName">The name of the annotated property, used in error messages.</param>
        /// <param name="attributes">The attributes of the annotated property; attributes of other types are ignored.</param>
        /// <returns>The resolved visibility, or <see cref="Always"/> when no condition is declared.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// A condition has an empty member path, a path segment that is not a readable property, no values, or a value
        /// that cannot be converted to the type of its controlling setting.
        /// </exception>
        /// <exception cref="MissingMemberException">A segment of a member path does not exist.</exception>
        /// <exception cref="AmbiguousMatchException">A segment of a member path names several properties.</exception>
        internal static SettingVisibility Resolve(Type ownerType, string memberName, Attribute[] attributes)
        {
            if (ownerType is null)
            {
                throw new ArgumentNullException(nameof(ownerType));
            }
            if (memberName is null)
            {
                throw new ArgumentNullException(nameof(memberName));
            }
            if (attributes is null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }

            var conditions = new List<Condition>();
            foreach (var attribute in attributes)
            {
                if (attribute is ShowInSettingUIWhenAttribute showWhen)
                {
                    conditions.Add(Condition.Resolve(ownerType, memberName, showWhen));
                }
            }
            return conditions.Count == 0 ? Always : new SettingVisibility(conditions.ToArray());
        }

        /// <summary>
        /// Evaluates every condition against the current settings.
        /// </summary>
        /// <param name="owner">The instance that declares the annotated property: the menu instance for an option, the setting root for a menu.</param>
        /// <returns>True when every condition holds, which is always the case without conditions.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
        internal bool IsVisible(object owner)
        {
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            foreach (var condition in _conditions)
            {
                if (!condition.IsSatisfied(owner))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// One <see cref="ShowInSettingUIWhenAttribute"/> with its member path resolved and its values converted.
        /// </summary>
        private sealed class Condition
        {
            private readonly PropertyInfo[] _path;
            private readonly object?[] _values;

            private Condition(PropertyInfo[] path, object?[] values)
            {
                _path = path;
                _values = values;
            }

            /// <summary>
            /// Resolves the member path and converts the values of one attribute.
            /// </summary>
            /// <param name="ownerType">The type the member path starts at.</param>
            /// <param name="memberName">The name of the annotated property, used in error messages.</param>
            /// <param name="attribute">The attribute to resolve.</param>
            /// <returns>The resolved condition.</returns>
            /// <exception cref="ArgumentException">The path is empty or names a property that cannot be read, or a value is missing or cannot be converted.</exception>
            /// <exception cref="MissingMemberException">A segment of the path does not exist.</exception>
            /// <exception cref="AmbiguousMatchException">A segment of the path names several properties.</exception>
            internal static Condition Resolve(Type ownerType, string memberName, ShowInSettingUIWhenAttribute attribute)
            {
                if (string.IsNullOrWhiteSpace(attribute.MemberPath))
                {
                    throw new ArgumentException($"The condition on {ownerType.Name}.{memberName} has no member path.");
                }
                if (attribute.Values.Count == 0)
                {
                    throw new ArgumentException($"The condition \"{attribute.MemberPath}\" on {ownerType.Name}.{memberName} has no values.");
                }

                var segments = attribute.MemberPath.Split('.');
                var path = new PropertyInfo[segments.Length];
                var type = ownerType;
                for (var i = 0; i < segments.Length; i++)
                {
                    var property = SettingReflectionCache.GetProperty(type, segments[i]);
                    if (property.GetGetMethod() is null || property.GetIndexParameters().Length != 0)
                    {
                        throw new ArgumentException($"{type.Name}.{property.Name} in the condition on {ownerType.Name}.{memberName} cannot be read.");
                    }
                    path[i] = property;
                    type = property.PropertyType;
                }

                var values = new object?[attribute.Values.Count];
                for (var i = 0; i < values.Length; i++)
                {
                    values[i] = ConvertValue(attribute.Values[i], type, $"the condition \"{attribute.MemberPath}\" on {ownerType.Name}.{memberName}");
                }
                return new Condition(path, values);
            }

            /// <summary>
            /// Reads the controlling setting and compares it with the values.
            /// </summary>
            /// <param name="owner">The instance the member path starts at.</param>
            /// <returns>True when the controlling setting equals any of the values; false when an object on the path is null.</returns>
            internal bool IsSatisfied(object owner)
            {
                object? value = owner;
                foreach (var property in _path)
                {
                    if (value is null)
                    {
                        return false;
                    }
                    value = property.GetValue(value);
                }
                foreach (var expected in _values)
                {
                    if (Equals(expected, value))
                    {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Converts an attribute value to the type of the controlling setting, so it compares equal to the boxed setting value.
            /// </summary>
            /// <param name="value">The attribute value.</param>
            /// <param name="settingType">The declared type of the controlling setting.</param>
            /// <param name="context">Describes the condition in error messages.</param>
            /// <returns>The converted value, or null.</returns>
            /// <exception cref="ArgumentException"><paramref name="value"/> cannot be converted to <paramref name="settingType"/>.</exception>
            private static object? ConvertValue(object? value, Type settingType, string context)
            {
                var nullableType = Nullable.GetUnderlyingType(settingType);
                if (value is null)
                {
                    if (settingType.IsValueType && nullableType is null)
                    {
                        throw new ArgumentException($"null never matches the {settingType.Name} setting of {context}.");
                    }
                    return null;
                }

                // A nullable setting boxes its underlying value, so compare with that type.
                var targetType = nullableType ?? settingType;
                if (targetType.IsInstanceOfType(value))
                {
                    return value;
                }
                try
                {
                    // Only integers convert to an enum constant; a constant of another enum type is a mistake.
                    if (targetType.IsEnum && value.GetType().IsIntType())
                    {
                        return Enum.ToObject(targetType, value);
                    }
                    if (!targetType.IsEnum && value is IConvertible && (targetType.IsPrimitive || targetType == typeof(decimal) || targetType == typeof(string)))
                    {
                        return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
                {
                    throw new ArgumentException($"The value {value} of {context} cannot be converted to {targetType.Name}: {e.Message}", e);
                }
                throw new ArgumentException($"The {value.GetType().Name} value {value} of {context} cannot be converted to {targetType.Name}.");
            }
        }
    }
}

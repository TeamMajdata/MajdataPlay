using MajdataPlay.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Holds the values of a setting option that <see cref="DisableValuesAttribute"/> disables on the current platform.
    /// </summary>
    /// <remarks>
    /// The option enumerators use this to skip disabled choices; see <see cref="DisableValuesAttribute"/> for the rules.
    /// Instances are immutable and may be shared.
    /// </remarks>
    internal sealed class SettingDisabledValues
    {
        /// <summary>
        /// Gets the rule of an option without disabled values.
        /// </summary>
        internal static SettingDisabledValues None { get; } = new(Array.Empty<object>(), Array.Empty<decimal>());

        /// <summary>
        /// Gets or sets the platform the rules are evaluated for; the build target platform by default.
        /// </summary>
        /// <remarks>
        /// Tests replace it to evaluate the rules of another platform and must restore it afterwards.
        /// </remarks>
        internal static SettingPlatform CurrentPlatform { get; set; } = GetBuildPlatform();

        /// <summary>
        /// Gets whether no value is disabled.
        /// </summary>
        internal bool IsEmpty => _enumValues.Length == 0 && _numbers.Length == 0;

        /// <summary>
        /// The disabled enum constants.
        /// </summary>
        private readonly object[] _enumValues;

        /// <summary>
        /// The disabled numbers.
        /// </summary>
        private readonly decimal[] _numbers;

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingDisabledValues"/> class.
        /// </summary>
        /// <param name="enumValues">The disabled enum constants.</param>
        /// <param name="numbers">The disabled numbers.</param>
        private SettingDisabledValues(object[] enumValues, decimal[] numbers)
        {
            _enumValues = enumValues;
            _numbers = numbers;
        }

        /// <summary>
        /// Resolves the disabled values of a setting option for <see cref="CurrentPlatform"/>.
        /// </summary>
        /// <remarks>
        /// For an enum option this combines the attributes of the option with those of the enum constants. A value that is
        /// not an invariant-culture number on a numeric option, or not a constant name on an enum option, is logged as an
        /// error and dropped.
        /// </remarks>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="memberName">The declared member name, used in log messages.</param>
        /// <param name="valueType">The type of the option value, possibly nullable.</param>
        /// <returns>The disabled values, or <see cref="None"/> when the option disables nothing.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static SettingDisabledValues Resolve(Attribute[] attributes, string memberName, Type valueType)
        {
            if (attributes is null)
            {
                throw new ArgumentNullException(nameof(attributes));
            }
            if (memberName is null)
            {
                throw new ArgumentNullException(nameof(memberName));
            }
            if (valueType is null)
            {
                throw new ArgumentNullException(nameof(valueType));
            }

            var underlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (underlyingType.IsEnum)
            {
                return ResolveEnum(attributes, memberName, underlyingType);
            }
            if (underlyingType.IsIntType() || underlyingType.IsFloatType())
            {
                return ResolveNumbers(attributes, memberName);
            }
            return None;
        }

        /// <summary>
        /// Checks whether an option value is disabled.
        /// </summary>
        /// <param name="value">The option value, or null for a missing value, which is never disabled.</param>
        /// <returns>True when the value is a disabled enum constant or equals a disabled number; otherwise false.</returns>
        internal bool IsDisabled(object? value)
        {
            if (value is null)
            {
                return false;
            }
            if (value is Enum)
            {
                return Array.IndexOf(_enumValues, value) >= 0;
            }
            if (_numbers.Length == 0)
            {
                return false;
            }
            try
            {
                return IsDisabled(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
            }
            catch (Exception exception) when (exception is InvalidCastException || exception is OverflowException || exception is FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether a number is disabled.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>True when the number equals a disabled number; otherwise false.</returns>
        internal bool IsDisabled(decimal value)
        {
            return Array.IndexOf(_numbers, value) >= 0;
        }

        /// <summary>
        /// Removes the disabled values from the choices of a finite option.
        /// </summary>
        /// <param name="values">The choices in display order.</param>
        /// <param name="memberName">The declared member name, used in log messages.</param>
        /// <returns>
        /// The enabled choices; <paramref name="values"/> itself when nothing is disabled, or when every choice would be
        /// disabled, which is logged as an error.
        /// </returns>
        internal object[] Filter(object[] values, string memberName)
        {
            if (IsEmpty)
            {
                return values;
            }
            var enabled = new List<object>(values.Length);
            foreach (var value in values)
            {
                if (!IsDisabled(value))
                {
                    enabled.Add(value);
                }
            }
            if (enabled.Count == values.Length)
            {
                return values;
            }
            if (enabled.Count == 0)
            {
                MajDebug.LogError(nameof(SettingDisabledValues), $"[SettingUI] {nameof(DisableValuesAttribute)} disables every value of option {memberName} on {CurrentPlatform}; the values stay enabled.");
                return values;
            }
            return enabled.ToArray();
        }

        /// <summary>
        /// Resolves the disabled constants of an enum option.
        /// </summary>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="memberName">The declared member name, used in log messages.</param>
        /// <param name="enumType">The enum type.</param>
        /// <returns>The disabled constants, or <see cref="None"/>.</returns>
        private static SettingDisabledValues ResolveEnum(Attribute[] attributes, string memberName, Type enumType)
        {
            var disabled = new List<object>();
            foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                foreach (var attribute in SettingReflectionCache.GetAttributes(field))
                {
                    if (attribute is DisableValuesAttribute rule && AppliesToCurrentPlatform(rule))
                    {
                        AddDistinct(disabled, field.GetValue(null)!);
                        break;
                    }
                }
            }
            foreach (var attribute in attributes)
            {
                if (attribute is not DisableValuesAttribute rule || !AppliesToCurrentPlatform(rule))
                {
                    continue;
                }
                foreach (var text in rule.Values)
                {
                    var field = string.IsNullOrEmpty(text) ? null : enumType.GetField(text, BindingFlags.Public | BindingFlags.Static);
                    if (field is null)
                    {
                        MajDebug.LogError(nameof(SettingDisabledValues), $"[SettingUI] Value \"{text}\" of {nameof(DisableValuesAttribute)} on option {memberName} is not a constant of {enumType.Name}.");
                        continue;
                    }
                    AddDistinct(disabled, field.GetValue(null)!);
                }
            }
            return disabled.Count == 0 ? None : new SettingDisabledValues(disabled.ToArray(), Array.Empty<decimal>());
        }

        /// <summary>
        /// Resolves the disabled numbers of a numeric option.
        /// </summary>
        /// <param name="attributes">The attributes of the setting member.</param>
        /// <param name="memberName">The declared member name, used in log messages.</param>
        /// <returns>The disabled numbers, or <see cref="None"/>.</returns>
        private static SettingDisabledValues ResolveNumbers(Attribute[] attributes, string memberName)
        {
            var disabled = new List<decimal>();
            foreach (var attribute in attributes)
            {
                if (attribute is not DisableValuesAttribute rule || !AppliesToCurrentPlatform(rule))
                {
                    continue;
                }
                foreach (var text in rule.Values)
                {
                    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    {
                        MajDebug.LogError(nameof(SettingDisabledValues), $"[SettingUI] Value \"{text}\" of {nameof(DisableValuesAttribute)} on option {memberName} is not an invariant-culture number.");
                        continue;
                    }
                    if (!disabled.Contains(number))
                    {
                        disabled.Add(number);
                    }
                }
            }
            return disabled.Count == 0 ? None : new SettingDisabledValues(Array.Empty<object>(), disabled.ToArray());
        }

        /// <summary>
        /// Checks whether a rule applies to <see cref="CurrentPlatform"/>.
        /// </summary>
        /// <param name="rule">The rule.</param>
        /// <returns>True when <see cref="DisableValuesAttribute.Platforms"/> includes the current platform; otherwise false.</returns>
        private static bool AppliesToCurrentPlatform(DisableValuesAttribute rule)
        {
            return (rule.Platforms & CurrentPlatform) != 0;
        }

        /// <summary>
        /// Adds a value to a list unless it is already present.
        /// </summary>
        /// <param name="values">The list.</param>
        /// <param name="value">The value to add.</param>
        private static void AddDistinct(List<object> values, object value)
        {
            if (!values.Contains(value))
            {
                values.Add(value);
            }
        }

        /// <summary>
        /// Gets the build target platform.
        /// </summary>
        /// <returns>The platform selected by the Unity platform defines, or <see cref="SettingPlatform.None"/> on an unknown platform.</returns>
        private static SettingPlatform GetBuildPlatform()
        {
#if UNITY_ANDROID
            return SettingPlatform.Android;
#elif UNITY_IOS
            return SettingPlatform.IOS;
#elif UNITY_STANDALONE_WIN
            return SettingPlatform.Windows;
#elif UNITY_STANDALONE_LINUX
            return SettingPlatform.Linux;
#elif UNITY_STANDALONE_OSX
            return SettingPlatform.MacOS;
#else
            return SettingPlatform.None;
#endif
        }
    }
}

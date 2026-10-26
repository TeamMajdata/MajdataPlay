using System;
using System.Collections.Generic;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Removes values from the choices of a setting option in the setting UI on the given platforms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attribute can be applied in two places. On an enum constant, written without values, it disables that
    /// constant for every setting option of the enum type. On a setting option, it disables the listed
    /// <see cref="Values"/> of that option only. <see cref="Platforms"/> selects where the values are disabled and defaults
    /// to <see cref="SettingPlatform.All"/>, so the attribute can also be wrapped in platform <c>#if</c> blocks.
    /// </para>
    /// <para>
    /// Enum options skip the disabled constants. Numeric options keep stepping past disabled values and do not move when
    /// only disabled values remain before the range bound. Values of numeric options are written as invariant-culture
    /// numbers and match by numeric value, so <c>"0"</c> matches <c>0</c>, <c>0f</c> and <c>0.0m</c>; values of enum
    /// options are constant names. Other option types ignore the attribute.
    /// </para>
    /// <para>
    /// A disabled value that is already stored, for example in a settings file copied from another platform, is replaced
    /// by the first enabled enum constant, or by the first enabled number reached by stepping up and then down, unless the
    /// option is read-only.
    /// When every enum constant would be disabled the rule is ignored and logged as an error. Values are disabled only
    /// in the setting UI; loading and saving the settings file is not affected.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public enum OutputBackend
    /// {
    ///     Unity,
    ///     // ASIO is only available on Windows.
    ///     [DisableValues(Platforms = SettingPlatform.All & ~SettingPlatform.Windows)]
    ///     Asio,
    /// }
    ///
    /// // Hides 0 on Android only.
    /// [DisableValues("0", Platforms = SettingPlatform.Android)]
    /// public int Threads { get; set; } = 1;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public sealed class DisableValuesAttribute : Attribute
    {
        /// <summary>
        /// Gets the values to disable on a setting option, written as invariant-culture numbers for numeric options and
        /// as constant names for enum options; empty on an enum constant, which disables the constant itself.
        /// </summary>
        public IReadOnlyList<string> Values { get; }

        /// <summary>
        /// Gets or sets the platforms on which the values are disabled; <see cref="SettingPlatform.All"/> by default.
        /// </summary>
        public SettingPlatform Platforms { get; set; } = SettingPlatform.All;

        /// <summary>
        /// Initializes a new instance of the <see cref="DisableValuesAttribute"/> class without values, which disables the
        /// annotated enum constant.
        /// </summary>
        public DisableValuesAttribute()
        {
            Values = Array.Empty<string>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DisableValuesAttribute"/> class that disables values of the
        /// annotated setting option.
        /// </summary>
        /// <param name="values">The values to disable; null is treated as no values.</param>
        public DisableValuesAttribute(params string[] values)
        {
            Values = values is null ? Array.Empty<string>() : (string[])values.Clone();
        }
    }
}

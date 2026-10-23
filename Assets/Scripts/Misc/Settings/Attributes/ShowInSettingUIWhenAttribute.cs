using System;
using System.Collections.Generic;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Shows a setting option or a setting menu in the setting UI only while another setting has one of the given values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="MemberPath"/> names the controlling setting relative to the type that declares the annotated
    /// property, with dots between nested settings. On an option it usually names a sibling option of the same menu,
    /// such as <c>nameof(DisplaySensor)</c>. On a menu, which is a property of <see cref="GameSetting"/>, it starts at the
    /// setting root, such as <c>"Debug.DisplaySensor"</c>. Every segment must be a readable public property.
    /// </para>
    /// <para>
    /// A condition holds when the controlling setting equals any of <see cref="Values"/>. Each value is converted to the
    /// type of the controlling setting, so <c>1</c> matches a <see cref="float"/> setting of <c>1f</c> and an integer
    /// matches the enum constant with that value. <c>null</c> only matches a nullable or reference type setting without
    /// a value, and never matches when an object on the path is null. When the attribute is applied several times,
    /// every condition must hold.
    /// </para>
    /// <para>
    /// The setting UI re-evaluates the conditions every frame while a menu is shown, so an entry appears or disappears
    /// as soon as the controlling setting is edited; the selected menu stays in the category list until another one is
    /// selected. A hidden option keeps its value and is still loaded and saved. <see cref="HideInSettingUIAttribute"/>
    /// takes precedence over this attribute. A path or value that cannot be resolved is logged as an error and leaves
    /// the entry always visible.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// public bool DisplaySensor { get; set; }
    ///
    /// [ShowInSettingUIWhen(nameof(DisplaySensor), true)]
    /// public SensorDebugStyle SensorStyle { get; set; }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public sealed class ShowInSettingUIWhenAttribute : Attribute
    {
        /// <summary>
        /// Gets the dot-separated path of the controlling setting, relative to the type that declares the annotated property.
        /// </summary>
        public string MemberPath { get; }
        /// <summary>
        /// Gets the values of the controlling setting under which the annotated entry is shown.
        /// </summary>
        public IReadOnlyList<object?> Values { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ShowInSettingUIWhenAttribute"/> class.
        /// </summary>
        /// <param name="memberPath">The dot-separated path of the controlling setting, relative to the type that declares the annotated property.</param>
        /// <param name="values">
        /// The values of the controlling setting under which the entry is shown; at least one is required. A single
        /// <c>null</c> argument binds to the array itself and is treated as the value <c>null</c>.
        /// </param>
        public ShowInSettingUIWhenAttribute(string memberPath, params object?[]? values)
        {
            MemberPath = memberPath;
            // [ShowInSettingUIWhen(path, null)] passes a null array rather than an array holding null.
            Values = values is null ? new object?[] { null } : (object?[])values.Clone();
        }
    }
}

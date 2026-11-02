using System;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Excludes a setting option from JSON serialization and deserialization on the specified build target platforms.
    /// </summary>
    /// <remarks>
    /// Applied by the settings contract resolver. Matching options retain their defaults when settings are loaded,
    /// even when the JSON contains a value. This attribute does not change visibility in the setting UI.
    /// Combine flags to select multiple platforms, or use <see cref="SettingPlatform.Desktop"/> or
    /// <see cref="SettingPlatform.Mobile"/>. <see cref="SettingPlatform.None"/> excludes nothing.
    /// In the Unity Editor, the platform is the selected build target.
    /// </remarks>
    /// <example>
    /// <code>
    /// [JsonIgnoreOnPlatform(SettingPlatform.IOS)]
    /// public bool Enable { get; set; } = false;
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class JsonIgnoreOnPlatformAttribute : Attribute
    {
        /// <summary>
        /// Gets the platforms on which the annotated option is excluded from JSON serialization and deserialization.
        /// </summary>
        public SettingPlatform Platforms { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonIgnoreOnPlatformAttribute"/> class.
        /// </summary>
        /// <param name="platforms">The build target platforms on which to ignore the annotated option.</param>
        public JsonIgnoreOnPlatformAttribute(SettingPlatform platforms)
        {
            Platforms = platforms;
        }
    }
}

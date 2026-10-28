using System;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Specifies the target platforms that a setting UI rule such as <see cref="DisableValuesAttribute"/> applies to.
    /// </summary>
    /// <remarks>
    /// The platform is the build target: in the Unity Editor it is the platform currently selected in the build settings.
    /// </remarks>
    [Flags]
    public enum SettingPlatform
    {
        /// <summary>No platform.</summary>
        None = 0,

        /// <summary>Windows standalone players.</summary>
        Windows = 1 << 0,

        /// <summary>Linux standalone players.</summary>
        Linux = 1 << 1,

        /// <summary>macOS standalone players.</summary>
        MacOS = 1 << 2,

        /// <summary>Android players.</summary>
        Android = 1 << 3,

        /// <summary>iOS players.</summary>
        IOS = 1 << 4,

        /// <summary>The desktop platforms: Windows, Linux and macOS.</summary>
        Desktop = Windows | Linux | MacOS,

        /// <summary>The mobile platforms: Android and iOS.</summary>
        Mobile = Android | IOS,

        /// <summary>Every platform.</summary>
        All = Desktop | Mobile
    }
}

using System;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Holds the user and system settings shown together by the setting UI.
    /// </summary>
    /// <remarks>
    /// The categories of <see cref="UserSetting"/> and <see cref="SystemSetting"/> are the menus of the setting UI. The
    /// member paths of <see cref="ShowInSettingUIWhenAttribute"/> on a menu start at this type, so a menu of one root
    /// can depend on an option of the other, such as <c>"User.Game.RecordMode"</c>.
    /// </remarks>
    public sealed class SettingRoot
    {
        /// <summary>
        /// Gets the user settings, which are stored in settings.json.
        /// </summary>
        public UserSetting User { get; }

        /// <summary>
        /// Gets the system settings, which are stored in system-settings.json.
        /// </summary>
        public SystemSetting System { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SettingRoot"/> class.
        /// </summary>
        /// <param name="user">The user settings.</param>
        /// <param name="system">The system settings.</param>
        /// <exception cref="ArgumentNullException"><paramref name="user"/> or <paramref name="system"/> is null.</exception>
        public SettingRoot(UserSetting user, SystemSetting system)
        {
            User = user ?? throw new ArgumentNullException(nameof(user));
            System = system ?? throw new ArgumentNullException(nameof(system));
        }

        /// <summary>
        /// Determines whether a type is one of the roots whose categories are the menus of the setting UI.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True for <see cref="UserSetting"/> and <see cref="SystemSetting"/>; otherwise false.</returns>
        internal static bool IsRootType(Type? type)
        {
            return type == typeof(UserSetting) || type == typeof(SystemSetting);
        }
    }
}

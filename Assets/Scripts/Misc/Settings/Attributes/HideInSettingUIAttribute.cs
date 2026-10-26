using System;
using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Excludes a setting property from the setting UI while keeping it serializable.
    /// </summary>
    /// <remarks>
    /// The property is still loaded from and saved to the settings file; it is only skipped when the
    /// setting menus enumerate their visible options. Deriving from <see cref="PreserveAttribute"/>
    /// keeps the hidden property alive under IL2CPP managed code stripping, because the setting UI
    /// no longer references it.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class HideInSettingUIAttribute : PreserveAttribute
    {
    }
}

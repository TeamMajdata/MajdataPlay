using System;
using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class HideInSettingUIAttribute : PreserveAttribute
    {
    }
}

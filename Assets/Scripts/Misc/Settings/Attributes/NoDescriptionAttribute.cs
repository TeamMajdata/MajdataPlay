using System;

namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NoDescriptionAttribute : Attribute
    {
    }
}

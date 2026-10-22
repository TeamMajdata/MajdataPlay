using System;

namespace MajdataPlay.Settings
{
#nullable enable
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class MenuNameAttribute : Attribute
    {
        public string Name { get; }
        public MenuNameAttribute(string name)
        {
            Name = name ?? string.Empty;
        }
    }
}

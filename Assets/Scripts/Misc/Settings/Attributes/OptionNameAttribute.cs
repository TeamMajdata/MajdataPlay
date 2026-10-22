using System;

namespace MajdataPlay.Settings
{
#nullable enable
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class OptionNameAttribute : Attribute
    {
        public string Name { get; }
        public OptionNameAttribute(string name)
        {
            Name = name ?? string.Empty;
        }
    }
}

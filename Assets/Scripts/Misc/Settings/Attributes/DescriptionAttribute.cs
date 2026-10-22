using System;

namespace MajdataPlay.Settings
{
#nullable enable
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class DescriptionAttribute : Attribute
    {
        public string Text { get; }

        public DescriptionAttribute(string text)
        {
            Text = text ?? string.Empty;
        }
    }
}

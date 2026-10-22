using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class UseTranslationAttribute : Attribute
    {
        public TranslationTarget Target { get; }
        public string Key { get; }

        public UseTranslationAttribute(TranslationTarget target, string key)
        {
            Target = target;
            Key = string.IsNullOrEmpty(key) ? throw new ArgumentException(nameof(key)) : key;
        }
    }
}

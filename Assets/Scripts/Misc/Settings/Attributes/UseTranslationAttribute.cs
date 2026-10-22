using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;
#nullable enable
namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class UseTranslationAttribute : Attribute
    {
        public TranslationTarget Target { get; }
        public string? Key { get; }

        public UseTranslationAttribute(TranslationTarget target) : this(target, string.Empty) { }
        public UseTranslationAttribute(TranslationTarget target, string key)
        {
            Target = target;
            if (string.IsNullOrEmpty(key))
            {
                Key = null;
            }
            else
            {
                Key = key;
            }
        }
    }
}

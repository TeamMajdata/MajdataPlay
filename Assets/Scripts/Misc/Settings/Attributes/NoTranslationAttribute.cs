using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class NoTranslationAttribute : Attribute
    {
        public TranslationTarget Target { get; }

        public NoTranslationAttribute(TranslationTarget target)
        {
            Target = target;
        }
    }
}

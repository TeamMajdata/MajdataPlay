using MajdataPlay.Settings.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class UseGeneralTranslationAttribute : Attribute
    {
        public TranslationTarget Target { get; }

        public UseGeneralTranslationAttribute(TranslationTarget target)
        {
            Target = target;
        }
    }
}

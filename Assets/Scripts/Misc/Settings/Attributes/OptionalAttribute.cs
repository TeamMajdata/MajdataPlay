using System;

namespace MajdataPlay.Settings
{
#nullable enable
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class OptionalAttribute : Attribute
    {

    }
}

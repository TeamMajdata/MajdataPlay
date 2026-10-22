using System;

namespace MajdataPlay.Settings
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class StepAttribute : Attribute
    {
        public decimal Value { get; }

        public StepAttribute(string value)
        {
            Value = decimal.Parse(value);
        }
    }
}

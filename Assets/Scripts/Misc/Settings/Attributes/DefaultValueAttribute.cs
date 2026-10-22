using System;

namespace MajdataPlay.Settings
{
#nullable enable
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class DefaultValueAttribute : Attribute
    {
        public Type Type
        {
            get => _type;
        }
        public string Value
        {
            get => _value;
        }
        private readonly string _value;
        private readonly Type _type;

        public DefaultValueAttribute(string value, Type type)
        {
            _value = value;
            _type = type;
        }
    }
}

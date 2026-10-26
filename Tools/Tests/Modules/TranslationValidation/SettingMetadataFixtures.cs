using System;
using MajdataPlay.TranslationValidation;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Provides the production option-name attribute metadata without executing its constructor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
    internal sealed class OptionNameAttribute : Attribute
    {
        /// <summary>
        /// Gets the localization key supplied by the attribute.
        /// </summary>
        public string Name
        {
            get;
        }

        /// <summary>
        /// Stores an attribute argument and detects runtime attribute construction.
        /// </summary>
        /// <param name="name">The replacement localization key for the option name.</param>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public OptionNameAttribute(string name)
        {
            Name = name;
            throw ExecutionGuard.Executed(nameof(OptionNameAttribute));
        }
    }

    /// <summary>
    /// Provides the production description attribute metadata without executing game code.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
    internal sealed class DescriptionAttribute : Attribute
    {
        /// <summary>
        /// Gets the localization key supplied for the description.
        /// </summary>
        public string Text
        {
            get;
        }

        /// <summary>
        /// Stores an attribute argument and detects runtime attribute construction.
        /// </summary>
        /// <param name="text">The replacement localization key for the option description.</param>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public DescriptionAttribute(string text)
        {
            Text = text;
            throw ExecutionGuard.Executed(nameof(DescriptionAttribute));
        }
    }

    /// <summary>
    /// Provides the production enumerator-type metadata without constructing the enumerator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
    internal sealed class OptionEnumeratorAttribute : Attribute
    {
        /// <summary>
        /// Gets the custom enumerator type stored in metadata.
        /// </summary>
        public Type EnumeratorType
        {
            get;
        }

        /// <summary>
        /// Stores a type argument and detects runtime attribute construction.
        /// </summary>
        /// <param name="enumeratorType">The enumerator whose finite values may be inspected as IL.</param>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public OptionEnumeratorAttribute(Type enumeratorType)
        {
            EnumeratorType = enumeratorType;
            throw ExecutionGuard.Executed(nameof(OptionEnumeratorAttribute));
        }
    }

    /// <summary>
    /// Marks a property as absent from the production settings UI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    internal sealed class HideInSettingUIAttribute : Attribute
    {
        /// <summary>
        /// Detects runtime construction of an attribute that should be read as metadata only.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public HideInSettingUIAttribute()
        {
            throw ExecutionGuard.Executed(nameof(HideInSettingUIAttribute));
        }
    }

    /// <summary>
    /// Marks a property as lacking a description localization key.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    internal sealed class NoDescriptionAttribute : Attribute
    {
        /// <summary>
        /// Detects runtime construction of an attribute that should be read as metadata only.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public NoDescriptionAttribute()
        {
            throw ExecutionGuard.Executed(nameof(NoDescriptionAttribute));
        }
    }

    /// <summary>
    /// Selects the UNSET display token for a nullable boolean option.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    internal sealed class OptionalAttribute : Attribute
    {
        /// <summary>
        /// Detects runtime construction of an attribute that should be read as metadata only.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public OptionalAttribute()
        {
            throw ExecutionGuard.Executed(nameof(OptionalAttribute));
        }
    }

    /// <summary>
    /// Supplies unused category-name metadata to distinguish actual settings UI naming rules.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
    internal sealed class MenuNameAttribute : Attribute
    {
        /// <summary>
        /// Gets the name stored by the attribute.
        /// </summary>
        public string Name
        {
            get;
        }

        /// <summary>
        /// Stores category-name metadata and detects runtime attribute construction.
        /// </summary>
        /// <param name="name">A name not consulted by the current settings category binding.</param>
        /// <exception cref="InvalidOperationException">Always thrown if the attribute is instantiated.</exception>
        public MenuNameAttribute(string name)
        {
            Name = name;
            throw ExecutionGuard.Executed(nameof(MenuNameAttribute));
        }
    }
}

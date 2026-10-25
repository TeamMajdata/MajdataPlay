using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.Settings.Runtime;
using MajdataPlay.Tests.Game.Scenes.PlayMode.Support;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// A settings menu whose properties cover every value source the setting key analyzer distinguishes. It is only
    /// inspected through metadata and never instantiated by the analyzer.
    /// </summary>
    internal sealed class SettingAnalyzerFixture
    {
        /// <summary>Gets or sets a plain boolean option.</summary>
        public bool Flag { get; set; }

        /// <summary>Gets or sets an optional nullable boolean option.</summary>
        [Optional]
        public bool? Maybe { get; set; }

        /// <summary>Gets or sets an enum option with overridden keys.</summary>
        [OptionName("FX_NAME")]
        [Description("FX_DESC")]
        public FixtureMode Mode { get; set; }

        /// <summary>Gets or sets a numeric option without description.</summary>
        [NoDescription]
        public int Count { get; set; }

        /// <summary>Gets a read-only reference value.</summary>
        public string Text { get; } = string.Empty;

        /// <summary>Gets an optional read-only reference value.</summary>
        [Optional]
        public string? OptionalText { get; }

        /// <summary>Gets a read-only nullable enum value.</summary>
        public FixtureMode? NullableMode { get; }

        /// <summary>Gets a read-only nullable number.</summary>
        public int? NullableNumber { get; }

        /// <summary>Gets or sets a number displayed by a boolean enumerator.</summary>
        [OptionEnumerator(typeof(DefaultBooleanEnumerator))]
        public int WrongBoolean { get; set; }

        /// <summary>Gets or sets a number displayed by an enum enumerator.</summary>
        [OptionEnumerator(typeof(DefaultEnumEnumerator))]
        public int WrongEnum { get; set; }

        /// <summary>Gets or sets a string displayed by a number enumerator.</summary>
        [OptionEnumerator(typeof(DefaultNumberEnumerator))]
        public string WrongNumber { get; set; } = string.Empty;

        /// <summary>Gets or sets a language selection.</summary>
        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language { get; set; } = string.Empty;

        /// <summary>Gets or sets a skin selection.</summary>
        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin { get; set; } = string.Empty;

        /// <summary>Gets or sets an option with custom literal values.</summary>
        [OptionEnumerator(typeof(LiteralOptionFixture))]
        public string Custom { get; set; } = string.Empty;

        /// <summary>Gets or sets an option whose custom enumerator yields no values.</summary>
        [OptionEnumerator(typeof(EmptyOptionFixture))]
        public string EmptyCustom { get; set; } = string.Empty;

        /// <summary>Gets or sets an option whose name override is empty.</summary>
        [OptionName("")]
        public bool EmptyName { get; set; }

        /// <summary>Gets or sets an option whose name and values are never translated.</summary>
        [NoTranslation(TranslationTarget.OptionName)]
        [NoTranslation(TranslationTarget.OptionValue)]
        public bool Silent { get; set; }

        /// <summary>Gets or sets an enum option whose value keys use a custom template with the option name override.</summary>
        [OptionName("Alias")]
        [UseTranslation(TranslationTarget.OptionValue, "FX_{0}_{1}")]
        public FixtureMode Aliased { get; set; }

        /// <summary>Gets or sets a numeric option whose values opt into translation.</summary>
        [UseTranslation(TranslationTarget.OptionValue)]
        public int TranslatedCount { get; set; }

        /// <summary>Gets or sets an integer option whose translated values form a finite Range/Step grid.</summary>
        [Step("1")]
        [Range("1", "3")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public int Countdown { get; set; } = 1;

        /// <summary>Gets or sets a floating-point option whose last step is clamped to the upper bound.</summary>
        [Step("0.25")]
        [Range("0", "0.6")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public float Ratio { get; set; }

        /// <summary>Gets or sets a translated numeric option whose Range has no Step, so it never moves.</summary>
        [Range("0", "1")]
        [UseTranslation(TranslationTarget.OptionValue)]
        public int Stuck { get; set; }

        /// <summary>Gets or sets an option shown only while <see cref="Flag"/> is true.</summary>
        [ShowInSettingUIWhen(nameof(Flag), true)]
        public bool Conditional { get; set; }

        /// <summary>Gets or sets an option whose visibility condition names a missing setting.</summary>
        [ShowInSettingUIWhen("Missing.Path", 1)]
        public bool BrokenCondition { get; set; }

        /// <summary>Gets or sets a boolean option whose value keys use a custom template, still followed by the general table.</summary>
        [UseTranslation(TranslationTarget.OptionValue, "FX_SWITCH_{1}")]
        public bool Switch { get; set; }

        /// <summary>Gets or sets an option whose name uses the general table.</summary>
        [UseGeneralTranslation(TranslationTarget.OptionName)]
        public bool GeneralName { get; set; }

        /// <summary>Gets or sets an option annotated with the translation selection attributes.</summary>
        [UseTranslation(TranslationTarget.OptionName, "FX_CUSTOM_NAME")]
        [UseGeneralTranslation(TranslationTarget.OptionValue)]
        [NoTranslation(TranslationTarget.MenuName)]
        public bool Annotated { get; set; }

        /// <summary>Gets or sets an option hidden from the UI.</summary>
        [HideInSettingUI]
        public bool HiddenFlag { get; set; }

        /// <summary>Gets or sets a static value that is not an instance option.</summary>
        public static bool StaticFlag { get; set; }

        /// <summary>Sets a value that cannot be displayed.</summary>
        public bool WriteOnly
        {
            set
            {
                StaticFlag = value;
            }
        }
    }
}

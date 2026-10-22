using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
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

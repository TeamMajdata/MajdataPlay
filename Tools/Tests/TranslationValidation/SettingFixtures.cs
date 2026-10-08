using System;
using MajdataPlay.Settings.OptionEnumerators;
using MajdataPlay.TranslationValidation;
#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Supplies the exact root settings type name expected by the production metadata scanner.
    /// </summary>
    internal sealed class GameSetting
    {
        /// <summary>
        /// Rejects construction because a setting scanner must not create live setting objects.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if this fixture is constructed.</exception>
        public GameSetting()
        {
            throw ExecutionGuard.Executed(nameof(GameSetting));
        }

        /// <summary>
        /// Gets the game category through a getter that must not be evaluated.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the getter executes.</exception>
        [MenuName("NonKey.MenuAlias")]
        public GameOptions Game
        {
            get
            {
                throw ExecutionGuard.Executed(nameof(Game));
            }
        }

        /// <summary>
        /// Gets the display category through a getter that must not be evaluated.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the getter executes.</exception>
        public DisplayOptions Display
        {
            get
            {
                throw ExecutionGuard.Executed(nameof(Display));
            }
        }

        /// <summary>
        /// Gets audio metadata whose Volume property is the actual visible category.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the getter executes.</exception>
        public SoundOptions Audio
        {
            get
            {
                throw ExecutionGuard.Executed(nameof(Audio));
            }
        }

        /// <summary>
        /// Gets a category excluded from all setting localization keys by its hidden marker.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the getter executes.</exception>
        [HideInSettingUI]
        public HiddenOptions HiddenOnline
        {
            get
            {
                throw ExecutionGuard.Executed(nameof(HiddenOnline));
            }
        }
    }

    /// <summary>
    /// Supplies finite boolean and enum options along with name and description overrides.
    /// </summary>
    internal sealed class GameOptions
    {
        /// <summary>
        /// Gets or sets a boolean whose displayed name and description keys are overridden.
        /// </summary>
        [OptionName("Setting.Name.EnabledOverride")]
        [Description("Setting.Description.EnabledOverride")]
        public bool Enabled
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets an optional boolean whose finite choices include UNSET.
        /// </summary>
        [Optional]
        public bool? OptionalBoolean
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a nullable boolean without an Optional marker or manufactured null choice.
        /// </summary>
        public bool? NullableWithoutOptional
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets an enum with aliases and nonsequential underlying values.
        /// </summary>
        public ModeOption Mode
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets an enum with numeric-looking display names rather than numeric primitives.
        /// </summary>
        public NumericDisplayOption NumericEnum
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a flags enum whose declared values are finite but arbitrary combinations are not.
        /// </summary>
        public AccessOption Access
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a visible option with no description key.
        /// </summary>
        [NoDescription]
        [Description("NonKey.SuppressedDescription")]
        public bool NoDescriptionFlag
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets an entirely hidden option, including hidden override metadata.
        /// </summary>
        [HideInSettingUI]
        [OptionName("NonKey.HiddenOptionName")]
        [Description("NonKey.HiddenOptionDescription")]
        public bool HiddenGameFlag
        {
            get;
            set;
        }

        /// <summary>
        /// Gets privately or sets publicly an option lacking a public getter.
        /// </summary>
        public bool NoPublicGetter
        {
            private get;
            set;
        }

        /// <summary>
        /// Gets or sets an indexed property that the settings UI cannot bind as an option.
        /// </summary>
        /// <param name="index">The ignored index used to identify an unsupported property shape.</param>
        /// <returns>No value because evaluation of a setting fixture is forbidden.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if an accessor executes.</exception>
        public bool this[int index]
        {
            get
            {
                throw ExecutionGuard.Executed("GameOptions.Indexer.get");
            }
            set
            {
                throw ExecutionGuard.Executed("GameOptions.Indexer.set");
            }
        }
    }

    /// <summary>
    /// Supplies numeric, textual, finite custom, and runtime-dependent display options.
    /// </summary>
    internal sealed class DisplayOptions
    {
        /// <summary>
        /// Gets or sets the production-style finite note-mask choice.
        /// </summary>
        [OptionEnumerator(typeof(NoteMaskEnumerator))]
        public string NoteMask
        {
            get;
            set;
        } = "NonKey.DefaultNoteMask";

        /// <summary>
        /// Gets or sets the language choice, whose runtime language names must not be collected.
        /// </summary>
        [OptionEnumerator(typeof(LanguageEnumerator))]
        public string Language
        {
            get;
            set;
        } = "NonKey.DefaultLanguage";

        /// <summary>
        /// Gets or sets a skin identifier discovered at runtime rather than a fixed translation key.
        /// </summary>
        [OptionEnumerator(typeof(SkinEnumerator))]
        public string Skin
        {
            get;
            set;
        } = "NonKey.DefaultSkin";

        /// <summary>
        /// Gets or sets a custom finite string choice supplied through a reassigned local array.
        /// </summary>
        [OptionEnumerator(typeof(ReassignedArrayEnumerator))]
        public string FiniteLocal
        {
            get;
            set;
        } = string.Empty;

        /// <summary>
        /// Gets or sets a custom finite choice whose initializer contains a branch join.
        /// </summary>
        [OptionEnumerator(typeof(BranchArrayEnumerator))]
        public string FiniteBranch
        {
            get;
            set;
        } = string.Empty;

        /// <summary>
        /// Gets or sets numeric strings, which are not textual localization choices.
        /// </summary>
        [OptionEnumerator(typeof(NumericStringEnumerator))]
        public string NumericLabels
        {
            get;
            set;
        } = string.Empty;

        /// <summary>
        /// Gets or sets a dynamically populated custom string choice.
        /// </summary>
        [OptionEnumerator(typeof(DynamicOptionEnumerator))]
        public string DynamicCustom
        {
            get;
            set;
        } = string.Empty;

        /// <summary>
        /// Gets or sets a runtime-computed readonly-array choice without evaluating its initializer.
        /// </summary>
        [OptionEnumerator(typeof(ReadonlyArrayEnumerator))]
        public string ReadonlyCustom
        {
            get;
            set;
        } = string.Empty;

        /// <summary>
        /// Gets or sets a free-form string whose default text is not a fixed option label.
        /// </summary>
        public string FreeText
        {
            get;
            set;
        } = "NonKey.DefaultFreeText";

        /// <summary>
        /// Gets or sets a floating-point value rather than a finite textual choice.
        /// </summary>
        public float Brightness
        {
            get;
            set;
        } = 0.25f;

        /// <summary>
        /// Gets or sets an integer value rather than a finite textual choice.
        /// </summary>
        public int NumericCount
        {
            get;
            set;
        } = 32;

        /// <summary>
        /// Gets or sets a nullable numeric value whose concrete number is not a fixed label.
        /// </summary>
        public decimal? NullableNumeric
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Contains non-volume audio options that the current settings category mapping does not expose.
    /// </summary>
    internal sealed class SoundOptions
    {
        /// <summary>
        /// Gets the actual visible audio category without evaluating a runtime getter.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the getter executes.</exception>
        public SFXVolume Volume
        {
            get
            {
                throw ExecutionGuard.Executed(nameof(Volume));
            }
        }

        /// <summary>
        /// Gets or sets an audio-container boolean that is outside the bound Volume category.
        /// </summary>
        public bool ForceMono
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets an audio-container enum that must not be recursively treated as a visible option.
        /// </summary>
        public ModeOption Backend
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Supplies the properties of the specially bound Audio.Volume category.
    /// </summary>
    internal sealed class SFXVolume
    {
        /// <summary>
        /// Gets or sets a numeric global volume, retaining its name and description but no numeric labels.
        /// </summary>
        public float Global
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a finite boolean proving that Audio.Volume properties are the ones scanned.
        /// </summary>
        public bool Muted
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Supplies a root category whose properties must be excluded altogether.
    /// </summary>
    internal sealed class HiddenOptions
    {
        /// <summary>
        /// Gets or sets a property reachable only through a hidden root category.
        /// </summary>
        public bool HiddenCategoryFlag
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Supplies the independently scanned per-chart setting type with hidden and optional members.
    /// </summary>
    internal sealed class ChartSetting
    {
        /// <summary>
        /// Rejects construction of a chart setting during metadata scanning.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if this fixture is constructed.</exception>
        public ChartSetting()
        {
            throw ExecutionGuard.Executed(nameof(ChartSetting));
        }

        /// <summary>
        /// Gets or sets a hidden chart identifier that must not become a localization key.
        /// </summary>
        [HideInSettingUI]
        public string Hash
        {
            get;
            set;
        } = "NonKey.ChartHash";

        /// <summary>
        /// Gets or sets a visible chart-specific boolean.
        /// </summary>
        public bool DisableVideoBG
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the production-style optional per-chart boolean.
        /// </summary>
        [Optional]
        public bool? SlideSkipping
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a numeric chart value rather than a finite localization choice.
        /// </summary>
        public float AudioOffset
        {
            get;
            set;
        }
    }

    /// <summary>
    /// Provides finite display names, including a duplicate underlying value.
    /// </summary>
    internal enum ModeOption
    {
        /// <summary>
        /// Represents a named negative enum value.
        /// </summary>
        Calm = -5,

        /// <summary>
        /// Represents an alias whose displayed string follows Enum.ToString rather than field enumeration.
        /// </summary>
        CalmAlias = Calm,

        /// <summary>
        /// Represents a separate nonsequential enum value.
        /// </summary>
        Busy = 17
    }

    /// <summary>
    /// Reproduces numeric-looking enum option names such as gameplay rotation choices.
    /// </summary>
    internal enum NumericDisplayOption
    {
        /// <summary>
        /// Represents a zero-degree display choice.
        /// </summary>
        Zero = 0,

        /// <summary>
        /// Represents a named ninety-degree display choice, not the numeric primitive 90.
        /// </summary>
        _90 = 90,

        /// <summary>
        /// Represents a named one-hundred-eighty-degree display choice.
        /// </summary>
        _180 = 180
    }

    /// <summary>
    /// Provides finite declared flags without requiring every possible runtime combination.
    /// </summary>
    [Flags]
    internal enum AccessOption
    {
        /// <summary>
        /// Represents a named empty flag value.
        /// </summary>
        None = 0,

        /// <summary>
        /// Represents the first named flag.
        /// </summary>
        Read = 1,

        /// <summary>
        /// Represents the second named flag.
        /// </summary>
        Write = 2,

        /// <summary>
        /// Represents a declared combination with its own display name.
        /// </summary>
        ReadWrite = Read | Write
    }
}

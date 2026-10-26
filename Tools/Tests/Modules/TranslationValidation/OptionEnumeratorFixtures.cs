using System;
using MajdataPlay.TranslationValidation;
#nullable enable
namespace MajdataPlay.Settings.OptionEnumerators
{
    /// <summary>
    /// Provides the finite-option storage shape used by the production enumerator hierarchy.
    /// </summary>
    internal abstract class OptionEnumeratorBase
    {
        /// <summary>
        /// Stores values assigned by enumerator initialization methods.
        /// </summary>
        protected object[] OptionValues = Array.Empty<object>();

        /// <summary>
        /// Rejects construction because the analyzer should inspect only metadata and IL.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if a fixture enumerator is constructed.</exception>
        protected OptionEnumeratorBase()
        {
            throw ExecutionGuard.Executed(nameof(OptionEnumeratorBase));
        }

        /// <summary>
        /// Defines the method where production enumerators assign their finite option values.
        /// </summary>
        protected abstract void InitInternal();

        /// <summary>
        /// Supplies an opaque runtime string-array source without touching the actual filesystem.
        /// </summary>
        /// <param name="directory">An unrelated directory identifier carried by the fixture IL.</param>
        /// <returns>No values because executing a fixture runtime source is forbidden.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if this method is executed.</exception>
        protected static string[] ReadRuntimeValues(string directory)
        {
            throw ExecutionGuard.Executed(directory);
        }

        /// <summary>
        /// Rejects runtime formatting of option values during static analysis.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if fixture initialization is executed.</exception>
        protected void InitValueTexts()
        {
            throw ExecutionGuard.Executed(nameof(InitValueTexts));
        }
    }

    /// <summary>
    /// Reproduces the production note-mask string array with an unrelated logging literal.
    /// </summary>
    internal sealed class NoteMaskEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Rejects type initialization so reflection cannot be used to obtain runtime values.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if this type initializes at runtime.</exception>
        static NoteMaskEnumerator()
        {
            throw ExecutionGuard.Executed(nameof(NoteMaskEnumerator));
        }

        /// <summary>
        /// Assigns the exact finite labels provided by the production note-mask enumerator.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if value-text initialization is executed.</exception>
        protected override void InitInternal()
        {
            OptionValues = new string[3]
            {
                "Disable",
                "Inner",
                "Outer"
            };
            var unrelated = "NonKey.EnumeratorNoise";
            GC.KeepAlive(unrelated);
            InitValueTexts();
        }
    }

    /// <summary>
    /// Provides numeric string labels that must not generate option localization keys.
    /// </summary>
    internal sealed class NumericStringEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Assigns finite numeric strings rather than fixed textual option labels.
        /// </summary>
        protected override void InitInternal()
        {
            OptionValues = new string[]
            {
                "0",
                "1.25",
                "-2",
                "6e2"
            };
        }
    }

    /// <summary>
    /// Provides an array through a reassigned local to exercise value-flow tracking.
    /// </summary>
    internal sealed class ReassignedArrayEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Assigns only the final local array to the option-value field.
        /// </summary>
        protected override void InitInternal()
        {
            var values = new string[]
            {
                "NonKey.SupersededOption"
            };
            values = new string[]
            {
                "Finite.Local.First",
                "Finite.Local.Second"
            };
            OptionValues = values;
        }
    }

    /// <summary>
    /// Provides different finite arrays on paths that converge at an option-value assignment.
    /// </summary>
    internal sealed class BranchArrayEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Assigns one of two known arrays selected by a runtime branch.
        /// </summary>
        protected override void InitInternal()
        {
            string[] values;
            if (Environment.TickCount > 0)
            {
                values = new string[]
                {
                    "Finite.Branch.Left"
                };
            }
            else
            {
                values = new string[]
                {
                    "Finite.Branch.Right"
                };
            }
            OptionValues = values;
        }
    }

    /// <summary>
    /// Reproduces a language enumerator whose labels come from runtime filesystem contents.
    /// </summary>
    internal sealed class LanguageEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Assigns dynamic labels while containing a path literal that must not be a value label.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the runtime value source executes.</exception>
        protected override void InitInternal()
        {
            OptionValues = ReadRuntimeValues("NonKey.LanguagePath");
        }
    }

    /// <summary>
    /// Reproduces a skin enumerator whose labels are not a finite compile-time set.
    /// </summary>
    internal sealed class SkinEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Assigns runtime filesystem labels rather than collecting the directory-name literal.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the runtime value source executes.</exception>
        protected override void InitInternal()
        {
            OptionValues = ReadRuntimeValues("NonKey.SkinPath");
        }
    }

    /// <summary>
    /// Provides a custom dynamic enumerator unrelated to the well-known skin or language names.
    /// </summary>
    internal sealed class DynamicOptionEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Assigns runtime values while exposing a nearby non-value literal.
        /// </summary>
        /// <exception cref="InvalidOperationException">Always thrown if the runtime value source executes.</exception>
        protected override void InitInternal()
        {
            OptionValues = ReadRuntimeValues("NonKey.DynamicOptionPath");
        }
    }

    /// <summary>
    /// Tests that a static readonly option array is not obtained by executing its initializer.
    /// </summary>
    internal sealed class ReadonlyArrayEnumerator : OptionEnumeratorBase
    {
        /// <summary>
        /// Holds runtime-computed values behind an initializer that is forbidden to execute.
        /// </summary>
        private static readonly string[] s_values = InitializeValues();

        /// <summary>
        /// Assigns the runtime array without providing any statically known string elements.
        /// </summary>
        protected override void InitInternal()
        {
            OptionValues = s_values;
        }

        /// <summary>
        /// Records accidental evaluation of the readonly array initializer.
        /// </summary>
        /// <returns>No values because executing a fixture initializer is forbidden.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if this initializer executes.</exception>
        private static string[] InitializeValues()
        {
            throw ExecutionGuard.Executed(nameof(ReadonlyArrayEnumerator));
        }
    }
}

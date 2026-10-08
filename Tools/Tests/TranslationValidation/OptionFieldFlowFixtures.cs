using System;
using MajdataPlay.TranslationValidation;

#nullable enable
namespace MajdataPlay.Settings.OptionEnumerators
{
    /// <summary>Ensures field writes superseded before localization are not treated as option keys.</summary>
    internal sealed class OverwrittenFieldEnumerator : OptionEnumeratorBase
    {
        /// <summary>Overwrites an unused finite array before leaving initialization.</summary>
        protected override void InitInternal()
        {
            OptionValues = new string[] { "NonKey.OverwrittenField" };
            OptionValues = new string[] { "Finite.Field.Final" };
        }
    }

    /// <summary>Ensures a runtime overwrite invalidates previously known field contents.</summary>
    internal sealed class DynamicOverwriteEnumerator : OptionEnumeratorBase
    {
        /// <summary>Replaces a finite array with values requiring runtime information.</summary>
        /// <exception cref="InvalidOperationException">The runtime provider throws if accidentally executed.</exception>
        protected override void InitInternal()
        {
            OptionValues = new string[] { "NonKey.BeforeDynamicOverwrite" };
            OptionValues = ReadRuntimeValues("NonKey.DynamicOverwritePath");
        }
    }

    /// <summary>Ensures direct known element writes update the tracked field's array.</summary>
    internal sealed class MutatedFieldEnumerator : OptionEnumeratorBase
    {
        /// <summary>Replaces one known element before values can be localized.</summary>
        protected override void InitInternal()
        {
            OptionValues = new string[] { "NonKey.BeforeElementWrite" };
            OptionValues[0] = "Finite.Field.Mutated";
        }
    }

    /// <summary>Ensures an array passed to an opaque helper does not retain stale constant contents.</summary>
    internal sealed class EscapedArrayEnumerator : OptionEnumeratorBase
    {
        /// <summary>Passes the current finite array to a helper whose effects are not interpreted.</summary>
        /// <exception cref="InvalidOperationException">The opaque helper throws if accidentally executed.</exception>
        protected override void InitInternal()
        {
            OptionValues = new string[] { "NonKey.BeforeArrayEscape" };
            RewriteArray(OptionValues);
        }

        /// <summary>Rejects invocation of a helper that might rewrite the array at runtime.</summary>
        /// <param name="values">The array whose contents are no longer statically proven.</param>
        /// <exception cref="InvalidOperationException">Always thrown to detect accidental fixture execution.</exception>
        private static void RewriteArray(object[] values)
        {
            throw ExecutionGuard.Executed(nameof(RewriteArray));
        }
    }
}

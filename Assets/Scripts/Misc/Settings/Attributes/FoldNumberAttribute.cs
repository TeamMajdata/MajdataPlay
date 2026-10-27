using System;

namespace MajdataPlay.Settings
{
#nullable enable
    /// <summary>
    /// Folds large values of a numeric setting option into a short form with a magnitude suffix in the setting UI,
    /// for example <c>1000</c> is shown as <c>1k</c> and <c>1000000</c> as <c>1M</c>.
    /// </summary>
    /// <remarks>
    /// Only integer and floating-point members (including their nullable forms) are folded; the attribute is ignored on
    /// any other member type, including enums. Values whose magnitude is below 1000 keep their normal text. Folding only
    /// changes the displayed text: translation keys of the option values are still built from the unfolded value text, and
    /// a value that has a translation shows that translation instead. Values are folded with SI suffixes (<c>k</c>, <c>M</c>,
    /// <c>G</c>, ...) and at most two decimal places, for example <c>1500</c> is shown as <c>1.5k</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class FoldNumberAttribute : Attribute
    {
    }
}

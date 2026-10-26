using System;
using MajdataPlay.TranslationValidation;
#nullable enable
namespace MajdataPlay
{
    /// <summary>
    /// Reproduces the actual localization extension type without linking Unity or game services.
    /// </summary>
    internal static class StringExtensions
    {
        /// <summary>
        /// Provides the production extension signature for IL matching; it must never execute.
        /// </summary>
        /// <param name="origin">The localization key.</param>
        /// <param name="args">Formatting arguments, which are not localization keys.</param>
        /// <returns>The localized text when implemented by the game.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if fixture code is executed.</exception>
        internal static string i18n(this string origin, params object[] args)
        {
            throw ExecutionGuard.Executed(nameof(i18n));
        }

        /// <summary>
        /// Provides the production try-localization signature without evaluating the output value.
        /// </summary>
        /// <param name="origin">The localization key.</param>
        /// <param name="result">The text written by the runtime localization service.</param>
        /// <returns>Whether a translation was found when implemented by the game.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if fixture code is executed.</exception>
        internal static bool Tryi18n(this string origin, out string result)
        {
            throw ExecutionGuard.Executed(nameof(Tryi18n));
        }
    }
}

namespace MajdataPlay.i18n
{
    /// <summary>
    /// Reproduces the exact localization service type and method signature used by the game.
    /// </summary>
    internal static class Localization
    {
        /// <summary>
        /// Provides an IL target without executing game localization.
        /// </summary>
        /// <param name="key">The localization key to resolve.</param>
        /// <param name="result">The runtime translation output, not a second key.</param>
        /// <returns>Whether a translation was found when implemented by the game.</returns>
        /// <exception cref="InvalidOperationException">Always thrown if fixture code is executed.</exception>
        internal static bool TryGetLocalizedText(string key, out string result)
        {
            throw ExecutionGuard.Executed(nameof(TryGetLocalizedText));
        }
    }
}

namespace MajdataPlay.Extensions
{
    /// <summary>
    /// Deliberately uses the previously mistaken namespace to catch loose type-name matching.
    /// </summary>
    internal static class StringExtensions
    {
        /// <summary>
        /// Represents an unrelated method with the same short name as the real extension.
        /// </summary>
        /// <param name="origin">An unrelated string that must not be collected.</param>
        /// <param name="args">Unrelated formatting arguments.</param>
        /// <returns>The input string without localization.</returns>
        internal static string i18n(string origin, params object[] args)
        {
            return origin;
        }

        /// <summary>
        /// Represents an unrelated try method in the wrong namespace.
        /// </summary>
        /// <param name="origin">An unrelated string that must not be collected.</param>
        /// <param name="result">The copied input value.</param>
        /// <returns>Always returns false because this is not a localization operation.</returns>
        internal static bool Tryi18n(string origin, out string result)
        {
            result = origin;
            return false;
        }
    }
}

namespace MajdataPlay.TranslationValidation.Decoys
{
    /// <summary>
    /// Represents an unrelated service with the same short type and method names.
    /// </summary>
    internal static class Localization
    {
        /// <summary>
        /// Copies text without looking up a localization key.
        /// </summary>
        /// <param name="key">Unrelated text that must not be collected.</param>
        /// <param name="result">The copied text.</param>
        /// <returns>Always returns false because this service is unrelated.</returns>
        internal static bool TryGetLocalizedText(string key, out string result)
        {
            result = key;
            return false;
        }
    }
}

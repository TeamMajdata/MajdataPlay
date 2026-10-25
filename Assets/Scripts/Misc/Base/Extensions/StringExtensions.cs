
using MajdataPlay.Diagnostics;
using MajdataPlay.i18n;
using System;

namespace MajdataPlay
{
    internal static class StringExtensions
    {
        /// <summary>
        /// Translates a key in the current language and optionally formats the translation.
        /// </summary>
        /// <remarks>
        /// A missing key is reported once per language and then returned as-is.
        /// </remarks>
        /// <param name="origin">The localization key.</param>
        /// <param name="args">The format arguments substituted into the translation, if any.</param>
        /// <returns>The formatted translation, or <paramref name="origin"/> when the key is missing.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="origin"/> is null.</exception>
        public static string i18n(this string origin, params object[] args)
        {
            Localization.TryGetLocalizedText(origin, out var result);
            if (args == null || args.Length == 0 || !result.Contains('{'))
            {
                return result;
            }
            try
            {
                return string.Format(result, args);
            }
            catch (Exception e)
            {
                MajDebug.LogError($"i18n format failed: key={origin}, template={result}");
                MajDebug.LogError(e);
                return result;
            }
        }

        /// <summary>
        /// Looks up the translation of a key in the current language without reporting a missing key.
        /// </summary>
        /// <remarks>
        /// The caller handles a miss, typically by trying another key, so a miss is not logged; use
        /// <see cref="i18n(string, object[])"/> when a missing translation should be reported.
        /// </remarks>
        /// <param name="origin">The localization key.</param>
        /// <param name="result">Receives the translation, or <paramref name="origin"/> when the key is missing.</param>
        /// <returns>True when the current language translates <paramref name="origin"/>; otherwise false.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="origin"/> is null.</exception>
        public static bool Tryi18n(this string origin, out string result)
        {
            return Localization.TryFindLocalizedText(origin, out result);
        }
    }
}

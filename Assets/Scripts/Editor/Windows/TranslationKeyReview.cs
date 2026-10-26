using System;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>
    /// The change a key review candidate proposes for the translation template.
    /// </summary>
    internal enum TranslationKeyChange
    {
        /// <summary>
        /// The scan found the key, but the template does not contain it.
        /// </summary>
        Add,

        /// <summary>
        /// The template contains the key, but the scan did not find it.
        /// </summary>
        Remove,
    }

    /// <summary>
    /// One proposed template change, which the user confirms by checking it before it is applied.
    /// </summary>
    [Serializable]
    internal sealed class TranslationKeyCandidate
    {
        /// <summary>
        /// The localization key.
        /// </summary>
        public string Key = string.Empty;

        /// <summary>
        /// Whether the key would be added to or removed from the template.
        /// </summary>
        public TranslationKeyChange Change;

        /// <summary>
        /// Whether the key is optional: an addition found only as a numeric option value, which displays the number when untranslated.
        /// </summary>
        public bool IsOptional;

        /// <summary>
        /// Whether the user confirmed the change.
        /// </summary>
        public bool IsChecked;

        /// <summary>
        /// The sources of an addition, or the reason for a removal, one per line.
        /// </summary>
        public string Details = string.Empty;
    }

    /// <summary>
    /// Compares the scanned keys with the translation template and applies the changes the user confirmed.
    /// </summary>
    internal static class TranslationKeyReview
    {
        /// <summary>
        /// The details of every removal candidate.
        /// </summary>
        internal const string RemovalReason =
            "Not found by the current scan of code, setting metadata, scenes and prefabs. It may still be used by dynamic keys, " +
            "other platform branches or external data, so confirm it before removing it.";

        /// <summary>
        /// Builds the candidates that turn the template into the scanned key set, sorted by change and key.
        /// </summary>
        /// <remarks>
        /// Required additions start checked. Optional additions, which only numeric option values need, and removals,
        /// which the scan cannot prove, start unchecked so that the user confirms them explicitly.
        /// </remarks>
        /// <param name="scannedKeys">The keys found by the scan, mapped to their sources.</param>
        /// <param name="template">The current template keys.</param>
        /// <returns>The additions followed by the removals.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static List<TranslationKeyCandidate> Build(IReadOnlyDictionary<string, HashSet<string>> scannedKeys, IEnumerable<string> template)
        {
            if (scannedKeys is null)
            {
                throw new ArgumentNullException(nameof(scannedKeys));
            }
            if (template is null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            var templateKeys = new HashSet<string>(template, StringComparer.Ordinal);
            var candidates = new List<TranslationKeyCandidate>();
            foreach (var pair in scannedKeys.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (templateKeys.Contains(pair.Key))
                {
                    continue;
                }
                var isOptional = pair.Value.Count != 0 && pair.Value.All(TranslationSettingAnalyzer.IsNumericValueSource);
                candidates.Add(new TranslationKeyCandidate
                {
                    Key = pair.Key,
                    Change = TranslationKeyChange.Add,
                    IsOptional = isOptional,
                    IsChecked = !isOptional,
                    Details = string.Join(Environment.NewLine, pair.Value.OrderBy(source => source, StringComparer.Ordinal)),
                });
            }
            foreach (var key in templateKeys.OrderBy(key => key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(key) || scannedKeys.ContainsKey(key))
                {
                    continue;
                }
                candidates.Add(new TranslationKeyCandidate
                {
                    Key = key,
                    Change = TranslationKeyChange.Remove,
                    Details = RemovalReason,
                });
            }
            return candidates;
        }

        /// <summary>
        /// Applies the checked candidates to the template and removes them from the candidate list.
        /// </summary>
        /// <param name="template">The template keys, edited in place; additions are appended.</param>
        /// <param name="candidates">The candidates, from which the applied ones are removed.</param>
        /// <returns>The number of keys added to and removed from the template.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        internal static (int Added, int Removed) Apply(List<string> template, List<TranslationKeyCandidate> candidates)
        {
            if (template is null)
            {
                throw new ArgumentNullException(nameof(template));
            }
            if (candidates is null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            var existing = new HashSet<string>(template, StringComparer.Ordinal);
            var removals = new HashSet<string>(StringComparer.Ordinal);
            var added = 0;
            foreach (var candidate in candidates)
            {
                if (!candidate.IsChecked)
                {
                    continue;
                }
                if (candidate.Change == TranslationKeyChange.Add)
                {
                    if (existing.Add(candidate.Key))
                    {
                        template.Add(candidate.Key);
                        added++;
                    }
                }
                else
                {
                    removals.Add(candidate.Key);
                }
            }
            var removed = template.RemoveAll(removals.Contains);
            candidates.RemoveAll(candidate => candidate.IsChecked);
            return (added, removed);
        }

        /// <summary>
        /// Sets the checked state of several candidates at once.
        /// </summary>
        /// <param name="candidates">The candidates to update.</param>
        /// <param name="isChecked">The new checked state.</param>
        /// <returns>The number of candidates whose state changed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is null.</exception>
        internal static int SetChecked(IEnumerable<TranslationKeyCandidate> candidates, bool isChecked)
        {
            if (candidates is null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }
            var changed = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.IsChecked != isChecked)
                {
                    candidate.IsChecked = isChecked;
                    changed++;
                }
            }
            return changed;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers <see cref="TranslationKeyReview"/>, the reviewed diff between scanned keys and the translation template.</summary>
    public class TranslationKeyReviewTests
    {
        /// <summary>Creates a scanned key inventory.</summary>
        /// <param name="entries">The keys and their sources.</param>
        /// <returns>The inventory.</returns>
        private static Dictionary<string, HashSet<string>> Scanned(params (string Key, string[] Sources)[] entries)
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (key, sources) in entries)
            {
                keys.Add(key, new HashSet<string>(sources, StringComparer.Ordinal));
            }
            return keys;
        }

        /// <summary>Checks the proposed additions and removals, their order and their default check state.</summary>
        [Test]
        public void Build_ProposesAdditionsAndRemovals()
        {
            var scanned = Scanned(
                ("KEEP", new[] { "A.Method IL_0000" }),
                ("NEW_B", new[] { "B.Method IL_0001" }),
                ("NEW_A", new[] { "Menu.Option" + TranslationSettingAnalyzer.NumericValueUsage }),
                ("MIXED", new[] { "Menu.Option" + TranslationSettingAnalyzer.NumericValueFallbackUsage, "C.Method IL_0002" }));

            var candidates = TranslationKeyReview.Build(scanned, new[] { "KEEP", "OLD_Z", "OLD_Y", " " });

            Assert.That(candidates.Select(candidate => candidate.Key), Is.EqualTo(new[] { "MIXED", "NEW_A", "NEW_B", "OLD_Y", "OLD_Z" }));
            var mixed = candidates[0];
            Assert.That(mixed.Change, Is.EqualTo(TranslationKeyChange.Add));
            Assert.That(mixed.IsOptional, Is.False);
            Assert.That(mixed.IsChecked, Is.True);
            Assert.That(mixed.Details, Does.Contain("C.Method IL_0002"));
            var optional = candidates[1];
            Assert.That(optional.IsOptional, Is.True);
            Assert.That(optional.IsChecked, Is.False);
            Assert.That(candidates[2].IsChecked, Is.True);
            var removal = candidates[3];
            Assert.That(removal.Change, Is.EqualTo(TranslationKeyChange.Remove));
            Assert.That(removal.IsChecked, Is.False);
            Assert.That(removal.Details, Is.EqualTo(TranslationKeyReview.RemovalReason));
        }

        /// <summary>Checks that the template already matching the scan yields no candidates.</summary>
        [Test]
        public void Build_MatchingTemplate_HasNoCandidates()
        {
            var candidates = TranslationKeyReview.Build(Scanned(("A", new[] { "s" })), new[] { "A" });

            Assert.That(candidates, Is.Empty);
            Assert.Throws<ArgumentNullException>(() => TranslationKeyReview.Build(null!, Array.Empty<string>()));
            Assert.Throws<ArgumentNullException>(() => TranslationKeyReview.Build(Scanned(), null!));
        }

        /// <summary>Checks that only checked candidates are applied and that applied ones leave the review.</summary>
        [Test]
        public void Apply_AppliesCheckedCandidatesOnly()
        {
            var template = new List<string> { "KEEP", "OLD_Y", "OLD_Z" };
            var candidates = TranslationKeyReview.Build(
                Scanned(("KEEP", new[] { "s" }), ("NEW", new[] { "s" }), ("OPTIONAL", new[] { "o" + TranslationSettingAnalyzer.NumericValueUsage })),
                template);
            candidates.Single(candidate => candidate.Key == "OLD_Z").IsChecked = true;

            var (added, removed) = TranslationKeyReview.Apply(template, candidates);

            Assert.That(added, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(template, Is.EqualTo(new[] { "KEEP", "OLD_Y", "NEW" }));
            Assert.That(candidates.Select(candidate => candidate.Key), Is.EquivalentTo(new[] { "OPTIONAL", "OLD_Y" }));
            Assert.That(TranslationKeyReview.Apply(template, candidates), Is.EqualTo((0, 0)));
            Assert.Throws<ArgumentNullException>(() => TranslationKeyReview.Apply(null!, candidates));
            Assert.Throws<ArgumentNullException>(() => TranslationKeyReview.Apply(template, null!));
        }

        /// <summary>Checks that an addition already present in the template is not duplicated.</summary>
        [Test]
        public void Apply_DoesNotDuplicateKeys()
        {
            var template = new List<string> { "A" };
            var candidates = new List<TranslationKeyCandidate>
            {
                new TranslationKeyCandidate { Key = "A", Change = TranslationKeyChange.Add, IsChecked = true },
                new TranslationKeyCandidate { Key = "B", Change = TranslationKeyChange.Add, IsChecked = true },
                new TranslationKeyCandidate { Key = "B", Change = TranslationKeyChange.Add, IsChecked = true },
            };

            var (added, removed) = TranslationKeyReview.Apply(template, candidates);

            Assert.That(added, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(0));
            Assert.That(template, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(candidates, Is.Empty);
        }

        /// <summary>Checks the batch check state change and its count of changed candidates.</summary>
        [Test]
        public void SetChecked_ChangesSeveralCandidates()
        {
            var candidates = new[]
            {
                new TranslationKeyCandidate { Key = "A", IsChecked = true },
                new TranslationKeyCandidate { Key = "B" },
                new TranslationKeyCandidate { Key = "C" },
            };

            Assert.That(TranslationKeyReview.SetChecked(candidates, true), Is.EqualTo(2));
            Assert.That(candidates.All(candidate => candidate.IsChecked), Is.True);
            Assert.That(TranslationKeyReview.SetChecked(candidates.Take(1), false), Is.EqualTo(1));
            Assert.That(candidates[0].IsChecked, Is.False);
            Assert.Throws<ArgumentNullException>(() => TranslationKeyReview.SetChecked(null!, true));
        }
    }
}

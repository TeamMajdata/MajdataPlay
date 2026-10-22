using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>
    /// Covers the IL-based localization key analyzer against the fixtures in <see cref="TranslationCallFixtures"/>
    /// and the option enumerator shapes in OptionValueFixtures.
    /// </summary>
    public class TranslationCallAnalyzerTests
    {
        /// <summary>The keys discovered in the test assembly, shared by the tests.</summary>
        private static Dictionary<string, HashSet<string>>? s_keys;

        /// <summary>The diagnostics produced while analyzing the test assembly.</summary>
        private static List<string>? s_diagnostics;

        /// <summary>The full name of the fixture type, used to filter sources.</summary>
        private static readonly string s_fixtureName = typeof(TranslationCallFixtures).FullName!;

        /// <summary>Analyzes the assembly containing the fixtures once.</summary>
        [OneTimeSetUp]
        public void AnalyzeFixtures()
        {
            if (s_keys is not null)
            {
                return;
            }
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            TranslationCallAnalyzer.Collect(typeof(TranslationCallFixtures).Assembly, keys, diagnostics);
            s_keys = keys;
            s_diagnostics = diagnostics;
        }

        /// <summary>Gets the fixture keys found by the analysis.</summary>
        private static IEnumerable<string> FixtureKeys => s_keys!.Keys.Where(key => key.StartsWith("MajTest", StringComparison.Ordinal));

        /// <summary>Checks that every supported entry point reports its literal key.</summary>
        [Test]
        public void Collect_FindsEveryEntryPoint()
        {
            Assert.That(s_keys!.Keys, Does.Contain("MajTestCall.Extension"));
            Assert.That(s_keys.Keys, Does.Contain("MajTestCall.Static"));
            Assert.That(s_keys.Keys, Does.Contain("MajTestCall.Constant"));
            Assert.That(s_keys.Keys, Does.Contain("MajTestCall.Try"));
            Assert.That(s_keys.Keys, Does.Contain("MajTestCall.Service"));
        }

        /// <summary>Checks that keys follow local reassignment and branch, switch and loop joins.</summary>
        [Test]
        public void Collect_TracksLocalsAndControlFlow()
        {
            Assert.That(s_keys!.Keys, Is.SupersetOf(new[]
            {
                "MajTestCall.LocalFirst",
                "MajTestCall.LocalSecond",
                "MajTestCall.BranchLeft",
                "MajTestCall.BranchRight",
                "MajTestCall.SwitchZero",
                "MajTestCall.SwitchOne",
                "MajTestCall.SwitchOther",
                "MajTestCall.LoopStart",
                "MajTestCall.LoopNext",
                "MajTestCall.InTry",
            }));
        }

        /// <summary>Checks that literal concatenations, readonly initializers and constant array reads are resolved.</summary>
        [Test]
        public void Collect_ResolvesConcatenationFieldsAndArrays()
        {
            Assert.That(s_keys!.Keys, Is.SupersetOf(new[]
            {
                "MajTestCall.ConcatPair",
                "MajTestCall.ConcatTriple",
                "MajTestCall.Readonly",
                "MajTestCall.ArraySecond",
                "MajTestCall.Format",
            }));
        }

        /// <summary>Checks that compiler-generated async and lambda bodies are analyzed.</summary>
        [Test]
        public void Collect_IncludesGeneratedMethods()
        {
            Assert.That(s_keys!.Keys, Does.Contain("MajTestCall.Async"));
            Assert.That(s_keys.Keys, Does.Contain("MajTestCall.Lambda"));
            Assert.That(s_keys["MajTestCall.Async"].Single(), Does.Contain("MoveNext"));
        }

        /// <summary>Checks that nearby or unrelated strings are never reported as keys.</summary>
        [Test]
        public void Collect_IgnoresNonKeys()
        {
            Assert.That(FixtureKeys.Where(key => key.StartsWith("MajTestNonKey", StringComparison.Ordinal)), Is.Empty);
        }

        /// <summary>Checks that a key used twice keeps both sources with method names and IL offsets.</summary>
        [Test]
        public void Collect_KeepsEverySource()
        {
            var sources = s_keys!["MajTestCall.Shared"];

            Assert.That(sources.Count, Is.EqualTo(2));
            Assert.That(sources.Any(source => source.StartsWith(s_fixtureName + ".SharedFirst IL_", StringComparison.Ordinal)), Is.True);
            Assert.That(sources.Any(source => source.StartsWith(s_fixtureName + ".SharedSecond IL_", StringComparison.Ordinal)), Is.True);
        }

        /// <summary>Checks that dynamic and mixed keys are reported as unresolved and their literal parts are not keys.</summary>
        [Test]
        public void Collect_ReportsDynamicKeys()
        {
            var unresolved = s_diagnostics!.Where(diagnostic => diagnostic.StartsWith("Unresolved localization key: " + s_fixtureName, StringComparison.Ordinal)).ToArray();

            Assert.That(unresolved.Any(diagnostic => diagnostic.Contains(".Dynamic IL_")), Is.True);
            Assert.That(unresolved.Any(diagnostic => diagnostic.Contains(".MixedDynamic IL_")), Is.True);
            Assert.That(unresolved.Any(diagnostic => diagnostic.Contains(".Fields IL_")), Is.True);
            Assert.That(unresolved.All(diagnostic => diagnostic.Contains("MajdataPlay.StringExtensions.i18n")), Is.True);
            Assert.That(s_keys!.Keys, Does.Not.Contain("MajTestCall.MixedLiteral"));
        }

        /// <summary>Checks that the analyzer accumulates into existing inventories instead of replacing them.</summary>
        [Test]
        public void Collect_AddsToExistingInventory()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                { "MajTestCall.Extension", new HashSet<string>(StringComparer.Ordinal) { "manual" } },
                { "Manual.Key", new HashSet<string>(StringComparer.Ordinal) },
            };

            TranslationCallAnalyzer.Collect(typeof(TranslationCallFixtures).Assembly, keys, new List<string>());

            Assert.That(keys["MajTestCall.Extension"], Does.Contain("manual"));
            Assert.That(keys["MajTestCall.Extension"].Count, Is.GreaterThan(1));
            Assert.That(keys.ContainsKey("Manual.Key"), Is.True);
        }

        /// <summary>Checks that the production runtime assembly contains discoverable keys.</summary>
        [Test]
        public void Collect_FindsKeysInRuntimeAssembly()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            TranslationCallAnalyzer.Collect(typeof(MajdataPlay.Scenes.List.CoverListManager).Assembly, keys, diagnostics);

            Assert.That(keys.Keys, Does.Contain("MAJTEXT_LIST_EMPTY_COLLECTION"));
            Assert.That(keys["MAJTEXT_LIST_EMPTY_COLLECTION"].Any(source => source.Contains("CoverListManager")), Is.True);
        }

        /// <summary>Checks argument validation.</summary>
        [Test]
        public void Collect_RejectsNullArguments()
        {
            var assembly = typeof(TranslationCallFixtures).Assembly;
            var keys = new Dictionary<string, HashSet<string>>();
            var diagnostics = new List<string>();

            Assert.That(() => TranslationCallAnalyzer.Collect(null!, keys, diagnostics), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("assembly"));
            Assert.That(() => TranslationCallAnalyzer.Collect(assembly, null!, diagnostics), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("keys"));
            Assert.That(() => TranslationCallAnalyzer.Collect(assembly, keys, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("diagnostics"));
        }

        /// <summary>Checks that literal option arrays are returned sorted and deduplicated.</summary>
        [Test]
        public void CollectOptionValueStrings_ReturnsSortedLiterals()
        {
            var diagnostics = new List<string>();

            var values = TranslationCallAnalyzer.CollectOptionValueStrings(typeof(LiteralOptionFixture), diagnostics);

            Assert.That(values, Is.EqualTo(new[] { "42", "Disable", "Inner", "Outer" }));
        }

        /// <summary>Checks that both arrays of a branch are returned.</summary>
        [Test]
        public void CollectOptionValueStrings_JoinsBranches()
        {
            var values = TranslationCallAnalyzer.CollectOptionValueStrings(typeof(BranchOptionFixture), new List<string>());

            Assert.That(values, Is.EqualTo(new[] { "BranchA", "BranchB" }));
        }

        /// <summary>Checks that values from opaque helpers are reported and not returned.</summary>
        [Test]
        public void CollectOptionValueStrings_ReportsDynamicValues()
        {
            var diagnostics = new List<string>();

            var values = TranslationCallAnalyzer.CollectOptionValueStrings(typeof(DynamicOptionFixture), diagnostics);

            Assert.That(values, Is.Empty);
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Contains("no proven current literal OptionValues array")), Is.True);
        }

        /// <summary>Checks that types without the enumerator shape are reported.</summary>
        [Test]
        public void CollectOptionValueStrings_ReportsMissingShape()
        {
            var diagnostics = new List<string>();

            var values = TranslationCallAnalyzer.CollectOptionValueStrings(typeof(MissingOptionFixture), diagnostics);

            Assert.That(values, Is.Empty);
            Assert.That(diagnostics.Single(), Does.Contain("has no inspectable InitInternal/OptionValues pair"));
        }

        /// <summary>Checks that an empty literal array yields no values and no diagnostics.</summary>
        [Test]
        public void CollectOptionValueStrings_EmptyArrayYieldsNothing()
        {
            var diagnostics = new List<string>();

            var values = TranslationCallAnalyzer.CollectOptionValueStrings(typeof(EmptyOptionFixture), diagnostics);

            Assert.That(values, Is.Empty);
            Assert.That(diagnostics, Is.Empty);
        }

        /// <summary>Checks argument validation of the option value extractor.</summary>
        [Test]
        public void CollectOptionValueStrings_RejectsNullArguments()
        {
            Assert.That(() => TranslationCallAnalyzer.CollectOptionValueStrings(null!, new List<string>()), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("enumeratorType"));
            Assert.That(() => TranslationCallAnalyzer.CollectOptionValueStrings(typeof(LiteralOptionFixture), null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("diagnostics"));
        }
    }
}

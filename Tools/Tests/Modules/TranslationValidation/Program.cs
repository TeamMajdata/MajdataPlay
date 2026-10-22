using System;
using System.Collections.Generic;
using MajdataPlay.Editor.Windows;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
#nullable enable
namespace MajdataPlay.TranslationValidation
{
    /// <summary>
    /// Runs dependency-free regressions against the linked production translation analyzers.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Counts all assertions, including assertions in failed test groups.
        /// </summary>
        private static int s_assertions;

        /// <summary>
        /// Counts test groups that failed without suppressing later groups.
        /// </summary>
        private static int s_failures;

        /// <summary>
        /// Runs every regression and reports an unambiguous process exit code.
        /// </summary>
        /// <returns>Zero if all groups pass, or one if an assertion or scan fails.</returns>
        private static int Main()
        {
            try
            {
                var calls = CollectCalls();
                var settings = CollectSettings();
                Run("literal targets, constants, locals, and reassignment", () =>
                {
                    LiteralAndLocalCalls(calls);
                });
                Run("branch and switch joins, string concatenation", () =>
                {
                    BranchAndConcatCalls(calls);
                });
                Run("unrelated strings, format payloads, out values, and wrong declaring types", () =>
                {
                    NonKeyExclusion(calls);
                });
                Run("nested methods, lambdas, async and coroutine state machines", () =>
                {
                    CompilerGeneratedCalls(calls);
                });
                Run("dynamic arguments, out invalidation, and readonly diagnostics", () =>
                {
                    DynamicCallDiagnostics(calls);
                });
                Run("custom finite option arrays, local reassignment, and branch joins", FiniteOptionArrays);
                Run("dynamic option arrays and guarded readonly initializers", DynamicOptionArrays);
                Run("option field overwrites, element mutations and escaped arrays", OptionFieldFlow);
                Run("setting category mapping and metadata name/description overrides", () =>
                {
                    SettingNamesAndDescriptions(settings);
                });
                Run("finite bool, nullable bool, enum, numeric-looking enum, and custom labels", () =>
                {
                    SettingFiniteValues(settings);
                });
                Run("hidden, numeric, dynamic skin/language, indexer and getter exclusions", () =>
                {
                    SettingExclusions(settings);
                });
                Run("case-sensitive inventories preserve existing keys, sources and diagnostics", InventoryMerging);
                Run("public analyzer argument validation", ArgumentValidation);
                Run("no localization, setting, attribute, getter, constructor or initializer execution", () =>
                {
                    Check(ExecutionGuard.ExecutionCount == 0, "Static analysis executed guarded fixture code.");
                });
                if (s_failures != 0)
                {
                    Console.Error.WriteLine($"TRANSLATION_VALIDATION_FAILED ({s_failures} groups, {s_assertions} assertions)");
                    return 1;
                }
                Console.WriteLine($"TRANSLATION_VALIDATION_PASSED ({s_assertions} assertions)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"TRANSLATION_VALIDATION_FAILED (unexpected scan error): {exception}");
                return 1;
            }
        }

        /// <summary>
        /// Executes a test group while allowing independent groups to report their own failures.
        /// </summary>
        /// <param name="name">The human-readable test group name.</param>
        /// <param name="test">The assertions to execute.</param>
        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine($"PASS: {name}");
            }
            catch (Exception exception)
            {
                s_failures++;
                Console.Error.WriteLine($"FAIL: {name}: {exception.Message}");
            }
        }

        /// <summary>
        /// Counts an assertion and raises a contextual failure if its condition is false.
        /// </summary>
        /// <param name="condition">The assertion result.</param>
        /// <param name="message">The failure description.</param>
        /// <exception cref="InvalidOperationException">The assertion condition is false.</exception>
        private static void Check(bool condition, string message)
        {
            s_assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Scans the harness assembly for exact production localization call signatures.
        /// </summary>
        /// <returns>The discovered call keys and dynamic-analysis diagnostics.</returns>
        private static AnalysisSnapshot CollectCalls()
        {
            var snapshot = new AnalysisSnapshot();
            TranslationCallAnalyzer.Collect(typeof(CallFixtures).Assembly, snapshot.Keys, snapshot.Diagnostics);
            return snapshot;
        }

        /// <summary>
        /// Scans the harness's production-named setting metadata without creating setting instances.
        /// </summary>
        /// <returns>The discovered setting keys and runtime-dependent-value diagnostics.</returns>
        private static AnalysisSnapshot CollectSettings()
        {
            var snapshot = new AnalysisSnapshot();
            TranslationSettingAnalyzer.Collect(typeof(GameSetting).Assembly, snapshot.Keys, snapshot.Diagnostics);
            return snapshot;
        }

        /// <summary>
        /// Requires a set of keys and their nonempty source inventories.
        /// </summary>
        /// <param name="snapshot">The inventory to inspect.</param>
        /// <param name="keys">The case-sensitive keys that must be present.</param>
        /// <exception cref="InvalidOperationException">A required key or its source information is missing.</exception>
        private static void RequireKeys(AnalysisSnapshot snapshot, params string[] keys)
        {
            foreach (var key in keys)
            {
                Check(snapshot.Keys.TryGetValue(key, out var sources), $"Missing fixed localization key: {key}.");
                Check(sources is not null && sources.Count > 0, $"Key has no source locations: {key}.");
            }
        }

        /// <summary>
        /// Requires that unrelated strings have not been added as localization keys.
        /// </summary>
        /// <param name="snapshot">The inventory to inspect.</param>
        /// <param name="keys">The strings that must be absent.</param>
        /// <exception cref="InvalidOperationException">An unrelated or excluded string was collected.</exception>
        private static void RequireAbsent(AnalysisSnapshot snapshot, params string[] keys)
        {
            foreach (var key in keys)
            {
                Check(!snapshot.Keys.ContainsKey(key), $"Collected an unrelated or excluded key: {key}.");
            }
        }

        /// <summary>
        /// Requires that an excluded property or numeric value family has no localization keys.
        /// </summary>
        /// <param name="snapshot">The inventory to inspect.</param>
        /// <param name="prefix">The case-sensitive key prefix to exclude.</param>
        /// <exception cref="InvalidOperationException">A key begins with the excluded prefix.</exception>
        private static void RequireNoPrefix(AnalysisSnapshot snapshot, string prefix)
        {
            var excludedKey = string.Empty;
            foreach (var key in snapshot.Keys.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    excludedKey = key;
                    break;
                }
            }
            Check(excludedKey.Length == 0, $"Excluded key family was collected: {excludedKey}.");
        }

        /// <summary>
        /// Requires an actionable diagnostic identifying an unresolved fixture member.
        /// </summary>
        /// <param name="diagnostics">The analyzer diagnostics to inspect.</param>
        /// <param name="member">The member or type name that should appear in a diagnostic.</param>
        /// <exception cref="InvalidOperationException">No diagnostic identifies the unresolved member.</exception>
        private static void RequireDiagnostic(IReadOnlyList<string> diagnostics, string member)
        {
            var found = false;
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.IndexOf(member, StringComparison.Ordinal) >= 0)
                {
                    found = true;
                    break;
                }
            }
            Check(found, $"No dynamic-analysis diagnostic identifies {member}.");
        }

        /// <summary>
        /// Checks exact signatures and straight-line constant propagation.
        /// </summary>
        /// <param name="calls">The call-key snapshot.</param>
        /// <exception cref="InvalidOperationException">A required key or origin is missing.</exception>
        private static void LiteralAndLocalCalls(AnalysisSnapshot calls)
        {
            RequireKeys(calls,
                "Call.Literal", "Call.Extension", "Call.Const", "Call.Try", "Call.Localization",
                "Call.Local", "Call.Assigned", "Call.Reassigned", "Call.FormatKey", "Call.OutKey", "Call.TryOutKey",
                "Call.OutProducesDynamic", "Call.Shared", "Call.Case", "call.case");
            Check(calls.Keys["Call.Shared"].Count >= 2, "Two methods using Call.Shared should retain distinct origins.");
        }

        /// <summary>
        /// Checks finite alternatives at branch joins and runtime concatenation of known strings.
        /// </summary>
        /// <param name="calls">The call-key snapshot.</param>
        /// <exception cref="InvalidOperationException">A branch alternative or concatenated key is missing.</exception>
        private static void BranchAndConcatCalls(AnalysisSnapshot calls)
        {
            RequireKeys(calls, "Call.Branch.Left", "Call.Branch.Right",
                "Call.Switch.Zero", "Call.Switch.One", "Call.Switch.Other",
                "Call.Concat.Pair", "Call.Concat.Triple", "Call.Operator.Concat");
            RequireAbsent(calls, "Call.Concat.", "Pair", "Tri", "ple", "Call.Operator.", "Concat");
        }

        /// <summary>
        /// Checks that only the first argument of exact localization methods is a key.
        /// </summary>
        /// <param name="calls">The call-key snapshot.</param>
        /// <exception cref="InvalidOperationException">A payload, stale out value, or decoy argument was collected.</exception>
        private static void NonKeyExclusion(AnalysisSnapshot calls)
        {
            RequireAbsent(calls, "NonKey.UnusedNearby", "NonKey.OverwrittenLocal", "NonKey.FormatPayload",
                "NonKey.OutputSeed", "NonKey.TryOutputSeed", "NonKey.StaleOutLocal", "NonKey.DynamicNearby",
                "NonKey.PoisonedNearby", "NonKey.WrongNamespace", "NonKey.WrongTryNamespace", "NonKey.WrongLocalizationType");
        }

        /// <summary>
        /// Checks nested types and compiler-generated bodies that contain fixed localization calls.
        /// </summary>
        /// <param name="calls">The call-key snapshot.</param>
        /// <exception cref="InvalidOperationException">A generated or nested method key is missing.</exception>
        private static void CompilerGeneratedCalls(AnalysisSnapshot calls)
        {
            RequireKeys(calls, "Call.Nested", "Call.Lambda", "Call.Async.AfterAwait",
                "Call.Coroutine.BeforeYield", "Call.Coroutine.AfterYield");
        }

        /// <summary>
        /// Checks dynamic diagnostics without requiring unsafe evaluation of readonly fields.
        /// </summary>
        /// <param name="calls">The call-key snapshot.</param>
        /// <exception cref="InvalidOperationException">An unresolved call lacks a diagnostic or fixture code ran.</exception>
        private static void DynamicCallDiagnostics(AnalysisSnapshot calls)
        {
            RequireDiagnostic(calls.Diagnostics, nameof(CallFixtures.DynamicArgument));
            RequireDiagnostic(calls.Diagnostics, nameof(CallFixtures.MixedDynamicBranch));
            RequireDiagnostic(calls.Diagnostics, nameof(CallFixtures.OutValueBecomesDynamic));
            RequireDiagnostic(calls.Diagnostics, nameof(CallFixtures.PoisonedReadonly));
            if (!calls.Keys.ContainsKey("Call.Readonly.Known"))
            {
                RequireDiagnostic(calls.Diagnostics, nameof(CallFixtures.LiteralReadonly));
            }
            Check(ExecutionGuard.ExecutionCount == 0, "Readonly field scanning executed a fixture initializer.");
        }

        /// <summary>
        /// Checks finite option array extraction without treating arbitrary nearby literals as values.
        /// </summary>
        /// <exception cref="InvalidOperationException">Extracted array values differ from the actual assignments.</exception>
        private static void FiniteOptionArrays()
        {
            RequireOptionValues(typeof(NoteMaskEnumerator), "Disable", "Inner", "Outer");
            RequireOptionValues(typeof(ReassignedArrayEnumerator), "Finite.Local.First", "Finite.Local.Second");
            RequireOptionValues(typeof(BranchArrayEnumerator), "Finite.Branch.Left", "Finite.Branch.Right");
            RequireOptionValues(typeof(NumericStringEnumerator), "0", "1.25", "-2", "6e2");
        }

        /// <summary>
        /// Requires the exact raw string values assigned to a custom enumerator's OptionValues field.
        /// </summary>
        /// <param name="enumeratorType">The metadata type whose initializer should be inspected.</param>
        /// <param name="expected">The finite raw values, irrespective of discovery order.</param>
        /// <exception cref="InvalidOperationException">Values are missing, duplicated, or contain unrelated literals.</exception>
        private static void RequireOptionValues(Type enumeratorType, params string[] expected)
        {
            var diagnostics = new List<string>();
            var values = TranslationCallAnalyzer.CollectOptionValueStrings(enumeratorType, diagnostics);
            var actual = new HashSet<string>(values, StringComparer.Ordinal);
            Check(actual.SetEquals(expected), $"Wrong finite values for {enumeratorType.Name}: {string.Join(", ", values)}. Diagnostics: {string.Join(" | ", diagnostics)}");
            Check(values.Count == actual.Count, $"Duplicate finite values returned for {enumeratorType.Name}.");
            Check(ExecutionGuard.ExecutionCount == 0, $"Option extraction executed {enumeratorType.Name}.");
        }

        /// <summary>Checks that superseded or escaped field arrays do not manufacture fixed option keys.</summary>
        /// <exception cref="InvalidOperationException">The analyzer retains stale values or executes fixture code.</exception>
        private static void OptionFieldFlow()
        {
            RequireOptionValues(typeof(OverwrittenFieldEnumerator), "Finite.Field.Final");
            RequireOptionValues(typeof(MutatedFieldEnumerator), "Finite.Field.Mutated");
            foreach (var type in new[] { typeof(DynamicOverwriteEnumerator), typeof(EscapedArrayEnumerator) })
            {
                var diagnostics = new List<string>();
                var values = TranslationCallAnalyzer.CollectOptionValueStrings(type, diagnostics);
                Check(values.Count == 0, $"Stale finite values leaked from {type.Name}.");
                Check(diagnostics.Count != 0, $"Dynamic field contents need diagnostics for {type.Name}.");
                Check(ExecutionGuard.ExecutionCount == 0, $"Option flow analysis executed {type.Name}.");
            }
        }

        /// <summary>
        /// Checks dynamic sources and readonly arrays without evaluating any runtime value provider.
        /// </summary>
        /// <exception cref="InvalidOperationException">Dynamic arrays produced guessed labels or fixture code executed.</exception>
        private static void DynamicOptionArrays()
        {
            var types = new Type[]
            {
                typeof(LanguageEnumerator),
                typeof(SkinEnumerator),
                typeof(DynamicOptionEnumerator),
                typeof(ReadonlyArrayEnumerator)
            };
            foreach (var type in types)
            {
                var diagnostics = new List<string>();
                var values = TranslationCallAnalyzer.CollectOptionValueStrings(type, diagnostics);
                Check(values.Count == 0, $"Dynamic array values were guessed for {type.Name}.");
                RequireDiagnostic(diagnostics, type.Name);
                Check(ExecutionGuard.ExecutionCount == 0, $"Dynamic option scanning executed {type.Name}.");
            }
        }

        /// <summary>
        /// Checks category binding and static attribute arguments against the runtime settings schema.
        /// </summary>
        /// <param name="settings">The setting-key snapshot.</param>
        /// <exception cref="InvalidOperationException">A category, name, description, or override has the wrong key.</exception>
        private static void SettingNamesAndDescriptions(AnalysisSnapshot settings)
        {
            RequireKeys(settings, "MAJSETTING_CATEGORY_Game", "MAJSETTING_CATEGORY_Display",
                "MAJSETTING_CATEGORY_Volume", "MAJSETTING_CATEGORY_ChartSetting",
                "Setting.Name.EnabledOverride", "Setting.Description.EnabledOverride",
                "MAJSETTING_PROPERTY_OptionalBoolean", "MAJSETTING_PROPERTY_OptionalBoolean_DESC",
                "MAJSETTING_PROPERTY_NoteMask", "MAJSETTING_PROPERTY_NoteMask_DESC",
                "MAJSETTING_PROPERTY_Global", "MAJSETTING_PROPERTY_Global_DESC",
                "MAJSETTING_PROPERTY_Muted", "MAJSETTING_PROPERTY_Muted_DESC",
                "MAJSETTING_PROPERTY_NoDescriptionFlag", "MAJSETTING_PROPERTY_DisableVideoBG",
                "MAJSETTING_PROPERTY_SlideSkipping", "MAJSETTING_PROPERTY_AudioOffset");
            RequireAbsent(settings, "MAJSETTING_CATEGORY_Audio", "NonKey.MenuAlias",
                "MAJSETTING_CATEGORY_NonKey.MenuAlias", "MAJSETTING_PROPERTY_Enabled",
                "MAJSETTING_PROPERTY_Enabled_DESC", "MAJSETTING_PROPERTY_NoDescriptionFlag_DESC",
                "NonKey.SuppressedDescription");
        }

        /// <summary>
        /// Checks property-specific and fallback labels for each statically finite option family.
        /// </summary>
        /// <param name="settings">The setting-key snapshot.</param>
        /// <exception cref="InvalidOperationException">A finite value label is missing or has the wrong spelling.</exception>
        private static void SettingFiniteValues(AnalysisSnapshot settings)
        {
            RequireValueKeys(settings, nameof(GameOptions.Enabled), "False", "True");
            RequireValueKeys(settings, nameof(GameOptions.OptionalBoolean), "False", "True", "UNSET");
            RequireValueKeys(settings, nameof(GameOptions.NullableWithoutOptional), "False", "True");
            RequireValueKeys(settings, nameof(ChartSetting.SlideSkipping), "False", "True", "UNSET");
            RequireValueKeys(settings, nameof(ChartSetting.DisableVideoBG), "False", "True");
            RequireValueKeys(settings, nameof(SFXVolume.Muted), "False", "True");
            RequireEnumValueKeys(settings, nameof(GameOptions.Mode), typeof(ModeOption));
            RequireEnumValueKeys(settings, nameof(GameOptions.NumericEnum), typeof(NumericDisplayOption));
            RequireEnumValueKeys(settings, nameof(GameOptions.Access), typeof(AccessOption));
            RequireValueKeys(settings, nameof(DisplayOptions.NoteMask), "Disable", "Inner", "Outer");
            RequireValueKeys(settings, nameof(DisplayOptions.FiniteLocal), "Finite.Local.First", "Finite.Local.Second");
            RequireValueKeys(settings, nameof(DisplayOptions.FiniteBranch), "Finite.Branch.Left", "Finite.Branch.Right");
            RequireValueKeys(settings, nameof(DisplayOptions.Language), "Unavailable");
            RequireAbsent(settings, "MAJSETTING_PROPERTY_Enabled_OPTION_false", "MAJSETTING_PROPERTY_Enabled_OPTION_true",
                "MAJSETTING_PROPERTY_OptionalBoolean_OPTION_NULL", "MAJSETTING_PROPERTY_NullableWithoutOptional_OPTION_UNSET",
                "MAJSETTING_PROPERTY_NullableWithoutOptional_OPTION_NULL", "MAJSETTING_PROPERTY_NumericEnum_OPTION_90",
                "MAJSETTING_PROPERTY_Mode_OPTION_-5", "MAJSETTING_PROPERTY_Mode_OPTION_17",
                "MAJSETTING_PROPERTY_Setting.Name.EnabledOverride_OPTION_True");
        }

        /// <summary>
        /// Checks both localization templates for the finite values of one setting property.
        /// </summary>
        /// <param name="settings">The setting-key snapshot.</param>
        /// <param name="property">The original property name, not its OptionName override.</param>
        /// <param name="values">The exact display labels emitted by the runtime enumerator.</param>
        /// <exception cref="InvalidOperationException">A property-specific or general fallback label is missing.</exception>
        private static void RequireValueKeys(AnalysisSnapshot settings, string property, params string[] values)
        {
            foreach (var value in values)
            {
                RequireKeys(settings, $"MAJSETTING_PROPERTY_{property}_OPTION_{value}", $"MAJSETTING_GENERAL_OPTION_{value}");
            }
        }

        /// <summary>
        /// Checks actual Enum.GetValues().ToString() results rather than guessing aliases or numbers.
        /// </summary>
        /// <param name="settings">The setting-key snapshot.</param>
        /// <param name="property">The property containing the enum option.</param>
        /// <param name="enumType">The finite enum metadata type.</param>
        /// <exception cref="InvalidOperationException">A runtime enum display label is missing.</exception>
        private static void RequireEnumValueKeys(AnalysisSnapshot settings, string property, Type enumType)
        {
            foreach (var value in Enum.GetValues(enumType))
            {
                RequireValueKeys(settings, property, value.ToString()!);
            }
        }

        /// <summary>
        /// Checks settings that are hidden, unsupported, runtime-dependent, or numeric.
        /// </summary>
        /// <param name="settings">The setting-key snapshot.</param>
        /// <exception cref="InvalidOperationException">An excluded setting key was collected or a dynamic diagnostic is missing.</exception>
        private static void SettingExclusions(AnalysisSnapshot settings)
        {
            RequireAbsent(settings, "MAJSETTING_CATEGORY_HiddenOnline", "NonKey.HiddenOptionName",
                "NonKey.HiddenOptionDescription", "NonKey.DefaultNoteMask", "NonKey.DefaultLanguage",
                "NonKey.DefaultSkin", "NonKey.DefaultFreeText", "NonKey.ChartHash");
            foreach (var property in new string[]
            {
                "HiddenGameFlag", "HiddenCategoryFlag", "Hash", "NoPublicGetter", "Item", "ForceMono", "Backend", "Volume"
            })
            {
                RequireNoPrefix(settings, $"MAJSETTING_PROPERTY_{property}");
            }
            foreach (var property in new string[]
            {
                "Brightness", "NumericCount", "Global", "Skin"
            })
            {
                RequireNoPrefix(settings, $"MAJSETTING_PROPERTY_{property}_OPTION_");
            }
            foreach (var numeric in new string[]
            {
                "0", "1.25", "-2", "6e2"
            })
            {
                RequireAbsent(settings, $"MAJSETTING_PROPERTY_NumericLabels_OPTION_{numeric}", $"MAJSETTING_GENERAL_OPTION_{numeric}");
            }
            foreach (var key in settings.Keys.Keys)
            {
                Check(key.IndexOf("NonKey.", StringComparison.Ordinal) < 0, $"Non-key fixture text leaked into the settings inventory: {key}.");
            }
            RequireDiagnostic(settings.Diagnostics, nameof(DisplayOptions.Language));
            RequireDiagnostic(settings.Diagnostics, nameof(DisplayOptions.Skin));
            RequireDiagnostic(settings.Diagnostics, nameof(DisplayOptions.DynamicCustom));
            RequireDiagnostic(settings.Diagnostics, nameof(DisplayOptions.ReadonlyCustom));
            Check(ExecutionGuard.ExecutionCount == 0, "Settings analysis invoked a fixture constructor, attribute, getter, or initializer.");
        }

        /// <summary>
        /// Checks preservation of caller-owned inventory entries and idempotent source merging.
        /// </summary>
        /// <exception cref="InvalidOperationException">An existing inventory entry is replaced or a repeated scan changes the result.</exception>
        private static void InventoryMerging()
        {
            var assembly = typeof(CallFixtures).Assembly;
            var snapshot = new AnalysisSnapshot();
            var existingSources = new HashSet<string>(StringComparer.Ordinal)
            {
                "Fixture.ExistingSource"
            };
            snapshot.Keys.Add("Fixture.ExistingKey", existingSources);
            snapshot.Keys.Add("Call.Shared", new HashSet<string>(StringComparer.Ordinal)
            {
                "Fixture.PreexistingCallSource"
            });
            snapshot.Diagnostics.Add("Fixture.ExistingDiagnostic");
            TranslationCallAnalyzer.Collect(assembly, snapshot.Keys, snapshot.Diagnostics);
            TranslationSettingAnalyzer.Collect(assembly, snapshot.Keys, snapshot.Diagnostics);
            Check(ReferenceEquals(snapshot.Keys["Fixture.ExistingKey"], existingSources), "An existing inventory source set was replaced.");
            Check(existingSources.Contains("Fixture.ExistingSource"), "An existing inventory source was erased.");
            Check(snapshot.Keys["Call.Shared"].Contains("Fixture.PreexistingCallSource"), "An existing call source was erased.");
            Check(snapshot.Keys["Call.Shared"].Count >= 3, "Call origins did not merge with an existing source.");
            Check(snapshot.Diagnostics[0] == "Fixture.ExistingDiagnostic", "The caller's diagnostics were replaced.");
            var previous = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var pair in snapshot.Keys)
            {
                previous.Add(pair.Key, new HashSet<string>(pair.Value, StringComparer.Ordinal));
            }
            TranslationCallAnalyzer.Collect(assembly, snapshot.Keys, snapshot.Diagnostics);
            TranslationSettingAnalyzer.Collect(assembly, snapshot.Keys, snapshot.Diagnostics);
            Check(snapshot.Keys.Count == previous.Count, "Repeated scans changed the number of fixed keys.");
            foreach (var pair in previous)
            {
                Check(snapshot.Keys[pair.Key].SetEquals(pair.Value), $"Repeated scans changed source locations for {pair.Key}.");
            }
        }

        /// <summary>
        /// Checks argument failures without invoking any runtime fixture members.
        /// </summary>
        /// <exception cref="InvalidOperationException">An analyzer accepts an invalid required argument.</exception>
        private static void ArgumentValidation()
        {
            var assembly = typeof(CallFixtures).Assembly;
            var snapshot = new AnalysisSnapshot();
            Throws<ArgumentNullException>(() =>
            {
                TranslationCallAnalyzer.Collect(null!, snapshot.Keys, snapshot.Diagnostics);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationCallAnalyzer.Collect(assembly, null!, snapshot.Diagnostics);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationCallAnalyzer.Collect(assembly, snapshot.Keys, null!);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationCallAnalyzer.CollectOptionValueStrings(null!, snapshot.Diagnostics);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationCallAnalyzer.CollectOptionValueStrings(typeof(NoteMaskEnumerator), null!);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationSettingAnalyzer.Collect(null!, snapshot.Keys, snapshot.Diagnostics);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationSettingAnalyzer.Collect(assembly, null!, snapshot.Diagnostics);
            });
            Throws<ArgumentNullException>(() =>
            {
                TranslationSettingAnalyzer.Collect(assembly, snapshot.Keys, null!);
            });
        }

        /// <summary>
        /// Requires an action to reject its input with the specified exception type.
        /// </summary>
        /// <typeparam name="TException">The expected failure type.</typeparam>
        /// <param name="action">The invalid-input analyzer invocation.</param>
        /// <exception cref="InvalidOperationException">The invocation does not throw the required exception.</exception>
        private static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Check(true, $"Expected {typeof(TException).Name} was thrown.");
                return;
            }
            Check(false, $"Expected {typeof(TException).Name} was not thrown.");
        }

        /// <summary>
        /// Holds caller-owned collections passed directly to production analyzers.
        /// </summary>
        private sealed class AnalysisSnapshot
        {
            /// <summary>
            /// Gets the case-sensitive key-to-source inventory.
            /// </summary>
            internal Dictionary<string, HashSet<string>> Keys
            {
                get;
            } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            /// <summary>
            /// Gets diagnostics for dynamic or unsupported inputs.
            /// </summary>
            internal List<string> Diagnostics
            {
                get;
            } = new List<string>();
        }
    }
}

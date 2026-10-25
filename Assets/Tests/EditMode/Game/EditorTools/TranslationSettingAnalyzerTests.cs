using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Editor.Windows;
using MajdataPlay.Settings;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.EditorTools
{
    /// <summary>Covers the metadata-only setting key analyzer on the real settings and on <see cref="SettingAnalyzerFixture"/>.</summary>
    public class TranslationSettingAnalyzerTests
    {
        /// <summary>The keys collected from the fixture menu.</summary>
        private Dictionary<string, HashSet<string>> _keys = null!;

        /// <summary>The diagnostics collected from the fixture menu.</summary>
        private List<string> _diagnostics = null!;

        /// <summary>Analyzes the fixture menu through the analyzer's private menu entry point.</summary>
        [SetUp]
        public void SetUp()
        {
            _keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            _diagnostics = new List<string>();
            PrivateMember.InvokeStatic(typeof(TranslationSettingAnalyzer), "CollectMenu", typeof(SettingAnalyzerFixture), "Fx", _keys, _diagnostics);
        }

        /// <summary>Gets whether a diagnostic mentions an option and a reason.</summary>
        /// <param name="option">The option name.</param>
        /// <param name="reason">The expected reason text.</param>
        /// <returns>Whether a matching diagnostic exists.</returns>
        private bool HasDiagnostic(string option, string reason)
        {
            return _diagnostics.Any(diagnostic => diagnostic.Contains("Fx." + option) && diagnostic.Contains(reason));
        }

        /// <summary>Checks that both value lookup templates are added for a value.</summary>
        /// <param name="property">The property name.</param>
        /// <param name="value">The value text.</param>
        private void AssertValue(string property, string value)
        {
            Assert.That(_keys.Keys, Does.Contain($"MAJSETTING_PROPERTY_{property}_OPTION_{value}"));
            Assert.That(_keys.Keys, Does.Contain($"MAJSETTING_GENERAL_OPTION_{value}"));
        }

        /// <summary>Checks the default name and description keys and their sources.</summary>
        [Test]
        public void Options_UseDefaultNameAndDescriptionKeys()
        {
            Assert.That(_keys["MAJSETTING_PROPERTY_Flag"], Does.Contain("Fx.Flag (name)"));
            Assert.That(_keys["MAJSETTING_PROPERTY_Flag_DESC"], Does.Contain("Fx.Flag (description)"));
        }

        /// <summary>Checks that attribute overrides and suppressed descriptions are honoured.</summary>
        [Test]
        public void Options_HonourNameDescriptionAttributes()
        {
            Assert.That(_keys.Keys, Does.Contain("FX_NAME").And.Contain("FX_DESC"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Mode"));
            Assert.That(_keys.Keys, Does.Contain("MAJSETTING_PROPERTY_Count"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Count_DESC"));
        }

        /// <summary>Checks that boolean values are collected, including the optional unset value.</summary>
        [Test]
        public void BooleanOptions_CollectFalseTrueAndUnset()
        {
            // Booleans without attributes only use the general table.
            Assert.That(_keys["MAJSETTING_GENERAL_OPTION_False"], Does.Contain("Fx.Flag (option value)"));
            Assert.That(_keys["MAJSETTING_GENERAL_OPTION_True"], Does.Contain("Fx.Flag (option value)"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Flag_OPTION_True"));
            // The unset placeholder keeps the option key and the general fallback.
            AssertValue("Maybe", "UNSET");
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Flag_OPTION_UNSET"));
        }

        /// <summary>Checks that enum values use the property name, not the overridden option name.</summary>
        [Test]
        public void EnumOptions_CollectEveryConstant()
        {
            AssertSpecificValueOnly("Mode", "First");
            AssertSpecificValueOnly("Mode", "Second");
            AssertSpecificValueOnly("Mode", "Third");
        }

        /// <summary>Checks that an enum option requesting the general table uses only its general keys.</summary>
        [Test]
        public void EnumOptions_UseGeneralTableOnRequest()
        {
            Assert.That(_keys["MAJSETTING_GENERAL_OPTION_Second"], Does.Contain("Fx.SharedMode (option value)"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_SharedMode_OPTION_Second"));
        }

        /// <summary>Checks that an enum value only adds its option-specific key, because enums skip the general table by default.</summary>
        /// <param name="property">The property name.</param>
        /// <param name="value">The value text.</param>
        private void AssertSpecificValueOnly(string property, string value)
        {
            Assert.That(_keys.Keys, Does.Contain($"MAJSETTING_PROPERTY_{property}_OPTION_{value}"));
            var hasFallback = _keys.TryGetValue($"MAJSETTING_GENERAL_OPTION_{value}", out var sources) &&
                sources.Contains($"Fx.{property} (option value fallback)");
            Assert.That(hasFallback, Is.False, $"{property}.{value} must not fall back to the general table.");
        }

        /// <summary>Checks that numbers produce no value keys.</summary>
        [Test]
        public void NumberOptions_HaveNoValueKeys()
        {
            Assert.That(_keys.Keys.Any(key => key.StartsWith("MAJSETTING_PROPERTY_Count_OPTION_", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>Checks the read-only value sources: runtime references, nullable enums and null sentinels.</summary>
        [Test]
        public void ReadOnlyOptions_CollectFiniteValuesAndNullSentinels()
        {
            AssertValue("Text", "NULL");
            AssertValue("OptionalText", "UNSET");
            AssertSpecificValueOnly("NullableMode", "Second");
            // The null placeholder is a word, so it keeps the general fallback even for an enum.
            AssertValue("NullableMode", "NULL");
            AssertValue("NullableNumber", "NULL");
            Assert.That(HasDiagnostic("Text", "values require runtime data"), Is.True);
            Assert.That(HasDiagnostic("NullableNumber", "values require runtime data"), Is.False);
        }

        /// <summary>Checks that mismatched built-in enumerators are reported.</summary>
        [Test]
        public void MismatchedEnumerators_AreReported()
        {
            Assert.That(HasDiagnostic("WrongBoolean", "cannot initialize a non-boolean property"), Is.True);
            Assert.That(HasDiagnostic("WrongEnum", "cannot initialize a non-enum property"), Is.True);
            Assert.That(HasDiagnostic("WrongNumber", "cannot initialize this non-numeric property"), Is.True);
        }

        /// <summary>Checks the language and skin enumerators' fixed fallback and dynamic diagnostics.</summary>
        [Test]
        public void LanguageAndSkin_ReportDynamicValues()
        {
            AssertValue("Language", "Unavailable");
            Assert.That(HasDiagnostic("Language", "loaded language names are dynamic"), Is.True);
            Assert.That(HasDiagnostic("Skin", "loaded skin names are dynamic"), Is.True);
            Assert.That(_keys.Keys.Any(key => key.StartsWith("MAJSETTING_PROPERTY_Skin_OPTION_", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>Checks that custom finite strings are collected while numeric labels are excluded with a diagnostic.</summary>
        [Test]
        public void CustomEnumerators_CollectLiteralStrings()
        {
            AssertValue("Custom", "Disable");
            AssertValue("Custom", "Inner");
            AssertValue("Custom", "Outer");
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Custom_OPTION_42"));
            Assert.That(HasDiagnostic("Custom", "numeric option display strings were excluded"), Is.True);
            Assert.That(HasDiagnostic("EmptyCustom", "no finite string values could be established"), Is.True);
        }

        /// <summary>Checks that empty overrides are ignored with a diagnostic and hidden or unreadable properties are skipped.</summary>
        [Test]
        public void InvalidAndHiddenProperties_AreSkipped()
        {
            Assert.That(HasDiagnostic("EmptyName", "an empty localization key was ignored"), Is.True);
            Assert.That(_keys.Keys, Does.Contain("MAJSETTING_PROPERTY_EmptyName_DESC"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_HiddenFlag"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_StaticFlag"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_WriteOnly"));
        }

        /// <summary>
        /// Checks that the translation selection attributes change the collected name and value keys the same way the
        /// setting UI resolves them, while descriptions keep their default key.
        /// </summary>
        [Test]
        public void TranslationSelectionAttributes_SelectKeys()
        {
            Assert.That(_keys["FX_CUSTOM_NAME"], Does.Contain("Fx.Annotated (name)"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Annotated"));
            Assert.That(_keys.Keys, Does.Contain("MAJSETTING_PROPERTY_Annotated_DESC"));
            Assert.That(_keys["MAJSETTING_GENERAL_OPTION_True"], Does.Contain("Fx.Annotated (option value)"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Annotated_OPTION_True"));
            Assert.That(_diagnostics.Any(diagnostic => diagnostic.Contains("Fx.Annotated")), Is.False);

            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Silent"));
            Assert.That(_keys.Keys, Does.Contain("MAJSETTING_PROPERTY_Silent_DESC"));
            Assert.That(_keys.Keys.Any(key => key.StartsWith("MAJSETTING_PROPERTY_Silent_OPTION_", StringComparison.Ordinal)), Is.False);
            Assert.That(_keys["MAJSETTING_GENERAL_OPTION_False"], Does.Not.Contain("Fx.Silent (option value fallback)"));

            Assert.That(_keys["Alias"], Does.Contain("Fx.Aliased (name)"));
            Assert.That(_keys.Keys, Does.Contain("FX_Alias_First").And.Contain("FX_Alias_Second").And.Contain("FX_Alias_Third"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Aliased_OPTION_First"));
            Assert.That(_keys.TryGetValue("MAJSETTING_GENERAL_OPTION_First", out var firstSources) && firstSources.Contains("Fx.Aliased (option value fallback)"), Is.False);

            Assert.That(_keys["FX_SWITCH_True"], Does.Contain("Fx.Switch (option value)"));
            Assert.That(_keys["FX_SWITCH_False"], Does.Contain("Fx.Switch (option value)"));
            // Explicit keys do not fall back to the general table.
            Assert.That(_keys.Values.Any(sources => sources.Any(source => source.StartsWith("Fx.Switch ", StringComparison.Ordinal) && source.Contains("fallback"))), Is.False);
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Switch_OPTION_True"));

            Assert.That(_keys.Keys, Does.Contain("MAJSETTING_GENERAL_PROPERTY_GeneralName"));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_GeneralName"));
            Assert.That(HasDiagnostic("TranslatedCount", "opt into translation"), Is.True);
            Assert.That(_keys.Keys.Any(key => key.StartsWith("MAJSETTING_PROPERTY_TranslatedCount_OPTION_", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>Checks that translated numeric options contribute the optional keys of their finite Range/Step grid.</summary>
        [Test]
        public void NumericOptions_EnumerateRangeStepValues()
        {
            foreach (var value in new[] { "1", "2", "3" })
            {
                Assert.That(_keys[$"MAJSETTING_PROPERTY_Countdown_OPTION_{value}"], Does.Contain("Fx.Countdown" + TranslationSettingAnalyzer.NumericValueUsage));
            }
            // Numeric values only use the keys their attributes request, so no general fallback key is collected.
            Assert.That(_keys.Values.Any(sources => sources.Contains("Fx.Countdown" + TranslationSettingAnalyzer.NumericValueFallbackUsage)), Is.False);
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Countdown_OPTION_4"));
            var ratioValues = _keys.Keys.Where(key => key.StartsWith("MAJSETTING_PROPERTY_Ratio_OPTION_", StringComparison.Ordinal)).ToArray();
            Assert.That(ratioValues, Is.EquivalentTo(new[]
            {
                "MAJSETTING_PROPERTY_Ratio_OPTION_0",
                "MAJSETTING_PROPERTY_Ratio_OPTION_0.25",
                "MAJSETTING_PROPERTY_Ratio_OPTION_0.5",
                "MAJSETTING_PROPERTY_Ratio_OPTION_0.6",
            }));
            Assert.That(HasDiagnostic("Stuck", "no positive Step"), Is.True);
            Assert.That(_keys.Keys.Any(key => key.StartsWith("MAJSETTING_PROPERTY_Stuck_OPTION_", StringComparison.Ordinal)), Is.False);
            Assert.That(TranslationSettingAnalyzer.IsNumericValueSource("Fx.Countdown" + TranslationSettingAnalyzer.NumericValueUsage), Is.True);
            Assert.That(TranslationSettingAnalyzer.IsNumericValueSource("Fx.Flag (option value)"), Is.False);
        }

        /// <summary>Checks the keys TranslateValues requests for untranslated options and the diagnostics of its misuse.</summary>
        [Test]
        public void TranslateValues_CollectSelectedValueKeys()
        {
            Assert.That(_keys["MAJSETTING_PROPERTY_Selected_OPTION_0"], Does.Contain("Fx.Selected" + TranslationSettingAnalyzer.SelectedValueUsage));
            Assert.That(_keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_Selected_OPTION_x"));
            Assert.That(HasDiagnostic("Selected", "not an invariant-culture number"), Is.True);
            Assert.That(_keys["FX_PICK_Second"], Does.Contain("Fx.Picked" + TranslationSettingAnalyzer.SelectedValueUsage));
            Assert.That(_keys.Keys.Any(key => key.StartsWith("MAJSETTING_PROPERTY_Picked_OPTION_", StringComparison.Ordinal)), Is.False);
            Assert.That(_keys.Keys, Does.Not.Contain("FX_WASTED_1"));
            Assert.That(HasDiagnostic("Wasted", "TranslateValues has no effect"), Is.True);
        }

        /// <summary>Checks that visibility conditions qualify the usage paths and that unresolvable paths are reported.</summary>
        [Test]
        public void ConditionalOptions_DescribeTheirCondition()
        {
            Assert.That(_keys["MAJSETTING_PROPERTY_Conditional"], Does.Contain("Fx.Conditional [shown when Flag = True] (name)"));
            Assert.That(_keys["MAJSETTING_PROPERTY_BrokenCondition"], Does.Contain("Fx.BrokenCondition [shown when Missing.Path = 1] (name)"));
            Assert.That(HasDiagnostic("BrokenCondition", "cannot be resolved"), Is.True);
            Assert.That(_diagnostics.Any(diagnostic => diagnostic.Contains("Fx.Conditional:")), Is.False);
        }

        /// <summary>Checks the full analysis of the runtime settings: categories, Audio's volume page, chart settings and offset units.</summary>
        [Test]
        public void Collect_RuntimeAssembly_FindsCategoriesAndOptions()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            TranslationSettingAnalyzer.Collect(typeof(GameSetting).Assembly, keys, diagnostics);

            Assert.That(keys.Keys, Does.Contain("MAJSETTING_CATEGORY_Game"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_CATEGORY_Volume"));
            Assert.That(keys.Keys, Does.Not.Contain("MAJSETTING_CATEGORY_Audio"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_CATEGORY_ChartSetting"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_CATEGORY_Rendering"));
            Assert.That(keys.Keys, Does.Contain("MAJTEXT_SETTING_OFFSETUNIT_Second"));
            Assert.That(keys.Keys, Does.Contain("MAJTEXT_SETTING_OFFSETUNIT_Frame"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_PROPERTY_JudgeOffset"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_GENERAL_OPTION_True"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_GENERAL_OPTION_Inner"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_PROPERTY_LeadInTime_OPTION_5"));
            Assert.That(keys["MAJSETTING_PROPERTY_OuterJudgeDistance_OPTION_0"].Any(source => source.EndsWith(TranslationSettingAnalyzer.SelectedValueUsage, StringComparison.Ordinal)), Is.True);
            Assert.That(keys.Keys, Does.Not.Contain("MAJSETTING_PROPERTY_OuterJudgeDistance_OPTION_0.05"));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Contains("skin names are dynamic")), Is.True);
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Contains("ShowInSettingUIWhen path")), Is.False);
        }

        /// <summary>Checks that an assembly without settings types only yields the fixed offset unit keys.</summary>
        [Test]
        public void Collect_AssemblyWithoutSettings_ReportsMissingTypes()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            TranslationSettingAnalyzer.Collect(typeof(Assert).Assembly, keys, diagnostics);

            Assert.That(keys.Keys, Is.EquivalentTo(new[] { "MAJTEXT_SETTING_OFFSETUNIT_Second", "MAJTEXT_SETTING_OFFSETUNIT_Frame" }));
            Assert.That(diagnostics.Count(diagnostic => diagnostic.Contains("is not present in")), Is.EqualTo(2));
        }

        /// <summary>Checks argument validation.</summary>
        [Test]
        public void Collect_RejectsNullArguments()
        {
            var assembly = typeof(GameSetting).Assembly;
            var keys = new Dictionary<string, HashSet<string>>();
            var diagnostics = new List<string>();

            Assert.That(() => TranslationSettingAnalyzer.Collect(null!, keys, diagnostics), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("assembly"));
            Assert.That(() => TranslationSettingAnalyzer.Collect(assembly, null!, diagnostics), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("keys"));
            Assert.That(() => TranslationSettingAnalyzer.Collect(assembly, keys, null!), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("diagnostics"));
        }

        /// <summary>Checks that a category named Audio without a volume page is reported.</summary>
        [Test]
        public void CollectCategories_AudioWithoutVolume_IsReported()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            PrivateMember.InvokeStatic(typeof(TranslationSettingAnalyzer), "CollectCategories", typeof(SettingRootFixture), keys, diagnostics);

            Assert.That(keys.Keys, Does.Contain("MAJSETTING_CATEGORY_Fixture"));
            Assert.That(keys.Keys, Does.Contain("MAJSETTING_PROPERTY_Flag"));
            Assert.That(diagnostics.Any(diagnostic => diagnostic.Contains("Audio.Volume does not expose a public, non-indexed getter")), Is.True);
        }

        /// <summary>Checks that the translation attributes of a category select or suppress its title key.</summary>
        [Test]
        public void CollectCategories_HonoursMenuNameTranslationAttributes()
        {
            var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var diagnostics = new List<string>();

            PrivateMember.InvokeStatic(typeof(TranslationSettingAnalyzer), "CollectCategories", typeof(SettingRootFixture), keys, diagnostics);

            Assert.That(keys["FX_MENU_Titled"], Does.Contain(typeof(SettingRootFixture).FullName + ".Titled (category)"));
            Assert.That(keys.Keys, Does.Not.Contain("MAJSETTING_CATEGORY_Titled"));
            Assert.That(keys.Keys, Does.Not.Contain("MAJSETTING_CATEGORY_Untitled"));
            Assert.That(keys["MAJSETTING_PROPERTY_Flag"], Does.Contain(typeof(SettingRootFixture).FullName + ".Untitled.Flag (name)"));
        }

        /// <summary>Checks the numeric text classifier used for custom values.</summary>
        [Test]
        public void IsNumericText_RecognizesNumbersOnly()
        {
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(TranslationSettingAnalyzer), "IsNumericText", "-1.5e3"), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(TranslationSettingAnalyzer), "IsNumericText", "1,000"), Is.True);
            Assert.That(PrivateMember.InvokeStatic<bool>(typeof(TranslationSettingAnalyzer), "IsNumericText", "_90"), Is.False);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

#nullable enable
namespace MajdataPlay.Editor.Windows
{
    /// <summary>
    /// Collects fixed localization keys from the setting UI's metadata without executing setting code.
    /// </summary>
    /// <remarks>
    /// Only the supplied assembly's compiled platform branches are available. This mirrors the current
    /// SettingManager, Menu, SettingOptionMetadata and OptionEnumeratorBase schema, not arbitrary object graphs.
    /// Root type names, built-in enumerator names, Audio.Volume, key templates, the Unavailable language
    /// fallback and the two offset-unit warmup labels deliberately model the current production schema.
    /// Custom finite string arrays are delegated to TranslationCallAnalyzer instead of executing enumerators.
    /// Numeric options that opt into value translation contribute the values of their finite Range/Step grid, labelled
    /// with <see cref="NumericValueUsage"/> so that they can be reviewed as optional. ShowInSettingUIWhen conditions are
    /// appended to the usage paths of conditional menus and options, and unresolvable condition paths are reported.
    /// </remarks>
    internal static class TranslationSettingAnalyzer
    {
        /// <summary>
        /// Identifies the runtime settings types and attributes without referencing their internal caches.
        /// </summary>
        private const string SettingsNamespace = "MajdataPlay.Settings.";

        /// <summary>
        /// Identifies enumerators whose value-generation behavior is known from the production implementation.
        /// </summary>
        private const string EnumeratorNamespace = SettingsNamespace + "OptionEnumerators.";

        /// <summary>
        /// The built-in and general key templates, mirroring the SettingTranslation constants.
        /// </summary>
        private const string DefaultMenuNameTemplate = "MAJSETTING_CATEGORY_{0}";
        private const string DefaultOptionNameTemplate = "MAJSETTING_PROPERTY_{0}";
        private const string DefaultOptionValueTemplate = "MAJSETTING_PROPERTY_{0}_OPTION_{1}";
        private const string GeneralMenuNameTemplate = "MAJSETTING_GENERAL_CATEGORY_{0}";
        private const string GeneralOptionNameTemplate = "MAJSETTING_GENERAL_PROPERTY_{0}";
        private const string GeneralOptionValueTemplate = "MAJSETTING_GENERAL_OPTION_{1}";

        /// <summary>
        /// The usage label of a numeric option value key enumerated from Range/Step.
        /// </summary>
        internal const string NumericValueUsage = " (numeric option value)";

        /// <summary>
        /// The usage label of the general fallback key of a numeric option value enumerated from Range/Step.
        /// </summary>
        internal const string NumericValueFallbackUsage = " (numeric option value fallback)";

        /// <summary>
        /// The usage label of a key requested by TranslateValuesAttribute for one value of an otherwise untranslated option.
        /// </summary>
        internal const string SelectedValueUsage = " (selected option value)";

        /// <summary>
        /// Limits how many numeric values one option may contribute, so a fine grid does not flood the key inventory.
        /// </summary>
        private const int MaxNumericValues = 128;

        /// <summary>
        /// Adds the setting menus, option labels, descriptions and finite localized values to a key inventory.
        /// </summary>
        /// <param name="assembly">The compiled MajdataPlay.Core assembly containing the runtime settings metadata.</param>
        /// <param name="keys">The localization keys mapped to their discovered UI usage locations.</param>
        /// <param name="diagnostics">Receives unsupported metadata and values that require runtime information.</param>
        /// <exception cref="ArgumentNullException">An input argument is null.</exception>
        internal static void Collect(Assembly assembly, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            if (assembly is null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }
            if (diagnostics is null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var gameSetting = FindType(assembly, SettingsNamespace + "GameSetting", diagnostics);
            if (gameSetting is not null)
            {
                CollectCategories(gameSetting, keys, diagnostics);
            }

            // ChartSetting is a separate, conditionally displayed menu, not a GameSetting category.
            var chartSetting = FindType(assembly, SettingsNamespace + "ChartSetting", diagnostics);
            if (chartSetting is not null)
            {
                var path = chartSetting.FullName ?? chartSetting.Name;
                AddKey("MAJSETTING_CATEGORY_" + chartSetting.Name, path + " (category)", keys, diagnostics);
                CollectMenu(chartSetting, path, keys, diagnostics);
            }

            // SettingManager.WarmupTextFonts requests both labels even without a visible offset option.
            AddKey("MAJTEXT_SETTING_OFFSETUNIT_Second", "SettingManager.WarmupTextFonts (offset unit)", keys, diagnostics);
            AddKey("MAJTEXT_SETTING_OFFSETUNIT_Frame", "SettingManager.WarmupTextFonts (offset unit)", keys, diagnostics);
        }

        /// <summary>
        /// Finds a settings type while reporting missing or unloadable metadata instead of constructing an instance.
        /// </summary>
        /// <param name="assembly">The assembly whose metadata is inspected.</param>
        /// <param name="name">The fully qualified settings type name.</param>
        /// <param name="diagnostics">Receives the reason that a type cannot be inspected.</param>
        /// <returns>The metadata type, or null when it is unavailable.</returns>
        private static Type? FindType(Assembly assembly, string name, IList<string> diagnostics)
        {
            try
            {
                var type = assembly.GetType(name, false);
                if (type is null)
                {
                    diagnostics.Add($"[Settings] Type {name} is not present in {assembly.GetName().Name}.");
                }
                return type;
            }
            catch (Exception exception)
            {
                diagnostics.Add($"[Settings] Cannot inspect {name}: {exception.GetType().Name}: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// Mirrors the root menu selection, including the Audio.Volume category substitution.
        /// </summary>
        /// <param name="settingType">The declared root settings type.</param>
        /// <param name="keys">Receives fixed category and option localization keys.</param>
        /// <param name="diagnostics">Receives metadata that cannot safely describe a menu.</param>
        private static void CollectCategories(Type settingType, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            foreach (var category in GetVisibleProperties(settingType, diagnostics))
            {
                var path = $"{settingType.FullName}.{category.Name}";
                try
                {
                    var menuProperty = category;
                    if (category.Name == "Audio")
                    {
                        // GetMenuMetadata looks up Volume directly; its own HideInSettingUI is not consulted.
                        var volume = category.PropertyType.GetProperty("Volume", BindingFlags.Public | BindingFlags.Instance);
                        if (volume is null || !IsReadableProperty(volume))
                        {
                            diagnostics.Add($"[Settings] {path}.Volume does not expose a public, non-indexed getter.");
                            continue;
                        }
                        menuProperty = volume;
                        path += ".Volume";
                    }

                    // A condition on the category hides the whole menu, so it also qualifies every option path.
                    path += DescribeVisibility(category, settingType, path, diagnostics);

                    // MenuMetadata.Name is the value property's name; MenuNameAttribute is not used by the UI.
                    // The translation attributes of the root category property select the title key.
                    var categoryAttributes = category.GetCustomAttributesData();
                    var titleSelection = SelectTranslation(categoryAttributes, "MenuName", menuProperty.Name, DefaultMenuNameTemplate, GeneralMenuNameTemplate, false, false);
                    AddTemplateKeys(titleSelection, menuProperty.Name, path + " (category)", keys, diagnostics);
                    CollectMenu(menuProperty.PropertyType, path, keys, diagnostics);
                }
                catch (Exception exception)
                {
                    diagnostics.Add($"[Settings] Cannot inspect category {path}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Collects only the direct properties rendered by a menu, rather than recursively expanding nested settings.
        /// </summary>
        /// <param name="menuType">The declared type whose visible properties become options.</param>
        /// <param name="path">The UI path to the menu instance.</param>
        /// <param name="keys">Receives option localization keys and usage locations.</param>
        /// <param name="diagnostics">Receives properties or values that cannot be resolved from metadata.</param>
        private static void CollectMenu(Type menuType, string path, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            foreach (var property in GetVisibleProperties(menuType, diagnostics))
            {
                var optionPath = path + "." + property.Name;
                try
                {
                    optionPath += DescribeVisibility(property, menuType, optionPath, diagnostics);
                    CollectOption(property, optionPath, keys, diagnostics);
                }
                catch (Exception exception)
                {
                    diagnostics.Add($"[Settings] Cannot inspect option {optionPath}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Reads visible instance properties without invoking accessors or attribute constructors.
        /// </summary>
        /// <param name="type">The metadata type supplying menu or option properties.</param>
        /// <param name="diagnostics">Receives failures to load a property's attributes or type metadata.</param>
        /// <returns>The public readable, non-indexed properties not marked HideInSettingUI.</returns>
        private static IReadOnlyList<PropertyInfo> GetVisibleProperties(Type type, IList<string> diagnostics)
        {
            var result = new List<PropertyInfo>();
            try
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        if (IsReadableProperty(property) &&
                            !HasAttribute(property.GetCustomAttributesData(), SettingsNamespace + "HideInSettingUIAttribute"))
                        {
                            result.Add(property);
                        }
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Add($"[Settings] Cannot inspect {type.FullName}.{property.Name}: {exception.GetType().Name}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                diagnostics.Add($"[Settings] Cannot list properties of {type.FullName}: {exception.GetType().Name}: {exception.Message}");
            }
            return result;
        }

        /// <summary>
        /// Checks that a setting property can be read by the instance-based UI without getter invocation.
        /// </summary>
        /// <param name="property">The property metadata to examine.</param>
        /// <returns>True for a public instance getter without index parameters; otherwise false.</returns>
        private static bool IsReadableProperty(PropertyInfo property)
        {
            var getter = property.GetGetMethod();
            return getter is not null && !getter.IsStatic && property.GetIndexParameters().Length == 0;
        }

        /// <summary>
        /// Checks for a declared runtime attribute by full name, respecting the built-in attributes' non-inherited usage.
        /// </summary>
        /// <param name="attributes">The property's custom attribute metadata.</param>
        /// <param name="name">The fully qualified attribute type name.</param>
        /// <returns>True when the specified attribute is present; otherwise false.</returns>
        private static bool HasAttribute(IList<CustomAttributeData> attributes, string name)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.FullName == name)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Describes the ShowInSettingUIWhen conditions of an option or menu for its key sources, and reports paths the
        /// runtime cannot resolve.
        /// </summary>
        /// <remarks>
        /// Conditional entries are still displayed whenever their condition holds, so their keys are collected; the
        /// description only tells a reviewer when a text is visible. An unresolvable path makes the runtime always show
        /// the entry and log an error, which is reported here ahead of time.
        /// </remarks>
        /// <param name="property">The option property, or for a menu its category property.</param>
        /// <param name="ownerType">The type the member paths start at: the menu type, or the settings root for a menu.</param>
        /// <param name="path">The UI path of the entry, for diagnostics.</param>
        /// <param name="diagnostics">Receives conditions whose member path cannot be resolved.</param>
        /// <returns>A suffix such as <c> [shown when Mode = Custom]</c>, or an empty string without conditions.</returns>
        private static string DescribeVisibility(PropertyInfo property, Type ownerType, string path, IList<string> diagnostics)
        {
            var conditions = new List<string>();
            foreach (var attribute in property.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName != SettingsNamespace + "ShowInSettingUIWhenAttribute" ||
                    attribute.ConstructorArguments.Count != 2 ||
                    attribute.ConstructorArguments[0].Value is not string memberPath)
                {
                    continue;
                }
                if (!CanResolveMemberPath(ownerType, memberPath))
                {
                    diagnostics.Add($"[Settings] {path}: ShowInSettingUIWhen path \"{memberPath}\" cannot be resolved from {ownerType.FullName}; the runtime always shows this entry.");
                }
                var values = attribute.ConstructorArguments[1].Value is IReadOnlyCollection<CustomAttributeTypedArgument> arguments
                    ? string.Join(" or ", arguments.Select(FormatArgument))
                    : "null";
                conditions.Add($"{memberPath} = {values}");
            }
            return conditions.Count == 0 ? string.Empty : $" [shown when {string.Join(" and ", conditions)}]";
        }

        /// <summary>
        /// Checks that every segment of a dot-separated member path is a readable public instance property.
        /// </summary>
        /// <param name="ownerType">The type the path starts at.</param>
        /// <param name="memberPath">The dot-separated property path.</param>
        /// <returns>True when the path can be followed; otherwise false.</returns>
        private static bool CanResolveMemberPath(Type ownerType, string memberPath)
        {
            var type = ownerType;
            foreach (var segment in memberPath.Split('.'))
            {
                var property = type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);
                if (property is null || !IsReadableProperty(property))
                {
                    return false;
                }
                type = property.PropertyType;
            }
            return true;
        }

        /// <summary>
        /// Formats an attribute argument for display, naming enum constants instead of their underlying numbers.
        /// </summary>
        /// <param name="argument">The attribute argument.</param>
        /// <returns>The display text.</returns>
        private static string FormatArgument(CustomAttributeTypedArgument argument)
        {
            if (argument.Value is null)
            {
                return "null";
            }
            if (argument.Value is CustomAttributeTypedArgument nested)
            {
                return FormatArgument(nested);
            }
            if (argument.ArgumentType.IsEnum)
            {
                return Enum.GetName(argument.ArgumentType, argument.Value) ?? Convert.ToString(argument.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            return Convert.ToString(argument.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>
        /// Mirrors SettingOptionMetadata's name, description and enumerator selection using custom attribute data.
        /// </summary>
        /// <param name="property">The option property metadata.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives fixed localization keys and their usage locations.</param>
        /// <param name="diagnostics">Receives unresolved or dynamic option information.</param>
        private static void CollectOption(PropertyInfo property, string path, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            var attributes = property.GetCustomAttributesData();
            var descriptionKey = "MAJSETTING_PROPERTY_" + property.Name + "_DESC";
            var hasDescription = true;
            string? nameKeyOverride = null;
            Type? enumeratorType = null;
            foreach (var attribute in attributes)
            {
                switch (attribute.AttributeType.FullName)
                {
                    case SettingsNamespace + "OptionNameAttribute":
                        nameKeyOverride = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
                        break;
                    case SettingsNamespace + "DescriptionAttribute":
                        descriptionKey = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
                        break;
                    case SettingsNamespace + "NoDescriptionAttribute":
                        hasDescription = false;
                        break;
                    case SettingsNamespace + "OptionEnumeratorAttribute":
                        enumeratorType = attribute.ConstructorArguments[0].Value as Type;
                        break;
                }
            }

            var nameSelection = SelectTranslation(attributes, "OptionName", property.Name, DefaultOptionNameTemplate, GeneralOptionNameTemplate, false, false);
            if (!nameSelection.IsExplicit && nameKeyOverride is not null)
            {
                // Without translation attributes an OptionName override is the complete localization key.
                AddKey(nameKeyOverride, path + " (name)", keys, diagnostics);
            }
            else
            {
                AddTemplateKeys(nameSelection, property.Name, path + " (name)", keys, diagnostics);
            }
            if (hasDescription)
            {
                AddKey(descriptionKey, path + " (description)", keys, diagnostics);
            }
            var valueSelection = SelectTranslation(attributes, "OptionValue", property.Name, DefaultOptionValueTemplate, GeneralOptionValueTemplate, HasDefaultGeneralFallback(property.PropertyType),
                HasGeneralValueFallback(property.PropertyType));
            CollectValues(property, enumeratorType, HasAttribute(attributes, SettingsNamespace + "OptionalAttribute"), valueSelection, path, keys, diagnostics);
            CollectSelectedValues(property, attributes, valueSelection, path, keys, diagnostics);
        }

        /// <summary>
        /// Selects finite value sources using the same precedence as the runtime enumerator factory.
        /// </summary>
        /// <param name="property">The option property metadata.</param>
        /// <param name="enumeratorType">The explicit enumerator override, or null for the default factory.</param>
        /// <param name="isOptional">Whether the Optional attribute changes null display text to UNSET.</param>
        /// <param name="source">The value key templates selected by the option's translation attributes.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives property-specific and general fallback value keys.</param>
        /// <param name="diagnostics">Receives unsupported enumerators and dynamic value sources.</param>
        private static void CollectValues(PropertyInfo property, Type? enumeratorType, bool isOptional, TranslationSelection source, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            var type = property.PropertyType;
            var enumeratorName = enumeratorType?.FullName;
            if (enumeratorType is null)
            {
                if (type.IsEnum)
                {
                    enumeratorName = EnumeratorNamespace + "DefaultEnumEnumerator";
                }
                else if (type == typeof(bool) || type == typeof(bool?))
                {
                    enumeratorName = EnumeratorNamespace + "DefaultBooleanEnumerator";
                }
                else if (IsNumber(type))
                {
                    enumeratorName = EnumeratorNamespace + "DefaultNumberEnumerator";
                }
                else
                {
                    enumeratorName = EnumeratorNamespace + "DefaultReadOnlyEnumerator";
                }
            }

            switch (enumeratorName)
            {
                case EnumeratorNamespace + "DefaultBooleanEnumerator":
                case EnumeratorNamespace + "EngineBooleanSettingEnumerator":
                    if (type != typeof(bool) && type != typeof(bool?))
                    {
                        diagnostics.Add($"[Settings] {path}: {enumeratorName} cannot initialize a non-boolean property.");
                        return;
                    }
                    AddValue(source, bool.FalseString, path, keys, diagnostics);
                    AddValue(source, bool.TrueString, path, keys, diagnostics);
                    // Nullable<bool> alone does not add null to DefaultBooleanEnumerator.OptionValues.
                    if (isOptional)
                    {
                        AddValue(source, "UNSET", path, keys, diagnostics);
                    }
                    return;
                case EnumeratorNamespace + "DefaultEnumEnumerator":
                case EnumeratorNamespace + "EngineEnumSettingEnumerator":
                    if (!type.IsEnum)
                    {
                        diagnostics.Add($"[Settings] {path}: {enumeratorName} cannot initialize a non-enum property.");
                        return;
                    }
                    CollectEnumValues(type, source, path, keys, diagnostics);
                    return;
                case EnumeratorNamespace + "DefaultNumberEnumerator":
                case EnumeratorNamespace + "EngineNumberSettingEnumerator":
                case EnumeratorNamespace + "GameOffsetEnumerator":
                case EnumeratorNamespace + "AudioVolumeEnumerator":
                    // Numeric InitValueTexts/UpdateValueText lookups depend on the actual value and culture.
                    if (!IsNumber(type))
                    {
                        diagnostics.Add($"[Settings] {path}: {enumeratorName} cannot initialize this non-numeric property.");
                    }
                    else if (source.IsExplicit && source.IsTranslated)
                    {
                        // Numbers are untranslated by default; an opt-in only has fixed keys within a finite Range/Step grid.
                        if (TryGetNumericValueTexts(property, type, out var valueTexts, out var reason))
                        {
                            foreach (var valueText in valueTexts)
                            {
                                AddValue(source, valueText, path, keys, diagnostics, true);
                            }
                        }
                        else
                        {
                            diagnostics.Add($"[Settings] {path}: numeric option values opt into translation, but {reason}; their keys depend on runtime values and were not collected.");
                        }
                    }
                    return;
                case EnumeratorNamespace + "DefaultReadOnlyEnumerator":
                    CollectReadOnlyValues(property, isOptional, source, path, keys, diagnostics);
                    return;
                case EnumeratorNamespace + "LanguageEnumerator":
                    // Only the no-language branch has a fixed value; loaded language identifiers are dynamic.
                    AddValue(source, "Unavailable", path, keys, diagnostics);
                    diagnostics.Add($"[Settings] {path}: loaded language names are dynamic; only the fixed Unavailable fallback was collected.");
                    return;
                case EnumeratorNamespace + "SkinEnumerator":
                    diagnostics.Add($"[Settings] {path}: loaded skin names are dynamic and were not treated as fixed keys.");
                    return;
                case EnumeratorNamespace + "NoteMaskEnumerator":
                default:
                    // Includes NoteMask's Disable/Inner/Outer array, as well as other finite custom strings.
                    // The IL analyzer reads OptionValues without running InitInternal or constructors.
                    if (enumeratorType is not null)
                    {
                        var values = TranslationCallAnalyzer.CollectOptionValueStrings(enumeratorType, diagnostics);
                        var hasNumericValues = false;
                        foreach (var value in values)
                        {
                            if (IsNumericText(value))
                            {
                                hasNumericValues = true;
                                continue;
                            }
                            AddValue(source, value, path, keys, diagnostics);
                        }
                        if (hasNumericValues)
                        {
                            diagnostics.Add($"[Settings] {path}: numeric option display strings were excluded from the fixed non-numeric key inventory.");
                        }
                        if (values.Count == 0)
                        {
                            diagnostics.Add($"[Settings] {path}: no finite string values could be established for {enumeratorType.FullName}.");
                        }
                    }
                    return;
            }
        }

        /// <summary>
        /// Collects finite typed read-only values and null sentinels without guessing runtime ToString output.
        /// </summary>
        /// <param name="property">The property displayed by DefaultReadOnlyEnumerator.</param>
        /// <param name="isOptional">Whether null is displayed as UNSET instead of NULL.</param>
        /// <param name="source">The value key templates selected by the option's translation attributes.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives statically known non-numeric values and null sentinel keys.</param>
        /// <param name="diagnostics">Receives runtime-dependent values that were intentionally excluded.</param>
        private static void CollectReadOnlyValues(PropertyInfo property, bool isOptional, TranslationSelection source, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            var nullableType = Nullable.GetUnderlyingType(property.PropertyType);
            var valueType = nullableType ?? property.PropertyType;
            if (valueType.IsEnum)
            {
                CollectEnumValues(valueType, source, path, keys, diagnostics);
            }
            else if (valueType == typeof(bool))
            {
                AddValue(source, bool.FalseString, path, keys, diagnostics);
                AddValue(source, bool.TrueString, path, keys, diagnostics);
            }
            else if (!IsNumber(valueType))
            {
                diagnostics.Add($"[Settings] {path}: read-only {valueType.FullName} values require runtime data; non-null value keys were not inferred.");
            }

            // Read-only nullable enums/numbers and reference values really do use this null fallback.
            // Optional on a non-nullable enum or number does not manufacture a null option.
            if (nullableType is not null || !property.PropertyType.IsValueType)
            {
                // The placeholders are words, so an enum option, whose values skip the general table by default, still uses it.
                var placeholderSource = valueType.IsEnum
                    ? SelectTranslation(property.GetCustomAttributesData(), "OptionValue", property.Name, DefaultOptionValueTemplate, GeneralOptionValueTemplate, true, false)
                    : source;
                AddValue(placeholderSource, isOptional ? "UNSET" : "NULL", path, keys, diagnostics);
            }
        }

        /// <summary>
        /// Mirrors the boxed enum constants' ToString conversion, including duplicate aliases and flags formatting.
        /// </summary>
        /// <param name="enumType">The non-nullable enum type to inspect.</param>
        /// <param name="source">The value key templates selected by the option's translation attributes.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives the enum label localization keys.</param>
        /// <param name="diagnostics">Receives empty localization keys if malformed metadata supplies them.</param>
        private static void CollectEnumValues(Type enumType, TranslationSelection source, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            // Enum.GetNames would invent aliases that runtime ToString never requests; underlying numbers
            // would lose names such as _90. Reading enum constants never executes a settings accessor.
            foreach (var value in Enum.GetValues(enumType))
            {
                var text = value.ToString();
                if (text is not null)
                {
                    AddValue(source, text, path, keys, diagnostics);
                }
            }
        }

        /// <summary>
        /// Checks exactly the non-nullable numeric types recognized by the runtime TypeExtensions helpers.
        /// </summary>
        /// <param name="type">The property type to classify.</param>
        /// <returns>True for a supported integer or floating-point type; otherwise false.</returns>
        private static bool IsNumber(Type type)
        {
            return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte) ||
                type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }

        /// <summary>
        /// Recognizes plain numeric custom string labels without suppressing named enum constants.
        /// </summary>
        /// <param name="value">The string literal recovered from a custom enumerator's option values.</param>
        /// <returns>True for numeric display text, including signed decimals and scientific notation; otherwise false.</returns>
        private static bool IsNumericText(string value)
        {
            return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _);
        }
        /// <summary>
        /// Adds the value lookup keys selected for an option while retaining the runtime's exact casing.
        /// </summary>
        /// <param name="source">The value key templates and the option name substituted for <c>{0}</c>.</param>
        /// <param name="value">The fixed text resulting from the option value's ToString conversion.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives the primary key and any fallback keys.</param>
        /// <param name="diagnostics">Receives invalid empty localization keys.</param>
        /// <param name="isNumericValue">
        /// Whether the value is one of the numbers enumerated from Range/Step; its keys are then labelled with
        /// <see cref="NumericValueUsage"/> or <see cref="NumericValueFallbackUsage"/>.
        /// </param>
        private static void AddValue(TranslationSelection source, string value, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics, bool isNumericValue = false)
        {
            for (var i = 0; i < source.Templates.Count; i++)
            {
                var usage = isNumericValue
                    ? (i == 0 ? NumericValueUsage : NumericValueFallbackUsage)
                    : (i == 0 ? " (option value)" : " (option value fallback)");
                AddKey(FormatKey(source.Templates[i], source.OptionName, value), path + usage, keys, diagnostics);
            }
        }

        /// <summary>
        /// Gets whether a key source is a numeric option value enumerated from Range/Step.
        /// </summary>
        /// <remarks>
        /// Numeric options usually translate only a few special values and display the number otherwise, so keys found
        /// only through such sources are optional.
        /// </remarks>
        /// <param name="source">A usage location recorded by this analyzer.</param>
        /// <returns>True for a numeric option value or its general fallback; otherwise false.</returns>
        internal static bool IsNumericValueSource(string source)
        {
            return source.EndsWith(NumericValueUsage, StringComparison.Ordinal) ||
                source.EndsWith(NumericValueFallbackUsage, StringComparison.Ordinal);
        }

        /// <summary>
        /// Mirrors DefaultNumberEnumerator: lists the value texts a numeric option can be stepped to from its Range/Step grid.
        /// </summary>
        /// <remarks>
        /// The values run from the lower bound in steps and end at the upper bound, which the enumerator clamps to. A
        /// saved value off the grid is not covered. Texts are formatted with the invariant culture, while the runtime
        /// uses the current culture; for cultures with another decimal separator the generated keys differ.
        /// </remarks>
        /// <param name="property">The numeric option property.</param>
        /// <param name="type">The numeric property type.</param>
        /// <param name="valueTexts">Receives the distinct value texts in ascending order.</param>
        /// <param name="reason">Receives why the values cannot be listed, when the method returns false.</param>
        /// <returns>True when the option has a finite grid of at most <see cref="MaxNumericValues"/> values; otherwise false.</returns>
        private static bool TryGetNumericValueTexts(PropertyInfo property, Type type, out List<string> valueTexts, out string reason)
        {
            valueTexts = new List<string>();
            var attributes = property.GetCustomAttributesData();
            if (HasAttribute(attributes, SettingsNamespace + "ReadOnlyOptionAttribute") || property.GetSetMethod() is null)
            {
                reason = "the option is read-only and only shows its current value";
                return false;
            }
            decimal? min = null;
            decimal? max = null;
            decimal? step = null;
            var hasRange = false;
            foreach (var attribute in attributes)
            {
                switch (attribute.AttributeType.FullName)
                {
                    case SettingsNamespace + "RangeAttribute":
                        hasRange = true;
                        if (attribute.ConstructorArguments.Count == 2)
                        {
                            min = ParseDecimal(attribute.ConstructorArguments[0].Value as string);
                            max = ParseDecimal(attribute.ConstructorArguments[1].Value as string);
                        }
                        break;
                    case SettingsNamespace + "StepAttribute":
                        step = ParseDecimal(attribute.ConstructorArguments.Count == 1 ? attribute.ConstructorArguments[0].Value as string : null);
                        break;
                }
            }
            if (!hasRange || min is null || max is null)
            {
                reason = "it has no Range with both bounds";
                return false;
            }
            // With a Range but without a Step, DefaultNumberEnumerator keeps a step of 0 and never moves.
            if (step is not decimal stepValue || stepValue <= 0m)
            {
                reason = "it has no positive Step";
                return false;
            }
            if (max < min || (max.Value - min.Value) / stepValue >= MaxNumericValues)
            {
                reason = $"its Range/Step grid is empty or exceeds {MaxNumericValues} values";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var value = min.Value; ; value += stepValue)
            {
                var clamped = Math.Min(value, max.Value);
                var text = FormatNumber(clamped, type);
                if (seen.Add(text))
                {
                    valueTexts.Add(text);
                }
                if (clamped >= max.Value)
                {
                    break;
                }
            }
            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Parses a Range or Step argument the way the attributes do.
        /// </summary>
        /// <param name="text">The invariant-culture decimal text, or null or empty for none.</param>
        /// <returns>The parsed value, or null when the text is empty or invalid.</returns>
        private static decimal? ParseDecimal(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        /// <summary>
        /// Formats a stepped value as the property type displays it, using the invariant culture.
        /// </summary>
        /// <param name="value">The stepped decimal value.</param>
        /// <param name="type">The numeric property type the value is converted to.</param>
        /// <returns>The value text.</returns>
        private static string FormatNumber(decimal value, Type type)
        {
            var converted = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            return converted is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : converted.ToString();
        }

        /// <summary>
        /// Adds the keys of a name or title selection, whose templates only substitute the option name.
        /// </summary>
        /// <param name="source">The key templates and the option name substituted for <c>{0}</c>.</param>
        /// <param name="displayedName">The untranslated text, which an untranslated selection shows as-is.</param>
        /// <param name="usage">The usage location recorded for the keys.</param>
        /// <param name="keys">Receives the selected keys.</param>
        /// <param name="diagnostics">Receives invalid empty localization keys.</param>
        private static void AddTemplateKeys(TranslationSelection source, string displayedName, string usage,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            foreach (var template in source.Templates)
            {
                AddKey(FormatKey(template, source.OptionName, displayedName), usage, keys, diagnostics);
            }
        }

        /// <summary>
        /// Builds a localization key from a template the same way SettingTranslation does.
        /// </summary>
        /// <param name="template">The key template; <c>{0}</c> is the option name and <c>{1}</c> the value text.</param>
        /// <param name="optionName">The option name.</param>
        /// <param name="valueText">The value text.</param>
        /// <returns>The formatted key, or an empty key when the template is malformed.</returns>
        private static string FormatKey(string template, string optionName, string valueText)
        {
            try
            {
                return string.Format(template, optionName, valueText);
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Mirrors SettingValueSelection: adds the keys TranslateValuesAttribute requests for single values of an option
        /// whose values are otherwise untranslated, and reports the attribute where it has no effect.
        /// </summary>
        /// <param name="property">The option property metadata.</param>
        /// <param name="attributes">The property's custom attribute metadata.</param>
        /// <param name="valueSelection">The value key templates selected by the option's translation attributes.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives the selected value keys.</param>
        /// <param name="diagnostics">Receives ineffective attributes and numeric values that cannot be parsed.</param>
        private static void CollectSelectedValues(PropertyInfo property, IList<CustomAttributeData> attributes, TranslationSelection valueSelection,
            string path, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            var selections = attributes.Where(attribute => attribute.AttributeType.FullName == SettingsNamespace + "TranslateValuesAttribute").ToList();
            if (selections.Count == 0)
            {
                return;
            }
            var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var isNumeric = IsNumber(valueType);
            // Mirrors OptionEnumeratorBase.LocalizeValueText: selected values apply only while the values are untranslated.
            var isUntranslated = !valueSelection.IsTranslated || (isNumeric && !valueSelection.IsExplicit);
            if (!isUntranslated)
            {
                diagnostics.Add($"[Settings] {path}: TranslateValues has no effect because the option values are already translated.");
                return;
            }

            var optionName = GetOptionName(attributes, property.Name);
            foreach (var selection in selections)
            {
                var template = DefaultOptionValueTemplate;
                foreach (var argument in selection.NamedArguments)
                {
                    if (argument.MemberName == "Key" && argument.TypedValue.Value is string key && key.Length != 0)
                    {
                        template = key;
                    }
                }
                if (selection.ConstructorArguments.Count != 1 ||
                    selection.ConstructorArguments[0].Value is not IReadOnlyCollection<CustomAttributeTypedArgument> values)
                {
                    continue;
                }
                foreach (var value in values)
                {
                    if (value.Value is not string text || text.Length == 0)
                    {
                        continue;
                    }
                    if (isNumeric && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    {
                        diagnostics.Add($"[Settings] {path}: TranslateValues value \"{text}\" is not an invariant-culture number and is ignored.");
                        continue;
                    }
                    AddKey(FormatKey(template, optionName, text), path + SelectedValueUsage, keys, diagnostics);
                }
            }
        }

        /// <summary>
        /// Mirrors SettingTranslation.GetOptionName: the OptionNameAttribute value when present, otherwise the member name.
        /// </summary>
        /// <param name="attributes">The member's custom attribute metadata.</param>
        /// <param name="memberName">The declared member name.</param>
        /// <returns>The option name substituted for <c>{0}</c> in key templates.</returns>
        private static string GetOptionName(IList<CustomAttributeData> attributes, string memberName)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.FullName == SettingsNamespace + "OptionNameAttribute" &&
                    attribute.ConstructorArguments[0].Value is string name && name.Length != 0)
                {
                    return name;
                }
            }
            return memberName;
        }

        /// <summary>
        /// Mirrors SettingTranslation.Resolve on custom attribute data: the key templates of one translation target.
        /// </summary>
        /// <param name="attributes">The member's custom attribute metadata.</param>
        /// <param name="target">The TranslationTarget constant name: MenuName, OptionName or OptionValue.</param>
        /// <param name="memberName">The declared member name, or the displayed menu name.</param>
        /// <param name="defaultTemplate">The built-in template of the target.</param>
        /// <param name="generalTemplate">The general table template of the target.</param>
        /// <param name="generalByDefault">Whether the built-in default also tries the general template.</param>
        /// <param name="generalAfterSpecific">
        /// Whether explicitly selected option-specific templates are always followed by the general template, as the
        /// runtime does for boolean option values.
        /// </param>
        /// <returns>The selection; an untranslated one has no templates.</returns>
        private static TranslationSelection SelectTranslation(IList<CustomAttributeData> attributes, string target, string memberName,
            string defaultTemplate, string generalTemplate, bool generalByDefault, bool generalAfterSpecific)
        {
            var optionName = GetOptionName(attributes, memberName);
            var isExplicit = false;
            var specific = new List<string>();
            var general = new List<string>();
            foreach (var attribute in attributes)
            {
                var attributeName = attribute.AttributeType.FullName;
                if (attributeName != SettingsNamespace + "NoTranslationAttribute" &&
                    attributeName != SettingsNamespace + "UseTranslationAttribute" &&
                    attributeName != SettingsNamespace + "UseGeneralTranslationAttribute")
                {
                    continue;
                }
                if (attribute.ConstructorArguments.Count == 0 || GetEnumName(attribute.ConstructorArguments[0]) != target)
                {
                    continue;
                }
                isExplicit = true;
                switch (attributeName)
                {
                    case SettingsNamespace + "NoTranslationAttribute":
                        return new TranslationSelection(optionName, true, new List<string>());
                    case SettingsNamespace + "UseTranslationAttribute":
                        var key = attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value as string : null;
                        AddDistinct(specific, string.IsNullOrEmpty(key) ? defaultTemplate : key!);
                        break;
                    default:
                        AddDistinct(general, generalTemplate);
                        break;
                }
            }

            if (!isExplicit)
            {
                specific.Add(defaultTemplate);
                if (generalByDefault)
                {
                    specific.Add(generalTemplate);
                }
                return new TranslationSelection(memberName, false, specific);
            }
            if (generalAfterSpecific && specific.Count != 0)
            {
                AddDistinct(general, generalTemplate);
            }
            specific.AddRange(general);
            return new TranslationSelection(optionName, true, specific);
        }

        /// <summary>
        /// Adds a template unless the list already contains it, as the runtime SettingTranslation does.
        /// </summary>
        /// <param name="templates">The template list to extend.</param>
        /// <param name="template">The key template.</param>
        private static void AddDistinct(List<string> templates, string template)
        {
            if (!templates.Contains(template))
            {
                templates.Add(template);
            }
        }

        /// <summary>
        /// Mirrors SettingTranslation.HasGeneralValueFallback: whether option values of a type always fall back to
        /// the general table after their option-specific keys.
        /// </summary>
        /// <param name="type">The option property type, possibly nullable.</param>
        /// <returns>True for bool and Nullable&lt;bool&gt;; numeric and enum values follow their attributes only.</returns>
        private static bool HasGeneralValueFallback(Type type)
        {
            return (Nullable.GetUnderlyingType(type) ?? type) == typeof(bool);
        }

        /// <summary>
        /// Mirrors SettingTranslation.HasDefaultGeneralFallback: whether option values of a type without translation
        /// attributes try the general table after their option-specific key.
        /// </summary>
        /// <param name="type">The option property type, possibly nullable.</param>
        /// <returns>False for enums and their nullable forms, which only use the general table on request; otherwise true.</returns>
        private static bool HasDefaultGeneralFallback(Type type)
        {
            return !(Nullable.GetUnderlyingType(type) ?? type).IsEnum;
        }

        /// <summary>
        /// Reads an enum attribute argument by constant name, so the runtime enum type itself is never referenced.
        /// </summary>
        /// <param name="argument">The attribute constructor argument.</param>
        /// <returns>The constant name, or null for a non-enum or unnamed value.</returns>
        private static string? GetEnumName(CustomAttributeTypedArgument argument)
        {
            if (!argument.ArgumentType.IsEnum || argument.Value is null)
            {
                return null;
            }
            return Enum.GetName(argument.ArgumentType, argument.Value);
        }

        /// <summary>
        /// The key templates selected for one translation target, mirroring the runtime SettingTranslation rule.
        /// </summary>
        private sealed class TranslationSelection
        {
            /// <summary>
            /// Gets the option name substituted for <c>{0}</c>.
            /// </summary>
            internal string OptionName { get; }

            /// <summary>
            /// Gets whether a translation attribute selected the templates instead of the built-in default.
            /// </summary>
            internal bool IsExplicit { get; }

            /// <summary>
            /// Gets the key templates in lookup order; empty when the target is not translated.
            /// </summary>
            internal List<string> Templates { get; }

            /// <summary>
            /// Gets whether any key is looked up.
            /// </summary>
            internal bool IsTranslated => Templates.Count != 0;

            /// <summary>
            /// Initializes a new instance of the <see cref="TranslationSelection"/> class.
            /// </summary>
            /// <param name="optionName">The option name substituted for <c>{0}</c>.</param>
            /// <param name="isExplicit">Whether a translation attribute selected the templates.</param>
            /// <param name="templates">The key templates in lookup order.</param>
            internal TranslationSelection(string optionName, bool isExplicit, List<string> templates)
            {
                OptionName = optionName;
                IsExplicit = isExplicit;
                Templates = templates;
            }
        }

        /// <summary>
        /// Merges a fixed localization key and its usage location into the existing inventory.
        /// </summary>
        /// <param name="key">The exact, case-sensitive key requested by the runtime UI.</param>
        /// <param name="source">The usage location that led to discovering this key.</param>
        /// <param name="keys">The inventory that retains existing keys and sources.</param>
        /// <param name="diagnostics">Receives empty name or description overrides without inventing a default key.</param>
        private static void AddKey(string key, string source, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            if (string.IsNullOrEmpty(key))
            {
                diagnostics.Add($"[Settings] {source}: an empty localization key was ignored.");
                return;
            }
            if (!keys.TryGetValue(key, out var sources))
            {
                sources = new HashSet<string>(StringComparer.Ordinal);
                keys.Add(key, sources);
            }
            sources.Add(source);
        }
    }
}

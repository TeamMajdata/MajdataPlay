using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// Adds the setting menus, option labels, descriptions and finite localized values to a key inventory.
        /// </summary>
        /// <param name="assembly">The compiled Assembly-CSharp assembly containing the runtime settings metadata.</param>
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

                    // MenuMetadata.Name is the value property's name; MenuNameAttribute is not used by the UI.
                    AddKey("MAJSETTING_CATEGORY_" + menuProperty.Name, path + " (category)", keys, diagnostics);
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
        /// Mirrors SettingOptionMetadata's name, description and enumerator selection using custom attribute data.
        /// </summary>
        /// <param name="property">The option property metadata.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives fixed localization keys and their usage locations.</param>
        /// <param name="diagnostics">Receives unresolved or dynamic option information.</param>
        private static void CollectOption(PropertyInfo property, string path, IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            var attributes = property.GetCustomAttributesData();
            var nameKey = "MAJSETTING_PROPERTY_" + property.Name;
            var descriptionKey = nameKey + "_DESC";
            var hasDescription = true;
            Type? enumeratorType = null;
            foreach (var attribute in attributes)
            {
                switch (attribute.AttributeType.FullName)
                {
                    case SettingsNamespace + "OptionNameAttribute":
                        nameKey = attribute.ConstructorArguments[0].Value as string ?? string.Empty;
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

            AddKey(nameKey, path + " (name)", keys, diagnostics);
            if (hasDescription)
            {
                AddKey(descriptionKey, path + " (description)", keys, diagnostics);
            }
            CollectValues(property, enumeratorType, HasAttribute(attributes, SettingsNamespace + "OptionalAttribute"), path, keys, diagnostics);
        }

        /// <summary>
        /// Selects finite value sources using the same precedence as the runtime enumerator factory.
        /// </summary>
        /// <param name="property">The option property metadata.</param>
        /// <param name="enumeratorType">The explicit enumerator override, or null for the default factory.</param>
        /// <param name="isOptional">Whether the Optional attribute changes null display text to UNSET.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives property-specific and general fallback value keys.</param>
        /// <param name="diagnostics">Receives unsupported enumerators and dynamic value sources.</param>
        private static void CollectValues(PropertyInfo property, Type? enumeratorType, bool isOptional, string path,
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
                    AddValue(property.Name, bool.FalseString, path, keys, diagnostics);
                    AddValue(property.Name, bool.TrueString, path, keys, diagnostics);
                    // Nullable<bool> alone does not add null to DefaultBooleanEnumerator.OptionValues.
                    if (isOptional)
                    {
                        AddValue(property.Name, "UNSET", path, keys, diagnostics);
                    }
                    return;
                case EnumeratorNamespace + "DefaultEnumEnumerator":
                case EnumeratorNamespace + "EngineEnumSettingEnumerator":
                    if (!type.IsEnum)
                    {
                        diagnostics.Add($"[Settings] {path}: {enumeratorName} cannot initialize a non-enum property.");
                        return;
                    }
                    CollectEnumValues(type, property.Name, path, keys, diagnostics);
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
                    return;
                case EnumeratorNamespace + "DefaultReadOnlyEnumerator":
                    CollectReadOnlyValues(property, isOptional, path, keys, diagnostics);
                    return;
                case EnumeratorNamespace + "LanguageEnumerator":
                    // Only the no-language branch has a fixed value; loaded language identifiers are dynamic.
                    AddValue(property.Name, "Unavailable", path, keys, diagnostics);
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
                            AddValue(property.Name, value, path, keys, diagnostics);
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
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives statically known non-numeric values and null sentinel keys.</param>
        /// <param name="diagnostics">Receives runtime-dependent values that were intentionally excluded.</param>
        private static void CollectReadOnlyValues(PropertyInfo property, bool isOptional, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            var nullableType = Nullable.GetUnderlyingType(property.PropertyType);
            var valueType = nullableType ?? property.PropertyType;
            if (valueType.IsEnum)
            {
                CollectEnumValues(valueType, property.Name, path, keys, diagnostics);
            }
            else if (valueType == typeof(bool))
            {
                AddValue(property.Name, bool.FalseString, path, keys, diagnostics);
                AddValue(property.Name, bool.TrueString, path, keys, diagnostics);
            }
            else if (!IsNumber(valueType))
            {
                diagnostics.Add($"[Settings] {path}: read-only {valueType.FullName} values require runtime data; non-null value keys were not inferred.");
            }

            // Read-only nullable enums/numbers and reference values really do use this null fallback.
            // Optional on a non-nullable enum or number does not manufacture a null option.
            if (nullableType is not null || !property.PropertyType.IsValueType)
            {
                AddValue(property.Name, isOptional ? "UNSET" : "NULL", path, keys, diagnostics);
            }
        }

        /// <summary>
        /// Mirrors the boxed enum constants' ToString conversion, including duplicate aliases and flags formatting.
        /// </summary>
        /// <param name="enumType">The non-nullable enum type to inspect.</param>
        /// <param name="propertyName">The original property name used in option value templates.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives the enum label localization keys.</param>
        /// <param name="diagnostics">Receives empty localization keys if malformed metadata supplies them.</param>
        private static void CollectEnumValues(Type enumType, string propertyName, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            // Enum.GetNames would invent aliases that runtime ToString never requests; underlying numbers
            // would lose names such as _90. Reading enum constants never executes a settings accessor.
            foreach (var value in Enum.GetValues(enumType))
            {
                var text = value.ToString();
                if (text is not null)
                {
                    AddValue(propertyName, text, path, keys, diagnostics);
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
        /// Adds both lookup templates used by OptionEnumeratorBase while retaining the runtime's exact casing.
        /// </summary>
        /// <param name="propertyName">The original property name, not the OptionName override.</param>
        /// <param name="value">The fixed text resulting from the option value's ToString conversion.</param>
        /// <param name="path">The UI path identifying this option.</param>
        /// <param name="keys">Receives property-specific and general fallback keys.</param>
        /// <param name="diagnostics">Receives invalid empty localization keys.</param>
        private static void AddValue(string propertyName, string value, string path,
            IDictionary<string, HashSet<string>> keys, IList<string> diagnostics)
        {
            AddKey($"MAJSETTING_PROPERTY_{propertyName}_OPTION_{value}", path + " (option value)", keys, diagnostics);
            AddKey("MAJSETTING_GENERAL_OPTION_" + value, path + " (option value fallback)", keys, diagnostics);
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

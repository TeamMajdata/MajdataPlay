using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MajdataPlay.Settings;
using Newtonsoft.Json;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers <see cref="SettingDefaults"/>: every setting class takes its option defaults from it, and every option has
    /// one.
    /// </summary>
    /// <remarks>The default values themselves are checked by <see cref="SettingTests.Defaults_MatchFirstStartup"/>.</remarks>
    public class SettingDefaultsTests
    {
        /// <summary>The options that intentionally have no default, as <c>Class.Option</c>.</summary>
        private static readonly HashSet<string> s_optionsWithoutDefault = new()
        {
            $"{nameof(ChartSetting)}.{nameof(ChartSetting.Hash)}"
        };

        /// <summary>Gets the nested classes of <see cref="SettingDefaults"/>.</summary>
        /// <returns>The classes, one per setting class.</returns>
        private static Type[] GetDefaultClasses()
        {
            return typeof(SettingDefaults).GetNestedTypes(BindingFlags.Public);
        }

        /// <summary>Finds the setting class whose defaults a nested class of <see cref="SettingDefaults"/> holds.</summary>
        /// <param name="defaultClass">The nested class.</param>
        /// <returns>The setting class, or null when none is named after the nested class.</returns>
        private static Type? FindSettingClass(Type defaultClass)
        {
            var assembly = typeof(SettingDefaults).Assembly;
            return assembly.GetType($"{typeof(SettingDefaults).Namespace}.{defaultClass.Name}Options")
                ?? assembly.GetType($"{typeof(SettingDefaults).Namespace}.{defaultClass.Name}Setting");
        }

        /// <summary>Gets the default values a nested class of <see cref="SettingDefaults"/> declares.</summary>
        /// <param name="defaultClass">The nested class.</param>
        /// <returns>The option names and their default values.</returns>
        private static IEnumerable<(string Name, Type Type, object? Value)> GetDefaults(Type defaultClass)
        {
            foreach (var field in defaultClass.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return (field.Name, field.FieldType, field.GetValue(null));
            }
            foreach (var property in defaultClass.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                yield return (property.Name, property.PropertyType, property.GetValue(null));
            }
        }

        /// <summary>Determines whether a type is a group of options rather than the value of one option.</summary>
        /// <param name="type">The property type.</param>
        /// <returns>True for the option classes nested in a setting; otherwise false.</returns>
        private static bool IsOptionGroup(Type type)
        {
            return type.IsClass
                && type.Namespace == typeof(SettingDefaults).Namespace
                && type.Name.EndsWith("Options", StringComparison.Ordinal);
        }

        /// <summary>Gets the options of a setting class that hold a value and can be stored.</summary>
        /// <param name="settingClass">The setting class.</param>
        /// <returns>The writable properties whose type is not another option group.</returns>
        private static IEnumerable<PropertyInfo> GetValueOptions(Type settingClass)
        {
            return settingClass.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && !IsOptionGroup(p.PropertyType));
        }

        /// <summary>Every nested class is named after a setting class and only declares defaults of its options.</summary>
        [Test]
        public void DefaultClasses_MatchSettingClasses()
        {
            foreach (var defaultClass in GetDefaultClasses())
            {
                var settingClass = FindSettingClass(defaultClass);
                Assert.That(settingClass, Is.Not.Null, $"No setting class for {nameof(SettingDefaults)}.{defaultClass.Name}");
                foreach (var (name, type, _) in GetDefaults(defaultClass))
                {
                    var option = settingClass!.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    Assert.That(option, Is.Not.Null, $"{settingClass.Name} has no option {name}");
                    Assert.That(option!.PropertyType, Is.EqualTo(type), $"{settingClass.Name}.{name}");
                }
            }
        }

        /// <summary>A new instance of every setting class holds the values declared in <see cref="SettingDefaults"/>.</summary>
        [Test]
        public void NewSettings_UseTheDeclaredDefaults()
        {
            foreach (var defaultClass in GetDefaultClasses())
            {
                var settingClass = FindSettingClass(defaultClass)!;
                var setting = Activator.CreateInstance(settingClass);
                foreach (var (name, type, expected) in GetDefaults(defaultClass))
                {
                    var actual = settingClass.GetProperty(name)!.GetValue(setting);
                    if (type.IsValueType || type == typeof(string))
                    {
                        Assert.That(actual, Is.EqualTo(expected), $"{settingClass.Name}.{name}");
                    }
                    else
                    {
                        Assert.That(JsonConvert.SerializeObject(actual), Is.EqualTo(JsonConvert.SerializeObject(expected)),
                            $"{settingClass.Name}.{name}");
                    }
                }
            }
        }

        /// <summary>Every stored option of every setting class has a default in <see cref="SettingDefaults"/>.</summary>
        [Test]
        public void EveryOption_HasADefault()
        {
            var defaultClasses = GetDefaultClasses().ToDictionary(t => FindSettingClass(t)!, t => t);
            var settingClasses = typeof(SettingDefaults).Assembly.GetTypes()
                .Where(t => IsOptionGroup(t) || t == typeof(ChartSetting));
            foreach (var settingClass in settingClasses)
            {
                var options = GetValueOptions(settingClass)
                    .Where(p => !s_optionsWithoutDefault.Contains($"{settingClass.Name}.{p.Name}"))
                    .ToArray();
                if (options.Length == 0)
                {
                    continue;
                }
                Assert.That(defaultClasses.TryGetValue(settingClass, out var defaultClass), Is.True,
                    $"{settingClass.Name} has no nested class in {nameof(SettingDefaults)}");
                var declared = GetDefaults(defaultClass!).Select(d => d.Name).ToHashSet();
                foreach (var option in options)
                {
                    Assert.That(declared, Does.Contain(option.Name), $"{settingClass.Name}.{option.Name} has no default");
                }
            }
        }

        /// <summary>Defaults of mutable types are new instances, so one setting cannot change another's default.</summary>
        [Test]
        public void MutableDefaults_AreNewInstances()
        {
            Assert.That(SettingDefaults.Online.ApiEndpoints, Is.Not.SameAs(SettingDefaults.Online.ApiEndpoints));
            Assert.That(SettingDefaults.Online.Machine, Is.Not.SameAs(SettingDefaults.Online.Machine));
            Assert.That(new OnlineOptions().Machine, Is.Not.SameAs(new OnlineOptions().Machine));
        }
    }
}

using System;
using System.Collections.Generic;
using MajdataPlay.Settings;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>
    /// Covers <see cref="ShowInSettingUIWhenAttribute"/> and <see cref="SettingVisibility"/>, the conditions under which the
    /// setting UI shows an option or a menu.
    /// </summary>
    public class SettingVisibilityTests
    {
        /// <summary>A nested settings object reached through a dotted member path.</summary>
        public sealed class NestedProbe
        {
            /// <summary>Gets or sets a nested switch.</summary>
            public bool Flag { get; set; }
        }

        /// <summary>A settings section whose options are shown under various conditions.</summary>
        public sealed class VisibilityProbe
        {
            /// <summary>Gets or sets the switch most conditions depend on.</summary>
            public bool Enabled { get; set; }

            /// <summary>Gets or sets an enum setting.</summary>
            public MirrorOption Mirror { get; set; }

            /// <summary>Gets or sets a floating-point setting.</summary>
            public float Ratio { get; set; }

            /// <summary>Gets or sets a nullable setting.</summary>
            public int? Maybe { get; set; }

            /// <summary>Gets or sets a text setting.</summary>
            public string? Text { get; set; }

            /// <summary>Gets or sets a nested settings object.</summary>
            public NestedProbe Nested { get; set; } = new();

            /// <summary>Gets or sets a nested settings object that may be missing.</summary>
            public NestedProbe? Absent { get; set; }

            /// <summary>Sets a value that cannot be read back.</summary>
            public bool WriteOnly
            {
                set => Enabled = value;
            }

            /// <summary>Gets or sets an option without conditions.</summary>
            public int Plain { get; set; }

            /// <summary>Gets or sets an option shown while <see cref="Enabled"/> is set.</summary>
            [ShowInSettingUIWhen(nameof(Enabled), true)]
            public int WhenEnabled { get; set; }

            /// <summary>Gets or sets an option shown for either mirror mode.</summary>
            [ShowInSettingUIWhen(nameof(Mirror), MirrorOption.LRMirror, MirrorOption.UDMirror)]
            public int WhenMirrored { get; set; }

            /// <summary>Gets or sets an option shown only while both conditions hold.</summary>
            [ShowInSettingUIWhen(nameof(Enabled), true)]
            [ShowInSettingUIWhen(nameof(Ratio), 1)]
            public int WhenBoth { get; set; }

            /// <summary>Gets or sets an option whose enum condition is written as an integer.</summary>
            [ShowInSettingUIWhen(nameof(Mirror), 2)]
            public int WhenUpsideDown { get; set; }

            /// <summary>Gets or sets an option shown while the nested switch is set.</summary>
            [ShowInSettingUIWhen("Nested.Flag", true)]
            public int WhenNested { get; set; }

            /// <summary>Gets or sets an option whose condition crosses a missing object.</summary>
            [ShowInSettingUIWhen("Absent.Flag", false)]
            public int WhenAbsent { get; set; }

            /// <summary>Gets or sets an option shown while <see cref="Maybe"/> has no value.</summary>
            [ShowInSettingUIWhen(nameof(Maybe), null)]
            public int WhenUnset { get; set; }

            /// <summary>Gets or sets an option shown while <see cref="Maybe"/> is one or unset.</summary>
            [ShowInSettingUIWhen(nameof(Maybe), 1, null)]
            public int WhenOneOrUnset { get; set; }

            /// <summary>Gets or sets an option shown while <see cref="Text"/> has a given value.</summary>
            [ShowInSettingUIWhen(nameof(Text), "on")]
            public int WhenText { get; set; }

            /// <summary>Gets or sets an option whose condition names a missing setting.</summary>
            [ShowInSettingUIWhen("Missing", true)]
            public int BrokenPath { get; set; }

            /// <summary>Gets or sets an option whose condition cannot be read.</summary>
            [ShowInSettingUIWhen(nameof(WriteOnly), true)]
            public int BrokenWriteOnly { get; set; }

            /// <summary>Gets or sets an option whose condition has an empty path.</summary>
            [ShowInSettingUIWhen("", true)]
            public int BrokenEmptyPath { get; set; }

            /// <summary>Gets or sets an option whose condition has no values.</summary>
            [ShowInSettingUIWhen(nameof(Enabled))]
            public int BrokenNoValues { get; set; }

            /// <summary>Gets or sets an option whose value cannot be parsed as the setting type.</summary>
            [ShowInSettingUIWhen(nameof(Ratio), "abc")]
            public int BrokenUnparsable { get; set; }

            /// <summary>Gets or sets an option whose enum condition uses another enum type.</summary>
            [ShowInSettingUIWhen(nameof(Mirror), RandomModeOption.Disabled)]
            public int BrokenEnumType { get; set; }

            /// <summary>Gets or sets an option whose condition expects null from a non-nullable setting.</summary>
            [ShowInSettingUIWhen(nameof(Enabled), null)]
            public int BrokenNull { get; set; }

            /// <summary>Gets or sets an option hidden from the setting UI whatever its condition says.</summary>
            [HideInSettingUI]
            [ShowInSettingUIWhen(nameof(Enabled), true)]
            public int Hidden { get; set; }
        }

        /// <summary>A setting root whose menus are shown under conditions on another menu.</summary>
        public sealed class RootProbe
        {
            /// <summary>Gets the menu the conditions read.</summary>
            public VisibilityProbe Main { get; } = new();

            /// <summary>Gets a menu shown while the main menu is enabled.</summary>
            [ShowInSettingUIWhen("Main.Enabled", true)]
            public NestedProbe Extra { get; } = new();
        }

        /// <summary>Resolves the visibility of one probe option directly, so resolution errors surface as exceptions.</summary>
        /// <param name="name">The option name.</param>
        /// <returns>The resolved visibility.</returns>
        private static SettingVisibility Resolve(string name)
        {
            var property = typeof(VisibilityProbe).GetProperty(name)!;
            return SettingVisibility.Resolve(typeof(VisibilityProbe), name, Attribute.GetCustomAttributes(property, true));
        }

        /// <summary>Checks that the attribute copies its values and treats a single null argument as the value null.</summary>
        [Test]
        public void Attribute_StoresPathAndValues()
        {
            var values = new object?[] { 1, 2 };
            var attribute = new ShowInSettingUIWhenAttribute("Path", values);
            values[0] = 3;
            var nullValue = new ShowInSettingUIWhenAttribute("Path", null);
            var property = typeof(VisibilityProbe).GetProperty(nameof(VisibilityProbe.WhenUnset))!;
            var declared = (ShowInSettingUIWhenAttribute)Attribute.GetCustomAttribute(property, typeof(ShowInSettingUIWhenAttribute))!;

            Assert.That(attribute.MemberPath, Is.EqualTo("Path"));
            Assert.That(attribute.Values, Is.EqualTo(new object?[] { 1, 2 }));
            Assert.That(nullValue.Values, Is.EqualTo(new object?[] { null }));
            Assert.That(declared.Values, Is.EqualTo(new object?[] { null }));
        }

        /// <summary>Checks that an option without conditions is always shown and shares one instance.</summary>
        [Test]
        public void Resolve_WithoutConditions_IsAlways()
        {
            var visibility = Resolve(nameof(VisibilityProbe.Plain));

            Assert.That(visibility, Is.SameAs(SettingVisibility.Always));
            Assert.That(visibility.IsVisible(new VisibilityProbe()), Is.True);
        }

        /// <summary>Checks that a boolean condition follows the controlling setting.</summary>
        [Test]
        public void IsVisible_FollowsBooleanSetting()
        {
            var visibility = Resolve(nameof(VisibilityProbe.WhenEnabled));
            var probe = new VisibilityProbe();

            Assert.That(visibility.IsVisible(probe), Is.False);
            probe.Enabled = true;
            Assert.That(visibility.IsVisible(probe), Is.True);
        }

        /// <summary>Checks that a condition with several values holds for any of them.</summary>
        [Test]
        public void IsVisible_MatchesAnyValue()
        {
            var visibility = Resolve(nameof(VisibilityProbe.WhenMirrored));
            var probe = new VisibilityProbe();

            Assert.That(visibility.IsVisible(probe), Is.False);
            probe.Mirror = MirrorOption.LRMirror;
            Assert.That(visibility.IsVisible(probe), Is.True);
            probe.Mirror = MirrorOption.UDMirror;
            Assert.That(visibility.IsVisible(probe), Is.True);
        }

        /// <summary>Checks that several attributes must all hold, and that an integer matches a float setting.</summary>
        [Test]
        public void IsVisible_RequiresEveryCondition()
        {
            var visibility = Resolve(nameof(VisibilityProbe.WhenBoth));
            var probe = new VisibilityProbe { Enabled = true };

            Assert.That(visibility.IsVisible(probe), Is.False);
            probe.Ratio = 1f;
            Assert.That(visibility.IsVisible(probe), Is.True);
            probe.Enabled = false;
            Assert.That(visibility.IsVisible(probe), Is.False);
        }

        /// <summary>Checks that an integer value matches the enum constant with that value.</summary>
        [Test]
        public void IsVisible_ConvertsIntegerToEnum()
        {
            var visibility = Resolve(nameof(VisibilityProbe.WhenUpsideDown));
            var probe = new VisibilityProbe { Mirror = MirrorOption.LRMirror };

            Assert.That(visibility.IsVisible(probe), Is.False);
            probe.Mirror = MirrorOption.UDMirror;
            Assert.That(visibility.IsVisible(probe), Is.True);
        }

        /// <summary>Checks that a dotted path reads a nested setting and that a missing object on the path never matches.</summary>
        [Test]
        public void IsVisible_FollowsNestedPath()
        {
            var nested = Resolve(nameof(VisibilityProbe.WhenNested));
            var absent = Resolve(nameof(VisibilityProbe.WhenAbsent));
            var probe = new VisibilityProbe();

            Assert.That(nested.IsVisible(probe), Is.False);
            probe.Nested.Flag = true;
            Assert.That(nested.IsVisible(probe), Is.True);
            Assert.That(absent.IsVisible(probe), Is.False);
            probe.Absent = new NestedProbe();
            Assert.That(absent.IsVisible(probe), Is.True);
        }

        /// <summary>Checks that null matches a nullable or reference setting without a value, alone or among other values.</summary>
        [Test]
        public void IsVisible_MatchesNullValues()
        {
            var unset = Resolve(nameof(VisibilityProbe.WhenUnset));
            var oneOrUnset = Resolve(nameof(VisibilityProbe.WhenOneOrUnset));
            var text = Resolve(nameof(VisibilityProbe.WhenText));
            var probe = new VisibilityProbe();

            Assert.That(unset.IsVisible(probe), Is.True);
            Assert.That(oneOrUnset.IsVisible(probe), Is.True);
            probe.Maybe = 1;
            Assert.That(unset.IsVisible(probe), Is.False);
            Assert.That(oneOrUnset.IsVisible(probe), Is.True);
            probe.Maybe = 2;
            Assert.That(oneOrUnset.IsVisible(probe), Is.False);
            Assert.That(text.IsVisible(probe), Is.False);
            probe.Text = "on";
            Assert.That(text.IsVisible(probe), Is.True);
        }

        /// <summary>Checks that a menu condition starts at the setting root and reads another menu.</summary>
        [Test]
        public void IsVisible_MenuConditionReadsAnotherMenu()
        {
            var property = typeof(RootProbe).GetProperty(nameof(RootProbe.Extra))!;
            var visibility = SettingVisibility.Resolve(typeof(RootProbe), property.Name, Attribute.GetCustomAttributes(property, true));
            var root = new RootProbe();

            Assert.That(visibility.IsVisible(root), Is.False);
            root.Main.Enabled = true;
            Assert.That(visibility.IsVisible(root), Is.True);
        }

        /// <summary>Checks the exceptions reported for conditions that cannot be resolved.</summary>
        [Test]
        public void Resolve_InvalidConditions_Throw()
        {
            Assert.Throws<MissingMemberException>(() => Resolve(nameof(VisibilityProbe.BrokenPath)));
            Assert.Throws<ArgumentException>(() => Resolve(nameof(VisibilityProbe.BrokenWriteOnly)));
            Assert.Throws<ArgumentException>(() => Resolve(nameof(VisibilityProbe.BrokenEmptyPath)));
            Assert.Throws<ArgumentException>(() => Resolve(nameof(VisibilityProbe.BrokenNoValues)));
            Assert.Throws<ArgumentException>(() => Resolve(nameof(VisibilityProbe.BrokenUnparsable)));
            Assert.Throws<ArgumentException>(() => Resolve(nameof(VisibilityProbe.BrokenEnumType)));
            Assert.Throws<ArgumentException>(() => Resolve(nameof(VisibilityProbe.BrokenNull)));
            Assert.Throws<ArgumentNullException>(() => SettingVisibility.Resolve(null!, "Name", Array.Empty<Attribute>()));
            Assert.Throws<ArgumentNullException>(() => SettingVisibility.Always.IsVisible(null!));
        }

        /// <summary>Checks that the cache logs an unresolvable condition and keeps the option visible instead of failing.</summary>
        [Test]
        public void GetVisibility_InvalidCondition_FallsBackToAlways()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var property = SettingReflectionCache.GetProperty(typeof(VisibilityProbe), nameof(VisibilityProbe.BrokenPath));

                var visibility = SettingReflectionCache.GetVisibility(property);

                Assert.That(visibility, Is.SameAs(SettingVisibility.Always));
                Assert.That(SettingReflectionCache.GetVisibility(property), Is.SameAs(visibility));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        /// <summary>Checks that the cache resolves valid conditions and that hidden properties stay out of the visible list.</summary>
        [Test]
        public void GetVisibility_ResolvesAndCachesConditions()
        {
            var property = SettingReflectionCache.GetProperty(typeof(RootProbe), nameof(RootProbe.Extra));

            var visibility = SettingReflectionCache.GetVisibility(property);

            Assert.That(visibility, Is.Not.SameAs(SettingVisibility.Always));
            Assert.That(SettingReflectionCache.GetVisibility(property), Is.SameAs(visibility));
            Assert.That(visibility.IsVisible(new RootProbe()), Is.False);
            Assert.That(Array.Exists(SettingReflectionCache.GetVisibleProperties(typeof(RootProbe)), p => p.Name == nameof(RootProbe.Extra)), Is.True);
            Assert.Throws<ArgumentNullException>(() => SettingReflectionCache.GetVisibility(null!));
        }

        /// <summary>Checks that every condition declared on the real settings resolves, so a typo fails here instead of being logged at runtime.</summary>
        [Test]
        public void RealSettings_AllConditionsResolve()
        {
            var pending = new Queue<Type>(new[] { typeof(GameSetting), typeof(ChartSetting) });
            var visited = new HashSet<Type>();
            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                if (!visited.Add(type))
                {
                    continue;
                }
                foreach (var property in SettingReflectionCache.GetVisibleProperties(type))
                {
                    var attributes = Attribute.GetCustomAttributes(property, true);
                    Assert.DoesNotThrow(() => SettingVisibility.Resolve(type, property.Name, attributes), $"{type.Name}.{property.Name}");
                    var propertyType = property.PropertyType;
                    if (propertyType.IsClass && propertyType != typeof(string) && propertyType.Namespace == typeof(GameSetting).Namespace)
                    {
                        pending.Enqueue(propertyType);
                    }
                }
            }
            Assert.That(visited, Does.Contain(typeof(DebugOptions)));
        }
    }
}

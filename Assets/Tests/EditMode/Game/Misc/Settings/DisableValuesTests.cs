using System;
using MajdataPlay.Settings;
using MajdataPlay.Settings.OptionEnumerators;
using NUnit.Framework;
using UnityEngine.TestTools;

#nullable enable
namespace MajdataPlay.Tests.Game.Misc.Settings
{
    /// <summary>Covers <see cref="DisableValuesAttribute"/> and how the option enumerators skip the values it disables.</summary>
    public class DisableValuesTests
    {
        /// <summary>An enum whose constants are disabled on some platforms.</summary>
        public enum PlatformBackend
        {
            /// <summary>Enabled everywhere.</summary>
            Portable = 0,

            /// <summary>Disabled on every platform except Windows.</summary>
            [DisableValues(Platforms = SettingPlatform.All & ~SettingPlatform.Windows)]
            WindowsOnly = 1,

            /// <summary>Disabled on the mobile platforms.</summary>
            [DisableValues(Platforms = SettingPlatform.Mobile)]
            DesktopOnly = 2,

            /// <summary>Disabled on every platform through the parameterless constructor.</summary>
            [DisableValues]
            Retired = 3
        }

        /// <summary>An enum whose constants are all disabled.</summary>
        public enum NothingLeft
        {
            /// <summary>Disabled on every platform.</summary>
            [DisableValues]
            First = 0,

            /// <summary>Disabled on every platform.</summary>
            [DisableValues]
            Second = 1
        }

        /// <summary>A settings section with options that disable values.</summary>
        public sealed class DisableProbe
        {
            /// <summary>An enum option using the constant rules only.</summary>
            public PlatformBackend Backend { get; set; } = PlatformBackend.Portable;

            /// <summary>An enum option that also disables a constant on Android.</summary>
            [DisableValues(nameof(PlatformBackend.Portable), Platforms = SettingPlatform.Android)]
            public PlatformBackend AndroidBackend { get; set; } = PlatformBackend.DesktopOnly;

            /// <summary>A read-only enum option holding a disabled constant.</summary>
            [ReadOnlyOption]
            public PlatformBackend LockedBackend { get; set; } = PlatformBackend.Retired;

            /// <summary>An enum option whose every constant is disabled.</summary>
            public NothingLeft Empty { get; set; } = NothingLeft.Second;

            /// <summary>A bounded integer option that disables 1 and 2 everywhere and 4 on Android.</summary>
            [DisableValues("1", "2")]
            [DisableValues("4", Platforms = SettingPlatform.Android)]
            [Step("1")]
            [global::MajdataPlay.Settings.Range("0", "4")]
            public int Count { get; set; }

            /// <summary>A floating-point option that disables 0.5.</summary>
            [DisableValues("0.5")]
            [Step("0.25")]
            [global::MajdataPlay.Settings.Range("0", "1")]
            public float Scale { get; set; } = 0.25f;

            /// <summary>An integer option whose stored value is disabled.</summary>
            [DisableValues("0", "1")]
            [Step("1")]
            [global::MajdataPlay.Settings.Range("0", "3")]
            public int Stored { get; set; }

            /// <summary>An integer option whose every value in range is disabled.</summary>
            [DisableValues("0", "1")]
            [Step("1")]
            [global::MajdataPlay.Settings.Range("0", "1")]
            public int Stuck { get; set; }

            /// <summary>An option with an invalid number.</summary>
            [DisableValues("one")]
            public int Invalid { get; set; }

            /// <summary>An enum option with an unknown constant name.</summary>
            [DisableValues("Missing")]
            public PlatformBackend Unknown { get; set; }

            /// <summary>A text option, which ignores the attribute.</summary>
            [DisableValues("x")]
            public string Text { get; set; } = "x";
        }

        /// <summary>The platform active before the test.</summary>
        private SettingPlatform _savedPlatform;

        /// <summary>Captures the evaluated platform.</summary>
        [SetUp]
        public void SetUp()
        {
            _savedPlatform = SettingDisabledValues.CurrentPlatform;
        }

        /// <summary>Restores the evaluated platform.</summary>
        [TearDown]
        public void TearDown()
        {
            SettingDisabledValues.CurrentPlatform = _savedPlatform;
        }

        /// <summary>Creates and initializes an enumerator for a property of a new probe.</summary>
        /// <typeparam name="T">The enumerator type.</typeparam>
        /// <param name="probe">The probe owning the property.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The initialized enumerator.</returns>
        private static T Create<T>(DisableProbe probe, string name) where T : IOptionEnumerator, new()
        {
            var enumerator = new T();
            enumerator.Init(typeof(DisableProbe).GetProperty(name)!, probe);
            return enumerator;
        }

        /// <summary>Resolves the disabled values of a probe property.</summary>
        /// <param name="name">The property name.</param>
        /// <returns>The resolved rule.</returns>
        private static SettingDisabledValues Resolve(string name)
        {
            var property = typeof(DisableProbe).GetProperty(name)!;
            return SettingDisabledValues.Resolve(Attribute.GetCustomAttributes(property, true), name, property.PropertyType);
        }

        /// <summary>Collects the values an enumerator cycles through, starting from its current value.</summary>
        /// <param name="enumerator">The enumerator.</param>
        /// <param name="count">The number of values to collect.</param>
        /// <returns>The values in cycle order.</returns>
        private static object?[] Cycle(IOptionEnumerator enumerator, int count)
        {
            var values = new object?[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = enumerator.Current;
                enumerator.MoveNext();
            }
            return values;
        }

        /// <summary>Checks that the attribute can be written without arguments and defaults to every platform.</summary>
        [Test]
        public void Attribute_DefaultsToEveryPlatform()
        {
            var empty = new DisableValuesAttribute();
            var listed = new DisableValuesAttribute("1", "2");

            Assert.That(empty.Values, Is.Empty);
            Assert.That(empty.Platforms, Is.EqualTo(SettingPlatform.All));
            Assert.That(listed.Values, Is.EqualTo(new[] { "1", "2" }));
            Assert.That(new DisableValuesAttribute(null!).Values, Is.Empty);
            Assert.That(SettingPlatform.All, Is.EqualTo(SettingPlatform.Desktop | SettingPlatform.Mobile));
        }

        /// <summary>Checks that enum constants are disabled per platform by their own attributes.</summary>
        [TestCase(SettingPlatform.Windows, new[] { PlatformBackend.Portable, PlatformBackend.WindowsOnly, PlatformBackend.DesktopOnly })]
        [TestCase(SettingPlatform.Linux, new[] { PlatformBackend.Portable, PlatformBackend.DesktopOnly })]
        [TestCase(SettingPlatform.Android, new[] { PlatformBackend.Portable })]
        [TestCase(SettingPlatform.IOS, new[] { PlatformBackend.Portable })]
        public void EnumEnumerator_SkipsDisabledConstants(SettingPlatform platform, PlatformBackend[] expected)
        {
            SettingDisabledValues.CurrentPlatform = platform;
            var enumerator = Create<DefaultEnumEnumerator>(new DisableProbe(), nameof(DisableProbe.Backend));

            var values = Cycle(enumerator, expected.Length + 1);

            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(values[i], Is.EqualTo(expected[i]), platform.ToString());
            }
            Assert.That(values[expected.Length], Is.EqualTo(expected[0]), "wraps around");
        }

        /// <summary>Checks that an option-level rule adds to the constant rules and that a stored disabled value is replaced.</summary>
        [Test]
        public void EnumEnumerator_CombinesOptionRulesAndReplacesStoredValue()
        {
            SettingDisabledValues.CurrentPlatform = SettingPlatform.Android;
            var probe = new DisableProbe();

            var enumerator = Create<DefaultEnumEnumerator>(probe, nameof(DisableProbe.AndroidBackend));

            // Android disables everything but Portable through the constants and Portable through the option.
            // That leaves nothing, so the rule is ignored as a whole.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.That(enumerator.Current, Is.EqualTo(PlatformBackend.DesktopOnly));
                Assert.That(Cycle(enumerator, 5)[4], Is.EqualTo(PlatformBackend.DesktopOnly), "all four constants stay listed");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            SettingDisabledValues.CurrentPlatform = SettingPlatform.Linux;
            probe = new DisableProbe { AndroidBackend = PlatformBackend.Retired };
            enumerator = Create<DefaultEnumEnumerator>(probe, nameof(DisableProbe.AndroidBackend));

            Assert.That(probe.AndroidBackend, Is.EqualTo(PlatformBackend.Portable));
            Assert.That(enumerator.Current, Is.EqualTo(PlatformBackend.Portable));
            enumerator.MoveNext();
            Assert.That(probe.AndroidBackend, Is.EqualTo(PlatformBackend.DesktopOnly));
            enumerator.MoveNext();
            Assert.That(probe.AndroidBackend, Is.EqualTo(PlatformBackend.Portable));
        }

        /// <summary>Checks that a read-only option keeps a disabled value and that a fully disabled enum keeps every constant.</summary>
        [Test]
        public void EnumEnumerator_KeepsValuesWhenItCannotReplaceThem()
        {
            SettingDisabledValues.CurrentPlatform = SettingPlatform.Windows;
            var probe = new DisableProbe();

            Create<DefaultEnumEnumerator>(probe, nameof(DisableProbe.LockedBackend));
            LogAssert.ignoreFailingMessages = true;
            IOptionEnumerator empty;
            try
            {
                empty = Create<DefaultEnumEnumerator>(probe, nameof(DisableProbe.Empty));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(probe.LockedBackend, Is.EqualTo(PlatformBackend.Retired));
            Assert.That(empty.Current, Is.EqualTo(NothingLeft.Second));
            empty.MoveNext();
            Assert.That(probe.Empty, Is.EqualTo(NothingLeft.First));
        }

        /// <summary>Checks that numeric options step past disabled values and stop before a disabled bound.</summary>
        [Test]
        public void NumberEnumerator_StepsPastDisabledValues()
        {
            SettingDisabledValues.CurrentPlatform = SettingPlatform.Android;
            var probe = new DisableProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(DisableProbe.Count));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Count, Is.EqualTo(3));
            Assert.That(enumerator.ValueText, Is.EqualTo("3"));
            Assert.That(enumerator.MoveNext(), Is.False, "4 is disabled on Android");
            Assert.That(probe.Count, Is.EqualTo(3));
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(probe.Count, Is.EqualTo(0));
            Assert.That(enumerator.MovePrevious(), Is.False);

            SettingDisabledValues.CurrentPlatform = SettingPlatform.Windows;
            probe = new DisableProbe { Count = 3 };
            enumerator = Create<DefaultNumberEnumerator>(probe, nameof(DisableProbe.Count));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Count, Is.EqualTo(4));
        }

        /// <summary>Checks that floating-point options match disabled values numerically.</summary>
        [Test]
        public void NumberEnumerator_MatchesFloatingPointValues()
        {
            var probe = new DisableProbe();
            var enumerator = Create<DefaultNumberEnumerator>(probe, nameof(DisableProbe.Scale));

            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(probe.Scale, Is.EqualTo(0.75f));
            Assert.That(enumerator.MovePrevious(), Is.True);
            Assert.That(probe.Scale, Is.EqualTo(0.25f));
        }

        /// <summary>Checks that a stored disabled number is replaced by the next enabled one, and kept when none is in range.</summary>
        [Test]
        public void NumberEnumerator_ReplacesStoredDisabledValue()
        {
            var probe = new DisableProbe();

            var stored = Create<DefaultNumberEnumerator>(probe, nameof(DisableProbe.Stored));
            var stuck = Create<DefaultNumberEnumerator>(probe, nameof(DisableProbe.Stuck));

            Assert.That(probe.Stored, Is.EqualTo(2));
            Assert.That(stored.ValueText, Is.EqualTo("2"));
            Assert.That(probe.Stuck, Is.EqualTo(0));
            Assert.That(stuck.MoveNext(), Is.False);
            Assert.That(stuck.MovePrevious(), Is.False);
        }

        /// <summary>Checks that invalid entries are dropped with errors and that other option types ignore the attribute.</summary>
        [Test]
        public void Resolve_DropsInvalidEntries()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.That(Resolve(nameof(DisableProbe.Invalid)).IsEmpty, Is.True);
                Assert.That(Resolve(nameof(DisableProbe.Unknown)).IsDisabled(PlatformBackend.Portable), Is.False);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
            Assert.That(Resolve(nameof(DisableProbe.Text)), Is.SameAs(SettingDisabledValues.None));
            Assert.That(SettingDisabledValues.None.IsDisabled(null), Is.False);
            Assert.Throws<ArgumentNullException>(() => SettingDisabledValues.Resolve(null!, "Name", typeof(int)));
            Assert.Throws<ArgumentNullException>(() => SettingDisabledValues.Resolve(Array.Empty<Attribute>(), null!, typeof(int)));
            Assert.Throws<ArgumentNullException>(() => SettingDisabledValues.Resolve(Array.Empty<Attribute>(), "Name", null!));
        }
    }
}

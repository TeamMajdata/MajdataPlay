using System.Linq;
using System.Reflection;
using MajdataPlay.Utils;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Game.Utils
{
    /// <summary>Verifies the cached member tables of <see cref="Reflection{T}"/>.</summary>
    public class ReflectionTests
    {
        /// <summary>Checks that public and non-public instance and static methods are cached.</summary>
        [Test]
        public void Methods_IncludeInstanceStaticAndNonPublicMethods()
        {
            var names = Reflection<ReflectionSample>.Methods.ToArray().Select(x => x.Name).ToArray();

            Assert.That(names, Does.Contain(nameof(ReflectionSample.PublicMethod)));
            Assert.That(names, Does.Contain("PrivateMethod"));
            Assert.That(names, Does.Contain(nameof(ReflectionSample.StaticMethod)));
        }

        /// <summary>Checks that properties and fields of every visibility are cached.</summary>
        [Test]
        public void PropertiesAndFields_IncludeNonPublicMembers()
        {
            var properties = Reflection<ReflectionSample>.Properties.ToArray().Select(x => x.Name).ToArray();
            var fields = Reflection<ReflectionSample>.Fields.ToArray().Select(x => x.Name).ToArray();

            Assert.That(properties, Does.Contain(nameof(ReflectionSample.Value)));
            Assert.That(properties, Does.Contain("Hidden"));
            Assert.That(fields, Does.Contain("_counter"));
            Assert.That(fields, Does.Contain(nameof(ReflectionSample.SharedField)));
        }

        /// <summary>Checks that only public constructors are cached.</summary>
        [Test]
        public void Constructors_ContainOnlyPublicConstructors()
        {
            var constructors = Reflection<ReflectionSample>.Constructors.ToArray();

            Assert.That(constructors, Has.Length.EqualTo(1));
            Assert.That(constructors[0].IsPublic, Is.True);
        }

        /// <summary>Checks that the member table contains members of every kind and is shared between reads.</summary>
        [Test]
        public void Members_ContainEveryKindAndAreCached()
        {
            var first = Reflection<ReflectionSample>.Members;
            var second = Reflection<ReflectionSample>.Members;
            var kinds = first.ToArray().Select(x => x.MemberType).Distinct().ToArray();

            Assert.That(kinds, Does.Contain(MemberTypes.Method));
            Assert.That(kinds, Does.Contain(MemberTypes.Field));
            Assert.That(kinds, Does.Contain(MemberTypes.Property));
            Assert.That(kinds, Does.Contain(MemberTypes.Constructor));
            Assert.That(first.Equals(second), Is.True);
        }

        /// <summary>A type with members of several visibilities used to inspect the reflection cache.</summary>
        private sealed class ReflectionSample
        {
            /// <summary>A public static field.</summary>
            public static int SharedField;

            /// <summary>A private instance field.</summary>
            private int _counter;

            /// <summary>Creates the sample.</summary>
            public ReflectionSample()
            {
                _counter = 1;
            }

            /// <summary>Creates the sample with a start value; private so it is excluded from the public constructor table.</summary>
            /// <param name="counter">The start value.</param>
            private ReflectionSample(int counter)
            {
                _counter = counter;
            }

            /// <summary>Gets a public value.</summary>
            public int Value => _counter;

            /// <summary>Gets a private value.</summary>
            private int Hidden => _counter + SharedField;

            /// <summary>A public instance method.</summary>
            /// <returns>The counter.</returns>
            public int PublicMethod()
            {
                return Hidden;
            }

            /// <summary>A public static method.</summary>
            /// <returns>A sample built with the private constructor.</returns>
            public static object StaticMethod()
            {
                return new ReflectionSample(2);
            }

            /// <summary>A private instance method.</summary>
            private void PrivateMethod()
            {
                _counter++;
            }
        }
    }
}

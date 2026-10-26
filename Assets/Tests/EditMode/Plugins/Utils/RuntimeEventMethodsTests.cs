using System;
using System.Collections.Generic;
using System.Linq;
using MajdataPlay.Tests.Plugins.Utils.PlayMode;
using MajdataPlay.Utils.Events;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>
    /// Covers the binding rules of <see cref="RuntimeEventMethods"/>: the attribute requirement, exact signature matching
    /// including by-reference modifiers, inheritance, the Inspector method list, signature formatting and delegate
    /// creation.
    /// </summary>
    public class RuntimeEventMethodsTests
    {
        /// <summary>Creates and destroys the test components.</summary>
        private TestObjects _objects = null!;

        /// <summary>Creates the object tracker.</summary>
        [SetUp]
        public void SetUp()
        {
            _objects = new TestObjects();
        }

        /// <summary>Destroys the test components.</summary>
        [TearDown]
        public void TearDown()
        {
            _objects.Dispose();
        }

        /// <summary>Checks that a marked public method is found and that the overload with the delegate's exact parameter type wins.</summary>
        [Test]
        public void FindBindableMethod_ReturnsMarkedMethodWithExactParameterType()
        {
            var intMethod = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValue", typeof(Action<int>));
            var longMethod = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValue", typeof(Action<long>));

            Assert.That(intMethod, Is.Not.Null);
            Assert.That(intMethod!.GetParameters()[0].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(longMethod, Is.Not.Null);
            Assert.That(longMethod!.GetParameters()[0].ParameterType, Is.EqualTo(typeof(long)));
        }

        /// <summary>Checks that a marked private method is found.</summary>
        [Test]
        public void FindBindableMethod_ReturnsMarkedPrivateMethod()
        {
            var method = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnPrivateValue", typeof(Action<int>));

            Assert.That(method, Is.Not.Null);
            Assert.That(method!.IsPrivate, Is.True);
        }

        /// <summary>Checks that a method without <see cref="BindableMethodAttribute"/> is rejected even when its signature matches.</summary>
        [Test]
        public void FindBindableMethod_RejectsUnmarkedMethod()
        {
            var method = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnUnmarkedValue", typeof(Action<int>));

            Assert.That(method, Is.Null);
        }

        /// <summary>Checks that the return type must match: a method returning int binds to Func but not to Action.</summary>
        [Test]
        public void FindBindableMethod_RequiresMatchingReturnType()
        {
            var asAction = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValueReturningDouble", typeof(Action<int>));
            var asFunc = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValueReturningDouble", typeof(Func<int, int>));

            Assert.That(asAction, Is.Null);
            Assert.That(asFunc, Is.Not.Null);
        }

        /// <summary>Checks that parameter types are not converted: <c>Action&lt;short&gt;</c> does not bind <c>OnValue(int)</c>.</summary>
        [Test]
        public void FindBindableMethod_RejectsWideningParameterConversion()
        {
            var method = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValue", typeof(Action<short>));

            Assert.That(method, Is.Null);
        }

        /// <summary>Checks that marked static and generic methods are rejected.</summary>
        [Test]
        public void FindBindableMethod_RejectsStaticAndGenericMethods()
        {
            var staticMethod = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnStaticValue", typeof(Action<int>));
            var genericMethod = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnGenericValue", typeof(Action<int>));

            Assert.That(staticMethod, Is.Null);
            Assert.That(genericMethod, Is.Null);
        }

        /// <summary>Checks that <c>in</c>, <c>ref</c> and <c>out</c> parameters bind only to delegates with the same modifier.</summary>
        /// <param name="methodName">The listener method.</param>
        /// <param name="delegateType">The delegate type to bind to.</param>
        /// <returns>Whether the method is bindable.</returns>
        [TestCase("OnInPayload", typeof(RuntimeEventInHandler), ExpectedResult = true)]
        [TestCase("OnInPayload", typeof(RuntimeEventRefHandler), ExpectedResult = false)]
        [TestCase("OnRefPayload", typeof(RuntimeEventRefHandler), ExpectedResult = true)]
        [TestCase("OnRefPayload", typeof(RuntimeEventInHandler), ExpectedResult = false)]
        [TestCase("OnRefValue", typeof(RuntimeEventRefIntHandler), ExpectedResult = true)]
        [TestCase("OnRefValue", typeof(RuntimeEventOutIntHandler), ExpectedResult = false)]
        [TestCase("OnOutValue", typeof(RuntimeEventOutIntHandler), ExpectedResult = true)]
        [TestCase("OnOutValue", typeof(RuntimeEventRefIntHandler), ExpectedResult = false)]
        public bool FindBindableMethod_MatchesByReferenceModifiersExactly(string methodName, Type delegateType)
        {
            return RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), methodName, delegateType) is not null;
        }

        /// <summary>Checks that a marked private method declared by a base class is found from the derived type.</summary>
        [Test]
        public void FindBindableMethod_FindsPrivateBaseMethodFromDerivedType()
        {
            var method = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventDerivedListener), "OnPrivateValue", typeof(Action<int>));

            Assert.That(method, Is.Not.Null);
            Assert.That(method!.DeclaringType, Is.EqualTo(typeof(RuntimeEventListener)));
        }

        /// <summary>Checks that a marked override is returned instead of the base declaration.</summary>
        [Test]
        public void FindBindableMethod_ReturnsMarkedOverride()
        {
            var method = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventDerivedListener), "OnVirtualValue", typeof(Action<int>));

            Assert.That(method, Is.Not.Null);
            Assert.That(method!.DeclaringType, Is.EqualTo(typeof(RuntimeEventDerivedListener)));
        }

        /// <summary>Checks that an unmarked override hides the marked base method, because the attribute is not inherited.</summary>
        [Test]
        public void FindBindableMethod_UnmarkedOverrideHidesMarkedBaseMethod()
        {
            var derivedMethod = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventDerivedListener), "OnShadowedValue", typeof(Action<int>));
            var baseMethod = RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnShadowedValue", typeof(Action<int>));

            Assert.That(derivedMethod, Is.Null);
            Assert.That(baseMethod, Is.Not.Null);
        }

        /// <summary>Checks that an empty method name and an unsupported delegate type find nothing.</summary>
        [Test]
        public void FindBindableMethod_ReturnsNullForEmptyNameOrUnsupportedDelegateType()
        {
            Assert.That(RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), string.Empty, typeof(Action<int>)), Is.Null);
            Assert.That(RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValue", typeof(Delegate)), Is.Null);
        }

        /// <summary>Checks that null arguments are rejected.</summary>
        [Test]
        public void FindBindableMethod_ThrowsForNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => RuntimeEventMethods.FindBindableMethod(null!, "OnValue", typeof(Action<int>)));
            Assert.Throws<ArgumentNullException>(() => RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), null!, typeof(Action<int>)));
            Assert.Throws<ArgumentNullException>(() => RuntimeEventMethods.FindBindableMethod(typeof(RuntimeEventListener), "OnValue", null!));
        }

        /// <summary>Checks that the Inspector list holds one bindable method per name, ordered by name.</summary>
        [Test]
        public void GetBindableMethods_ListsOneBindableMethodPerNameInNameOrder()
        {
            var baseNames = GetNames(RuntimeEventMethods.GetBindableMethods(typeof(RuntimeEventListener), typeof(Action<int>)));
            var derivedMethods = RuntimeEventMethods.GetBindableMethods(typeof(RuntimeEventDerivedListener), typeof(Action<int>));

            Assert.That(baseNames, Is.EqualTo(new[] { "OnPrivateValue", "OnShadowedValue", "OnValue", "OnVirtualValue" }));
            Assert.That(GetNames(derivedMethods), Is.EqualTo(new[] { "OnPrivateValue", "OnValue", "OnVirtualValue" }));
            Assert.That(derivedMethods.Single(method => method.Name == "OnVirtualValue").DeclaringType, Is.EqualTo(typeof(RuntimeEventDerivedListener)));
        }

        /// <summary>Checks that the list for by-reference and returning delegates holds only exact matches.</summary>
        [Test]
        public void GetBindableMethods_ListsOnlyExactSignatureMatches()
        {
            Assert.That(GetNames(RuntimeEventMethods.GetBindableMethods(typeof(RuntimeEventListener), typeof(RuntimeEventRefHandler))), Is.EqualTo(new[] { "OnRefPayload" }));
            Assert.That(GetNames(RuntimeEventMethods.GetBindableMethods(typeof(RuntimeEventListener), typeof(Func<int, int>))), Is.EqualTo(new[] { "OnValueReturningDouble" }));
        }

        /// <summary>Checks that an unsupported delegate type lists no methods.</summary>
        [Test]
        public void GetBindableMethods_ReturnsEmptyForUnsupportedDelegateType()
        {
            Assert.That(RuntimeEventMethods.GetBindableMethods(typeof(RuntimeEventListener), typeof(MulticastDelegate)), Is.Empty);
        }

        /// <summary>Checks which types are supported as event delegate types.</summary>
        /// <param name="delegateType">The type to check.</param>
        /// <returns>Whether the type is supported.</returns>
        [TestCase(typeof(Action), ExpectedResult = true)]
        [TestCase(typeof(Action<int>), ExpectedResult = true)]
        [TestCase(typeof(RuntimeEventInHandler), ExpectedResult = true)]
        [TestCase(typeof(Delegate), ExpectedResult = false)]
        [TestCase(typeof(MulticastDelegate), ExpectedResult = false)]
        [TestCase(typeof(Action<>), ExpectedResult = false)]
        [TestCase(typeof(int), ExpectedResult = false)]
        public bool IsSupportedDelegateType_AcceptsOnlyConcreteDelegateTypes(Type delegateType)
        {
            return RuntimeEventMethods.IsSupportedDelegateType(delegateType);
        }

        /// <summary>Checks the C# notation of formatted signatures.</summary>
        /// <param name="delegateType">The delegate type to format.</param>
        /// <returns>The formatted signature.</returns>
        [TestCase(typeof(Action), ExpectedResult = "()")]
        [TestCase(typeof(Action<int>), ExpectedResult = "(int)")]
        [TestCase(typeof(Func<int, bool>), ExpectedResult = "(int) -> bool")]
        [TestCase(typeof(RuntimeEventInHandler), ExpectedResult = "(in RuntimeEventTestPayload)")]
        [TestCase(typeof(RuntimeEventRefIntHandler), ExpectedResult = "(ref int)")]
        [TestCase(typeof(RuntimeEventOutIntHandler), ExpectedResult = "(out int)")]
        [TestCase(typeof(Action<List<string>, int[], float?>), ExpectedResult = "(List<string>, int[], float?)")]
        [TestCase(typeof(Action<int[,]>), ExpectedResult = "(int[,])")]
        public string FormatSignature_UsesCSharpNotation(Type delegateType)
        {
            return RuntimeEventMethods.FormatSignature(delegateType);
        }

        /// <summary>Checks that formatting an unsupported delegate type throws.</summary>
        [Test]
        public void FormatSignature_ThrowsForUnsupportedDelegateType()
        {
            Assert.Throws<ArgumentException>(() => RuntimeEventMethods.FormatSignature(typeof(Delegate)));
        }

        /// <summary>Checks that a created delegate calls the bound method on the target, and that unbindable methods create none.</summary>
        [Test]
        public void TryCreateDelegate_BindsTargetOrFails()
        {
            var listener = _objects.Add<RuntimeEventListener>();

            var created = RuntimeEventMethods.TryCreateDelegate(listener, "OnValue", typeof(Action<int>), out var handler);
            ((Action<int>)handler!).Invoke(3);
            var rejected = RuntimeEventMethods.TryCreateDelegate(listener, "OnUnmarkedValue", typeof(Action<int>), out var rejectedHandler);

            Assert.That(created, Is.True);
            Assert.That(listener.Calls, Is.EqualTo(new[] { "OnValue:3" }));
            Assert.That(rejected, Is.False);
            Assert.That(rejectedHandler, Is.Null);
        }

        /// <summary>Gets the names of methods in list order.</summary>
        /// <param name="methods">The methods.</param>
        /// <returns>The method names.</returns>
        private static string[] GetNames(IReadOnlyList<System.Reflection.MethodInfo> methods)
        {
            return methods.Select(method => method.Name).ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ManagedBass;
using ManagedBass.Aac;
using ManagedBass.Asio;
using ManagedBass.Fx;
using ManagedBass.Mix;
using ManagedBass.Opus;
using ManagedBass.Wasapi;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.ManagedBass
{
    /// <summary>
    /// Checks the IL2CPP rules for the callbacks that ManagedBass hands to native code. IL2CPP only marshals a delegate to native
    /// code when it points to a static method carrying an attribute named <c>MonoPInvokeCallbackAttribute</c>; for any other
    /// delegate it throws <see cref="NotSupportedException"/> when the delegate is passed, as it did for the free sync of
    /// <see cref="ChannelReferences"/>. The Editor runs on Mono, which marshals any delegate, so these checks stand in for a Player.
    /// </summary>
    public class NativeCallbackRulesTests
    {
        /// <summary>The attribute name IL2CPP looks for, in any namespace.</summary>
        private const string CallbackAttributeName = "MonoPInvokeCallbackAttribute";

        /// <summary>The binding flags that find every member declared by a type.</summary>
        private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>The ManagedBass assemblies of the project.</summary>
        private static readonly Assembly[] Assemblies =
        {
            typeof(Bass).Assembly,
            typeof(BassAac).Assembly,
            typeof(BassAsio).Assembly,
            typeof(BassFx).Assembly,
            typeof(BassMix).Assembly,
            typeof(BassOpus).Assembly,
            typeof(BassWasapi).Assembly,
        };

        /// <summary>Checks that the free sync of <see cref="ChannelReferences"/>, which IL2CPP rejected, is now a marshallable callback.</summary>
        [Test]
        public void ChannelReferencesFreeSync_IsAStaticCallback()
        {
            var method = typeof(ChannelReferences).GetMethod("OnChannelFree", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            Assert.That(GetCallbackDelegateType(method!), Is.EqualTo(typeof(SyncProcedure)));
        }

        /// <summary>Checks that every method marked as a native callback is static and matches the delegate type of its attribute.</summary>
        [Test]
        public void CallbackMethods_AreStaticAndMatchTheirDelegateType()
        {
            var methods = GetTypes().SelectMany(type => type.GetMethods(DeclaredMembers)).Where(method => GetCallbackDelegateType(method) != null).ToList();

            Assert.That(methods, Is.Not.Empty);
            foreach (var method in methods)
            {
                var delegateType = GetCallbackDelegateType(method)!;
                Assert.That(method.IsStatic, Is.True, $"{Describe(method)} is not static.");
                Assert.That(typeof(Delegate).IsAssignableFrom(delegateType), Is.True, $"{Describe(method)} names {delegateType}, which is not a delegate type.");
                Assert.That(Delegate.CreateDelegate(delegateType, method, false), Is.Not.Null, $"{Describe(method)} does not match {delegateType}.");
            }
        }

        /// <summary>
        /// Checks that every delegate held in a static field, which is how ManagedBass keeps its native callbacks alive, points to a
        /// static method marked as a callback of the field's delegate type. A lambda or an instance method would fail on IL2CPP.
        /// </summary>
        [Test]
        public void StaticDelegateFields_PointToCallbackMethods()
        {
            var checkedFields = 0;
            foreach (var field in GetTypes().SelectMany(type => type.GetFields(DeclaredMembers)))
            {
                if (!field.IsStatic || !typeof(Delegate).IsAssignableFrom(field.FieldType) || field.DeclaringType!.ContainsGenericParameters)
                {
                    continue;
                }

                // Event fields hold the managed subscribers, not native callbacks.
                if (field.GetValue(null) is not Delegate value || field.DeclaringType.GetEvent(field.Name, DeclaredMembers) != null)
                {
                    continue;
                }
                foreach (var target in value.GetInvocationList())
                {
                    Assert.That(target.Target, Is.Null, $"{field.DeclaringType}.{field.Name} points to an instance method or a lambda.");
                    Assert.That(GetCallbackDelegateType(target.Method), Is.EqualTo(field.FieldType), $"{field.DeclaringType}.{field.Name} points to {Describe(target.Method)}, which is not marked as a {field.FieldType.Name} callback.");
                }
                checkedFields++;
            }

            Assert.That(checkedFields, Is.GreaterThanOrEqualTo(14), "Fewer callback fields than the ManagedBass trampolines were found.");
        }

        /// <summary>Gets the delegate type named by the native callback attribute of a method.</summary>
        /// <param name="method">The method to inspect.</param>
        /// <returns>The delegate type, or <see langword="null"/> when the method has no such attribute.</returns>
        private static Type? GetCallbackDelegateType(MethodInfo method)
        {
            var attribute = method.GetCustomAttributesData().FirstOrDefault(data => data.AttributeType.Name == CallbackAttributeName);
            if (attribute == null)
            {
                return null;
            }
            Assert.That(attribute.ConstructorArguments, Has.Count.EqualTo(1), $"The callback attribute of {Describe(method)} does not name a delegate type.");
            return (Type)attribute.ConstructorArguments[0].Value;
        }

        /// <summary>Gets every type of the ManagedBass assemblies, including nested and non-public ones.</summary>
        /// <returns>The types.</returns>
        private static IEnumerable<Type> GetTypes()
        {
            return Assemblies.SelectMany(assembly => assembly.GetTypes());
        }

        /// <summary>Formats a method for assertion messages.</summary>
        /// <param name="method">The method.</param>
        /// <returns>The declaring type and the method name.</returns>
        private static string Describe(MethodInfo method)
        {
            return $"{method.DeclaringType}.{method.Name}";
        }
    }
}

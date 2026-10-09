using MajdataPlay.Platform.Android;
using MajdataPlay.Platform.Android.Runtime.Java.Lang;
using MajdataPlay.Tests.Bindings;
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

#nullable enable

namespace MajdataPlay.Tests
{
    /// <summary>
    /// Verifies the real Unity compiler loads the analyzer and compiles SDK-generated JNI wrappers.
    /// </summary>
    public static class AndroidJavaGeneratorUnityValidation
    {
        /// <summary>
        /// Validates generated API shape and editor safety, then exits the isolated editor.
        /// </summary>
        public static void Run()
        {
            try
            {
                Require(typeof(JavaObject).IsAssignableFrom(typeof(BuildVersion)), "SDK wrapper must derive from JavaObject.");
                Require(typeof(JavaObject).IsAssignableFrom(typeof(Runnable)), "Java interface wrapper must derive from JavaObject.");
                Require(!typeof(JavaObject).IsAbstract, "JavaObject must be a non-abstract, directly constructible class.");
                RequireObjectOverrides();
                var sdkInt = typeof(BuildVersion).GetProperty("SdkInt", BindingFlags.Public | BindingFlags.Static);
                Require(sdkInt is not null && sdkInt.PropertyType == typeof(int) && sdkInt.SetMethod is null, "SDK_INT must be a static getter-only int property.");
                Require(typeof(Runnable).GetMethod("Run", Type.EmptyTypes) is not null, "Java interface method must be generated.");
                RequireReferenceWrappingOnly(typeof(Runnable), "Java interface wrapper");
                RequireReferenceWrappingOnly(typeof(InputStream), "Abstract Java class wrapper");
                RequireReferenceWrappingOnly(typeof(Looper), "SDK class without public Java constructors");
                RequireIntentConstructors();
                Require(typeof(JavaStringBuilder).GetConstructor(new[] { typeof(int) }) is not null, "SDK primitive constructor parameters must be preserved.");
                try
                {
                    using var builder = new JavaStringBuilder();
                    throw new InvalidOperationException("JNI construction must not execute in the Unity Editor.");
                }
                catch (PlatformNotSupportedException)
                {
                }

                RequireGuard(() => new JavaObject(), "JavaObject must reject JNI construction outside an Android player.");
                RequireGuard(() => new Intent(), "SDK parameterless constructors must reject JNI construction outside an Android player.");
                RequireGuard(() => new Intent("validation"), "SDK parameterized constructors must reject JNI construction outside an Android player.");

                Debug.Log("ANDROID_JAVA_GENERATOR_UNITY_PASSED: SDK public constructors, non-instantiable types, interfaces, readonly fields, overloads, and editor JNI guard.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("ANDROID_JAVA_GENERATOR_UNITY_FAILED: " + exception);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Verifies Object overrides and managed null equality without creating a JNI reference.
        /// </summary>
        /// <exception cref="InvalidOperationException">An override or nullable comparison is incorrect.</exception>
        private static void RequireObjectOverrides()
        {
            foreach (var name in new[] { nameof(object.Equals), nameof(object.GetHashCode), nameof(object.ToString) })
            {
                var parameters = name == nameof(object.Equals) ? new[] { typeof(object) } : Type.EmptyTypes;
                var method = typeof(JavaObject).GetMethod(name, parameters);
                Require(method is not null && method.DeclaringType == typeof(JavaObject)
                    && method.GetBaseDefinition().DeclaringType == typeof(object), name + " must override the managed Object method.");
            }

            JavaObject? left = null;
            JavaObject? right = null;
            Require(left == right, "Two null JavaObject wrappers must compare equal without JNI.");
            Require(!(left != right), "Two null JavaObject wrappers must not compare unequal.");
        }

        /// <summary>
        /// Requires a non-instantiable Java type to expose only the reference-wrapping constructor.
        /// </summary>
        /// <param name="wrapper">The generated wrapper under validation.</param>
        /// <param name="context">The failure description for this wrapper.</param>
        /// <exception cref="InvalidOperationException">The wrapper exposes a Java-instantiation constructor.</exception>
        private static void RequireReferenceWrappingOnly(Type wrapper, string context)
        {
            var constructors = wrapper.GetConstructors();
            Require(constructors.Length == 1, context + " must expose only the reference-wrapping constructor, but has " + constructors.Length + ".");
            var reference = constructors[0];
            Require(reference.GetParameters().Length == 2, context + " must expose a two-parameter reference-wrapping constructor.");
            Require(reference.GetParameters()[0].ParameterType == typeof(AndroidJavaObject) && reference.GetParameters()[1].ParameterType == typeof(bool),
                context + " must expose the (AndroidJavaObject, bool) reference-wrapping constructor.");
        }

        /// <summary>
        /// Requires every public API-36 Intent constructor in addition to reference wrapping.
        /// </summary>
        /// <exception cref="InvalidOperationException">An SDK constructor is absent or an extra constructor is generated.</exception>
        private static void RequireIntentConstructors()
        {
            var signatures = new[]
            {
                Type.EmptyTypes,
                new[] { typeof(Intent) },
                new[] { typeof(string) },
                new[] { typeof(AndroidJavaObject), typeof(AndroidJavaObject) },
                new[] { typeof(string), typeof(AndroidJavaObject) },
                new[] { typeof(string), typeof(AndroidJavaObject), typeof(AndroidJavaObject), typeof(AndroidJavaObject) },
                new[] { typeof(AndroidJavaObject), typeof(bool) }
            };
            Require(typeof(Intent).GetConstructors().Length == signatures.Length, "Intent must expose its six public SDK constructors and reference wrapping.");
            foreach (var signature in signatures)
            {
                Require(typeof(Intent).GetConstructor(signature) is not null,
                    "Missing Intent constructor with parameters: " + string.Join(", ", signature.Select(type => type.Name)) + ".");
            }
        }

        /// <summary>
        /// Requires an operation to fail with the non-Android player guard, never by constructing a Java object.
        /// </summary>
        /// <param name="operation">The construction under validation.</param>
        /// <param name="message">The failed expectation.</param>
        /// <exception cref="InvalidOperationException">The operation did not throw the expected guard.</exception>
        private static void RequireGuard(Action operation, string message)
        {
            try
            {
                operation();
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Rejects an unexpected generated API shape with an actionable failure.
        /// </summary>
        /// <param name="condition">Whether the expectation is satisfied.</param>
        /// <param name="message">The failed expectation.</param>
        /// <exception cref="InvalidOperationException">The expectation is false.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}

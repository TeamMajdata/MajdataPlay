using MajdataPlay.Platform.Android;
using MajdataPlay.Tests.Bindings;
using System;
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
                Require(typeof(AndroidObject).IsAssignableFrom(typeof(BuildVersion)), "SDK wrapper must derive from AndroidObject.");
                Require(typeof(AndroidObject).IsAssignableFrom(typeof(Runnable)), "Java interface wrapper must derive from AndroidObject.");
                var sdkInt = typeof(BuildVersion).GetProperty("SdkInt", BindingFlags.Public | BindingFlags.Static);
                Require(sdkInt is not null && sdkInt.PropertyType == typeof(int) && sdkInt.SetMethod is null, "SDK_INT must be a static getter-only int property.");
                Require(typeof(Runnable).GetMethod("Run", Type.EmptyTypes) is not null, "Java interface method must be generated.");
                var constructors = typeof(Runnable).GetConstructors();
                Require(constructors.Length == 1 && constructors[0].GetParameters()[0].ParameterType == typeof(AndroidJavaObject), "Interface wrappers must only wrap existing references.");
                try
                {
                    using var builder = new JavaStringBuilder();
                    throw new InvalidOperationException("JNI construction must not execute in the Unity Editor.");
                }
                catch (PlatformNotSupportedException)
                {
                }

                Debug.Log("ANDROID_JAVA_GENERATOR_UNITY_PASSED: SDK classes, interfaces, readonly fields, overloads, and editor JNI guard.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("ANDROID_JAVA_GENERATOR_UNITY_FAILED: " + exception);
                EditorApplication.Exit(1);
            }
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

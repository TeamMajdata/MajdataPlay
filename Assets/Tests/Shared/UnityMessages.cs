using System.Collections;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests
{
    /// <summary>
    /// Sends Unity messages such as Awake, Start and Update to components in EditMode tests, where the player loop does
    /// not call them on ordinary behaviours.
    /// </summary>
    public static class UnityMessages
    {
        /// <summary>Invokes a parameterless Unity message on a component when the component declares it.</summary>
        /// <param name="component">The component that receives the message.</param>
        /// <param name="message">The message name, for example Awake or Update.</param>
        /// <returns>True when the component declares the message and it was invoked.</returns>
        /// <exception cref="System.Reflection.TargetInvocationException">The message threw; the original exception is the inner exception.</exception>
        public static bool Send(Component component, string message)
        {
            var method = FindMessage(component, message);
            if (method is null)
            {
                return false;
            }
            method.Invoke(component, null);
            return true;
        }

        /// <summary>Invokes a coroutine message such as an IEnumerator Start and runs it to completion synchronously.</summary>
        /// <param name="component">The component that receives the message.</param>
        /// <param name="message">The message name.</param>
        /// <param name="maxSteps">The maximum number of MoveNext calls, protecting tests from endless coroutines.</param>
        /// <returns>The number of steps taken, or -1 when the component does not declare the message.</returns>
        /// <exception cref="System.Reflection.TargetInvocationException">The message threw; the original exception is the inner exception.</exception>
        public static int RunCoroutine(Component component, string message, int maxSteps = 10000)
        {
            var method = FindMessage(component, message);
            if (method is null)
            {
                return -1;
            }
            if (method.Invoke(component, null) is not IEnumerator enumerator)
            {
                return 0;
            }
            var steps = 0;
            while (steps < maxSteps && enumerator.MoveNext())
            {
                steps++;
            }
            return steps;
        }

        /// <summary>Finds a parameterless message method declared on the component type or its base types.</summary>
        /// <param name="component">The component to search.</param>
        /// <param name="message">The message name.</param>
        /// <returns>The method, or null when it is not declared.</returns>
        private static System.Reflection.MethodInfo? FindMessage(Component component, string message)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly;
            for (var type = component.GetType(); type is not null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                var method = type.GetMethod(message, flags, null, System.Type.EmptyTypes, null);
                if (method is not null)
                {
                    return method;
                }
            }
            return null;
        }
    }
}

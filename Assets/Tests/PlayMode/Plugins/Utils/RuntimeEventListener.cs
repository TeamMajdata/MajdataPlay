using System.Collections.Generic;
using MajdataPlay.Utils.Events;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils.PlayMode
{
    /// <summary>
    /// A component with bindable and unbindable methods of every shape that the <see cref="RuntimeEvent{TDelegate}"/>
    /// signature rules distinguish. Every call is recorded in <see cref="Calls"/>.
    /// </summary>
    internal class RuntimeEventListener : MonoBehaviour
    {
        /// <summary>Gets the received calls as <c>Method:value</c> entries in call order.</summary>
        public List<string> Calls { get; } = new();

        /// <summary>A marked public method matching <c>Action&lt;int&gt;</c>.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public void OnValue(int value)
        {
            Calls.Add($"{nameof(OnValue)}:{value}");
        }

        /// <summary>A marked overload of <see cref="OnValue(int)"/> matching <c>Action&lt;long&gt;</c>.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public void OnValue(long value)
        {
            Calls.Add($"{nameof(OnValue)}(long):{value}");
        }

        /// <summary>A method matching <c>Action&lt;int&gt;</c> without the attribute.</summary>
        /// <param name="value">The event value.</param>
        public void OnUnmarkedValue(int value)
        {
            Calls.Add($"{nameof(OnUnmarkedValue)}:{value}");
        }

        /// <summary>A marked method with a return value, matching <c>Func&lt;int, int&gt;</c> but not <c>Action&lt;int&gt;</c>.</summary>
        /// <param name="value">The event value.</param>
        /// <returns>Twice <paramref name="value"/>.</returns>
        [BindableMethod]
        public int OnValueReturningDouble(int value)
        {
            Calls.Add($"{nameof(OnValueReturningDouble)}:{value}");
            return value * 2;
        }

        /// <summary>A marked static method, which cannot be bound.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public static void OnStaticValue(int value)
        {
        }

        /// <summary>A marked generic method, which cannot be bound.</summary>
        /// <typeparam name="T">An unused type parameter.</typeparam>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public void OnGenericValue<T>(int value)
        {
            Calls.Add($"{nameof(OnGenericValue)}:{value}");
        }

        /// <summary>A marked method with an <c>in</c> parameter.</summary>
        /// <param name="payload">The payload.</param>
        [BindableMethod]
        public void OnInPayload(in RuntimeEventTestPayload payload)
        {
            Calls.Add($"{nameof(OnInPayload)}:{payload.Value}");
        }

        /// <summary>A marked method with a <c>ref</c> struct parameter; it increments the payload.</summary>
        /// <param name="payload">The payload to increment.</param>
        [BindableMethod]
        public void OnRefPayload(ref RuntimeEventTestPayload payload)
        {
            payload.Value++;
            Calls.Add($"{nameof(OnRefPayload)}:{payload.Value}");
        }

        /// <summary>A marked method with a <c>ref</c> integer parameter; it increments the value.</summary>
        /// <param name="value">The value to increment.</param>
        [BindableMethod]
        public void OnRefValue(ref int value)
        {
            value++;
            Calls.Add($"{nameof(OnRefValue)}:{value}");
        }

        /// <summary>A marked method with an <c>out</c> integer parameter; it sets the value to 7.</summary>
        /// <param name="value">Set to 7.</param>
        [BindableMethod]
        public void OnOutValue(out int value)
        {
            value = 7;
            Calls.Add($"{nameof(OnOutValue)}:{value}");
        }

        /// <summary>A marked virtual method that <see cref="RuntimeEventDerivedListener"/> overrides with a marked override.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public virtual void OnVirtualValue(int value)
        {
            Calls.Add($"Base.{nameof(OnVirtualValue)}:{value}");
        }

        /// <summary>A marked virtual method that <see cref="RuntimeEventDerivedListener"/> overrides without the attribute.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        public virtual void OnShadowedValue(int value)
        {
            Calls.Add($"Base.{nameof(OnShadowedValue)}:{value}");
        }

        /// <summary>A marked private method matching <c>Action&lt;int&gt;</c>.</summary>
        /// <param name="value">The event value.</param>
        [BindableMethod]
        private void OnPrivateValue(int value)
        {
            Calls.Add($"{nameof(OnPrivateValue)}:{value}");
        }
    }
}

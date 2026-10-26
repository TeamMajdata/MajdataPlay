using System;
using System.Reflection;
using MajdataPlay.IO;
using MajdataPlay.Settings;

#nullable enable
namespace MajdataPlay.Tests.Game.IO
{
    /// <summary>Represents a device method that consumes one block of raw report bytes.</summary>
    /// <param name="data">The raw bytes read from the device.</param>
    internal delegate void SpanParser(ReadOnlySpan<byte> data);

    /// <summary>Represents a device method that observes a cancellation token.</summary>
    /// <param name="token">The token passed to the device.</param>
    internal delegate void TokenAction(System.Threading.CancellationToken token);

    /// <summary>
    /// Gives tests access to protected device members whose span parameters cannot be passed through reflection
    /// invocation, and swaps the static configuration devices read.
    /// </summary>
    internal static class DeviceTestSupport
    {
        /// <summary>The binding flags used to find protected instance methods.</summary>
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>Binds the protected <c>Parse(ReadOnlySpan&lt;byte&gt;)</c> method of a device.</summary>
        /// <param name="device">The device instance.</param>
        /// <returns>A delegate that calls the device's parser.</returns>
        /// <exception cref="MissingMethodException">The device has no parser.</exception>
        public static SpanParser BindParser(object device)
        {
            return (SpanParser)Delegate.CreateDelegate(typeof(SpanParser), device, FindMethod(device.GetType(), "Parse", typeof(ReadOnlySpan<byte>)));
        }

        /// <summary>Binds a protected method taking a cancellation token, such as <c>Update</c>.</summary>
        /// <param name="device">The device instance.</param>
        /// <param name="name">The method name.</param>
        /// <returns>A delegate that calls the method.</returns>
        /// <exception cref="MissingMethodException">The device has no such method.</exception>
        public static TokenAction BindTokenAction(object device, string name)
        {
            return (TokenAction)Delegate.CreateDelegate(typeof(TokenAction), device, FindMethod(device.GetType(), name, typeof(System.Threading.CancellationToken)));
        }

        /// <summary>Sets the player slot reported by <see cref="IODetector"/>.</summary>
        /// <param name="playerIndex">The player slot, 1 or 2.</param>
        /// <returns>The previous player slot.</returns>
        public static int SetPlayerIndex(int playerIndex)
        {
            var previous = IODetector.PlayerIndex;
            PrivateMember.SetStaticProperty(typeof(IODetector), nameof(IODetector.PlayerIndex), playerIndex);
            return previous;
        }

        /// <summary>Finds a method with one parameter of the given type on a type or its base types.</summary>
        /// <param name="type">The type to search first.</param>
        /// <param name="name">The method name.</param>
        /// <param name="parameterType">The single parameter type.</param>
        /// <returns>The method.</returns>
        /// <exception cref="MissingMethodException">No matching method exists.</exception>
        private static MethodInfo FindMethod(Type type, string name, Type parameterType)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                var method = current.GetMethod(name, InstanceFlags | BindingFlags.DeclaredOnly, null, new[] { parameterType }, null);
                if (method is not null)
                {
                    return method;
                }
            }
            throw new MissingMethodException(type.FullName, name);
        }
    }

    /// <summary>Replaces <see cref="MajEnv.Settings"/> with fresh defaults and restores the original when disposed.</summary>
    internal sealed class SettingsScope : IDisposable
    {
        /// <summary>The settings object that was active before the scope.</summary>
        private readonly GameSetting _original;

        /// <summary>Installs a new default settings object.</summary>
        public SettingsScope()
        {
            _original = MajEnv.Settings;
            Settings = new GameSetting();
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), Settings);
        }

        /// <summary>Gets the settings object installed for the scope.</summary>
        public GameSetting Settings { get; }

        /// <summary>Restores the original settings object.</summary>
        public void Dispose()
        {
            PrivateMember.SetStaticProperty(typeof(MajEnv), nameof(MajEnv.Settings), _original);
        }
    }
}

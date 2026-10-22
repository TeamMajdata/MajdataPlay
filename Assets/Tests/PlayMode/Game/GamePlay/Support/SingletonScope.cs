using System;

#nullable enable
namespace MajdataPlay.Tests.Game.GamePlay.PlayMode.Support
{
    /// <summary>
    /// Captures the instance registered in <see cref="Majdata{T}"/> and restores it when disposed, so a test can register
    /// its own instance or clear the slot without leaking into other tests.
    /// </summary>
    /// <typeparam name="T">The registered service type.</typeparam>
    internal sealed class SingletonScope<T> : IDisposable where T : class
    {
        /// <summary>The instance that was registered before the scope was created.</summary>
        private readonly T? _previous;

        /// <summary>Captures the current instance and optionally replaces it.</summary>
        /// <param name="instance">The instance to register for the scope, or null to clear the slot.</param>
        public SingletonScope(T? instance = null)
        {
            _previous = Majdata<T>.Instance;
            Majdata<T>.Instance = instance;
        }

        /// <summary>Registers another instance for the rest of the scope.</summary>
        /// <param name="instance">The instance to register, or null to clear the slot.</param>
        public void Set(T? instance)
        {
            Majdata<T>.Instance = instance;
        }

        /// <summary>Restores the captured instance.</summary>
        public void Dispose()
        {
            Majdata<T>.Instance = _previous;
        }
    }
}

using System;
using MajdataPlay.Tests;

#nullable enable
namespace MajdataPlay.Tests.Game.Notes.PlayMode.Support
{
    /// <summary>
    /// Saves the storage of one <see cref="Majdata{T}"/> holder, clears it for a test and restores it afterwards, so
    /// tests can register their own instances without leaking them or tripping the singleton lock.
    /// </summary>
    internal sealed class SingletonSlot
    {
        /// <summary>The name of the holder field that stores the instance.</summary>
        private const string InstanceField = "_instance";

        /// <summary>The name of the holder field that stores the lock state.</summary>
        private const string StateField = "_state";

        /// <summary>The closed generic holder type.</summary>
        private readonly Type _holderType;

        /// <summary>The instance stored before the test.</summary>
        private readonly object? _savedInstance;

        /// <summary>The lock state stored before the test.</summary>
        private readonly int _savedState;

        /// <summary>Captures and clears the storage of a holder.</summary>
        /// <param name="holderType">The closed generic holder type.</param>
        private SingletonSlot(Type holderType)
        {
            _holderType = holderType;
            _savedInstance = PrivateMember.GetStaticField<object?>(holderType, InstanceField);
            _savedState = PrivateMember.GetStaticField<int>(holderType, StateField);
            Clear();
        }

        /// <summary>Captures and clears the singleton storage of a type.</summary>
        /// <typeparam name="T">The singleton type.</typeparam>
        /// <returns>The slot that restores the previous storage.</returns>
        public static SingletonSlot Capture<T>() where T : class
        {
            return new SingletonSlot(typeof(Majdata<T>));
        }

        /// <summary>Removes the instance and unlocks the holder.</summary>
        public void Clear()
        {
            PrivateMember.SetStaticField(_holderType, InstanceField, null);
            PrivateMember.SetStaticField(_holderType, StateField, 0);
        }

        /// <summary>Restores the instance and lock state captured before the test.</summary>
        public void Restore()
        {
            PrivateMember.SetStaticField(_holderType, InstanceField, _savedInstance);
            PrivateMember.SetStaticField(_holderType, StateField, _savedState);
        }
    }
}

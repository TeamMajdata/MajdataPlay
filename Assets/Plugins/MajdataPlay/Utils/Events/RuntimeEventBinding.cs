using System;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Utils.Events
{
    /// <summary>
    /// One serialized binding of a <see cref="RuntimeEvent{TDelegate}"/>: the object to call and the name of the
    /// <see cref="BindableMethodAttribute"/> method to call on it.
    /// </summary>
    [Serializable]
    public sealed class RuntimeEventBinding
    {
        /// <summary>The object whose method is called.</summary>
        [SerializeField]
        [FormerlySerializedAs("Target")]
        private Object? _target;

        /// <summary>The name of the bound method; empty when no method is selected.</summary>
        [SerializeField]
        [FormerlySerializedAs("MethodName")]
        private string _methodName = string.Empty;

        /// <summary>Initializes an empty binding, as Unity does when it adds a binding in the Inspector.</summary>
        public RuntimeEventBinding()
        {
        }

        /// <summary>Initializes a binding to a method of an object.</summary>
        /// <param name="target">The object whose method is called.</param>
        /// <param name="methodName">The name of the bound method.</param>
        /// <exception cref="ArgumentNullException"><paramref name="methodName"/> is null.</exception>
        internal RuntimeEventBinding(Object? target, string methodName)
        {
            _target = target;
            _methodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        }

        /// <summary>Gets the object whose method is called, or null when it is unset or missing.</summary>
        public Object? Target => _target;

        /// <summary>Gets the name of the bound method, or an empty string when no method is selected.</summary>
        public string MethodName => _methodName;
    }
}

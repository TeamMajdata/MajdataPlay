using System;
using System.Collections.Generic;
using MajdataPlay.Utils.Events;
using UnityEngine;
using UnityEngine.Serialization;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils.PlayMode
{
    /// <summary>
    /// A component that exposes serialized <see cref="RuntimeEvent{TDelegate}"/> fields the way production code should:
    /// private fields behind properties whose setters reject replacement.
    /// </summary>
    internal sealed class RuntimeEventOwner : MonoBehaviour
    {
        /// <summary>An event with an <c>Action&lt;int&gt;</c> signature.</summary>
        [SerializeField]
        [FormerlySerializedAs("ValueChanged")]
        private RuntimeEvent<Action<int>> _valueChanged = new();

        /// <summary>An event with a <c>ref</c> struct parameter.</summary>
        [SerializeField]
        [FormerlySerializedAs("PayloadChanged")]
        private RuntimeEvent<RuntimeEventRefHandler> _payloadChanged = new();

        /// <summary>An event whose delegate type is not concrete, which the drawer reports as unsupported.</summary>
        [SerializeField]
        [FormerlySerializedAs("UnsupportedEvent")]
        private RuntimeEvent<Delegate> _unsupportedEvent = new();

        /// <summary>A list of events, which the drawer draws element by element.</summary>
        [SerializeField]
        [FormerlySerializedAs("EventList")]
        private List<RuntimeEvent<Action<int>>> _eventList = new();

        /// <summary>Gets the <c>Action&lt;int&gt;</c> event; the setter accepts only the same instance.</summary>
        /// <exception cref="InvalidOperationException">The setter receives another instance.</exception>
        public RuntimeEvent<Action<int>> ValueChanged
        {
            get => _valueChanged;
            set => RuntimeEvent.ThrowIfReplaced(_valueChanged, value);
        }

        /// <summary>Gets the <c>ref</c> struct event; the setter accepts only the same instance.</summary>
        /// <exception cref="InvalidOperationException">The setter receives another instance.</exception>
        public RuntimeEvent<RuntimeEventRefHandler> PayloadChanged
        {
            get => _payloadChanged;
            set => RuntimeEvent.ThrowIfReplaced(_payloadChanged, value);
        }

        /// <summary>Gets the event with an unsupported delegate type.</summary>
        public RuntimeEvent<Delegate> UnsupportedEvent => _unsupportedEvent;

        /// <summary>Gets the list of events.</summary>
        public List<RuntimeEvent<Action<int>>> EventList => _eventList;
    }
}

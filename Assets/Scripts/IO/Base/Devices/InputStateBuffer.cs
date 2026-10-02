using System;

namespace MajdataPlay.IO
{
    /// <summary>Separates live I/O state from a frame snapshot while retaining short pulses.</summary>
    internal sealed class InputStateBuffer
    {
        readonly object _sync = new();
        readonly bool[] _current;
        readonly bool[] _pendingOn;
        readonly bool[] _pendingOff;
        readonly bool[] _states;
        readonly bool[] _hadOn;
        readonly bool[] _hadOff;

        public InputStateBuffer(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            _current = new bool[count];
            _pendingOn = new bool[count];
            _pendingOff = new bool[count];
            _states = new bool[count];
            _hadOn = new bool[count];
            _hadOff = new bool[count];
        }

        public void Publish(ReadOnlySpan<bool> states)
        {
            if (states.Length != _current.Length)
            {
                throw new ArgumentException("The number of states must match the device.", nameof(states));
            }
            lock (_sync)
            {
                for (var i = 0; i < states.Length; i++)
                {
                    var state = states[i];
                    _current[i] = state;
                    _pendingOn[i] |= state;
                    _pendingOff[i] |= !state;
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _current.AsSpan().Clear();
                for (var i = 0; i < _pendingOff.Length; i++)
                {
                    _pendingOff[i] = true;
                }
            }
        }

        public void OnPreUpdate()
        {
            lock (_sync)
            {
                _current.AsSpan().CopyTo(_states);
                _pendingOn.AsSpan().CopyTo(_hadOn);
                _pendingOff.AsSpan().CopyTo(_hadOff);
                _pendingOn.AsSpan().Clear();
                _pendingOff.AsSpan().Clear();
            }
        }

        public void CopyTo(Span<bool> states, Span<bool> hadOn, Span<bool> hadOff)
        {
            if (states.Length < _states.Length || hadOn.Length < _states.Length || hadOff.Length < _states.Length)
            {
                throw new ArgumentException("The destination spans must fit all device states.");
            }
            lock (_sync)
            {
                _states.AsSpan().CopyTo(states);
                _hadOn.AsSpan().CopyTo(hadOn);
                _hadOff.AsSpan().CopyTo(hadOff);
            }
        }

        public bool IsCurrentlyOn(int index)
        {
            lock (_sync)
            {
                return index >= 0 && index < _current.Length && _current[index];
            }
        }
    }
}

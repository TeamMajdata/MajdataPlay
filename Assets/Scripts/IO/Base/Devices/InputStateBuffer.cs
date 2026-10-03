using System;
using System.Threading;

namespace MajdataPlay.IO
{
    /// <summary>Separates live I/O state from a frame snapshot while retaining short pulses.</summary>
    internal sealed class InputStateBuffer
    {
        private SpinLock _sync = new();
        private readonly bool[] _current;
        private readonly bool[] _pendingOn;
        private readonly bool[] _pendingOff;
        private readonly bool[] _states;
        private readonly bool[] _hadOn;
        private readonly bool[] _hadOff;

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
            using (AcquireLock())
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
            using (AcquireLock())
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
            using (AcquireLock())
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
            using (AcquireLock())
            {
                _states.AsSpan().CopyTo(states);
                _hadOn.AsSpan().CopyTo(hadOn);
                _hadOff.AsSpan().CopyTo(hadOff);
            }
        }

        public bool IsCurrentlyOn(int index)
        {
            using (AcquireLock())
            {
                return index >= 0 && index < _current.Length && _current[index];
            }
        }

        private DisposableLock AcquireLock()
        {
            return new DisposableLock(this);
        }
        private readonly ref struct DisposableLock
        {
            private readonly bool _isLocked;
            private readonly InputStateBuffer _buffer;
            
            public DisposableLock(InputStateBuffer buffer)
            {
                _buffer = buffer;
                ref var sync = ref buffer._sync;
                var isLocked = false;
                try
                {
                    sync.Enter(ref isLocked);
                }
                catch
                {
                    if (isLocked)
                    {
                        sync.Exit();
                    }
                    throw;
                }
                _isLocked = isLocked;
            }
            public void Dispose()
            {
                if(_isLocked)
                {
                    _buffer._sync.Exit();
                }
            }
        }
    }
}

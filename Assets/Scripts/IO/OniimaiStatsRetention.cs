#nullable enable
namespace MajdataPlay.IO
{
    // Independent of rendering/USB: leaving a result must reset even with the monitor off.
    public sealed class OniimaiStatsRetention<T> where T : class
    {
        public T? Frame { get; private set; }
        public bool IsFinal { get; private set; }
        private bool _inGame;
        private int _session;

        public bool Advance(bool inGame, bool inResult, int session)
        {
            if (inGame)
            {
                // A scene can reload directly from Game to Game (retry/practice).
                // A zero ID during teardown must not erase the just-completed record.
                var changed = !_inGame || (session != 0 && session != _session);
                if (changed) Clear();
                _inGame = true;
                if (session != 0) _session = session;
                return changed;
            }
            _inGame = false;
            if (inResult && IsFinal) return false;
            var hadRecord = Frame != null || _session != 0;
            Clear();
            return hadRecord;
        }

        public void Capture(T frame, bool final)
        {
            if (IsFinal) return; // LateUpdate cannot replace the exact final snapshot.
            Frame = frame;
            IsFinal = final;
        }

        private void Clear()
        {
            Frame = null;
            IsFinal = false;
            _session = 0;
        }
    }
}

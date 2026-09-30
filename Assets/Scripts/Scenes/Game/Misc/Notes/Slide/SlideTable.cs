using MajdataPlay.Buffers;
using MajdataPlay.IO;
using System;

namespace MajdataPlay.Scenes.Game.Notes.Slide
{
    public class SlideTable : IDisposable
    {
        public string Name { get; init; }
        public Memory<SlideArea> JudgeQueue
        {
            get
            {
                ThrowIfDisposed();
                return _judgeQueue;
            }
        }
        public float Const { get; init; }
        public float ClassicConst { get; init; }

        bool _isDisposed = false;
        readonly Memory<SlideArea> _judgeQueue;
        readonly PooledArray<SlideArea> _rentedArray;

        ~SlideTable()
        {
            Dispose();
        }
        public SlideTable(PooledArray<SlideArea> rentedArray, int length)
        {
            _rentedArray = rentedArray;
            _judgeQueue = rentedArray.AsMemory(0, length);
        }
        public void Mirror()
        {
            ThrowIfDisposed();
            var areas = JudgeQueue.Span;
            for (var i = 0; i < areas.Length; i++)
            {
                ref var area = ref areas[i];
                area.Mirror(SensorArea.A1);
            }
        }
        public void Diff(int diff)
        {
            ThrowIfDisposed();
            var areas = JudgeQueue.Span;
            for (var i = 0; i < areas.Length; i++)
            {
                ref var area = ref areas[i];
                area.Diff(diff);
            }
        }
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            var areas = _judgeQueue.Span;
            for (var i = 0; i < areas.Length; i++)
            {
                ref var area = ref areas[i];
                area.Dispose();
            }
            _rentedArray.Dispose();
        }
        public void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SlideTable), $"The {nameof(SlideTable)} has been disposed.");
            }
        }
    }
}

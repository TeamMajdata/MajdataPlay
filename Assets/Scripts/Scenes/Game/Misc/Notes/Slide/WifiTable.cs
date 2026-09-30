using MajdataPlay.Buffers;
using MajdataPlay.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Scenes.Game.Notes.Slide;
public class WifiTable: IDisposable
{
    public string Name { get; init; }
    public Memory<SlideArea> Left
    {
        get
        {
            ThrowIfDisposed();
            return _left;
        }
    }
    public Memory<SlideArea> Center
    {
        get
        {
            ThrowIfDisposed();
            return _center;
        }
    }
    public Memory<SlideArea> Right
    {
        get
        {
            ThrowIfDisposed();
            return _right;
        }
    }
    public float Const { get; init; }

    bool _isDisposed = false;
    Memory<SlideArea> _left;
    Memory<SlideArea> _center;
    Memory<SlideArea> _right;

    readonly PooledArray<SlideArea> _rentedArrayForLeft;    
    readonly PooledArray<SlideArea> _rentedArrayForCenter;    
    readonly PooledArray<SlideArea> _rentedArrayForRight;   
    
    ~WifiTable()
    {
        Dispose();
    }
    public WifiTable(PooledArray<SlideArea> rentedArrayForLeft,
                     PooledArray<SlideArea> rentedArrayForCenter,
                     PooledArray<SlideArea> rentedArrayForRight, 
                     int lengthForLeft,
                     int lengthForCenter,
                     int LengthForRight)
    {
        _rentedArrayForLeft = rentedArrayForLeft;
        _rentedArrayForCenter = rentedArrayForCenter;
        _rentedArrayForRight = rentedArrayForRight;
        _left = _rentedArrayForLeft.AsMemory(0, lengthForLeft);
        _center = _rentedArrayForCenter.AsMemory(0, lengthForCenter);
        _right = _rentedArrayForRight.AsMemory(0, LengthForRight);
    }
    public void Mirror()
    {
        ThrowIfDisposed();
        var left = Left.Span;
        var center = Center.Span;
        var right = Right.Span;

        for (var i = 0; i < left.Length; i++)
        {
            ref var area = ref left[i];
            area.Mirror(SensorArea.A1);
        }
        for (var i = 0; i < center.Length; i++)
        {
            ref var area = ref center[i];
            area.Mirror(SensorArea.A1);
        }
        for (var i = 0; i < right.Length; i++)
        {
            ref var area = ref right[i];
            area.Mirror(SensorArea.A1);
        }
    }
    public void Diff(int diff)
    {
        ThrowIfDisposed();
        var left = Left.Span;
        var center = Center.Span;
        var right = Right.Span;

        for (var i = 0; i < left.Length; i++)
        {
            ref var area = ref left[i];
            area.Diff(diff);
        }
        for (var i = 0; i < center.Length; i++)
        {
            ref var area = ref center[i];
            area.Diff(diff);
        }
        for (var i = 0; i < right.Length; i++)
        {
            ref var area = ref right[i];
            area.Diff(diff);
        }
    }
    public void Dispose()
    {
        if(_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        var left = _left.Span;
        var center = _center.Span;
        var right = _right.Span;

        for (var i = 0; i < left.Length; i++)
        {
            ref var area = ref left[i];
            area.Dispose();
        }
        for (var i = 0; i < center.Length; i++)
        {
            ref var area = ref center[i];
            area.Dispose();
        }
        for (var i = 0; i < right.Length; i++)
        {
            ref var area = ref right[i];
            area.Dispose();
        }
        _left = Memory<SlideArea>.Empty;
        _center = Memory<SlideArea>.Empty;
        _right = Memory<SlideArea>.Empty;
        _rentedArrayForLeft.Dispose();
        _rentedArrayForCenter.Dispose();
        _rentedArrayForRight.Dispose();
    }
    void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(WifiTable));
        }
    }
}

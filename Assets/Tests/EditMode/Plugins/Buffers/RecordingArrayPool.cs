using System;
using System.Buffers;
using System.Collections.Generic;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>
    /// An array pool that always allocates new arrays of exactly the requested length and records every rent and
    /// return, so tests can observe how pooled containers use their pool without depending on shared pool state.
    /// </summary>
    /// <typeparam name="T">The element type of the pooled arrays.</typeparam>
    public sealed class RecordingArrayPool<T> : ArrayPool<T>
    {
        /// <summary>Gets the requested minimum lengths of every rent call, in call order.</summary>
        public List<int> RentedLengths { get; } = new();

        /// <summary>Gets every array passed to <see cref="Return"/>, in call order.</summary>
        public List<T[]> ReturnedArrays { get; } = new();

        /// <summary>Gets the clear flags of every return call, in call order.</summary>
        public List<bool> ReturnedClearFlags { get; } = new();

        /// <summary>Allocates a new array of exactly the requested length and records the request.</summary>
        /// <param name="minimumLength">The requested length.</param>
        /// <returns>A new array, or an empty array when the length is not positive.</returns>
        public override T[] Rent(int minimumLength)
        {
            RentedLengths.Add(minimumLength);
            if (minimumLength <= 0)
            {
                return Array.Empty<T>();
            }
            return new T[minimumLength];
        }

        /// <summary>Records a returned array and clears it when requested.</summary>
        /// <param name="array">The returned array.</param>
        /// <param name="clearArray">Whether the caller asked for the array to be cleared.</param>
        public override void Return(T[] array, bool clearArray = false)
        {
            ReturnedArrays.Add(array);
            ReturnedClearFlags.Add(clearArray);
            if (clearArray)
            {
                Array.Clear(array, 0, array.Length);
            }
        }
    }
}

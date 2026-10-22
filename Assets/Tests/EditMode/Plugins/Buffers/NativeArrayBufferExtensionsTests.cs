using System;
using System.Buffers;
using System.Runtime.InteropServices;
using MajdataPlay.Buffers;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>
    /// Covers the conversions between native arrays, spans and memory in MajdataPlay.Buffers, including the memory
    /// manager that exposes a native array as <see cref="Memory{T}"/>.
    /// </summary>
    public class NativeArrayBufferExtensionsTests
    {
        /// <summary>Checks that AsMemory exposes the native elements for reading and writing without taking ownership.</summary>
        [Test]
        public void AsMemory_ExposesNativeElements()
        {
            var native = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Persistent);
            try
            {
                var memory = MajdataPlay.Buffers.NativeArrayExtensions.AsMemory(native);

                memory.Span[1] = 20;

                Assert.That(memory.Length, Is.EqualTo(3));
                Assert.That(memory.ToArray(), Is.EqualTo(new[] { 1, 20, 3 }));
                Assert.That(native[1], Is.EqualTo(20));
            }
            finally
            {
                native.Dispose();
            }
        }

        /// <summary>Checks that pinning the memory returns a pointer to the requested element.</summary>
        [Test]
        public unsafe void AsMemory_Pin_PointsAtElement()
        {
            var native = new NativeArray<int>(4, Allocator.Persistent);
            try
            {
                var memory = MajdataPlay.Buffers.NativeArrayExtensions.AsMemory(native);
                var start = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(native);

                using (var whole = memory.Pin())
                {
                    Assert.That((IntPtr)whole.Pointer, Is.EqualTo((IntPtr)start));
                }
                using (var offset = memory.Slice(2).Pin())
                {
                    Assert.That((IntPtr)offset.Pointer, Is.EqualTo((IntPtr)(start + (2 * sizeof(int)))));
                }
            }
            finally
            {
                native.Dispose();
            }
        }

        /// <summary>Checks that the owner returned by AsMemoryOwner exposes the elements and disposes the native array.</summary>
        [Test]
        public void AsMemoryOwner_DisposesNativeArray()
        {
            var native = new NativeArray<int>(new[] { 4, 5 }, Allocator.Persistent);
            var owner = MajdataPlay.Buffers.NativeArrayExtensions.AsMemoryOwner(native);

            Assert.That(owner.Memory.ToArray(), Is.EqualTo(new[] { 4, 5 }));

            owner.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.That(() => _ = native[0], Throws.InstanceOf<InvalidOperationException>());
#endif
        }

        /// <summary>Checks that disposing the manager behind AsMemory leaves the native array alive and that Unpin is a no-op.</summary>
        [Test]
        public void AsMemory_ManagerDisposal_KeepsNativeArray()
        {
            var native = new NativeArray<int>(new[] { 1, 2 }, Allocator.Persistent);
            try
            {
                var memory = MajdataPlay.Buffers.NativeArrayExtensions.AsMemory(native);
                Assert.That(MemoryMarshal.TryGetMemoryManager<int, MemoryManager<int>>(memory, out var manager), Is.True);

                manager!.Unpin();
                ((IDisposable)manager).Dispose();

                Assert.That(native[1], Is.EqualTo(2));
                Assert.That(native.IsCreated, Is.True);
            }
            finally
            {
                native.Dispose();
            }
        }

        /// <summary>Checks that a span is viewed as a native array over the same memory.</summary>
        [Test]
        public void SpanAsNativeArray_SharesMemory()
        {
            Span<int> span = stackalloc int[] { 1, 2, 3 };

            var native = MajdataPlay.Buffers.SpanExtensions.AsNativeArray(span);
            try
            {
                Assert.That(native.Length, Is.EqualTo(3));
                Assert.That(native[2], Is.EqualTo(3));
                native[0] = 10;
                Assert.That(span[0], Is.EqualTo(10));
            }
            finally
            {
                ReleaseSafetyHandle(native);
            }
        }

        /// <summary>Checks that a read-only span is viewed as a native array over the same memory.</summary>
        [Test]
        public void ReadOnlySpanAsNativeArray_SharesMemory()
        {
            var source = new[] { 7, 8 };
            ReadOnlySpan<int> span = source;

            var native = MajdataPlay.Buffers.ReadOnlySpanExtensions.AsNativeArray(span);
            try
            {
                Assert.That(native.Length, Is.EqualTo(2));
                Assert.That(native[1], Is.EqualTo(8));
            }
            finally
            {
                ReleaseSafetyHandle(native);
            }
        }

        /// <summary>Checks that an empty span becomes an empty native array view.</summary>
        [Test]
        public void SpanAsNativeArray_Empty_HasNoElements()
        {
            var native = MajdataPlay.Buffers.SpanExtensions.AsNativeArray(Span<int>.Empty);
            try
            {
                Assert.That(native.Length, Is.EqualTo(0));
            }
            finally
            {
                ReleaseSafetyHandle(native);
            }
        }

        /// <summary>Releases the safety handle that the span conversions create for their views.</summary>
        /// <param name="native">The view whose handle is released.</param>
        private static void ReleaseSafetyHandle(NativeArray<int> native)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(native));
#endif
        }
    }
}

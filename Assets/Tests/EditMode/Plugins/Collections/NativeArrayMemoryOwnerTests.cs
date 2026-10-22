using System;
using System.Buffers;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Collections
{
    /// <summary>
    /// Covers the internal MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner, which exposes a native array as
    /// an owned <see cref="Memory{T}"/>.
    /// </summary>
    public class NativeArrayMemoryOwnerTests
    {
        /// <summary>Checks that a default, never created native array is rejected.</summary>
        [Test]
        public void AsMemoryOwner_NotCreated_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(() => MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(default(NativeArray<int>)));

            Assert.That(exception!.ParamName, Is.EqualTo("nativeArray"));
        }

        /// <summary>Checks that the owner's memory reads and writes the native elements.</summary>
        [Test]
        public void Memory_ReadsAndWritesNativeElements()
        {
            var native = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Persistent);
            var owner = MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(native);
            try
            {
                owner.Memory.Span[0] = 7;

                Assert.That(owner.Memory.Length, Is.EqualTo(3));
                Assert.That(native[0], Is.EqualTo(7));
                Assert.That(owner.Memory.ToArray(), Is.EqualTo(new[] { 7, 2, 3 }));
            }
            finally
            {
                owner.Dispose();
            }
        }

        /// <summary>Checks that pinning returns a pointer to the requested element and unpins on handle disposal.</summary>
        [Test]
        public unsafe void Pin_PointsAtElement()
        {
            var native = new NativeArray<long>(4, Allocator.Persistent);
            var owner = MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(native);
            try
            {
                var start = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(native);

                using (var handle = owner.Memory.Slice(3).Pin())
                {
                    Assert.That((IntPtr)handle.Pointer, Is.EqualTo((IntPtr)(start + (3 * sizeof(long)))));
                }
            }
            finally
            {
                owner.Dispose();
            }
        }

        /// <summary>Checks that pinning outside the elements is rejected.</summary>
        [Test]
        public void Pin_OutOfRange_Throws()
        {
            var native = new NativeArray<int>(2, Allocator.Persistent);
            var owner = MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(native);
            try
            {
                var manager = (MemoryManager<int>)owner;

                Assert.Throws<ArgumentOutOfRangeException>(() => manager.Pin(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => manager.Pin(2));
            }
            finally
            {
                owner.Dispose();
            }
        }

        /// <summary>Checks that disposing the owner releases the native array.</summary>
        [Test]
        public void Dispose_ReleasesNativeArray()
        {
            var native = new NativeArray<int>(2, Allocator.Persistent);
            var owner = MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(native);

            owner.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.That(() => _ = native[0], Throws.InstanceOf<InvalidOperationException>());
#endif
        }

        /// <summary>Checks that the memory of a disposed owner can no longer be accessed.</summary>
        [Test]
        public void Memory_AfterDispose_Throws()
        {
            var native = new NativeArray<int>(2, Allocator.Persistent);
            var owner = MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(native);
            var manager = (MemoryManager<int>)owner;

            owner.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.GetSpan());
        }

        /// <summary>Checks that disposing the owner twice is harmless.</summary>
        [Test]
        public void Dispose_Twice_IsHarmless()
        {
            var native = new NativeArray<int>(2, Allocator.Persistent);
            var owner = MajdataPlay.Collections.NativeArrayExtensions.AsMemoryOwner(native);

            owner.Dispose();

            Assert.DoesNotThrow(() => owner.Dispose());
        }
    }
}

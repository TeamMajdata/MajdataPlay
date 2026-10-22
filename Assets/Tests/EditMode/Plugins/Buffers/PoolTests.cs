using System;
using System.Buffers;
using MajdataPlay.Buffers;
using MajdataPlay.Buffers.Pools;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers the static <see cref="Pool{T}"/> facade over the shared array and memory pools.</summary>
    public class PoolTests
    {
        /// <summary>Checks that a negative length is rejected.</summary>
        [Test]
        public void Rent_NegativeLength_Throws()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Pool<int>.Rent(-1));

            Assert.That(exception!.ParamName, Is.EqualTo("length"));
        }

        /// <summary>Checks that renting zero elements yields an empty array.</summary>
        [Test]
        public void Rent_Zero_ReturnsEmptyArray()
        {
            var array = Pool<int>.Rent(0);

            Assert.That(array.IsEmpty, Is.True);
            array.Dispose();
        }

        /// <summary>Checks that generic element types use the pool whose smallest bucket holds eight elements.</summary>
        [Test]
        public void Rent_GenericType_UsesEightElementMinimum()
        {
            var small = Pool<int>.Rent(1);
            var medium = Pool<int>.Rent(9);

            Assert.That(small.Length, Is.EqualTo(8));
            Assert.That(medium.Length, Is.EqualTo(16));
            small.Dispose();
            medium.Dispose();
        }

        /// <summary>Checks that byte arrays come from the dedicated byte pool whose smallest bucket holds sixteen bytes.</summary>
        [Test]
        public void Rent_Byte_UsesDedicatedBytePool()
        {
            var array = Pool<byte>.Rent(1);

            Assert.That(array.Length, Is.EqualTo(16));
            array.Dispose();
        }

        /// <summary>Checks that requests above the largest bucket allocate an array of exactly the requested length.</summary>
        [Test]
        public void Rent_AboveLargestBucket_AllocatesExactLength()
        {
            var array = Pool<int>.Rent((512 * 1024) + 1);
            var bytes = Pool<byte>.Rent((1024 * 1024) + 1);

            Assert.That(array.Length, Is.EqualTo((512 * 1024) + 1));
            Assert.That(bytes.Length, Is.EqualTo((1024 * 1024) + 1));
            array.Dispose();
            bytes.Dispose();
        }

        /// <summary>Checks that an array rented with the clear flag is cleared when it is returned.</summary>
        [Test]
        public void Rent_WithClearFlag_ClearsOnReturn()
        {
            var array = Pool<long>.Rent(4, true);
            var raw = array.AsArray();
            raw[0] = 123;

            array.Dispose();

            Assert.That(raw[0], Is.EqualTo(0));
        }

        /// <summary>Checks the static pools exposed to the module.</summary>
        [Test]
        public void StaticPools_AreConfigured()
        {
            Assert.That(Pool<int>.ArrayPool, Is.InstanceOf<SharedArrayPool<int>>());
            Assert.That(Pool<int>.ByteArrayPool, Is.InstanceOf<SharedArrayPool<byte>>());
            Assert.That(Pool<int>.MemoryPool, Is.SameAs(MemoryPool<int>.Shared));
        }

        /// <summary>Checks that RentMemory rents memory of at least the requested length.</summary>
        [Test]
        public void RentMemory_ValidLength_RentsMemory()
        {
            using var owner = Pool<int>.RentMemory(10);

            Assert.That(owner.Memory.Length, Is.GreaterThanOrEqualTo(10));
        }

        /// <summary>Checks that RentMemory rejects lengths that are not positive.</summary>
        [Test]
        public void RentMemory_NotPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Pool<int>.RentMemory(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Pool<int>.RentMemory(-5));
        }
    }
}

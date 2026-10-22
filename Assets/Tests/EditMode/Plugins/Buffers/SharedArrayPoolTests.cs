using System;
using System.Threading.Tasks;
using MajdataPlay.Buffers.Pools;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Buffers
{
    /// <summary>Covers bucket sizing, reuse, limits, trimming and validation of <see cref="SharedArrayPool{T}"/>.</summary>
    public class SharedArrayPoolTests
    {
        /// <summary>Checks that invalid constructor arguments are rejected with the offending parameter name.</summary>
        [Test]
        public void Constructor_InvalidArguments_Throw()
        {
            var minimum = Assert.Throws<ArgumentOutOfRangeException>(() => new SharedArrayPool<int>(0, 16, 1));
            var maximum = Assert.Throws<ArgumentOutOfRangeException>(() => new SharedArrayPool<int>(16, 8, 1));
            var perBucket = Assert.Throws<ArgumentOutOfRangeException>(() => new SharedArrayPool<int>(8, 16, 0));

            Assert.That(minimum!.ParamName, Is.EqualTo("minArrayLength"));
            Assert.That(maximum!.ParamName, Is.EqualTo("maxArrayLength"));
            Assert.That(perBucket!.ParamName, Is.EqualTo("maxArraysPerBucket"));
        }

        /// <summary>Checks that non-positive requests return the shared empty array.</summary>
        [Test]
        public void Rent_NotPositive_ReturnsEmptyArray()
        {
            var pool = new SharedArrayPool<int>(10, 100, 2);

            Assert.That(pool.Rent(0), Is.SameAs(Array.Empty<int>()));
            Assert.That(pool.Rent(-3), Is.SameAs(Array.Empty<int>()));
        }

        /// <summary>Checks that requests are rounded up to power-of-two buckets between the rounded bounds.</summary>
        [Test]
        public void Rent_RoundsUpToBucketSizes()
        {
            var pool = new SharedArrayPool<int>(10, 100, 2);

            Assert.That(pool.Rent(1).Length, Is.EqualTo(16));
            Assert.That(pool.Rent(16).Length, Is.EqualTo(16));
            Assert.That(pool.Rent(17).Length, Is.EqualTo(32));
            Assert.That(pool.Rent(64).Length, Is.EqualTo(64));
            Assert.That(pool.Rent(128).Length, Is.EqualTo(128));
        }

        /// <summary>Checks that requests above the largest bucket allocate an exact, unpooled array.</summary>
        [Test]
        public void Rent_AboveMaximum_AllocatesExactLength()
        {
            var pool = new SharedArrayPool<int>(8, 32, 2);
            var array = pool.Rent(33);

            pool.Return(array);

            Assert.That(array.Length, Is.EqualTo(33));
            Assert.That(pool.Rent(33), Is.Not.SameAs(array));
        }

        /// <summary>Checks that a returned array is handed out again by the next rent of its bucket.</summary>
        [Test]
        public void Return_ThenRent_ReusesArray()
        {
            var pool = new SharedArrayPool<int>(8, 64, 2);
            var array = pool.Rent(20);

            pool.Return(array);

            Assert.That(pool.Rent(17), Is.SameAs(array));
            Assert.That(pool.Rent(17), Is.Not.SameAs(array));
        }

        /// <summary>Checks that the clear flag zeroes a returned array, even one too large to keep.</summary>
        [Test]
        public void Return_WithClear_ZeroesArray()
        {
            var pool = new SharedArrayPool<int>(8, 16, 2);
            var pooled = pool.Rent(8);
            var oversized = new int[40];
            pooled[0] = 1;
            oversized[0] = 1;

            pool.Return(pooled, true);
            pool.Return(oversized, true);

            Assert.That(pooled[0], Is.EqualTo(0));
            Assert.That(oversized[0], Is.EqualTo(0));
        }

        /// <summary>Checks that returning the array without the clear flag keeps its content.</summary>
        [Test]
        public void Return_WithoutClear_KeepsContent()
        {
            var pool = new SharedArrayPool<int>(8, 16, 2);
            var array = pool.Rent(8);
            array[0] = 5;

            pool.Return(array);

            Assert.That(pool.Rent(8)[0], Is.EqualTo(5));
        }

        /// <summary>Checks that null and empty arrays are ignored.</summary>
        [Test]
        public void Return_NullOrEmpty_IsIgnored()
        {
            var pool = new SharedArrayPool<int>(8, 16, 2);

            Assert.DoesNotThrow(() => pool.Return(null!));
            Assert.DoesNotThrow(() => pool.Return(Array.Empty<int>(), true));
            Assert.That(pool.Rent(8).Length, Is.EqualTo(8));
        }

        /// <summary>Checks that arrays whose length does not match their bucket are not pooled.</summary>
        [Test]
        public void Return_ForeignLength_IsNotPooled()
        {
            var pool = new SharedArrayPool<int>(8, 64, 2);
            var foreign = new int[20];

            pool.Return(foreign);

            Assert.That(pool.Rent(20), Is.Not.SameAs(foreign));
        }

        /// <summary>Checks that each bucket keeps at most the configured number of arrays.</summary>
        [Test]
        public void Return_BeyondBucketLimit_DropsArrays()
        {
            var pool = new SharedArrayPool<int>(16, 16, 2);
            var first = new int[16];
            var second = new int[16];
            var third = new int[16];

            pool.Return(first);
            pool.Return(second);
            pool.Return(third);

            Assert.That(pool.Rent(16), Is.SameAs(second));
            Assert.That(pool.Rent(16), Is.SameAs(first));
            var fresh = pool.Rent(16);
            Assert.That(fresh, Is.Not.SameAs(third));
            Assert.That(fresh.Length, Is.EqualTo(16));
        }

        /// <summary>Checks that Trim drops every pooled array.</summary>
        [Test]
        public void Trim_DropsPooledArrays()
        {
            var pool = new SharedArrayPool<int>(8, 64, 4);
            var array = pool.Rent(8);
            pool.Return(array);

            pool.Trim();

            Assert.That(pool.Rent(8), Is.Not.SameAs(array));
        }

        /// <summary>Checks that the garbage collection callback trims the pool and stays registered.</summary>
        [Test]
        public void OnGCCallback_TrimsAndStaysRegistered()
        {
            var pool = new SharedArrayPool<int>(8, 64, 4);
            var array = pool.Rent(8);
            pool.Return(array);

            var keep = PrivateMember.Invoke<bool>(pool, "OnGCCallback");

            Assert.That(keep, Is.True);
            Assert.That(pool.Rent(8), Is.Not.SameAs(array));
        }

        /// <summary>Checks the power-of-two rounding helper, including values at and below one.</summary>
        /// <param name="value">The value to round.</param>
        /// <param name="expected">The expected power of two.</param>
        [TestCase(-4, 1)]
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 4)]
        [TestCase(1000, 1024)]
        [TestCase(1024, 1024)]
        public void GetNextPowerOf2_RoundsUp(int value, int expected)
        {
            var result = PrivateMember.InvokeStatic<int>(typeof(SharedArrayPool<int>), "GetNextPowerOf2", value);

            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>Checks that a pool whose bounds round to the same size has a single working bucket.</summary>
        [Test]
        public void Constructor_EqualBounds_CreatesSingleBucket()
        {
            var pool = new SharedArrayPool<int>(5, 7, 1);
            var array = pool.Rent(3);

            pool.Return(array);

            Assert.That(array.Length, Is.EqualTo(8));
            Assert.That(pool.Rent(8), Is.SameAs(array));
            Assert.That(pool.Rent(9).Length, Is.EqualTo(9));
        }

        /// <summary>Checks that concurrent renting and returning always hands out arrays of a valid bucket size.</summary>
        [Test]
        public void RentAndReturn_Concurrently_StayConsistent()
        {
            var pool = new SharedArrayPool<int>(8, 256, 16);
            var failures = 0;

            Parallel.For(0, 2000, i =>
            {
                var length = (i % 200) + 1;
                var array = pool.Rent(length);
                if (array.Length < length || (array.Length & (array.Length - 1)) != 0)
                {
                    System.Threading.Interlocked.Increment(ref failures);
                }
                pool.Return(array, (i & 1) == 0);
            });

            Assert.That(failures, Is.EqualTo(0));
        }
    }
}

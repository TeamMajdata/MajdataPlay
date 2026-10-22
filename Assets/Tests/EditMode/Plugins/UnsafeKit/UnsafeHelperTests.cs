using System;
using System.Runtime.InteropServices;
using MajdataPlay.UnsafeKit;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.UnsafeKit
{
    /// <summary>Covers the unmanaged allocation, pointer arithmetic and GC handle helpers in <see cref="UnsafeHelper"/>.</summary>
    public class UnsafeHelperTests
    {
        /// <summary>Checks that typed allocation returns writable memory for the requested element count and frees it.</summary>
        [Test]
        public unsafe void AllocTyped_ReturnsWritableMemory()
        {
            var pointer = UnsafeHelper.Alloc<int>(4);
            try
            {
                Assert.That((IntPtr)pointer, Is.Not.EqualTo(IntPtr.Zero));
                for (var i = 0; i < 4; i++)
                {
                    pointer[i] = i * 3;
                }
                Assert.That(pointer[3], Is.EqualTo(9));
            }
            finally
            {
                UnsafeHelper.Free(pointer);
            }
        }

        /// <summary>Checks that untyped allocation returns writable memory and frees it.</summary>
        [Test]
        public unsafe void AllocUntyped_ReturnsWritableMemory()
        {
            var pointer = UnsafeHelper.Alloc((IntPtr)16);
            try
            {
                Assert.That(pointer, Is.Not.EqualTo(IntPtr.Zero));
                ((byte*)pointer)[15] = 0x5A;
                Assert.That(Marshal.ReadByte(pointer, 15), Is.EqualTo(0x5A));
            }
            finally
            {
                UnsafeHelper.Free(pointer);
            }
        }

        /// <summary>Checks that AddOffset and GetElement address elements by index.</summary>
        [Test]
        public unsafe void AddOffsetAndGetElement_AddressElements()
        {
            var buffer = stackalloc short[4];
            buffer[2] = 42;

            Assert.That((IntPtr)UnsafeHelper.AddOffset(buffer, 2), Is.EqualTo((IntPtr)(buffer + 2)));
            Assert.That(UnsafeHelper.GetElement(buffer, 2), Is.EqualTo(42));

            UnsafeHelper.GetElement(buffer, 3) = 7;

            Assert.That(buffer[3], Is.EqualTo(7));
        }

        /// <summary>Checks that AddByteOffset advances a byte pointer by the given number of bytes.</summary>
        [Test]
        public unsafe void AddByteOffset_BytePointer_AdvancesBytes()
        {
            var buffer = stackalloc byte[8];

            Assert.That((IntPtr)UnsafeHelper.AddByteOffset(buffer, 3), Is.EqualTo((IntPtr)(buffer + 3)));
        }

        /// <summary>Checks that AddByteOffset advances a wider pointer by bytes, not by squared element sizes.</summary>
        [Test]
        public unsafe void AddByteOffset_IntPointer_AdvancesBytes()
        {
            var buffer = stackalloc int[8];

            Assert.That((IntPtr)UnsafeHelper.AddByteOffset(buffer, 4), Is.EqualTo((IntPtr)((byte*)buffer + 4)));
        }

        /// <summary>Checks that a zero handle yields no instance.</summary>
        [Test]
        public void TryGetInstanceFromGCHandle_ZeroHandle_ReturnsFalse()
        {
            var found = UnsafeHelper.TryGetInstanceFromGCHandle<string>(IntPtr.Zero, out var instance);

            Assert.That(found, Is.False);
            Assert.That(instance, Is.Null);
        }

        /// <summary>Checks that an allocated handle yields its target.</summary>
        [Test]
        public void TryGetInstanceFromGCHandle_AllocatedHandle_ReturnsTarget()
        {
            var target = new object();
            var handle = GCHandle.Alloc(target);
            try
            {
                var found = UnsafeHelper.TryGetInstanceFromGCHandle<object>(GCHandle.ToIntPtr(handle), out var instance);

                Assert.That(found, Is.True);
                Assert.That(instance, Is.SameAs(target));
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Checks that a handle whose target is null yields no instance.</summary>
        [Test]
        public void TryGetInstanceFromGCHandle_NullTarget_ReturnsFalse()
        {
            var handle = GCHandle.Alloc(null);
            try
            {
                var found = UnsafeHelper.TryGetInstanceFromGCHandle<object>(GCHandle.ToIntPtr(handle), out var instance);

                Assert.That(found, Is.False);
                Assert.That(instance, Is.Null);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Checks that a handle whose target has another type is rejected with a cast error.</summary>
        [Test]
        public void TryGetInstanceFromGCHandle_WrongType_Throws()
        {
            var handle = GCHandle.Alloc("text");
            try
            {
                Assert.Throws<InvalidCastException>(() => UnsafeHelper.TryGetInstanceFromGCHandle<Version>(GCHandle.ToIntPtr(handle), out _));
            }
            finally
            {
                handle.Free();
            }
        }
    }
}

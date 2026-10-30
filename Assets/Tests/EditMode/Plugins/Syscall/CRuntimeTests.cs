using System;
using MajdataPlay.Syscall;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Syscall
{
    /// <summary>Covers the C runtime heap binding <see cref="CRuntime"/> in the Editor.</summary>
    public unsafe class CRuntimeTests
    {
        /// <summary>Checks that malloc returns an aligned, writable block that free accepts.</summary>
        [Test]
        public void Malloc_ReturnsAlignedWritableBlock()
        {
            var block = CRuntime.Malloc((UIntPtr)64);
            Assert.That(block, Is.Not.EqualTo(IntPtr.Zero));
            try
            {
                var bytes = (byte*)block;
                for (var i = 0; i < 64; i++)
                {
                    bytes[i] = (byte)i;
                }

                Assert.That(bytes[0], Is.EqualTo(0));
                Assert.That(bytes[63], Is.EqualTo(63));
                // malloc aligns for every fundamental C type, which is at least twice the pointer size on all targets.
                Assert.That(block.ToInt64() % (2 * IntPtr.Size), Is.EqualTo(0));
            }
            finally
            {
                CRuntime.Free(block);
            }
        }

        /// <summary>Checks that freeing a null pointer does nothing.</summary>
        [Test]
        public void Free_Null_DoesNothing()
        {
            Assert.DoesNotThrow(() => CRuntime.Free(IntPtr.Zero));
        }
    }
}

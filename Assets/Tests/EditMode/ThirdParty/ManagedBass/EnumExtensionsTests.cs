using System;
using System.Runtime.CompilerServices;
using ManagedBass;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.ThirdParty.ManagedBass
{
    /// <summary>
    /// Covers <see cref="EnumExtensions.Has{T}(T, T)"/>, which ManagedBass uses instead of <see cref="Enum.HasFlag(Enum)"/> to avoid
    /// boxing. It used to read eight bytes from four-byte enums, so the bytes after the value decided the result.
    /// </summary>
    public class EnumExtensionsTests
    {
        /// <summary>A flag enum with a one-byte underlying type.</summary>
        [Flags]
        private enum ByteFlags : byte
        {
            /// <summary>The lowest bit.</summary>
            Low = 0x01,

            /// <summary>The highest bit.</summary>
            High = 0x80,
        }

        /// <summary>A flag enum with a two-byte underlying type.</summary>
        [Flags]
        private enum UInt16Flags : ushort
        {
            /// <summary>The lowest bit.</summary>
            Low = 0x0001,

            /// <summary>The highest bit.</summary>
            High = 0x8000,
        }

        /// <summary>A flag enum with an eight-byte underlying type.</summary>
        [Flags]
        private enum Int64Flags : long
        {
            /// <summary>The lowest bit.</summary>
            Low = 1,

            /// <summary>The highest bit.</summary>
            High = long.MinValue,
        }

        /// <summary>Checks the result for single-bit flags, including the sign bit of <see cref="SyncFlags.Onetime"/>.</summary>
        [Test]
        public void Has_SingleBitFlag_ReportsOnlyThatBit()
        {
            Assert.That((SyncFlags.End | SyncFlags.Onetime).Has(SyncFlags.Onetime), Is.True);
            Assert.That((SyncFlags.End | SyncFlags.Mixtime).Has(SyncFlags.Mixtime), Is.True);
            Assert.That(SyncFlags.End.Has(SyncFlags.Onetime), Is.False);
            Assert.That(SyncFlags.End.Has(SyncFlags.Mixtime), Is.False);
            Assert.That((BassFlags.Decode | BassFlags.Float).Has(BassFlags.Decode), Is.True);
            Assert.That(BassFlags.Float.Has(BassFlags.Decode), Is.False);
        }

        /// <summary>Checks that a flag of several bits is reported only when all of them are set, like <see cref="Enum.HasFlag(Enum)"/>.</summary>
        [Test]
        public void Has_MultiBitFlag_RequiresEveryBit()
        {
            Assert.That((BassFlags.Decode | BassFlags.Float).Has(BassFlags.Decode | BassFlags.Float), Is.True);
            Assert.That(BassFlags.Float.Has(BassFlags.Decode | BassFlags.Float), Is.False);
            Assert.That(SyncFlags.End.Has(SyncFlags.Position), Is.True, "Every value has the empty flag, like Enum.HasFlag.");
        }

        /// <summary>Checks underlying types of every width, at their highest bit.</summary>
        [Test]
        public void Has_AnyUnderlyingType_ReadsOnlyItsOwnBytes()
        {
            Assert.That(ByteFlags.Low.Has(ByteFlags.High), Is.False);
            Assert.That((ByteFlags.Low | ByteFlags.High).Has(ByteFlags.High), Is.True);
            Assert.That(UInt16Flags.Low.Has(UInt16Flags.High), Is.False);
            Assert.That((UInt16Flags.Low | UInt16Flags.High).Has(UInt16Flags.High), Is.True);
            Assert.That(Int64Flags.Low.Has(Int64Flags.High), Is.False);
            Assert.That((Int64Flags.Low | Int64Flags.High).Has(Int64Flags.High), Is.True);
        }

        /// <summary>Checks a four-byte enum many times after writing to the stack, which used to leave non-zero bytes after the value.</summary>
        [Test]
        public void Has_RepeatedAfterStackWrites_NeverReportsAMissingFlag()
        {
            var falsePositives = 0;
            for (var i = 0; i < 100_000; i++)
            {
                FillStack(-1L, i);
                if (HasOnetime(SyncFlags.End))
                {
                    falsePositives++;
                }
            }

            Assert.That(falsePositives, Is.Zero);
        }

        /// <summary>Calls <see cref="EnumExtensions.Has{T}(T, T)"/> in a frame of its own.</summary>
        /// <param name="type">The sync type to test.</param>
        /// <returns>Whether <paramref name="type"/> has <see cref="SyncFlags.Onetime"/>.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HasOnetime(SyncFlags type)
        {
            return type.Has(SyncFlags.Onetime);
        }

        /// <summary>Uses a stack frame with all bits set, so later frames start on non-zero stack memory.</summary>
        /// <param name="value">A value with all bits set.</param>
        /// <param name="index">A value that varies between calls.</param>
        /// <returns>A value derived from the arguments, so the call is not removed.</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long FillStack(long value, long index)
        {
            var a = value ^ index;
            var b = value * index;
            return a + b;
        }
    }
}

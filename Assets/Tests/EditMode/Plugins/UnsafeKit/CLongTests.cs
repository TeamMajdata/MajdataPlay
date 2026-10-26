using System;
using System.Runtime.InteropServices;
using MajdataPlay.UnsafeKit;
using NUnit.Framework;

#nullable enable
namespace MajdataPlay.Tests.Plugins.UnsafeKit
{
    /// <summary>
    /// Covers <see cref="CLong"/> and <see cref="CULong"/>, the ABI-sized C long types (32 bits on Windows, 64 bits elsewhere).
    /// </summary>
    public class CLongTests
    {
        /// <summary>Checks that the native size matches the platform's C long.</summary>
        [Test]
        public void NativeSize_MatchesPlatformLong()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            const int expected = 4;
#else
            const int expected = 8;
#endif
            Assert.That(Marshal.SizeOf<CLong>(), Is.EqualTo(expected));
            Assert.That(Marshal.SizeOf<CULong>(), Is.EqualTo(expected));
        }

        /// <summary>Checks that every implicit conversion into CLong keeps small values.</summary>
        [Test]
        public void CLong_ImplicitConversionsIn_KeepValue()
        {
            CLong fromByte = (byte)200;
            CLong fromSByte = (sbyte)-5;
            CLong fromShort = (short)-300;
            CLong fromInt = -70000;
            CLong fromLong = 123456L;

            Assert.That((long)fromByte, Is.EqualTo(200));
            Assert.That((long)fromSByte, Is.EqualTo(-5));
            Assert.That((long)fromShort, Is.EqualTo(-300));
            Assert.That((long)fromInt, Is.EqualTo(-70000));
            Assert.That((long)fromLong, Is.EqualTo(123456));
        }

        /// <summary>Checks that every implicit conversion out of CLong narrows like a C cast.</summary>
        [Test]
        public void CLong_ImplicitConversionsOut_Narrow()
        {
            var value = new CLong(-1);

            byte asByte = value;
            sbyte asSByte = value;
            short asShort = value;
            int asInt = value;
            long asLong = value;

            Assert.That(asByte, Is.EqualTo(255));
            Assert.That(asSByte, Is.EqualTo(-1));
            Assert.That(asShort, Is.EqualTo(-1));
            Assert.That(asInt, Is.EqualTo(-1));
            Assert.That(asLong, Is.EqualTo(-1));
        }

        /// <summary>Checks that 64-bit values are truncated to the 32-bit Windows C long.</summary>
        [Test]
        public void CLong_FromLargeLong_TruncatesOnWindows()
        {
            CLong value = 0x1_0000_0005L;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            Assert.That((long)value, Is.EqualTo(5));
#else
            Assert.That((long)value, Is.EqualTo(0x1_0000_0005L));
#endif
        }

        /// <summary>Checks equality members, the operators, hashing and formatting of CLong.</summary>
        [Test]
        public void CLong_EqualityAndFormatting()
        {
            var a = new CLong(42);
            CLong b = 42;
            CLong c = 7;

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)42), Is.False);
            Assert.That(a.Equals(null), Is.False);
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a == c, Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.ToString(), Is.EqualTo("42"));
            Assert.That(new CLong(-3).ToString(), Is.EqualTo("-3"));
        }

        /// <summary>Checks the explicit conversions between the signed and unsigned types.</summary>
        [Test]
        public void ExplicitConversions_ReinterpretBits()
        {
            var minusOne = new CLong(-1);

            var unsigned = (CULong)minusOne;
            var signed = (CLong)unsigned;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            Assert.That((ulong)unsigned, Is.EqualTo(uint.MaxValue));
#else
            Assert.That((ulong)unsigned, Is.EqualTo(ulong.MaxValue));
#endif
            Assert.That((long)signed, Is.EqualTo(-1));
        }

        /// <summary>Checks that every implicit conversion into CULong keeps small values.</summary>
        [Test]
        public void CULong_ImplicitConversionsIn_KeepValue()
        {
            CULong fromByte = (byte)200;
            CULong fromUShort = (ushort)60000;
            CULong fromUInt = 4000000000u;
            CULong fromULong = 77UL;

            Assert.That((ulong)fromByte, Is.EqualTo(200));
            Assert.That((ulong)fromUShort, Is.EqualTo(60000));
            Assert.That((ulong)fromUInt, Is.EqualTo(4000000000u));
            Assert.That((ulong)fromULong, Is.EqualTo(77));
        }

        /// <summary>Checks that every implicit conversion out of CULong narrows like a C cast.</summary>
        [Test]
        public void CULong_ImplicitConversionsOut_Narrow()
        {
            CULong value = 0x1234_5678u;

            byte asByte = value;
            ushort asUShort = value;
            uint asUInt = value;
            ulong asULong = value;

            Assert.That(asByte, Is.EqualTo(0x78));
            Assert.That(asUShort, Is.EqualTo(0x5678));
            Assert.That(asUInt, Is.EqualTo(0x1234_5678u));
            Assert.That(asULong, Is.EqualTo(0x1234_5678UL));
        }

        /// <summary>Checks equality members, the operators, hashing and formatting of CULong.</summary>
        [Test]
        public void CULong_EqualityAndFormatting()
        {
            var a = new CULong(9);
            CULong b = 9u;
            CULong c = 1u;

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)9u), Is.False);
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a == c, Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.ToString(), Is.EqualTo("9"));
        }
    }
}

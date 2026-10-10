using System;
using System.Globalization;
using Number = MajdataPlay.Platform.Android.Runtime.Java.Lang.Number;
using NBigInteger = System.Numerics.BigInteger;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Math
{
    /// <summary>Wraps an immutable arbitrary-precision Java integer without changing its Java representation.</summary>
    [JavaClass("java.math.BigInteger", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class BigInteger : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java BigInteger to the C# System.Numerics.BigInteger value it represents.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        /// <exception cref="InvalidOperationException">Java returned a null string representation.</exception>
        /// <exception cref="FormatException">The Java string representation is not a valid integer.</exception>
        public static implicit operator System.Numerics.BigInteger(BigInteger? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var text = AndroidJni.Call<string?>(value.JavaReference, "java.math.BigInteger", "toString", "()Ljava/lang/String;");
            if (text is null)
            {
                throw new InvalidOperationException("Java BigInteger.toString() returned null.");
            }

            return NBigInteger.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        /// <summary>Implicitly boxes a C# System.Numerics.BigInteger value into a new Java java.math.BigInteger.</summary>
        public static implicit operator BigInteger(System.Numerics.BigInteger value) =>
            new BigInteger(AndroidJni.Construct("java.math.BigInteger", "(Ljava/lang/String;)V",
                value.ToString(CultureInfo.InvariantCulture)), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Object;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => (NBigInteger)this != NBigInteger.Zero;

        byte IConvertible.ToByte(IFormatProvider? provider) => (byte)(NBigInteger)this;

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((int)(NBigInteger)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java BigInteger cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => (decimal)(NBigInteger)this;

        double IConvertible.ToDouble(IFormatProvider? provider) => (double)(NBigInteger)this;

        short IConvertible.ToInt16(IFormatProvider? provider) => (short)(NBigInteger)this;

        int IConvertible.ToInt32(IFormatProvider? provider) => (int)(NBigInteger)this;

        long IConvertible.ToInt64(IFormatProvider? provider) => (long)(NBigInteger)this;

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => (sbyte)(NBigInteger)this;

        float IConvertible.ToSingle(IFormatProvider? provider) => (float)(NBigInteger)this;

        string IConvertible.ToString(IFormatProvider? provider) => ((NBigInteger)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => (ushort)(NBigInteger)this;

        uint IConvertible.ToUInt32(IFormatProvider? provider) => (uint)(NBigInteger)this;

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => (ulong)(NBigInteger)this;

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((NBigInteger)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

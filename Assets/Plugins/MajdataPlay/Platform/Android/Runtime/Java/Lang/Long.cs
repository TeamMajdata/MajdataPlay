using System;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>Wraps a Java boxed long without changing its Java representation.</summary>
    [JavaClass("java.lang.Long", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Long : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java boxed long to the C# long value it wraps.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        public static implicit operator long(Long? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return AndroidJni.Call<long>(value.JavaReference, "java.lang.Long", "longValue", "()J");
        }

        /// <summary>Implicitly boxes a C# long value into a new Java java.lang.Long.</summary>
        public static implicit operator Long(long value) =>
            new Long(AndroidJni.Construct("java.lang.Long", "(J)V", value), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Int64;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((long)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((long)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((long)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java boxed long cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal((long)this);

        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble((long)this);

        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16((long)this);

        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32((long)this);

        long IConvertible.ToInt64(IFormatProvider? provider) => (long)this;

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte((long)this);

        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle((long)this);

        string IConvertible.ToString(IFormatProvider? provider) => ((long)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((long)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((long)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((long)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((long)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

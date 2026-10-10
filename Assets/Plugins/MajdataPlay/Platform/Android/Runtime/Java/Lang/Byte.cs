using System;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>Wraps a Java boxed byte without changing its Java representation.</summary>
    [JavaClass("java.lang.Byte", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Byte : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java boxed byte to the C# sbyte value it wraps.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        public static implicit operator sbyte(Byte? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return AndroidJni.Call<sbyte>(value.JavaReference, "java.lang.Byte", "byteValue", "()B");
        }

        /// <summary>Implicitly boxes a C# sbyte value into a new Java java.lang.Byte.</summary>
        public static implicit operator Byte(sbyte value) =>
            new Byte(AndroidJni.Construct("java.lang.Byte", "(B)V", value), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.SByte;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((sbyte)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((sbyte)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((sbyte)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java boxed byte cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal((sbyte)this);

        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble((sbyte)this);

        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16((sbyte)this);

        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32((sbyte)this);

        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64((sbyte)this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => (sbyte)this;

        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle((sbyte)this);

        string IConvertible.ToString(IFormatProvider? provider) => ((sbyte)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((sbyte)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((sbyte)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((sbyte)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((sbyte)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

using System;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>Wraps a Java boxed short without changing its Java representation.</summary>
    [JavaClass("java.lang.Short", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Short : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java boxed short to the C# short value it wraps.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        public static implicit operator short(Short? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return AndroidJni.Call<short>(value.JavaReference, "java.lang.Short", "shortValue", "()S");
        }

        /// <summary>Implicitly boxes a C# short value into a new Java java.lang.Short.</summary>
        public static implicit operator Short(short value) =>
            new Short(AndroidJni.Construct("java.lang.Short", "(S)V", value), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Int16;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((short)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((short)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((short)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java boxed short cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal((short)this);

        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble((short)this);

        short IConvertible.ToInt16(IFormatProvider? provider) => (short)this;

        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32((short)this);

        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64((short)this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte((short)this);

        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle((short)this);

        string IConvertible.ToString(IFormatProvider? provider) => ((short)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((short)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((short)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((short)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((short)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

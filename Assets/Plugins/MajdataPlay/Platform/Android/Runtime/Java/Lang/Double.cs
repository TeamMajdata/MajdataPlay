using System;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>Wraps a Java boxed double without changing its Java representation.</summary>
    [JavaClass("java.lang.Double", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Double : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java boxed double to the C# double value it wraps.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        public static implicit operator double(Double? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return AndroidJni.Call<double>(value.JavaReference, "java.lang.Double", "doubleValue", "()D");
        }

        /// <summary>Implicitly boxes a C# double value into a new Java java.lang.Double.</summary>
        public static implicit operator Double(double value) =>
            new Double(AndroidJni.Construct("java.lang.Double", "(D)V", value), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Double;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((double)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((double)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((double)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java boxed double cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal((double)this);

        double IConvertible.ToDouble(IFormatProvider? provider) => (double)this;

        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16((double)this);

        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32((double)this);

        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64((double)this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte((double)this);

        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle((double)this);

        string IConvertible.ToString(IFormatProvider? provider) => ((double)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((double)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((double)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((double)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((double)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

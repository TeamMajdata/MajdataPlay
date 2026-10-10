using System;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>Wraps a Java boxed int without changing its Java representation.</summary>
    [JavaClass("java.lang.Integer", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Integer : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java boxed int to the C# int value it wraps.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        public static implicit operator int(Integer? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return AndroidJni.Call<int>(value.JavaReference, "java.lang.Integer", "intValue", "()I");
        }

        /// <summary>Implicitly boxes a C# int value into a new Java java.lang.Integer.</summary>
        public static implicit operator Integer(int value) =>
            new Integer(AndroidJni.Construct("java.lang.Integer", "(I)V", value), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Int32;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((int)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((int)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((int)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java boxed int cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal((int)this);

        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble((int)this);

        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16((int)this);

        int IConvertible.ToInt32(IFormatProvider? provider) => (int)this;

        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64((int)this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte((int)this);

        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle((int)this);

        string IConvertible.ToString(IFormatProvider? provider) => ((int)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((int)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((int)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((int)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((int)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

using System;
using System.Globalization;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Lang
{
    /// <summary>Wraps a Java boxed float without changing its Java representation.</summary>
    [JavaClass("java.lang.Float", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Float : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java boxed float to the C# float value it wraps.</summary>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        public static implicit operator float(Float? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return AndroidJni.Call<float>(value.JavaReference, "java.lang.Float", "floatValue", "()F");
        }

        /// <summary>Implicitly boxes a C# float value into a new Java java.lang.Float.</summary>
        public static implicit operator Float(float value) =>
            new Float(AndroidJni.Construct("java.lang.Float", "(F)V", value), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Single;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((float)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((float)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((float)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java boxed float cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal((float)this);

        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble((float)this);

        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16((float)this);

        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32((float)this);

        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64((float)this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte((float)this);

        float IConvertible.ToSingle(IFormatProvider? provider) => (float)this;

        string IConvertible.ToString(IFormatProvider? provider) => ((float)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((float)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((float)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((float)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((float)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

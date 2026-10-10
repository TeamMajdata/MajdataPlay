using System;
using System.Globalization;
using Number = MajdataPlay.Platform.Android.Runtime.Java.Lang.Number;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Math
{
    /// <summary>Wraps an immutable arbitrary-precision Java decimal without changing its Java representation.</summary>
    [JavaClass("java.math.BigDecimal", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class BigDecimal : Number, IConvertible
    {
        /// <summary>Implicitly converts a Java BigDecimal to the C# decimal value it represents.</summary>
        /// <remarks>Java values outside the C# decimal range throw <see cref="OverflowException"/>.</remarks>
        /// <exception cref="ArgumentNullException">The wrapper is null.</exception>
        /// <exception cref="InvalidOperationException">Java returned a null string representation.</exception>
        /// <exception cref="OverflowException">The Java value is outside the C# decimal range.</exception>
        /// <exception cref="FormatException">The Java string representation is not a valid decimal.</exception>
        public static implicit operator decimal(BigDecimal? value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var text = AndroidJni.Call<string?>(value.JavaReference, "java.math.BigDecimal", "toString", "()Ljava/lang/String;");
            if (text is null)
            {
                throw new InvalidOperationException("Java BigDecimal.toString() returned null.");
            }

            return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>Implicitly boxes a C# decimal value into a new Java java.math.BigDecimal.</summary>
        public static implicit operator BigDecimal(decimal value) =>
            new BigDecimal(AndroidJni.Construct("java.math.BigDecimal", "(Ljava/lang/String;)V",
                value.ToString(CultureInfo.InvariantCulture)), ownsReference: true);

        TypeCode IConvertible.GetTypeCode() => TypeCode.Decimal;

        bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean((decimal)this);

        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte((decimal)this);

        char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar((decimal)this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) =>
            throw new InvalidCastException("A Java BigDecimal cannot be converted to a DateTime.");

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => (decimal)this;

        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble((decimal)this);

        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16((decimal)this);

        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32((decimal)this);

        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64((decimal)this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte((decimal)this);

        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle((decimal)this);

        string IConvertible.ToString(IFormatProvider? provider) => ((decimal)this).ToString(provider);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16((decimal)this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32((decimal)this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64((decimal)this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            Convert.ChangeType((decimal)this, conversionType, provider ?? CultureInfo.InvariantCulture);
    }
}

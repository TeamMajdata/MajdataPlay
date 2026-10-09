using Number = MajdataPlay.Platform.Android.Runtime.Java.Lang.Number;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Math
{
    /// <summary>Wraps an immutable arbitrary-precision Java integer without changing its Java representation.</summary>
    [JavaClass("java.math.BigInteger", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class BigInteger : Number
    {
    }
}

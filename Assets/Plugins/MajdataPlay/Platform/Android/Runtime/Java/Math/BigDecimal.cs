using Number = MajdataPlay.Platform.Android.Runtime.Java.Lang.Number;

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Java.Math
{
    /// <summary>Wraps an immutable arbitrary-precision Java decimal without changing its Java representation.</summary>
    [JavaClass("java.math.BigDecimal", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class BigDecimal : Number
    {
    }
}

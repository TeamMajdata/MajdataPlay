using MajdataPlay.Platform.Android;

#nullable enable

namespace MajdataPlay.Tests.Bindings
{
    /// <summary>
    /// Exercises SDK nested binary names and getter-only fields without instantiating Java classes.
    /// </summary>
    [JavaClass("android.os.Build$VERSION", IncludeInheritedMembers = false)]
    public partial class BuildVersion
    {
    }

    /// <summary>
    /// Exercises a Java interface wrapper with no generated Java-instantiation constructors.
    /// </summary>
    [JavaClass("java.lang.Runnable", IncludeInheritedMembers = false)]
    public partial class Runnable
    {
    }

    /// <summary>
    /// Exercises SDK overloads, constructor dispatch, and default Unity analyzer loading.
    /// </summary>
    [JavaClass("java.lang.StringBuilder", IncludeInheritedMembers = false)]
    public partial class JavaStringBuilder
    {
    }
}

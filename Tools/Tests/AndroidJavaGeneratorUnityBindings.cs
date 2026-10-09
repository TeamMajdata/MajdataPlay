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

    /// <summary>
    /// Exercises all public Android SDK constructor overloads, including a typed copy constructor.
    /// </summary>
    [JavaClass("android.content.Intent", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Intent
    {
    }

    /// <summary>
    /// Exercises a concrete SDK class without any public Java constructor.
    /// </summary>
    [JavaClass("android.os.Looper", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class Looper
    {
    }

    /// <summary>
    /// Exercises an abstract SDK class whose public constructor cannot instantiate the Java type.
    /// </summary>
    [JavaClass("java.io.InputStream", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class InputStream
    {
    }
}

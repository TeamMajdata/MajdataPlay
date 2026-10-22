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
    /// Exercises inherited Object overrides and typed equality for an erased Java generic class.
    /// </summary>
    [JavaClass("java.util.ArrayList", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class JavaArrayList
    {
    }

    /// <summary>
    /// Exercises interface Object declarations without generating duplicate Java method aliases.
    /// </summary>
    [JavaClass("java.util.List", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class JavaList
    {
    }

    /// <summary>
    /// Exercises concrete equality and abstract formatting overrides on an Android SDK class.
    /// </summary>
    [JavaClass("android.net.Uri", ApiLevel = 36, IncludeInheritedMembers = false)]
    public partial class AndroidUri
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

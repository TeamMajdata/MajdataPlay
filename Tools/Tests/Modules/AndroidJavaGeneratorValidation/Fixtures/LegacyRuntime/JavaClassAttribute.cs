using System;

#nullable enable

namespace MajdataPlay.Platform.Android
{
    /// <summary>
    /// Requests a strongly typed JNI wrapper for a Java class or interface in a partial C# class.
    /// </summary>
    /// <remarks>
    /// Java nested classes use their binary name, for example <c>android.os.Build$VERSION</c>.
    /// Relative input paths are resolved against the Unity project root. This attribute describes
    /// compile-time inputs only; it does not package Java code into an Android player.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class JavaClassAttribute : Attribute
    {
        /// <summary>
        /// Gets the Java binary class name to bind.
        /// </summary>
        public string ClassName { get; }

        /// <summary>
        /// Gets or sets Java source files, source directories, class files, or JAR archives to inspect.
        /// </summary>
        public string[] Sources { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets additional JAR archives and compiled-class roots needed to resolve dependencies.
        /// </summary>
        public string[] ClassPath { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets optional Java source directories or source archives containing API documentation.
        /// </summary>
        public string[] DocumentationPaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the Android SDK root, overriding assembly configuration and SDK discovery.
        /// </summary>
        public string? AndroidSdkPath { get; set; }

        /// <summary>
        /// Gets or sets the JDK root, overriding assembly configuration and Unity's bundled OpenJDK.
        /// </summary>
        public string? JavaHome { get; set; }

        /// <summary>
        /// Gets or sets the Android API level. Zero selects the highest installed platform.
        /// </summary>
        /// <remarks>
        /// Pin this value when reproducible API surfaces across developer machines are required.
        /// It is independent of the application's minimum supported Android version.
        /// </remarks>
        public int ApiLevel { get; set; }

        /// <summary>
        /// Gets or sets whether inherited public Java fields and methods are included.
        /// </summary>
        public bool IncludeInheritedMembers { get; set; } = true;

        /// <summary>
        /// Initializes a wrapper request for the specified Java binary class name.
        /// </summary>
        /// <param name="className">The fully qualified Java binary class or interface name.</param>
        /// <exception cref="ArgumentException">The class name is null, empty, or whitespace.</exception>
        public JavaClassAttribute(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                throw new ArgumentException("A Java binary class name is required.", nameof(className));
            }

            ClassName = className;
        }
    }
}

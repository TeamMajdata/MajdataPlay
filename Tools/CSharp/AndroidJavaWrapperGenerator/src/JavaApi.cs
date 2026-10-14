using System.Collections.Generic;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Contains JVM metadata without loading or initializing Java classes.</summary>
    internal sealed class JavaApi
    {
        /// <summary>Gets all discovered types keyed by their dot-separated JVM binary names.</summary>
        internal Dictionary<string, JavaType> Types { get; } = new Dictionary<string, JavaType>(System.StringComparer.Ordinal);

        /// <summary>Gets extraction diagnostics that do not prevent generating the available public API.</summary>
        internal List<string> Warnings { get; } = new List<string>();

        /// <summary>Gets or sets the selected android.jar path, or null for SDK-free extraction.</summary>
        internal string? PlatformJar { get; set; }
    }

    /// <summary>Describes a Java class or interface and its declared members.</summary>
    internal sealed class JavaType
    {
        /// <summary>Gets or sets the official English API description, when available.</summary>
        internal JavaDocumentation? Documentation { get; set; }

        /// <summary>Gets or sets the binary name, preserving dollar signs for nested classes.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the exact JVM generic Signature attribute, when present.</summary>
        internal string? Signature { get; set; }

        /// <summary>Gets or sets the binary superclass name; null denotes the JVM root.</summary>
        internal string? SuperName { get; set; }

        /// <summary>Gets the binary names of directly implemented or extended Java interfaces.</summary>
        internal List<string> Interfaces { get; } = new List<string>();

        /// <summary>Gets or sets the JVM class access flags.</summary>
        internal ushort AccessFlags { get; set; }

        /// <summary>Gets or sets whether the type and all enclosing types are publicly accessible.</summary>
        internal bool IsPublic { get; set; }

        /// <summary>Gets or sets the enclosing binary name recorded by InnerClasses.</summary>
        internal string? EnclosingType { get; set; }

        /// <summary>Gets whether this declaration is a Java interface.</summary>
        internal bool IsInterface => (AccessFlags & 0x0200) != 0;

        /// <summary>Gets whether this declaration is abstract.</summary>
        internal bool IsAbstract => (AccessFlags & 0x0400) != 0;

        /// <summary>Gets whether the JVM marks the class as synthetic.</summary>
        internal bool IsSynthetic => (AccessFlags & 0x1000) != 0;

        /// <summary>Gets the declared field metadata, including nonpublic members needed for hierarchy analysis.</summary>
        internal List<JavaField> Fields { get; } = new List<JavaField>();

        /// <summary>Gets the declared method metadata, including constructors and bridge methods.</summary>
        internal List<JavaMethod> Methods { get; } = new List<JavaMethod>();
    }

    /// <summary>Describes a JVM field using its exact descriptor.</summary>
    internal sealed class JavaField
    {
        /// <summary>Gets or sets the exact JVM generic Signature attribute, when present.</summary>
        internal string? Signature { get; set; }

        /// <summary>Gets or sets the official English API description, when available.</summary>
        internal JavaDocumentation? Documentation { get; set; }

        /// <summary>Gets or sets the original Java field name.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the exact field descriptor.</summary>
        internal string Descriptor { get; set; } = string.Empty;

        /// <summary>Gets or sets the JVM field access flags.</summary>
        internal ushort AccessFlags { get; set; }

        /// <summary>Gets whether the field is public.</summary>
        internal bool IsPublic => (AccessFlags & 0x0001) != 0;

        /// <summary>Gets whether the field is static.</summary>
        internal bool IsStatic => (AccessFlags & 0x0008) != 0;
    }

    /// <summary>Describes a JVM method or constructor using its erased descriptor.</summary>
    internal sealed class JavaMethod
    {
        /// <summary>Gets or sets the exact JVM generic Signature attribute, when present.</summary>
        internal string? Signature { get; set; }

        /// <summary>Gets or sets the official English API description, when available.</summary>
        internal JavaDocumentation? Documentation { get; set; }

        /// <summary>Gets or sets the original method name or &lt;init&gt; for constructors.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the complete erased method descriptor.</summary>
        internal string Descriptor { get; set; } = string.Empty;

        /// <summary>Gets or sets the JVM method access flags.</summary>
        internal ushort AccessFlags { get; set; }

        /// <summary>Gets optional parameter names in descriptor order.</summary>
        internal List<string> ParameterNames { get; } = new List<string>();

        /// <summary>Gets whether the member is public.</summary>
        internal bool IsPublic => (AccessFlags & 0x0001) != 0;

        /// <summary>Gets whether the member is static.</summary>
        internal bool IsStatic => (AccessFlags & 0x0008) != 0;

        /// <summary>Gets whether the JVM generated a covariant or generic bridge.</summary>
        internal bool IsBridge => (AccessFlags & 0x0040) != 0;

        /// <summary>Gets whether the JVM marks the member as synthetic.</summary>
        internal bool IsSynthetic => (AccessFlags & 0x1000) != 0;
    }
}

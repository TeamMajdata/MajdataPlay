using System.Collections.Generic;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Contains a Java compilation unit and its lexical import scope.</summary>
    internal sealed class JavaSourceUnit
    {
        /// <summary>Gets the original path used in diagnostics.</summary>
        internal string Path { get; }

        /// <summary>Gets or sets the declared package, including the empty unnamed package.</summary>
        internal string PackageName { get; set; } = string.Empty;

        /// <summary>Gets the compilation unit's import declarations.</summary>
        internal List<JavaSourceImport> Imports { get; } = new List<JavaSourceImport>();

        /// <summary>Gets all named declarations, including nonpublic member types.</summary>
        internal List<JavaSourceType> Types { get; } = new List<JavaSourceType>();

        /// <summary>Creates a source scope.</summary>
        /// <param name="path">The original source path.</param>
        internal JavaSourceUnit(string path)
        {
            Path = path;
        }
    }

    /// <summary>Describes a single or on-demand import of packages or member types.</summary>
    internal sealed class JavaSourceImport
    {
        /// <summary>Gets the imported qualified name without a trailing wildcard.</summary>
        internal string Name { get; }

        /// <summary>Gets whether the declaration imports on demand.</summary>
        internal bool IsWildcard { get; }

        /// <summary>Gets whether the declaration is a static import.</summary>
        internal bool IsStatic { get; }

        /// <summary>Creates an import declaration.</summary>
        /// <param name="name">The qualified imported name.</param>
        /// <param name="isWildcard">Whether the import ends in a wildcard.</param>
        /// <param name="isStatic">Whether the import is static.</param>
        internal JavaSourceImport(string name, bool isWildcard, bool isStatic)
        {
            Name = name;
            IsWildcard = isWildcard;
            IsStatic = isStatic;
        }
    }

    /// <summary>Contains unresolved declarations for one named Java type.</summary>
    internal sealed class JavaSourceType
    {
        /// <summary>Gets the compilation unit supplying the lexical scope.</summary>
        internal JavaSourceUnit Unit { get; }

        /// <summary>Gets the declaration's identifier and diagnostic location.</summary>
        internal JavaSourceToken Token { get; }

        /// <summary>Gets the declaration kind: class, interface, annotation, enum, or record.</summary>
        internal string Kind { get; }

        /// <summary>Gets the enclosing named source declaration, if present.</summary>
        internal JavaSourceType? Enclosing { get; }

        /// <summary>Gets the new metadata instance populated by the resolver.</summary>
        internal JavaType Metadata { get; }

        /// <summary>Gets the generic parameters declared by this type.</summary>
        internal List<JavaSourceTypeParameter> TypeParameters { get; } = new List<JavaSourceTypeParameter>();

        /// <summary>Gets or sets the explicit superclass reference.</summary>
        internal JavaSourceReference? SuperType { get; set; }

        /// <summary>Gets the explicit interface references.</summary>
        internal List<JavaSourceReference> Interfaces { get; } = new List<JavaSourceReference>();

        /// <summary>Gets the declared fields, including enum constants.</summary>
        internal List<JavaSourceField> Fields { get; } = new List<JavaSourceField>();

        /// <summary>Gets the explicitly declared methods and constructors.</summary>
        internal List<JavaSourceMethod> Methods { get; } = new List<JavaSourceMethod>();

        /// <summary>Gets the ordered record components.</summary>
        internal List<JavaSourceParameter> Components { get; } = new List<JavaSourceParameter>();

        /// <summary>Creates an unresolved named type declaration.</summary>
        /// <param name="unit">The compilation unit's lexical scope.</param>
        /// <param name="token">The type identifier and location.</param>
        /// <param name="kind">The Java declaration kind.</param>
        /// <param name="enclosing">The enclosing declaration, if any.</param>
        /// <param name="metadata">The new metadata object.</param>
        internal JavaSourceType(JavaSourceUnit unit, JavaSourceToken token, string kind, JavaSourceType? enclosing, JavaType metadata)
        {
            Unit = unit;
            Token = token;
            Kind = kind;
            Enclosing = enclosing;
            Metadata = metadata;
        }
    }

    /// <summary>Describes a Java reference while preserving generic arguments and enclosing parameterizations.</summary>
    internal sealed class JavaSourceReference
    {
        /// <summary>Gets the first type token for diagnostics.</summary>
        internal JavaSourceToken Token { get; }

        /// <summary>Gets the primitive, variable, or source-qualified type name.</summary>
        internal string Name { get; }

        /// <summary>Gets the total array rank, including declarator and varargs dimensions.</summary>
        internal int Dimensions { get; }

        /// <summary>Gets generic arguments attached to the final named class segment.</summary>
        internal IReadOnlyList<JavaSourceReference> TypeArguments { get; }

        /// <summary>Gets the preceding parameterized enclosing class reference, if present.</summary>
        internal JavaSourceReference? DeclaringType { get; }

        /// <summary>Gets the wildcard marker (*, +, or -), or null for ordinary references.</summary>
        internal char? Wildcard { get; }

        /// <summary>Gets the bound of an extends or super wildcard.</summary>
        internal JavaSourceReference? WildcardBound { get; }

        /// <summary>Creates an erased source type reference.</summary>
        /// <param name="token">The diagnostic token.</param>
        /// <param name="name">The name without generic arguments.</param>
        /// <param name="dimensions">The number of array dimensions.</param>
        /// <param name="typeArguments">The final class segment's generic arguments.</param>
        /// <param name="declaringType">The preceding parameterized enclosing class segment.</param>
        /// <param name="wildcard">The optional wildcard variance marker.</param>
        /// <param name="wildcardBound">The bounded wildcard's reference type.</param>
        internal JavaSourceReference(JavaSourceToken token, string name, int dimensions = 0, IReadOnlyList<JavaSourceReference>? typeArguments = null, JavaSourceReference? declaringType = null, char? wildcard = null, JavaSourceReference? wildcardBound = null)
        {
            Token = token;
            Name = name;
            Dimensions = dimensions;
            TypeArguments = typeArguments ?? System.Array.Empty<JavaSourceReference>();
            DeclaringType = declaringType;
            Wildcard = wildcard;
            WildcardBound = wildcardBound;
        }

        /// <summary>Returns a reference with additional declarator dimensions.</summary>
        /// <param name="dimensions">The additional dimension count.</param>
        /// <returns>The combined array reference.</returns>
        internal JavaSourceReference AddDimensions(int dimensions)
        {
            return new JavaSourceReference(Token, Name, Dimensions + dimensions, TypeArguments, DeclaringType, Wildcard, WildcardBound);
        }
    }

    /// <summary>Describes a generic variable and its ordered intersection bounds.</summary>
    internal sealed class JavaSourceTypeParameter
    {
        /// <summary>Gets the variable's declaration token.</summary>
        internal JavaSourceToken Token { get; }

        /// <summary>Gets the bounds in source order; the first bound determines erasure.</summary>
        internal List<JavaSourceReference> Bounds { get; } = new List<JavaSourceReference>();

        /// <summary>Creates a generic variable declaration.</summary>
        /// <param name="token">The variable identifier and location.</param>
        internal JavaSourceTypeParameter(JavaSourceToken token)
        {
            Token = token;
        }
    }

    /// <summary>Describes an unresolved Java field.</summary>
    internal sealed class JavaSourceField
    {
        /// <summary>Gets the field identifier and diagnostic location.</summary>
        internal JavaSourceToken Token { get; }

        /// <summary>Gets the field type with all array declarators applied.</summary>
        internal JavaSourceReference Type { get; }

        /// <summary>Gets the declared and implicit JVM field flags.</summary>
        internal ushort AccessFlags { get; }

        /// <summary>Creates an unresolved field declaration.</summary>
        /// <param name="token">The field identifier and location.</param>
        /// <param name="type">The erased source type reference.</param>
        /// <param name="accessFlags">The JVM field flags.</param>
        internal JavaSourceField(JavaSourceToken token, JavaSourceReference type, ushort accessFlags)
        {
            Token = token;
            Type = type;
            AccessFlags = accessFlags;
        }
    }

    /// <summary>Describes an unresolved Java method or constructor.</summary>
    internal sealed class JavaSourceMethod
    {
        /// <summary>Gets the source identifier and diagnostic location.</summary>
        internal JavaSourceToken Token { get; }

        /// <summary>Gets whether this declaration is a constructor.</summary>
        internal bool IsConstructor { get; }

        /// <summary>Gets or sets the return type; constructors have no return reference.</summary>
        internal JavaSourceReference? ReturnType { get; set; }

        /// <summary>Gets or sets the declared and implicit JVM method flags.</summary>
        internal ushort AccessFlags { get; set; }

        /// <summary>Gets the generic variables declared by this method.</summary>
        internal List<JavaSourceTypeParameter> TypeParameters { get; } = new List<JavaSourceTypeParameter>();

        /// <summary>Gets the ordinary source parameters, excluding receiver parameters.</summary>
        internal List<JavaSourceParameter> Parameters { get; } = new List<JavaSourceParameter>();

        /// <summary>Gets the declared exception references, including generic exception variables.</summary>
        internal List<JavaSourceReference> ThrowsTypes { get; } = new List<JavaSourceReference>();

        /// <summary>Creates an unresolved method declaration.</summary>
        /// <param name="token">The source identifier and location.</param>
        /// <param name="isConstructor">Whether this declaration is a constructor.</param>
        /// <param name="accessFlags">The JVM method flags.</param>
        internal JavaSourceMethod(JavaSourceToken token, bool isConstructor, ushort accessFlags)
        {
            Token = token;
            IsConstructor = isConstructor;
            AccessFlags = accessFlags;
        }
    }

    /// <summary>Describes a source method parameter or record component.</summary>
    internal sealed class JavaSourceParameter
    {
        /// <summary>Gets the parameter identifier and location.</summary>
        internal JavaSourceToken Token { get; }

        /// <summary>Gets the parameter's type including array and varargs dimensions.</summary>
        internal JavaSourceReference Type { get; }

        /// <summary>Gets whether this is a variable-arity parameter.</summary>
        internal bool IsVarargs { get; }

        /// <summary>Creates a parameter declaration.</summary>
        /// <param name="token">The source parameter identifier.</param>
        /// <param name="type">The complete parameter type.</param>
        /// <param name="isVarargs">Whether the parameter uses an ellipsis.</param>
        internal JavaSourceParameter(JavaSourceToken token, JavaSourceReference type, bool isVarargs)
        {
            Token = token;
            Type = type;
            IsVarargs = isVarargs;
        }
    }
}

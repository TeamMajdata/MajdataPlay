#nullable enable

using System;
using System.Collections.Generic;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Represents documentation shared by Java types and members.</summary>
    internal abstract class JavaDocumentation
    {
        /// <summary>Gets or sets the plain-text API summary.</summary>
        internal string Summary { get; set; } = string.Empty;

        /// <summary>Gets or sets the official API documentation link, when available.</summary>
        internal string DocumentationUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets whether Java marks the declaration as deprecated.</summary>
        internal bool Deprecated { get; set; }
    }

    /// <summary>Represents the public Java API of a requested binary type.</summary>
    internal sealed class JavaApiType : JavaDocumentation
    {
        /// <summary>Gets or sets the Java binary name, including '$' for nested types.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets whether this Java type is an interface.</summary>
        internal bool IsInterface { get; set; }

        /// <summary>Gets or sets whether this Java type is abstract.</summary>
        internal bool IsAbstract { get; set; }

        /// <summary>Gets or sets whether this Java type is final.</summary>
        internal bool IsFinal { get; set; }

        /// <summary>Gets the exported fields in deterministic metadata order.</summary>
        internal List<JavaApiField> Fields { get; } = new List<JavaApiField>();

        /// <summary>Gets public nested binary names for protocol validation; wrappers remain explicitly annotated.</summary>
        internal List<string> NestedTypes { get; } = new List<string>();

        /// <summary>Gets the exported constructors and methods.</summary>
        internal List<JavaApiMethod> Methods { get; } = new List<JavaApiMethod>();
    }

    /// <summary>Represents a public Java field and its exact JNI descriptor.</summary>
    internal sealed class JavaApiField : JavaDocumentation
    {
        /// <summary>Gets or sets the original Java field name.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the original JVM field descriptor.</summary>
        internal string Descriptor { get; set; } = string.Empty;

        /// <summary>Gets or sets the binary name that declares this field.</summary>
        internal string DeclaringType { get; set; } = string.Empty;

        /// <summary>Gets or sets whether the Java field is static.</summary>
        internal bool IsStatic { get; set; }

        /// <summary>Gets or sets whether the Java field is final.</summary>
        internal bool IsFinal { get; set; }

        /// <summary>Gets or sets the kind of an optional compile-time constant.</summary>
        internal string ConstantKind { get; set; } = string.Empty;

        /// <summary>Gets or sets the unescaped value of an optional compile-time constant.</summary>
        internal string? ConstantValue { get; set; }
    }

    /// <summary>Represents a Java method or constructor.</summary>
    internal sealed class JavaApiMethod : JavaDocumentation
    {
        /// <summary>Gets or sets the Java name, or '&lt;init&gt;' for a constructor.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the exact erased JVM method descriptor.</summary>
        internal string Descriptor { get; set; } = string.Empty;

        /// <summary>Gets or sets the binary name that declares this method.</summary>
        internal string DeclaringType { get; set; } = string.Empty;

        /// <summary>Gets or sets whether this method is static.</summary>
        internal bool IsStatic { get; set; }

        /// <summary>Gets or sets the return-value documentation.</summary>
        internal string Returns { get; set; } = string.Empty;

        /// <summary>Gets parameter names and documentation in descriptor order.</summary>
        internal List<JavaApiParameter> Parameters { get; } = new List<JavaApiParameter>();

        /// <summary>Gets the checked Java exceptions documented by the helper.</summary>
        internal List<JavaApiException> Exceptions { get; } = new List<JavaApiException>();
    }

    /// <summary>Represents a Java parameter's source name and documentation.</summary>
    internal sealed class JavaApiParameter
    {
        /// <summary>Gets or sets the Java parameter name when available.</summary>
        internal string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the plain-text parameter documentation.</summary>
        internal string Summary { get; set; } = string.Empty;
    }

    /// <summary>Represents a declared Java exception.</summary>
    internal sealed class JavaApiException
    {
        /// <summary>Gets or sets the exception's Java binary name.</summary>
        internal string Type { get; set; } = string.Empty;

        /// <summary>Gets or sets the plain-text exception condition.</summary>
        internal string Summary { get; set; } = string.Empty;
    }

    /// <summary>Represents one recursively parsed JVM descriptor type.</summary>
    internal sealed class JavaTypeReference
    {
        /// <summary>Gets the JVM type code, such as 'I', 'L', or '['.</summary>
        internal char Code { get; }

        /// <summary>Gets the Java binary name for an object reference.</summary>
        internal string Name { get; }

        /// <summary>Gets the element type for an array.</summary>
        internal JavaTypeReference? Element { get; }

        /// <summary>Creates a parsed JVM descriptor type.</summary>
        /// <param name="code">The primitive, reference, array, or void code.</param>
        /// <param name="name">The binary name for reference types.</param>
        /// <param name="element">The element descriptor for array types.</param>
        internal JavaTypeReference(char code, string name = "", JavaTypeReference? element = null)
        {
            Code = code;
            Name = name;
            Element = element;
        }
    }

    /// <summary>Represents the parameter and result types of a JNI method descriptor.</summary>
    internal sealed class JavaMethodSignature
    {
        /// <summary>Gets the parsed parameter types.</summary>
        internal IReadOnlyList<JavaTypeReference> Parameters { get; }

        /// <summary>Gets the parsed return type.</summary>
        internal JavaTypeReference ReturnType { get; }

        /// <summary>Creates an erased method signature.</summary>
        /// <param name="parameters">The ordered JVM parameter types.</param>
        /// <param name="returnType">The JVM return type.</param>
        internal JavaMethodSignature(IReadOnlyList<JavaTypeReference> parameters, JavaTypeReference returnType)
        {
            Parameters = parameters;
            ReturnType = returnType;
        }
    }

    /// <summary>Parses exact JVM descriptors without inferring JNI signatures.</summary>
    internal static class JavaDescriptors
    {
        /// <summary>Parses a JVM field descriptor.</summary>
        /// <param name="descriptor">The complete JVM field descriptor.</param>
        /// <returns>The parsed field type.</returns>
        /// <exception cref="GeneratorException">The descriptor is malformed.</exception>
        internal static JavaTypeReference ParseField(string descriptor)
        {
            var position = 0;
            var type = ParseType(descriptor, ref position, false, 0);
            if (position != descriptor.Length)
            {
                throw Invalid(descriptor);
            }
            return type;
        }

        /// <summary>Parses a JVM method or constructor descriptor.</summary>
        /// <param name="descriptor">The complete JVM method descriptor.</param>
        /// <returns>The parsed parameter and return types.</returns>
        /// <exception cref="GeneratorException">The descriptor is malformed.</exception>
        internal static JavaMethodSignature ParseMethod(string descriptor)
        {
            if (descriptor.Length < 3 || descriptor[0] != '(')
            {
                throw Invalid(descriptor);
            }
            var position = 1;
            var parameters = new List<JavaTypeReference>();
            while (position < descriptor.Length && descriptor[position] != ')')
            {
                parameters.Add(ParseType(descriptor, ref position, false, 0));
            }
            if (position >= descriptor.Length || descriptor[position++] != ')')
            {
                throw Invalid(descriptor);
            }
            var returnType = ParseType(descriptor, ref position, true, 0);
            if (position != descriptor.Length)
            {
                throw Invalid(descriptor);
            }
            return new JavaMethodSignature(parameters, returnType);
        }

        /// <summary>Checks a Java binary name without changing nested-class spelling.</summary>
        /// <param name="name">The dot-separated binary class name.</param>
        /// <returns>Whether the name can safely identify a declared JVM class.</returns>
        internal static bool IsBinaryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            var parts = name.Split('.');
            foreach (var part in parts)
            {
                if (part.Length == 0)
                {
                    return false;
                }
                foreach (var character in part)
                {
                    if (char.IsWhiteSpace(character) || char.IsControl(character) || "/\\;[:<>".IndexOf(character) >= 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Parses one type while enforcing JVM void and array restrictions.</summary>
        /// <param name="descriptor">The complete descriptor being parsed.</param>
        /// <param name="position">The next character to read.</param>
        /// <param name="allowVoid">Whether a void result is permitted.</param>
        /// <param name="depth">The current jagged-array depth.</param>
        /// <returns>The next parsed type.</returns>
        /// <exception cref="GeneratorException">The descriptor is malformed.</exception>
        private static JavaTypeReference ParseType(string descriptor, ref int position, bool allowVoid, int depth)
        {
            if (position >= descriptor.Length)
            {
                throw Invalid(descriptor);
            }
            var code = descriptor[position++];
            if ("ZBCSIJFD".IndexOf(code) >= 0 || (code == 'V' && allowVoid))
            {
                return new JavaTypeReference(code);
            }
            if (code == '[' && depth < 255)
            {
                return new JavaTypeReference(code, element: ParseType(descriptor, ref position, false, depth + 1));
            }
            if (code == 'L')
            {
                var end = descriptor.IndexOf(';', position);
                if (end > position)
                {
                    var internalName = descriptor.Substring(position, end - position);
                    var name = internalName.Replace('/', '.');
                    if (internalName.IndexOf('.') < 0 && IsBinaryName(name))
                    {
                        position = end + 1;
                        return new JavaTypeReference(code, name);
                    }
                }
            }
            throw Invalid(descriptor);
        }

        /// <summary>Creates a descriptor-specific validation failure.</summary>
        /// <param name="descriptor">The invalid descriptor.</param>
        /// <returns>The failure to report to the compilation.</returns>
        private static GeneratorException Invalid(string descriptor)
        {
            return new GeneratorException(GeneratorDiagnostics.InvalidMetadata, "Invalid JVM descriptor: '" + descriptor + "'.");
        }
    }
}

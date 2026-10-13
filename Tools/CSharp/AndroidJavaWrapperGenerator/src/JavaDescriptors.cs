using System;
using System.Collections.Generic;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Describes one erased JVM primitive, reference, array, or void type.</summary>
    internal sealed class JavaTypeDescriptor
    {
        /// <summary>Gets the exact JVM type code.</summary>
        internal char Code { get; }

        /// <summary>Gets a reference's dot-separated binary name, preserving dollar signs.</summary>
        internal string BinaryName { get; }

        /// <summary>Gets an array's element type, or null for nonarray descriptors.</summary>
        internal JavaTypeDescriptor? Element { get; }

        /// <summary>Creates a parsed, immutable descriptor node.</summary>
        /// <param name="code">The JVM descriptor code.</param>
        /// <param name="binaryName">The binary name for reference descriptors.</param>
        /// <param name="element">The element node for array descriptors.</param>
        internal JavaTypeDescriptor(char code, string binaryName = "", JavaTypeDescriptor? element = null)
        {
            Code = code;
            BinaryName = binaryName;
            Element = element;
        }
    }

    /// <summary>Contains a method's erased parameter and return types.</summary>
    internal sealed class JavaMethodDescriptor
    {
        /// <summary>Gets the parameters in their original JVM order.</summary>
        internal IReadOnlyList<JavaTypeDescriptor> Parameters { get; }

        /// <summary>Gets the return descriptor, including void.</summary>
        internal JavaTypeDescriptor ReturnType { get; }

        /// <summary>Creates a parsed method descriptor.</summary>
        /// <param name="parameters">The ordered JVM parameter descriptors.</param>
        /// <param name="returnType">The JVM return descriptor.</param>
        internal JavaMethodDescriptor(IReadOnlyList<JavaTypeDescriptor> parameters, JavaTypeDescriptor returnType)
        {
            Parameters = parameters;
            ReturnType = returnType;
        }
    }

    /// <summary>Parses exact class-file descriptors without Java loading or signature inference.</summary>
    internal static class JavaDescriptors
    {
        /// <summary>Parses one complete field descriptor.</summary>
        /// <param name="descriptor">The exact JVM field descriptor.</param>
        /// <returns>The primitive, reference, or array descriptor.</returns>
        /// <exception cref="FormatException">The descriptor is malformed or contains void.</exception>
        internal static JavaTypeDescriptor ParseField(string descriptor)
        {
            var position = 0;
            var result = ParseType(descriptor, ref position, false, 0);
            if (position != descriptor.Length)
            {
                throw Invalid(descriptor);
            }
            return result;
        }

        /// <summary>Parses a complete method or constructor descriptor.</summary>
        /// <param name="descriptor">The exact JVM method descriptor.</param>
        /// <returns>The ordered parameter types and return type.</returns>
        /// <exception cref="FormatException">The method descriptor is malformed.</exception>
        internal static JavaMethodDescriptor ParseMethod(string descriptor)
        {
            if (descriptor.Length < 3 || descriptor[0] != '(')
            {
                throw Invalid(descriptor);
            }
            var position = 1;
            var parameters = new List<JavaTypeDescriptor>();
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
            return new JavaMethodDescriptor(parameters, returnType);
        }

        /// <summary>Gets the JVM override identity, which excludes the return descriptor.</summary>
        /// <param name="name">The exact Java method name.</param>
        /// <param name="descriptor">A validated complete method descriptor.</param>
        /// <returns>The method name followed by its JVM parameter descriptor.</returns>
        internal static string ParameterSignature(string name, string descriptor)
        {
            return name + descriptor.Substring(0, descriptor.IndexOf(')') + 1);
        }

        /// <summary>Reads one descriptor node, enforcing the JVM's 255 array-dimension limit.</summary>
        /// <param name="descriptor">The complete descriptor being read.</param>
        /// <param name="position">The current character offset, advanced past this node.</param>
        /// <param name="allowVoid">Whether this position permits a void return.</param>
        /// <param name="dimensions">The array nesting depth.</param>
        /// <returns>The parsed descriptor node.</returns>
        /// <exception cref="FormatException">The next descriptor node is malformed.</exception>
        private static JavaTypeDescriptor ParseType(string descriptor, ref int position, bool allowVoid, int dimensions)
        {
            if (position >= descriptor.Length)
            {
                throw Invalid(descriptor);
            }
            var code = descriptor[position++];
            switch (code)
            {
                case 'Z':
                case 'B':
                case 'C':
                case 'S':
                case 'I':
                case 'J':
                case 'F':
                case 'D':
                    return new JavaTypeDescriptor(code);
                case 'V':
                    if (allowVoid)
                    {
                        return new JavaTypeDescriptor(code);
                    }
                    break;
                case '[':
                    if (dimensions < 255)
                    {
                        return new JavaTypeDescriptor(code, element: ParseType(descriptor, ref position, false, dimensions + 1));
                    }
                    break;
                case 'L':
                    var end = descriptor.IndexOf(';', position);
                    if (end > position)
                    {
                        var name = descriptor.Substring(position, end - position);
                        if (name.IndexOfAny(new[] { '.', '[', '(', ')', ';' }) < 0
                            && !name.StartsWith("/", StringComparison.Ordinal)
                            && !name.EndsWith("/", StringComparison.Ordinal)
                            && name.IndexOf("//", StringComparison.Ordinal) < 0)
                        {
                            position = end + 1;
                            return new JavaTypeDescriptor(code, name.Replace('/', '.'));
                        }
                    }
                    break;
            }
            throw Invalid(descriptor);
        }

        /// <summary>Creates a diagnostic exception retaining the malformed descriptor.</summary>
        /// <param name="descriptor">The original malformed JVM descriptor.</param>
        /// <returns>The exception describing the metadata error.</returns>
        private static FormatException Invalid(string descriptor)
        {
            return new FormatException("Invalid JVM descriptor: " + descriptor);
        }
    }
}

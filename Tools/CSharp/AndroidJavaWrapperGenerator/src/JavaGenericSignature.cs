using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Describes a primitive, class, variable, array, or wildcard in a JVM generic signature.</summary>
    internal sealed class JavaGenericType
    {
        /// <summary>Gets the JVM type marker, including L, T, [, +, -, and *.</summary>
        internal char Kind { get; }

        /// <summary>Gets the binary class name, variable name, or primitive marker.</summary>
        internal string Name { get; }

        /// <summary>Gets the ordered generic arguments of the current class segment.</summary>
        internal IReadOnlyList<JavaGenericType> TypeArguments { get; }

        /// <summary>Gets the enclosing parameterized class segment, if present.</summary>
        internal JavaGenericType? DeclaringType { get; }

        /// <summary>Gets the array component or bounded wildcard type, if present.</summary>
        internal JavaGenericType? ElementType { get; }

        /// <summary>Creates one generic type node.</summary>
        /// <param name="kind">The JVM signature marker.</param>
        /// <param name="name">The class, variable, or primitive name.</param>
        /// <param name="typeArguments">The current class segment's type arguments.</param>
        /// <param name="declaringType">The enclosing class segment.</param>
        /// <param name="elementType">The array component or wildcard bound.</param>
        internal JavaGenericType(char kind, string name, IReadOnlyList<JavaGenericType>? typeArguments = null, JavaGenericType? declaringType = null, JavaGenericType? elementType = null)
        {
            Kind = kind;
            Name = name;
            TypeArguments = typeArguments ?? Array.Empty<JavaGenericType>();
            DeclaringType = declaringType;
            ElementType = elementType;
        }
    }

    /// <summary>Describes a generic variable and its ordered class/interface intersection bounds.</summary>
    internal sealed class JavaGenericParameter
    {
        /// <summary>Gets the Java variable identifier.</summary>
        internal string Name { get; }

        /// <summary>Gets all explicit bounds in JVM declaration order.</summary>
        internal IReadOnlyList<JavaGenericType> Bounds { get; }

        /// <summary>Creates a generic variable declaration.</summary>
        /// <param name="name">The Java variable identifier.</param>
        /// <param name="bounds">The ordered class and interface bounds.</param>
        internal JavaGenericParameter(string name, IReadOnlyList<JavaGenericType> bounds)
        {
            Name = name;
            Bounds = bounds;
        }
    }

    /// <summary>Contains the generic declaration and inheritance of a JVM class.</summary>
    internal sealed class JavaClassSignature
    {
        /// <summary>Gets the type variables declared by this class.</summary>
        internal IReadOnlyList<JavaGenericParameter> TypeParameters { get; }

        /// <summary>Gets the generic superclass reference.</summary>
        internal JavaGenericType SuperType { get; }

        /// <summary>Gets the generic implemented or extended interfaces.</summary>
        internal IReadOnlyList<JavaGenericType> Interfaces { get; }

        /// <summary>Creates a complete class signature.</summary>
        /// <param name="typeParameters">The declared generic variables.</param>
        /// <param name="superType">The superclass reference.</param>
        /// <param name="interfaces">The direct interface references.</param>
        internal JavaClassSignature(IReadOnlyList<JavaGenericParameter> typeParameters, JavaGenericType superType, IReadOnlyList<JavaGenericType> interfaces)
        {
            TypeParameters = typeParameters;
            SuperType = superType;
            Interfaces = interfaces;
        }
    }

    /// <summary>Contains the generic parameter, return, and throws contract of a JVM method.</summary>
    internal sealed class JavaMethodSignature
    {
        /// <summary>Gets method-local generic variables.</summary>
        internal IReadOnlyList<JavaGenericParameter> TypeParameters { get; }

        /// <summary>Gets the generic parameter types in source signature order.</summary>
        internal IReadOnlyList<JavaGenericType> ParameterTypes { get; }

        /// <summary>Gets the return type, including the V marker for constructors.</summary>
        internal JavaGenericType ReturnType { get; }

        /// <summary>Gets the generic declared exception types.</summary>
        internal IReadOnlyList<JavaGenericType> ThrowsTypes { get; }

        /// <summary>Creates a complete method signature.</summary>
        /// <param name="typeParameters">The method-local variables.</param>
        /// <param name="parameterTypes">The ordered parameter types.</param>
        /// <param name="returnType">The return type.</param>
        /// <param name="throwsTypes">The declared exceptions.</param>
        internal JavaMethodSignature(IReadOnlyList<JavaGenericParameter> typeParameters, IReadOnlyList<JavaGenericType> parameterTypes, JavaGenericType returnType, IReadOnlyList<JavaGenericType> throwsTypes)
        {
            TypeParameters = typeParameters;
            ParameterTypes = parameterTypes;
            ReturnType = returnType;
            ThrowsTypes = throwsTypes;
        }
    }

    /// <summary>Parses JVM Signature attributes without loading Java classes.</summary>
    internal sealed class JavaGenericSignature
    {
        /// <summary>Stores the complete generic signature.</summary>
        private readonly string _signature;

        /// <summary>Tracks the next signature character.</summary>
        private int _offset;

        /// <summary>Bounds recursive generic and array nesting.</summary>
        private int _depth;

        /// <summary>Creates a bounded signature parser.</summary>
        /// <param name="signature">The complete JVM generic signature.</param>
        private JavaGenericSignature(string signature)
        {
            _signature = signature;
        }

        /// <summary>Parses a class signature including all generic inheritance arguments.</summary>
        /// <param name="signature">The complete JVM class Signature attribute.</param>
        /// <returns>The parsed class declaration.</returns>
        /// <exception cref="InvalidDataException">The signature is malformed or excessively nested.</exception>
        internal static JavaClassSignature ParseClass(string signature)
        {
            var reader = new JavaGenericSignature(signature);
            var parameters = reader.ReadParameters();
            var superType = reader.ReadClass();
            var interfaces = new List<JavaGenericType>();
            while (reader._offset < signature.Length)
            {
                interfaces.Add(reader.ReadClass());
            }
            return new JavaClassSignature(parameters, superType, interfaces);
        }

        /// <summary>Parses a method signature including method-local bounds and generic throws declarations.</summary>
        /// <param name="signature">The complete JVM method Signature attribute.</param>
        /// <returns>The parsed method declaration.</returns>
        /// <exception cref="InvalidDataException">The signature is malformed or excessively nested.</exception>
        internal static JavaMethodSignature ParseMethod(string signature)
        {
            var reader = new JavaGenericSignature(signature);
            var parameters = reader.ReadParameters();
            reader.Expect('(');
            var arguments = new List<JavaGenericType>();
            while (reader.Peek() != ')')
            {
                arguments.Add(reader.ReadType(false));
            }
            reader.Expect(')');
            var returnType = reader.ReadType(true);
            var exceptions = new List<JavaGenericType>();
            while (reader._offset < signature.Length)
            {
                reader.Expect('^');
                var exception = reader.ReadType(false);
                if (exception.Kind != 'L' && exception.Kind != 'T')
                {
                    throw reader.Invalid();
                }
                exceptions.Add(exception);
            }
            return new JavaMethodSignature(parameters, arguments, returnType, exceptions);
        }

        /// <summary>Parses a single complete field or descriptor type.</summary>
        /// <param name="signature">The JVM type signature or descriptor.</param>
        /// <returns>The parsed reference, variable, primitive, or array type.</returns>
        /// <exception cref="InvalidDataException">The signature is malformed or has trailing characters.</exception>
        internal static JavaGenericType ParseType(string signature)
        {
            var reader = new JavaGenericSignature(signature);
            var result = reader.ReadType(false);
            if (reader._offset != signature.Length)
            {
                throw reader.Invalid();
            }
            return result;
        }

        /// <summary>Reads optional formal type parameters and their ordered bounds.</summary>
        /// <returns>The parsed formal variables.</returns>
        /// <exception cref="InvalidDataException">The variable list is malformed.</exception>
        private IReadOnlyList<JavaGenericParameter> ReadParameters()
        {
            var parameters = new List<JavaGenericParameter>();
            if (!Take('<'))
            {
                return parameters;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (Peek() != '>')
            {
                var start = _offset;
                while (Peek() != ':')
                {
                    ReadIdentifierCharacter();
                }
                var name = _signature.Substring(start, _offset - start);
                if (name.Length == 0 || !names.Add(name))
                {
                    throw Invalid();
                }
                Expect(':');
                var bounds = new List<JavaGenericType>();
                if (Peek() != ':')
                {
                    bounds.Add(ReadReference());
                }
                while (Take(':'))
                {
                    bounds.Add(ReadReference());
                }
                parameters.Add(new JavaGenericParameter(name, bounds));
            }
            Expect('>');
            if (parameters.Count == 0)
            {
                throw Invalid();
            }
            return parameters;
        }

        /// <summary>Reads one reference type suitable for a variable bound or type argument.</summary>
        /// <returns>The parsed reference type.</returns>
        /// <exception cref="InvalidDataException">The next type is primitive or void.</exception>
        private JavaGenericType ReadReference()
        {
            var result = ReadType(false);
            if (result.Kind != 'L' && result.Kind != 'T' && result.Kind != '[')
            {
                throw Invalid();
            }
            return result;
        }

        /// <summary>Reads one primitive, variable, class, or array type.</summary>
        /// <param name="allowVoid">Whether a scalar void type is permitted.</param>
        /// <returns>The parsed type node.</returns>
        /// <exception cref="InvalidDataException">The type is malformed or exceeds the nesting limit.</exception>
        private JavaGenericType ReadType(bool allowVoid)
        {
            if (++_depth > 256)
            {
                throw Invalid();
            }
            try
            {
                var marker = Peek();
                if (marker == 'L')
                {
                    return ReadClass();
                }
                _offset++;
                if (marker == '[')
                {
                    return new JavaGenericType('[', string.Empty, elementType: ReadType(false));
                }
                if (marker == 'T')
                {
                    var start = _offset;
                    while (Peek() != ';')
                    {
                        ReadIdentifierCharacter();
                    }
                    var name = _signature.Substring(start, _offset - start);
                    Expect(';');
                    if (name.Length == 0)
                    {
                        throw Invalid();
                    }
                    return new JavaGenericType('T', name);
                }
                if ("BCDFIJSZ".IndexOf(marker) >= 0 || (allowVoid && marker == 'V'))
                {
                    return new JavaGenericType(marker, marker.ToString());
                }
                throw Invalid();
            }
            finally
            {
                _depth--;
            }
        }

        /// <summary>Reads a class type and any parameterized enclosing class segments.</summary>
        /// <returns>The innermost class node with its complete binary name.</returns>
        /// <exception cref="InvalidDataException">The class name or its arguments are malformed.</exception>
        private JavaGenericType ReadClass()
        {
            Expect('L');
            var name = new StringBuilder();
            JavaGenericType? owner = null;
            while (true)
            {
                var start = _offset;
                while (Peek() != '<' && Peek() != '.' && Peek() != ';')
                {
                    var character = Peek();
                    if (character == '/' && (_offset == start || _signature[_offset - 1] == '/' || owner is not null))
                    {
                        throw Invalid();
                    }
                    if (character == '[' || character == ':' || character == '>' || character == '(' || character == ')')
                    {
                        throw Invalid();
                    }
                    _offset++;
                }
                if (_offset == start || _signature[_offset - 1] == '/')
                {
                    throw Invalid();
                }
                name.Append(_signature, start, _offset - start);
                var arguments = new List<JavaGenericType>();
                if (Take('<'))
                {
                    while (Peek() != '>')
                    {
                        var variance = Peek();
                        if (variance == '*')
                        {
                            _offset++;
                            arguments.Add(new JavaGenericType('*', string.Empty));
                        }
                        else if (variance == '+' || variance == '-')
                        {
                            _offset++;
                            arguments.Add(new JavaGenericType(variance, string.Empty, elementType: ReadReference()));
                        }
                        else
                        {
                            arguments.Add(ReadReference());
                        }
                    }
                    Expect('>');
                    if (arguments.Count == 0)
                    {
                        throw Invalid();
                    }
                }
                var result = new JavaGenericType('L', name.ToString().Replace('/', '.'), arguments, owner);
                if (Take(';'))
                {
                    return result;
                }
                Expect('.');
                owner = result;
                name.Append('$');
            }
        }

        /// <summary>Reads a legal unqualified identifier character.</summary>
        /// <exception cref="InvalidDataException">The character is a signature delimiter.</exception>
        private void ReadIdentifierCharacter()
        {
            if (".;[/<>:()^".IndexOf(Peek()) >= 0)
            {
                throw Invalid();
            }
            _offset++;
        }

        /// <summary>Returns the next character while rejecting truncated signatures.</summary>
        /// <returns>The next unconsumed character.</returns>
        /// <exception cref="InvalidDataException">The signature has ended unexpectedly.</exception>
        private char Peek()
        {
            if (_offset >= _signature.Length)
            {
                throw Invalid();
            }
            return _signature[_offset];
        }

        /// <summary>Consumes an optional signature delimiter.</summary>
        /// <param name="value">The expected delimiter.</param>
        /// <returns>Whether the delimiter was consumed.</returns>
        private bool Take(char value)
        {
            if (_offset < _signature.Length && _signature[_offset] == value)
            {
                _offset++;
                return true;
            }
            return false;
        }

        /// <summary>Consumes a required signature delimiter.</summary>
        /// <param name="value">The required delimiter.</param>
        /// <exception cref="InvalidDataException">The next character differs from the delimiter.</exception>
        private void Expect(char value)
        {
            if (!Take(value))
            {
                throw Invalid();
            }
        }

        /// <summary>Creates a bounded diagnostic for an invalid generic signature.</summary>
        /// <returns>The malformed-signature exception.</returns>
        private InvalidDataException Invalid()
        {
            return new InvalidDataException($"Invalid JVM generic signature at character {_offset}: '{_signature}'.");
        }
    }
}

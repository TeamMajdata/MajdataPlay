using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Reads Java declaration syntax while treating executable bodies as opaque balanced tokens.</summary>
    internal sealed class JavaSourceDeclarationReader
    {
        /// <summary>Stores the compilation unit being populated.</summary>
        private readonly JavaSourceUnit _unit;

        /// <summary>Stores translated, comment-free source tokens.</summary>
        private readonly IReadOnlyList<JavaSourceToken> _tokens;

        /// <summary>Stores the extraction cancellation token.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Tracks the next source token.</summary>
        private int _position;

        /// <summary>Tracks recursive generic and qualified type references.</summary>
        private int _referenceDepth;

        /// <summary>Tracks named member-type nesting.</summary>
        private int _typeDepth;

        /// <summary>Creates a declaration reader.</summary>
        /// <param name="path">The source diagnostic path.</param>
        /// <param name="text">The decoded Java source.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <exception cref="InvalidDataException">A lexical construct is malformed.</exception>
        private JavaSourceDeclarationReader(string path, string text, CancellationToken cancellationToken)
        {
            _unit = new JavaSourceUnit(path);
            _tokens = JavaSourceLexer.Tokenize(path, text, cancellationToken);
            _cancellationToken = cancellationToken;
        }

        /// <summary>Reads all named declarations in one compilation unit.</summary>
        /// <param name="path">The source diagnostic path.</param>
        /// <param name="text">The decoded source text.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <returns>The declaration tree with unresolved reference types.</returns>
        /// <exception cref="InvalidDataException">A declaration is malformed or unsupported.</exception>
        /// <exception cref="OperationCanceledException">Extraction is canceled.</exception>
        internal static JavaSourceUnit Read(string path, string text, CancellationToken cancellationToken)
        {
            return new JavaSourceDeclarationReader(path, text, cancellationToken).ReadUnit();
        }

        /// <summary>Reads package, imports, and top-level type declarations.</summary>
        /// <returns>The populated compilation unit.</returns>
        /// <exception cref="InvalidDataException">A top-level declaration is unsupported.</exception>
        private JavaSourceUnit ReadUnit()
        {
            SkipAnnotations();
            if (Take("package"))
            {
                _unit.PackageName = ReadQualifiedName();
                Expect(";");
            }
            while (Take("import"))
            {
                var isStatic = Take("static");
                var name = new StringBuilder(ReadIdentifier().Text);
                var wildcard = false;
                while (Take("."))
                {
                    if (Take("*"))
                    {
                        wildcard = true;
                        break;
                    }
                    name.Append('.').Append(ReadIdentifier().Text);
                }
                Expect(";");
                _unit.Imports.Add(new JavaSourceImport(name.ToString(), wildcard, isStatic));
            }
            while (Current.Kind != JavaSourceTokenKind.End)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (Take(";"))
                {
                    continue;
                }
                var modifiers = ReadModifiers();
                if (!IsTypeDeclaration())
                {
                    throw Invalid(Current, "unsupported top-level declaration; expected class, interface, @interface, enum, or record");
                }
                ReadType(null, modifiers);
            }
            return _unit;
        }

        /// <summary>Reads one named type, its hierarchy, and its declared members.</summary>
        /// <param name="enclosing">The lexical enclosing declaration.</param>
        /// <param name="modifiers">The consumed declaration modifiers.</param>
        /// <exception cref="InvalidDataException">A type declaration is malformed or unsupported.</exception>
        private void ReadType(JavaSourceType? enclosing, SourceModifiers modifiers)
        {
            if (++_typeDepth > 256)
            {
                throw Invalid(Current, "named type nesting exceeds 256 levels");
            }
            var kind = ReadToken().Text;
            if (kind == "@")
            {
                Expect("interface");
                kind = "annotation";
            }
            var token = ReadIdentifier();
            var name = enclosing is null
                ? (_unit.PackageName.Length == 0 ? token.Text : _unit.PackageName + "." + token.Text)
                : enclosing.Metadata.Name + "$" + token.Text;
            var flags = (ushort)(modifiers.Flags & 0x041F);
            var isInterface = kind == "interface" || kind == "annotation";
            if (isInterface)
            {
                flags |= 0x0600;
            }
            else
            {
                flags |= 0x0020;
            }
            if (kind == "annotation")
            {
                flags |= 0x2000;
            }
            if (kind == "enum")
            {
                flags |= 0x4010;
            }
            if (kind == "record")
            {
                flags |= 0x0010;
            }
            if (enclosing is not null)
            {
                if (enclosing.Metadata.IsInterface)
                {
                    flags |= 0x0009;
                }
                if (isInterface || kind == "enum" || kind == "record")
                {
                    flags |= 0x0008;
                }
            }
            var metadata = new JavaType
            {
                Name = name,
                AccessFlags = flags,
                EnclosingType = enclosing?.Metadata.Name,
                IsPublic = (flags & 0x0001) != 0 && (enclosing is null || enclosing.Metadata.IsPublic)
            };
            var type = new JavaSourceType(_unit, token, kind, enclosing, metadata);
            _unit.Types.Add(type);
            if (Is("<"))
            {
                type.TypeParameters.AddRange(ReadTypeParameters());
            }
            if (kind == "record")
            {
                type.Components.AddRange(ReadParameters(true));
            }
            if (Take("extends"))
            {
                if (isInterface)
                {
                    ReadReferenceList(type.Interfaces);
                }
                else if (kind == "class")
                {
                    type.SuperType = ReadReference(false);
                }
                else
                {
                    throw Invalid(Current, $"an explicit extends clause is unsupported for {kind}");
                }
            }
            if (Take("implements"))
            {
                if (isInterface)
                {
                    throw Invalid(Current, "interfaces must use extends rather than implements");
                }
                ReadReferenceList(type.Interfaces);
            }
            if (Take("permits"))
            {
                var permitted = new List<JavaSourceReference>();
                ReadReferenceList(permitted);
            }
            Expect("{");
            if (kind == "enum")
            {
                ReadEnumConstants(type);
            }
            while (!Take("}"))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (Current.Kind == JavaSourceTokenKind.End)
                {
                    throw Invalid(token, $"unterminated body of '{name}'");
                }
                if (Take(";"))
                {
                    continue;
                }
                var memberModifiers = ReadModifiers();
                if (IsTypeDeclaration())
                {
                    ReadType(type, memberModifiers);
                }
                else if (Is("{"))
                {
                    if ((memberModifiers.Flags & ~0x0008) != 0 || memberModifiers.IsDefault)
                    {
                        throw Invalid(Current, "unsupported initializer modifiers");
                    }
                    SkipBalanced();
                }
                else
                {
                    ReadMember(type, memberModifiers);
                }
            }
            if (kind == "enum")
            {
                foreach (var method in type.Methods)
                {
                    if ((method.AccessFlags & 0x0400) != 0)
                    {
                        metadata.AccessFlags = (ushort)((metadata.AccessFlags | 0x0400) & ~0x0010);
                    }
                }
            }
            _typeDepth--;
        }

        /// <summary>Reads enum constants without entering anonymous constant bodies.</summary>
        /// <param name="type">The enum declaration to populate.</param>
        /// <exception cref="InvalidDataException">The constant list is malformed.</exception>
        private void ReadEnumConstants(JavaSourceType type)
        {
            if (Take(";") || Is("}"))
            {
                return;
            }
            while (true)
            {
                SkipAnnotations();
                var token = ReadIdentifier();
                type.Fields.Add(new JavaSourceField(token, new JavaSourceReference(token, type.Metadata.Name), 0x4019));
                if (Is("("))
                {
                    SkipBalanced();
                }
                if (Is("{"))
                {
                    type.Metadata.AccessFlags = (ushort)(type.Metadata.AccessFlags & ~0x0010);
                    SkipBalanced();
                }
                if (Take(","))
                {
                    if (Is(";") || Is("}"))
                    {
                        break;
                    }
                    continue;
                }
                break;
            }
            if (!Take(";") && !Is("}"))
            {
                throw Invalid(Current, "expected ';' or '}' after enum constants");
            }
        }

        /// <summary>Distinguishes constructor, method, and field declarations.</summary>
        /// <param name="type">The owning type.</param>
        /// <param name="modifiers">The member modifiers.</param>
        /// <exception cref="InvalidDataException">A member declaration is unsupported or malformed.</exception>
        private void ReadMember(JavaSourceType type, SourceModifiers modifiers)
        {
            var genericParameters = Is("<") ? ReadTypeParameters() : new List<JavaSourceTypeParameter>();
            var constructor = Current.Text == type.Token.Text && (Peek(1).Text == "(" || (type.Kind == "record" && Peek(1).Text == "{"));
            if (constructor)
            {
                var name = ReadIdentifier();
                var method = new JavaSourceMethod(name, true, (ushort)(modifiers.Flags & 0x0D3F));
                method.TypeParameters.AddRange(genericParameters);
                if (type.Kind == "enum")
                {
                    if ((method.AccessFlags & 0x0005) != 0)
                    {
                        throw Invalid(name, "enum constructors cannot be public or protected");
                    }
                    method.AccessFlags |= 0x0002;
                }
                if (Is("{"))
                {
                    method.Parameters.AddRange(type.Components);
                    ApplyVarargs(method);
                    SkipBalanced();
                }
                else
                {
                    method.Parameters.AddRange(ReadParameters(false));
                    ApplyVarargs(method);
                    ReadThrows(method.ThrowsTypes);
                    ReadMethodEnd(false);
                }
                type.Methods.Add(method);
                return;
            }
            var reference = ReadReference(true);
            var identifier = ReadIdentifier();
            if (Is("("))
            {
                var method = new JavaSourceMethod(identifier, false, (ushort)(modifiers.Flags & 0x0D3F));
                method.TypeParameters.AddRange(genericParameters);
                method.Parameters.AddRange(ReadParameters(false));
                method.ReturnType = reference.AddDimensions(ReadArrayDimensions());
                ApplyVarargs(method);
                if (type.Metadata.IsInterface && (method.AccessFlags & 0x0002) == 0)
                {
                    method.AccessFlags |= 0x0001;
                    if (!modifiers.IsDefault && (method.AccessFlags & 0x0008) == 0)
                    {
                        method.AccessFlags |= 0x0400;
                    }
                }
                ReadThrows(method.ThrowsTypes);
                ReadMethodEnd(type.Kind == "annotation");
                type.Methods.Add(method);
            }
            else
            {
                if (genericParameters.Count != 0 || modifiers.IsDefault || reference.Name == "void")
                {
                    throw Invalid(identifier, "unsupported field declaration");
                }
                var flags = (ushort)(modifiers.Flags & 0x00DF);
                if (type.Metadata.IsInterface)
                {
                    flags |= 0x0019;
                }
                while (true)
                {
                    type.Fields.Add(new JavaSourceField(identifier, reference.AddDimensions(ReadArrayDimensions()), flags));
                    if (Take("="))
                    {
                        SkipExpression(true);
                    }
                    if (Take(","))
                    {
                        identifier = ReadIdentifier();
                        continue;
                    }
                    Expect(";");
                    break;
                }
            }
        }

        /// <summary>Marks the JVM variable-arity flag from the last ordinary parameter.</summary>
        /// <param name="method">The method declaration to update.</param>
        private static void ApplyVarargs(JavaSourceMethod method)
        {
            if (method.Parameters.Count != 0 && method.Parameters[method.Parameters.Count - 1].IsVarargs)
            {
                method.AccessFlags |= 0x0080;
            }
        }

        /// <summary>Consumes a method body, semicolon, or annotation default expression.</summary>
        /// <param name="annotation">Whether annotation defaults are permitted.</param>
        /// <exception cref="InvalidDataException">The terminator is malformed.</exception>
        private void ReadMethodEnd(bool annotation)
        {
            if (Take("default"))
            {
                if (!annotation)
                {
                    throw Invalid(Current, "a default value is only supported on annotation elements");
                }
                SkipExpression(false);
                Expect(";");
            }
            else if (Is("{"))
            {
                SkipBalanced();
            }
            else
            {
                Expect(";");
            }
        }

        /// <summary>Consumes exception declarations, which do not affect JVM descriptors.</summary>
        /// <exception cref="InvalidDataException">The throws clause is malformed.</exception>
        /// <param name="exceptions">The destination for the declared exception references.</param>
        private void ReadThrows(List<JavaSourceReference> exceptions)
        {
            if (Take("throws"))
            {
                ReadReferenceList(exceptions);
            }
        }

        /// <summary>Reads parameters or record components and discards receiver parameters.</summary>
        /// <param name="components">Whether this is a record header.</param>
        /// <returns>The ordered ordinary parameters.</returns>
        /// <exception cref="InvalidDataException">A parameter is malformed or unsupported.</exception>
        private List<JavaSourceParameter> ReadParameters(bool components)
        {
            Expect("(");
            var parameters = new List<JavaSourceParameter>();
            if (Take(")"))
            {
                return parameters;
            }
            while (true)
            {
                var modifiers = ReadModifiers();
                if ((modifiers.Flags & ~0x0010) != 0 || modifiers.IsDefault)
                {
                    throw Invalid(Current, "unsupported parameter modifiers");
                }
                var reference = ReadReference(false);
                SkipAnnotations();
                var varargs = Take("...");
                var token = Is("this") ? ReadToken() : ReadIdentifier();
                var receiver = token.Text == "this";
                if (Take("."))
                {
                    Expect("this");
                    receiver = true;
                }
                var dimensions = ReadArrayDimensions();
                if (receiver)
                {
                    if (components || varargs || dimensions != 0 || parameters.Count != 0)
                    {
                        throw Invalid(token, "unsupported receiver parameter");
                    }
                }
                else
                {
                    parameters.Add(new JavaSourceParameter(token, reference.AddDimensions(dimensions + (varargs ? 1 : 0)), varargs));
                }
                if (Take(")"))
                {
                    break;
                }
                if (varargs)
                {
                    throw Invalid(token, "variable-arity parameter must be last");
                }
                Expect(",");
            }
            return parameters;
        }

        /// <summary>Reads generic variables and their ordered intersection bounds.</summary>
        /// <returns>The generic variables in declaration order.</returns>
        /// <exception cref="InvalidDataException">The parameter list is malformed.</exception>
        private List<JavaSourceTypeParameter> ReadTypeParameters()
        {
            Expect("<");
            var parameters = new List<JavaSourceTypeParameter>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                SkipAnnotations();
                var token = ReadIdentifier();
                if (!names.Add(token.Text))
                {
                    throw Invalid(token, $"duplicate type parameter '{token.Text}'");
                }
                var parameter = new JavaSourceTypeParameter(token);
                if (Take("extends"))
                {
                    parameter.Bounds.Add(ReadReference(false));
                    while (Take("&"))
                    {
                        parameter.Bounds.Add(ReadReference(false));
                    }
                }
                parameters.Add(parameter);
                if (Take(">"))
                {
                    break;
                }
                Expect(",");
            }
            return parameters;
        }

        /// <summary>Reads primitive or source-qualified references while retaining generic arguments.</summary>
        /// <param name="allowVoid">Whether void is permitted.</param>
        /// <returns>The reference including type-level array dimensions.</returns>
        /// <exception cref="InvalidDataException">The reference is malformed or excessively nested.</exception>
        private JavaSourceReference ReadReference(bool allowVoid)
        {
            if (++_referenceDepth > 256)
            {
                throw Invalid(Current, "type-reference nesting exceeds 256 levels");
            }
            try
            {
                SkipAnnotations();
                var token = Current;
                if (IsPrimitive(token.Text) || token.Text == "void")
                {
                    ReadToken();
                    if (token.Text == "void" && !allowVoid)
                    {
                        throw Invalid(token, "void is not a parameter or field type");
                    }
                    var dimensions = ReadArrayDimensions();
                    if (token.Text == "void" && dimensions != 0)
                    {
                        throw Invalid(token, "void cannot be an array element");
                    }
                    return new JavaSourceReference(token, token.Text, dimensions);
                }
                var name = new StringBuilder(ReadIdentifier().Text);
                IReadOnlyList<JavaSourceReference> arguments = Is("<") ? ReadTypeArguments() : Array.Empty<JavaSourceReference>();
                JavaSourceReference? declaringType = null;
                while (Is(".") && Peek(1).Text != "this" && Peek(1).Text != "class")
                {
                    if (arguments.Count != 0 || declaringType is not null)
                    {
                        declaringType = new JavaSourceReference(token, name.ToString(), typeArguments: arguments, declaringType: declaringType);
                    }
                    ReadToken();
                    SkipAnnotations();
                    name.Append('.').Append(ReadIdentifier().Text);
                    arguments = Is("<") ? ReadTypeArguments() : Array.Empty<JavaSourceReference>();
                }
                return new JavaSourceReference(token, name.ToString(), ReadArrayDimensions(), arguments, declaringType);
            }
            finally
            {
                _referenceDepth--;
            }
        }

        /// <summary>Reads generic arguments, annotated wildcards, and nested parameterizations.</summary>
        /// <returns>The ordered type arguments and preserved wildcard bounds.</returns>
        /// <exception cref="InvalidDataException">The argument list is malformed.</exception>
        private IReadOnlyList<JavaSourceReference> ReadTypeArguments()
        {
            Expect("<");
            var arguments = new List<JavaSourceReference>();
            while (true)
            {
                SkipAnnotations();
                var token = Current;
                if (Take("?"))
                {
                    if (Take("extends"))
                    {
                        arguments.Add(new JavaSourceReference(token, "?", wildcard: '+', wildcardBound: ReadReference(false)));
                    }
                    else if (Take("super"))
                    {
                        arguments.Add(new JavaSourceReference(token, "?", wildcard: '-', wildcardBound: ReadReference(false)));
                    }
                    else
                    {
                        arguments.Add(new JavaSourceReference(token, "?", wildcard: '*'));
                    }
                }
                else
                {
                    arguments.Add(ReadReference(false));
                }
                if (Take(">"))
                {
                    break;
                }
                Expect(",");
            }
            return arguments;
        }

        /// <summary>Reads type-use annotations and trailing empty array brackets.</summary>
        /// <returns>The dimension count.</returns>
        /// <exception cref="InvalidDataException">An array declarator is malformed.</exception>
        private int ReadArrayDimensions()
        {
            var dimensions = 0;
            while (true)
            {
                SkipAnnotations();
                if (!Take("["))
                {
                    return dimensions;
                }
                Expect("]");
                if (++dimensions > 255)
                {
                    throw Invalid(Current, "array rank exceeds the JVM limit of 255");
                }
            }
        }

        /// <summary>Reads a comma-separated type-reference list.</summary>
        /// <param name="references">The list to populate.</param>
        /// <exception cref="InvalidDataException">The list is malformed.</exception>
        private void ReadReferenceList(List<JavaSourceReference> references)
        {
            references.Add(ReadReference(false));
            while (Take(","))
            {
                references.Add(ReadReference(false));
            }
        }

        /// <summary>Reads annotations and Java declaration modifiers.</summary>
        /// <returns>The JVM-relevant flags and default-method marker.</returns>
        /// <exception cref="InvalidDataException">Visibility modifiers conflict.</exception>
        private SourceModifiers ReadModifiers()
        {
            ushort flags = 0;
            var isDefault = false;
            while (true)
            {
                if (Is("@") && Peek(1).Text != "interface")
                {
                    SkipAnnotation();
                    continue;
                }
                var flag = ModifierFlag(Current.Text);
                if (flag != 0)
                {
                    var token = ReadToken();
                    if ((flag & 0x0007) != 0 && (flags & 0x0007) != 0)
                    {
                        throw Invalid(token, "conflicting visibility modifiers");
                    }
                    flags |= flag;
                }
                else if (Take("default"))
                {
                    isDefault = true;
                }
                else if (Take("sealed"))
                {
                    continue;
                }
                else if (Is("non") && Peek(1).Text == "-" && Peek(2).Text == "sealed")
                {
                    ReadToken();
                    ReadToken();
                    ReadToken();
                }
                else
                {
                    return new SourceModifiers(flags, isDefault);
                }
            }
        }

        /// <summary>Maps declaration modifiers to their context-dependent JVM bits.</summary>
        /// <param name="name">The modifier token.</param>
        /// <returns>The bit or zero for another token.</returns>
        private static ushort ModifierFlag(string name)
        {
            switch (name)
            {
                case "public":
                    return 0x0001;
                case "private":
                    return 0x0002;
                case "protected":
                    return 0x0004;
                case "static":
                    return 0x0008;
                case "final":
                    return 0x0010;
                case "synchronized":
                    return 0x0020;
                case "volatile":
                    return 0x0040;
                case "transient":
                    return 0x0080;
                case "native":
                    return 0x0100;
                case "abstract":
                    return 0x0400;
                case "strictfp":
                    return 0x0800;
                default:
                    return 0;
            }
        }

        /// <summary>Consumes annotations while preserving @interface declarations.</summary>
        /// <exception cref="InvalidDataException">An annotation is malformed.</exception>
        private void SkipAnnotations()
        {
            while (Is("@") && Peek(1).Text != "interface")
            {
                SkipAnnotation();
            }
        }

        /// <summary>Consumes a qualified annotation and its balanced arguments.</summary>
        /// <exception cref="InvalidDataException">The annotation is malformed.</exception>
        private void SkipAnnotation()
        {
            Expect("@");
            ReadQualifiedName();
            if (Is("("))
            {
                SkipBalanced();
            }
        }

        /// <summary>Skips an initializer or annotation default without parsing expressions.</summary>
        /// <param name="stopAtComma">Whether a top-level comma terminates a field initializer.</param>
        /// <exception cref="InvalidDataException">The expression has mismatched or missing delimiters.</exception>
        private void SkipExpression(bool stopAtComma)
        {
            var start = Current;
            if (Is(";") || (stopAtComma && Is(",")))
            {
                throw Invalid(start, "empty declaration expression");
            }
            while (!Is(";") && !(stopAtComma && Is(",")))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (Current.Kind == JavaSourceTokenKind.End || Is("}"))
                {
                    throw Invalid(start, "unterminated declaration expression");
                }
                if (Is("(") || Is("[") || Is("{"))
                {
                    SkipBalanced();
                }
                else if (Is("<") && TrySkipExpressionTypeArguments())
                {
                    continue;
                }
                else if (Is(")") || Is("]"))
                {
                    throw Invalid(Current, "mismatched expression delimiter");
                }
                else
                {
                    ReadToken();
                }
            }
        }

        /// <summary>Recognizes expression generic spans so their commas do not split fields.</summary>
        /// <returns>Whether a generic argument span was consumed.</returns>
        private bool TrySkipExpressionTypeArguments()
        {
            var depth = 0;
            for (var index = _position; index < _tokens.Count; index++)
            {
                if ((index & 255) == 0)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                }
                var token = _tokens[index];
                if (token.Text == "<")
                {
                    depth++;
                }
                else if (token.Text == ">")
                {
                    if (--depth == 0)
                    {
                        var next = index + 1 < _tokens.Count ? _tokens[index + 1] : token;
                        var explicitMethod = _position > 0 && _tokens[_position - 1].Text == "." && next.Kind == JavaSourceTokenKind.Identifier;
                        if (next.Text == "(" || next.Text == "." || next.Text == "[" || next.Text == ":" || explicitMethod)
                        {
                            _position = index + 1;
                            return true;
                        }
                        return false;
                    }
                }
                else if (token.Kind == JavaSourceTokenKind.End || token.Text == ";" || token.Text == "{" || token.Text == "}"
                    || token.Text == "=" || token.Text == "+" || token.Text == "-" || token.Text == "|" || token.Text == "!")
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>Skips a balanced parenthesis, bracket, or brace group.</summary>
        /// <exception cref="InvalidDataException">A group is unterminated or mismatched.</exception>
        private void SkipBalanced()
        {
            var start = Current;
            var closers = new Stack<string>();
            var closer = ClosingDelimiter(start.Text);
            if (closer is null)
            {
                throw Invalid(start, "expected a balanced group");
            }
            closers.Push(closer);
            ReadToken();
            while (closers.Count != 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var token = ReadToken();
                if (token.Kind == JavaSourceTokenKind.End)
                {
                    throw Invalid(start, "unterminated balanced group");
                }
                var nested = ClosingDelimiter(token.Text);
                if (nested is not null)
                {
                    closers.Push(nested);
                }
                else if (token.Text == ")" || token.Text == "]" || token.Text == "}")
                {
                    if (closers.Pop() != token.Text)
                    {
                        throw Invalid(token, "mismatched group delimiter");
                    }
                }
            }
        }

        /// <summary>Gets the closing token for an opening delimiter.</summary>
        /// <param name="opening">The opening punctuation.</param>
        /// <returns>The closer, or null for another token.</returns>
        private static string? ClosingDelimiter(string opening)
        {
            switch (opening)
            {
                case "(":
                    return ")";
                case "[":
                    return "]";
                case "{":
                    return "}";
                default:
                    return null;
            }
        }

        /// <summary>Reads a dot-separated Java identifier sequence.</summary>
        /// <returns>The qualified name.</returns>
        /// <exception cref="InvalidDataException">An identifier is missing.</exception>
        private string ReadQualifiedName()
        {
            var name = new StringBuilder(ReadIdentifier().Text);
            while (Take("."))
            {
                name.Append('.').Append(ReadIdentifier().Text);
            }
            return name.ToString();
        }

        /// <summary>Checks for a supported named Java declaration.</summary>
        /// <returns>Whether a supported type declaration follows.</returns>
        private bool IsTypeDeclaration()
        {
            return Is("class") || Is("interface") || Is("enum") || Is("record") || (Is("@") && Peek(1).Text == "interface");
        }

        /// <summary>Checks Java primitive keywords other than void.</summary>
        /// <param name="name">The type token.</param>
        /// <returns>Whether this is a primitive.</returns>
        private static bool IsPrimitive(string name)
        {
            return name == "boolean" || name == "byte" || name == "char" || name == "short"
                || name == "int" || name == "long" || name == "float" || name == "double";
        }

        /// <summary>Reads an identifier excluding reserved keywords.</summary>
        /// <returns>The identifier token.</returns>
        /// <exception cref="InvalidDataException">An identifier is absent.</exception>
        private JavaSourceToken ReadIdentifier()
        {
            var token = Current;
            if (token.Kind != JavaSourceTokenKind.Identifier || IsReserved(token.Text))
            {
                throw Invalid(token, $"expected an identifier, found '{token.Text}'");
            }
            return ReadToken();
        }

        /// <summary>Checks reserved tokens while allowing contextual declaration names.</summary>
        /// <param name="name">The token spelling.</param>
        /// <returns>Whether this is a reserved keyword or literal.</returns>
        private static bool IsReserved(string name)
        {
            switch (name)
            {
                case "abstract":
                case "assert":
                case "boolean":
                case "break":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "class":
                case "const":
                case "continue":
                case "default":
                case "do":
                case "double":
                case "else":
                case "enum":
                case "extends":
                case "final":
                case "finally":
                case "float":
                case "for":
                case "goto":
                case "if":
                case "implements":
                case "import":
                case "instanceof":
                case "int":
                case "interface":
                case "long":
                case "native":
                case "new":
                case "package":
                case "private":
                case "protected":
                case "public":
                case "return":
                case "short":
                case "static":
                case "strictfp":
                case "super":
                case "switch":
                case "synchronized":
                case "this":
                case "throw":
                case "throws":
                case "transient":
                case "try":
                case "void":
                case "volatile":
                case "while":
                case "true":
                case "false":
                case "null":
                case "_":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Gets the current token, including the stable end token.</summary>
        private JavaSourceToken Current => _tokens[_position];

        /// <summary>Reads a token without advancing past the end.</summary>
        /// <returns>The previous current token.</returns>
        private JavaSourceToken ReadToken()
        {
            var token = Current;
            if (token.Kind != JavaSourceTokenKind.End)
            {
                _position++;
            }
            return token;
        }

        /// <summary>Looks ahead without passing the end.</summary>
        /// <param name="distance">The token distance.</param>
        /// <returns>The lookahead token.</returns>
        private JavaSourceToken Peek(int distance)
        {
            return _tokens[Math.Min(_position + distance, _tokens.Count - 1)];
        }

        /// <summary>Checks current punctuation or a keyword.</summary>
        /// <param name="text">The expected token spelling.</param>
        /// <returns>Whether it matches.</returns>
        private bool Is(string text)
        {
            return Current.Kind != JavaSourceTokenKind.Literal && Current.Text == text;
        }

        /// <summary>Consumes a matching token.</summary>
        /// <param name="text">The expected token spelling.</param>
        /// <returns>Whether a token was consumed.</returns>
        private bool Take(string text)
        {
            if (!Is(text))
            {
                return false;
            }
            ReadToken();
            return true;
        }

        /// <summary>Requires and consumes punctuation or a keyword.</summary>
        /// <param name="text">The expected token spelling.</param>
        /// <exception cref="InvalidDataException">The token is absent.</exception>
        private void Expect(string text)
        {
            if (!Take(text))
            {
                throw Invalid(Current, $"expected '{text}', found '{Current.Text}'");
            }
        }

        /// <summary>Creates a declaration-located extraction error.</summary>
        /// <param name="token">The contextual token.</param>
        /// <param name="message">The diagnostic detail.</param>
        /// <returns>The extraction error.</returns>
        private InvalidDataException Invalid(JavaSourceToken token, string message)
        {
            return new InvalidDataException($"Java source '{_unit.Path}' ({token.Line},{token.Column}): {message}.");
        }

        /// <summary>Contains JVM-relevant declaration flags and the default marker.</summary>
        private readonly struct SourceModifiers
        {
            /// <summary>Gets the modifier bits before context filtering.</summary>
            internal ushort Flags { get; }

            /// <summary>Gets whether the default modifier appeared.</summary>
            internal bool IsDefault { get; }

            /// <summary>Creates a modifier set.</summary>
            /// <param name="flags">The JVM-relevant bits.</param>
            /// <param name="isDefault">Whether default appeared.</param>
            internal SourceModifiers(ushort flags, bool isDefault)
            {
                Flags = flags;
                IsDefault = isDefault;
            }
        }
    }
}

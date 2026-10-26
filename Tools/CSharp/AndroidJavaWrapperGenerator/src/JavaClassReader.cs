using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Reads JVM declarations directly without loading or initializing target classes.</summary>
    internal sealed class JavaClassReader
    {
        /// <summary>Limits the uncompressed bytes accepted for one class file.</summary>
        internal const int MaxClassBytes = 64 * 1024 * 1024;

        /// <summary>Identifies flags supplied by an InnerClasses declaration.</summary>
        private const ushort NestedFlags = 0x761F;

        /// <summary>Contains the bounded class-file bytes.</summary>
        private readonly byte[] _bytes;

        /// <summary>Identifies the input in malformed-class diagnostics.</summary>
        private readonly string _source;

        /// <summary>Allows long constant pools and member tables to be canceled.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Contains decoded CONSTANT_Utf8 values by constant-pool index.</summary>
        private string?[] _text = Array.Empty<string?>();

        /// <summary>Contains CONSTANT_Class name indices by constant-pool index.</summary>
        private ushort[] _classes = Array.Empty<ushort>();

        /// <summary>Contains constant-pool tags for checked reference lookup.</summary>
        private byte[] _tags = Array.Empty<byte>();

        /// <summary>Tracks the next byte to consume.</summary>
        private int _offset;

        /// <summary>Creates a reader over an already bounded input.</summary>
        /// <param name="bytes">The complete class-file bytes.</param>
        /// <param name="source">The file or archive entry used in diagnostics.</param>
        /// <param name="cancellationToken">The scan cancellation token.</param>
        private JavaClassReader(byte[] bytes, string source, CancellationToken cancellationToken)
        {
            _bytes = bytes;
            _source = source;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Parses a complete class, retaining nonpublic declarations and compiler flags.</summary>
        /// <param name="bytes">The uncompressed class-file bytes.</param>
        /// <param name="source">The originating file or archive entry.</param>
        /// <param name="cancellationToken">The scan cancellation token.</param>
        /// <returns>The declared type and nesting metadata needed to resolve its visibility.</returns>
        /// <exception cref="InvalidDataException">The input is oversized, truncated, or malformed.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        internal static JavaClassInfo Read(byte[] bytes, string source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes.Length > MaxClassBytes)
            {
                throw new InvalidDataException($"Class '{source}' exceeds the 64 MiB class-file limit.");
            }
            return new JavaClassReader(bytes, source, cancellationToken).ReadClass();
        }

        /// <summary>Parses the class header, constant pool, members, and relevant attributes.</summary>
        /// <returns>The complete declaration and its nesting table.</returns>
        /// <exception cref="InvalidDataException">A class-file structure is invalid.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private JavaClassInfo ReadClass()
        {
            if (ReadUInt32() != 0xCAFEBABE)
            {
                throw Invalid("the JVM magic number is missing");
            }
            ReadUInt16();
            var version = ReadUInt16();
            if (version < 45)
            {
                throw Invalid($"unsupported class-file major version {version}");
            }
            ReadConstantPool();
            var type = new JavaType { AccessFlags = ReadUInt16() };
            type.Name = ClassName(ReadUInt16());
            var superIndex = ReadUInt16();
            type.SuperName = superIndex == 0 ? null : ClassName(superIndex);
            var interfaceCount = ReadUInt16();
            for (var index = 0; index < interfaceCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                type.Interfaces.Add(ClassName(ReadUInt16()));
            }
            var fieldCount = ReadUInt16();
            for (var index = 0; index < fieldCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                type.Fields.Add(ReadField());
            }
            var methodCount = ReadUInt16();
            for (var index = 0; index < methodCount; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                type.Methods.Add(ReadMethod());
            }
            var result = new JavaClassInfo(type);
            ReadClassAttributes(result);
            if (_offset != _bytes.Length)
            {
                throw Invalid("unexpected bytes follow the class attributes");
            }
            if (result.Nesting.TryGetValue(type.Name, out var ownNesting))
            {
                type.AccessFlags = (ushort)((type.AccessFlags & ~NestedFlags) | (ownNesting.AccessFlags & NestedFlags) | (type.AccessFlags & 0x1000));
                type.EnclosingType ??= ownNesting.EnclosingType;
            }
            type.IsPublic = (type.AccessFlags & 0x0001) != 0;
            return result;
        }

        /// <summary>Reads all standard JVM constant-pool tags, including dynamic and module constants.</summary>
        /// <exception cref="InvalidDataException">The pool is truncated or contains an unknown tag.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private void ReadConstantPool()
        {
            var count = ReadUInt16();
            if (count == 0)
            {
                throw Invalid("constant_pool_count is zero");
            }
            _text = new string?[count];
            _classes = new ushort[count];
            _tags = new byte[count];
            for (var index = 1; index < count; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var tag = ReadByte();
                _tags[index] = tag;
                switch (tag)
                {
                    case 1:
                        _text[index] = ReadModifiedUtf8(ReadUInt16());
                        break;
                    case 3:
                    case 4:
                        Skip(4);
                        break;
                    case 5:
                    case 6:
                        Skip(8);
                        index++;
                        if (index >= count)
                        {
                            throw Invalid("a two-slot constant overruns the constant pool");
                        }
                        break;
                    case 7:
                        _classes[index] = ReadUInt16();
                        break;
                    case 8:
                    case 16:
                    case 19:
                    case 20:
                        Skip(2);
                        break;
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 17:
                    case 18:
                        Skip(4);
                        break;
                    case 15:
                        Skip(3);
                        break;
                    default:
                        throw Invalid($"unknown constant-pool tag {tag} at index {index}");
                }
            }
        }

        /// <summary>Reads a field declaration and preserves legacy Synthetic attributes.</summary>
        /// <returns>The declared field metadata.</returns>
        /// <exception cref="InvalidDataException">The declaration or an attribute is malformed.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private JavaField ReadField()
        {
            var field = new JavaField
            {
                AccessFlags = ReadUInt16(),
                Name = Text(ReadUInt16()),
                Descriptor = Text(ReadUInt16())
            };
            ValidateDescriptor(field.Descriptor, false);
            var count = ReadUInt16();
            for (var index = 0; index < count; index++)
            {
                var attribute = ReadAttribute();
                if (attribute.Name == "Synthetic")
                {
                    RequireLength(attribute, 0);
                    field.AccessFlags |= 0x1000;
                }
                else if (attribute.Name == "Signature")
                {
                    field.Signature = ReadSignature(attribute, field.Signature);
                    JavaGenericSignature.ParseType(field.Signature);
                }
                _offset = attribute.End;
            }
            return field;
        }

        /// <summary>Reads a method and its descriptor-ordered MethodParameters names.</summary>
        /// <returns>The method, including constructors, bridges, and synthetic flags.</returns>
        /// <exception cref="InvalidDataException">The declaration or parameter table is malformed.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private JavaMethod ReadMethod()
        {
            var method = new JavaMethod
            {
                AccessFlags = ReadUInt16(),
                Name = Text(ReadUInt16()),
                Descriptor = Text(ReadUInt16())
            };
            var parameterCount = ValidateDescriptor(method.Descriptor, true);
            var count = ReadUInt16();
            var hasParameters = false;
            for (var index = 0; index < count; index++)
            {
                var attribute = ReadAttribute();
                if (attribute.Name == "Synthetic")
                {
                    RequireLength(attribute, 0);
                    method.AccessFlags |= 0x1000;
                }
                else if (attribute.Name == "MethodParameters")
                {
                    if (hasParameters || attribute.Length < 1)
                    {
                        throw Invalid("a MethodParameters attribute is duplicated or truncated");
                    }
                    hasParameters = true;
                    var namesCount = ReadByte();
                    RequireLength(attribute, 1 + (4 * namesCount));
                    if (namesCount != parameterCount)
                    {
                        throw Invalid($"MethodParameters count does not match '{method.Name}{method.Descriptor}'");
                    }
                    for (var parameter = 0; parameter < namesCount; parameter++)
                    {
                        var nameIndex = ReadUInt16();
                        method.ParameterNames.Add(nameIndex == 0 ? string.Empty : Text(nameIndex));
                        ReadUInt16();
                    }
                }
                else if (attribute.Name == "Signature")
                {
                    method.Signature = ReadSignature(attribute, method.Signature);
                    JavaGenericSignature.ParseMethod(method.Signature);
                }
                _offset = attribute.End;
            }
            return method;
        }

        /// <summary>Reads visibility and lexical enclosure attributes while skipping implementation data.</summary>
        /// <param name="result">The type and nesting table to populate.</param>
        /// <exception cref="InvalidDataException">An attribute has invalid references or size.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private void ReadClassAttributes(JavaClassInfo result)
        {
            var count = ReadUInt16();
            var hasInnerClasses = false;
            var hasEnclosingMethod = false;
            for (var index = 0; index < count; index++)
            {
                var attribute = ReadAttribute();
                if (attribute.Name == "InnerClasses")
                {
                    if (hasInnerClasses || attribute.Length < 2)
                    {
                        throw Invalid("an InnerClasses attribute is duplicated or truncated");
                    }
                    hasInnerClasses = true;
                    var nestingCount = ReadUInt16();
                    RequireLength(attribute, 2 + (8 * nestingCount));
                    for (var entry = 0; entry < nestingCount; entry++)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        var name = ClassName(ReadUInt16());
                        var outerIndex = ReadUInt16();
                        var simpleNameIndex = ReadUInt16();
                        var flags = ReadUInt16();
                        if (simpleNameIndex != 0)
                        {
                            Text(simpleNameIndex);
                        }
                        var nesting = new JavaInnerClassInfo(outerIndex == 0 ? null : ClassName(outerIndex), flags);
                        if (!result.Nesting.TryAdd(name, nesting))
                        {
                            throw Invalid($"InnerClasses repeats '{name}'");
                        }
                    }
                }
                else if (attribute.Name == "EnclosingMethod")
                {
                    if (hasEnclosingMethod)
                    {
                        throw Invalid("the EnclosingMethod attribute is duplicated");
                    }
                    hasEnclosingMethod = true;
                    RequireLength(attribute, 4);
                    result.Type.EnclosingType = ClassName(ReadUInt16());
                    var methodIndex = ReadUInt16();
                    if (methodIndex != 0 && (methodIndex >= _tags.Length || _tags[methodIndex] != 12))
                    {
                        throw Invalid("EnclosingMethod references an invalid name-and-type constant");
                    }
                }
                else if (attribute.Name == "Synthetic")
                {
                    RequireLength(attribute, 0);
                    result.Type.AccessFlags |= 0x1000;
                }
                else if (attribute.Name == "Signature")
                {
                    result.Type.Signature = ReadSignature(attribute, result.Type.Signature);
                    JavaGenericSignature.ParseClass(result.Type.Signature);
                }
                _offset = attribute.End;
            }
        }

        /// <summary>Reads one unique constant-pool-backed generic Signature attribute.</summary>
        /// <param name="attribute">The checked attribute header.</param>
        /// <param name="previous">A previously read signature, if present.</param>
        /// <returns>The exact generic signature text.</returns>
        /// <exception cref="InvalidDataException">The signature is duplicated or malformed.</exception>
        private string ReadSignature((string Name, int Length, int End) attribute, string? previous)
        {
            RequireLength(attribute, 2);
            if (previous is not null)
            {
                throw Invalid("the Signature attribute is duplicated");
            }
            return Text(ReadUInt16());
        }

        /// <summary>Checks field or method descriptor syntax without resolving referenced classes.</summary>
        /// <param name="descriptor">The JVM descriptor to validate.</param>
        /// <param name="isMethod">Whether the descriptor represents a method.</param>
        /// <returns>The number of method parameters, or zero for a field.</returns>
        /// <exception cref="InvalidDataException">The descriptor is malformed.</exception>
        private int ValidateDescriptor(string descriptor, bool isMethod)
        {
            var offset = 0;
            var count = 0;
            if (isMethod)
            {
                if (descriptor.Length == 0 || descriptor[offset++] != '(')
                {
                    throw Invalid($"invalid method descriptor '{descriptor}'");
                }
                while (offset < descriptor.Length && descriptor[offset] != ')')
                {
                    ReadDescriptorType(descriptor, ref offset, false);
                    count++;
                    if (count > 255)
                    {
                        throw Invalid("a method descriptor has more than 255 parameters");
                    }
                }
                if (offset >= descriptor.Length || descriptor[offset++] != ')')
                {
                    throw Invalid($"invalid method descriptor '{descriptor}'");
                }
            }
            ReadDescriptorType(descriptor, ref offset, isMethod);
            if (offset != descriptor.Length)
            {
                throw Invalid($"invalid descriptor '{descriptor}'");
            }
            return count;
        }

        /// <summary>Consumes one primitive, reference, array, or permitted void descriptor.</summary>
        /// <param name="descriptor">The complete field or method descriptor.</param>
        /// <param name="offset">The next character, advanced beyond the parsed type.</param>
        /// <param name="allowVoid">Whether a scalar void return type is permitted.</param>
        /// <exception cref="InvalidDataException">The type descriptor is malformed.</exception>
        private void ReadDescriptorType(string descriptor, ref int offset, bool allowVoid)
        {
            var dimensions = 0;
            while (offset < descriptor.Length && descriptor[offset] == '[')
            {
                offset++;
                dimensions++;
            }
            if (offset >= descriptor.Length || dimensions > 255)
            {
                throw Invalid($"invalid type descriptor '{descriptor}'");
            }
            var kind = descriptor[offset++];
            if (kind == 'L')
            {
                var end = descriptor.IndexOf(';', offset);
                if (end < 0)
                {
                    throw Invalid($"unterminated reference descriptor '{descriptor}'");
                }
                ValidateInternalName(descriptor.Substring(offset, end - offset));
                offset = end + 1;
            }
            else if ("BCDFIJSZ".IndexOf(kind) < 0 && !(allowVoid && dimensions == 0 && kind == 'V'))
            {
                throw Invalid($"invalid type descriptor '{descriptor}'");
            }
        }

        /// <summary>Reads a bounded attribute header.</summary>
        /// <returns>The attribute name, byte length, and absolute end offset.</returns>
        /// <exception cref="InvalidDataException">The header or body exceeds the class file.</exception>
        /// <exception cref="OperationCanceledException">The scan has been canceled.</exception>
        private (string Name, int Length, int End) ReadAttribute()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var name = Text(ReadUInt16());
            var length = ReadUInt32();
            if (length > (uint)(_bytes.Length - _offset))
            {
                throw Invalid($"attribute '{name}' is truncated");
            }
            return (name, (int)length, _offset + (int)length);
        }

        /// <summary>Checks the exact length of a parsed attribute.</summary>
        /// <param name="attribute">The attribute header.</param>
        /// <param name="expected">The required body length.</param>
        /// <exception cref="InvalidDataException">The attribute length is inconsistent.</exception>
        private void RequireLength((string Name, int Length, int End) attribute, int expected)
        {
            if (attribute.Length != expected)
            {
                throw Invalid($"attribute '{attribute.Name}' has length {attribute.Length}, expected {expected}");
            }
        }

        /// <summary>Resolves a class constant to a dot-separated binary declaration name.</summary>
        /// <param name="index">The CONSTANT_Class pool index.</param>
        /// <returns>The binary name with nested dollar signs preserved.</returns>
        /// <exception cref="InvalidDataException">The reference or name is invalid.</exception>
        private string ClassName(ushort index)
        {
            if (index == 0 || index >= _tags.Length || _tags[index] != 7)
            {
                throw Invalid($"invalid class constant index {index}");
            }
            var name = Text(_classes[index]);
            ValidateInternalName(name);
            return name.Replace('/', '.');
        }

        /// <summary>Checks that a declaration name contains valid nonempty JVM package components.</summary>
        /// <param name="name">The slash-separated internal class name.</param>
        /// <exception cref="InvalidDataException">The name is empty, an array, or malformed.</exception>
        private void ValidateInternalName(string name)
        {
            if (name.Length == 0 || name[0] == '/' || name[name.Length - 1] == '/'
                || name.Contains("//", StringComparison.Ordinal) || name.IndexOfAny(new[] { '.', ';', '[', '\0', '\r', '\n', '\\' }) >= 0)
            {
                throw Invalid($"invalid declared internal class name '{name}'");
            }
        }

        /// <summary>Resolves a checked CONSTANT_Utf8 entry.</summary>
        /// <param name="index">The UTF-8 constant-pool index.</param>
        /// <returns>The exact decoded UTF-16 string.</returns>
        /// <exception cref="InvalidDataException">The reference is not a UTF-8 constant.</exception>
        private string Text(ushort index)
        {
            if (index == 0 || index >= _text.Length || _text[index] is not string value)
            {
                throw Invalid($"invalid UTF-8 constant index {index}");
            }
            return value;
        }

        /// <summary>Decodes JVM modified UTF-8, preserving encoded NULs and UTF-16 surrogate units.</summary>
        /// <param name="length">The number of bytes in the UTF-8 constant.</param>
        /// <returns>The decoded UTF-16 value.</returns>
        /// <exception cref="InvalidDataException">The input is truncated or has an invalid encoding.</exception>
        private string ReadModifiedUtf8(int length)
        {
            EnsureBytes(length);
            var end = _offset + length;
            var text = new StringBuilder(length);
            while (_offset < end)
            {
                var first = _bytes[_offset++];
                if (first > 0 && first < 0x80)
                {
                    text.Append((char)first);
                }
                else if ((first & 0xE0) == 0xC0 && _offset < end)
                {
                    var second = _bytes[_offset++];
                    var value = ((first & 0x1F) << 6) | (second & 0x3F);
                    if ((second & 0xC0) != 0x80 || (value < 0x80 && !(first == 0xC0 && second == 0x80)))
                    {
                        throw Invalid("a CONSTANT_Utf8 entry has malformed modified UTF-8");
                    }
                    text.Append((char)value);
                }
                else if ((first & 0xF0) == 0xE0 && (end - _offset) >= 2)
                {
                    var second = _bytes[_offset++];
                    var third = _bytes[_offset++];
                    var value = ((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F);
                    if ((second & 0xC0) != 0x80 || (third & 0xC0) != 0x80 || value < 0x800)
                    {
                        throw Invalid("a CONSTANT_Utf8 entry has malformed modified UTF-8");
                    }
                    text.Append((char)value);
                }
                else
                {
                    throw Invalid("a CONSTANT_Utf8 entry has malformed modified UTF-8");
                }
            }
            return text.ToString();
        }

        /// <summary>Reads one bounded unsigned byte.</summary>
        /// <returns>The next byte.</returns>
        /// <exception cref="InvalidDataException">The class file is truncated.</exception>
        private byte ReadByte()
        {
            EnsureBytes(1);
            return _bytes[_offset++];
        }

        /// <summary>Reads an unsigned big-endian 16-bit value.</summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">The class file is truncated.</exception>
        private ushort ReadUInt16()
        {
            EnsureBytes(2);
            var result = (ushort)((_bytes[_offset] << 8) | _bytes[_offset + 1]);
            _offset += 2;
            return result;
        }

        /// <summary>Reads an unsigned big-endian 32-bit value.</summary>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">The class file is truncated.</exception>
        private uint ReadUInt32()
        {
            return ((uint)ReadUInt16() << 16) | ReadUInt16();
        }

        /// <summary>Skips bounded constant-pool data.</summary>
        /// <param name="count">The number of bytes to skip.</param>
        /// <exception cref="InvalidDataException">The class file is truncated.</exception>
        private void Skip(int count)
        {
            EnsureBytes(count);
            _offset += count;
        }

        /// <summary>Checks that an exact number of bytes remains.</summary>
        /// <param name="count">The number of bytes required.</param>
        /// <exception cref="InvalidDataException">The class file is truncated.</exception>
        private void EnsureBytes(int count)
        {
            if (count < 0 || count > (_bytes.Length - _offset))
            {
                throw Invalid($"the class file is truncated at byte {_offset}");
            }
        }

        /// <summary>Creates a malformed-class diagnostic with the original input location.</summary>
        /// <param name="reason">The specific structure or encoding error.</param>
        /// <returns>The exception to report to the CLI caller.</returns>
        private InvalidDataException Invalid(string reason)
        {
            return new InvalidDataException($"Invalid Java class '{_source}': {reason}.");
        }
    }

    /// <summary>Keeps a declaration and its enclosing-chain hints together during scanning.</summary>
    internal sealed class JavaClassInfo
    {
        /// <summary>Gets the parsed type declaration.</summary>
        internal JavaType Type { get; }

        /// <summary>Gets all InnerClasses entries, including enclosing types absent from the inputs.</summary>
        internal Dictionary<string, JavaInnerClassInfo> Nesting { get; } = new Dictionary<string, JavaInnerClassInfo>(StringComparer.Ordinal);

        /// <summary>Creates metadata for a parsed declaration.</summary>
        /// <param name="type">The declared class or interface.</param>
        internal JavaClassInfo(JavaType type)
        {
            Type = type;
        }
    }

    /// <summary>Records original nested declaration flags and its direct enclosing binary name.</summary>
    internal sealed class JavaInnerClassInfo
    {
        /// <summary>Gets the enclosing class, or null for a local or anonymous declaration.</summary>
        internal string? EnclosingType { get; }

        /// <summary>Gets the original source declaration flags.</summary>
        internal ushort AccessFlags { get; }

        /// <summary>Creates one checked InnerClasses entry.</summary>
        /// <param name="enclosingType">The direct enclosing binary name.</param>
        /// <param name="accessFlags">The nested class access flags.</param>
        internal JavaInnerClassInfo(string? enclosingType, ushort accessFlags)
        {
            EnclosingType = enclosingType;
            AccessFlags = accessFlags;
        }
    }
}

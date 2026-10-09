#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Reads a class file's declared binary name to infer its classpath root.</summary>
    internal static class JavaClassFile
    {
        /// <summary>Resolves a class file's root without guessing its declared package.</summary>
        /// <param name="path">The absolute path to a .class file.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The directory from which the declared binary name can be loaded.</returns>
        /// <exception cref="GeneratorException">The file is malformed or is not under its declared package path.</exception>
        /// <exception cref="IOException">The class file cannot be read.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        internal static string GetClassPathRoot(string path, CancellationToken cancellationToken)
        {
            var binaryName = ReadInternalName(path, cancellationToken);
            var suffix = binaryName.Replace('/', Path.DirectorySeparatorChar) + ".class";
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.EndsWith(suffix, comparison))
            {
                throw Invalid(path, "the declared class '" + binaryName.Replace('/', '.') + "' requires a path ending in '" + suffix + "'");
            }
            var rootLength = path.Length - suffix.Length;
            if (rootLength <= 0 || !IsSeparator(path[rootLength - 1]))
            {
                throw Invalid(path, "the path does not match the declared package boundary");
            }
            var root = Path.GetFullPath(path.Substring(0, rootLength));
            if (!Directory.Exists(root))
            {
                throw Invalid(path, "the inferred classpath root does not exist");
            }
            return root;
        }

        /// <summary>Reads the internal JVM name referenced by this_class.</summary>
        /// <param name="path">The class file to inspect.</param>
        /// <param name="cancellationToken">The compilation cancellation token.</param>
        /// <returns>The slash-separated internal class name.</returns>
        /// <exception cref="GeneratorException">The class file is invalid.</exception>
        /// <exception cref="IOException">The class file cannot be read.</exception>
        /// <exception cref="OperationCanceledException">Compilation has been canceled.</exception>
        private static string ReadInternalName(string path, CancellationToken cancellationToken)
        {
            if (new FileInfo(path).Length > 64L * 1024 * 1024)
            {
                throw Invalid(path, "the class file exceeds the 64 MiB inspection limit");
            }
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                try
                {
                    if (ReadUInt32(reader) != 0xCAFEBABE)
                    {
                        throw Invalid(path, "the JVM magic number is missing");
                    }
                    ReadUInt16(reader);
                    ReadUInt16(reader);
                    var count = ReadUInt16(reader);
                    if (count < 2)
                    {
                        throw Invalid(path, "the constant pool is empty");
                    }
                    var text = new string?[count];
                    var classes = new ushort[count];
                    for (var index = 1; index < count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        switch (reader.ReadByte())
                        {
                            case 1:
                                text[index] = DecodeModifiedUtf8(ReadBytes(reader, ReadUInt16(reader)), path);
                                break;
                            case 3:
                            case 4:
                                ReadBytes(reader, 4);
                                break;
                            case 5:
                            case 6:
                                ReadBytes(reader, 8);
                                index++;
                                if (index >= count)
                                {
                                    throw Invalid(path, "a two-slot constant overruns the constant pool");
                                }
                                break;
                            case 7:
                                classes[index] = ReadUInt16(reader);
                                break;
                            case 8:
                            case 16:
                            case 19:
                            case 20:
                                ReadBytes(reader, 2);
                                break;
                            case 9:
                            case 10:
                            case 11:
                            case 12:
                            case 17:
                            case 18:
                                ReadBytes(reader, 4);
                                break;
                            case 15:
                                ReadBytes(reader, 3);
                                break;
                            default:
                                throw Invalid(path, "an unsupported constant-pool tag was encountered");
                        }
                    }
                    ReadUInt16(reader);
                    var thisClass = ReadUInt16(reader);
                    if (thisClass == 0 || thisClass >= count)
                    {
                        throw Invalid(path, "this_class is not a valid constant-pool index");
                    }
                    var nameIndex = classes[thisClass];
                    var name = nameIndex > 0 && nameIndex < count ? text[nameIndex] : null;
                    if (name == null || name.IndexOf('.') >= 0 || !JavaDescriptors.IsBinaryName(name.Replace('/', '.')))
                    {
                        throw Invalid(path, "this_class does not refer to a valid declared class name");
                    }
                    return name;
                }
                catch (EndOfStreamException)
                {
                    throw Invalid(path, "the class file is truncated");
                }
            }
        }

        /// <summary>Decodes JVM modified UTF-8, including encoded NULs and surrogate code units.</summary>
        /// <param name="bytes">The bytes of a CONSTANT_Utf8 entry.</param>
        /// <param name="path">The class file used in diagnostic messages.</param>
        /// <returns>The decoded UTF-16 string.</returns>
        /// <exception cref="GeneratorException">The modified UTF-8 encoding is malformed.</exception>
        private static string DecodeModifiedUtf8(byte[] bytes, string path)
        {
            var result = new StringBuilder(bytes.Length);
            for (var index = 0; index < bytes.Length; index++)
            {
                var first = bytes[index];
                if (first > 0 && first < 128)
                {
                    result.Append((char)first);
                }
                else if ((first & 0xE0) == 0xC0 && index + 1 < bytes.Length)
                {
                    var second = bytes[++index];
                    var value = ((first & 0x1F) << 6) | (second & 0x3F);
                    if ((second & 0xC0) != 0x80 || (value != 0 && value < 128))
                    {
                        throw Invalid(path, "a CONSTANT_Utf8 entry is malformed");
                    }
                    result.Append((char)value);
                }
                else if ((first & 0xF0) == 0xE0 && index + 2 < bytes.Length)
                {
                    var second = bytes[++index];
                    var third = bytes[++index];
                    var value = ((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F);
                    if ((second & 0xC0) != 0x80 || (third & 0xC0) != 0x80 || value < 2048)
                    {
                        throw Invalid(path, "a CONSTANT_Utf8 entry is malformed");
                    }
                    result.Append((char)value);
                }
                else
                {
                    throw Invalid(path, "a CONSTANT_Utf8 entry is malformed");
                }
            }
            return result.ToString();
        }

        /// <summary>Reads an unsigned big-endian 16-bit value.</summary>
        /// <param name="reader">The reader positioned at the value.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="EndOfStreamException">The input is truncated.</exception>
        private static ushort ReadUInt16(BinaryReader reader)
        {
            return (ushort)((reader.ReadByte() << 8) | reader.ReadByte());
        }

        /// <summary>Reads an unsigned big-endian 32-bit value.</summary>
        /// <param name="reader">The reader positioned at the value.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="EndOfStreamException">The input is truncated.</exception>
        private static uint ReadUInt32(BinaryReader reader)
        {
            return ((uint)ReadUInt16(reader) << 16) | ReadUInt16(reader);
        }

        /// <summary>Reads an exact number of bytes.</summary>
        /// <param name="reader">The class-file reader.</param>
        /// <param name="count">The number of bytes to consume.</param>
        /// <returns>The consumed bytes.</returns>
        /// <exception cref="EndOfStreamException">The input is truncated.</exception>
        private static byte[] ReadBytes(BinaryReader reader, int count)
        {
            var bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
            {
                throw new EndOfStreamException();
            }
            return bytes;
        }

        /// <summary>Checks a filesystem package-path boundary.</summary>
        /// <param name="character">The character before the declared package suffix.</param>
        /// <returns>Whether it is a filesystem directory separator.</returns>
        private static bool IsSeparator(char character)
        {
            return character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;
        }

        /// <summary>Creates a class-file configuration diagnostic.</summary>
        /// <param name="path">The invalid class file.</param>
        /// <param name="reason">The specific validation failure.</param>
        /// <returns>The failure to report.</returns>
        private static GeneratorException Invalid(string path, string reason)
        {
            return new GeneratorException(GeneratorDiagnostics.InvalidConfiguration, "Cannot infer a classpath for '" + path + "': " + reason + ".");
        }
    }
}

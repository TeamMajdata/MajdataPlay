using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Converts JVM names and literals into deterministic, portable C# tokens.</summary>
    internal static class JavaNames
    {
        /// <summary>Contains reserved and contextual identifiers that need verbatim escaping.</summary>
        private static readonly HashSet<string> s_keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
            "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
            "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
            "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
            "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
            "add", "alias", "and", "ascending", "async", "await", "by", "descending", "dynamic", "equals",
            "from", "get", "global", "group", "init", "into", "join", "let", "nameof", "not", "notnull",
            "on", "or", "orderby", "partial", "record", "remove", "select", "set", "unmanaged", "value",
            "var", "when", "where", "with", "yield", "__arglist", "__makeref", "__reftype", "__refvalue"
        };

        /// <summary>Gets the package portion of a JVM binary name.</summary>
        /// <param name="binaryName">The dot-separated binary name, retaining nested dollar signs.</param>
        /// <returns>The Java package, or an empty string for the unnamed package.</returns>
        internal static string Package(string binaryName)
        {
            var separator = binaryName.LastIndexOf('.');
            return separator < 0 ? string.Empty : binaryName.Substring(0, separator);
        }

        /// <summary>Builds a package namespace below the configured root's Bindings namespace.</summary>
        /// <param name="rootNamespace">The C# namespace containing the generated runtime types.</param>
        /// <param name="package">The dot-separated Java package.</param>
        /// <returns>The C# namespace with PascalCase package components.</returns>
        internal static string Namespace(string rootNamespace, string package)
        {
            var bindingsNamespace = rootNamespace + ".Bindings";
            if (package.Length == 0)
            {
                return bindingsNamespace;
            }
            var components = package.Split('.');
            for (var index = 0; index < components.Length; index++)
            {
                components[index] = PascalCase(components[index]);
            }
            return bindingsNamespace + "." + string.Join(".", components);
        }

        /// <summary>Proposes a standalone name; the emitter scopes nested declarations in their parent wrappers.</summary>
        /// <param name="binaryName">The original JVM binary name.</param>
        /// <returns>The proposed wrapper name, including the unique JavaObject root spelling.</returns>
        internal static string TypeName(string binaryName)
        {
            if (binaryName == "java.lang.Object")
            {
                return "JavaObject";
            }
            var simpleName = binaryName.Substring(binaryName.LastIndexOf('.') + 1);
            var components = simpleName.Split('$');
            for (var index = 0; index < components.Length; index++)
            {
                components[index] = PascalCase(components[index]);
            }
            return string.Join("_", components);
        }

        /// <summary>Converts a Java member or package component into a valid PascalCase identifier.</summary>
        /// <param name="value">The original identifier.</param>
        /// <returns>An ASCII C# identifier with deterministic escaping for unsupported characters.</returns>
        internal static string PascalCase(string value)
        {
            var hasLowercase = false;
            foreach (var character in value)
            {
                if (character >= 'a' && character <= 'z')
                {
                    hasLowercase = true;
                    break;
                }
            }
            if (!hasLowercase)
            {
                value = value.ToLowerInvariant();
            }

            var result = new StringBuilder();
            var capitalize = true;
            foreach (var character in value)
            {
                if (character == '_' || character == '$')
                {
                    capitalize = true;
                    continue;
                }
                if (IsAsciiLetter(character) || character >= '0' && character <= '9')
                {
                    if (result.Length == 0 && character >= '0' && character <= '9')
                    {
                        result.Append('_');
                    }
                    result.Append(capitalize ? char.ToUpperInvariant(character) : character);
                    capitalize = false;
                }
                else
                {
                    result.Append("_u");
                    result.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    capitalize = true;
                }
            }
            return result.Length == 0 ? "JavaName" : result.ToString();
        }

        /// <summary>Produces a parameter identifier while preserving recognizable Java names.</summary>
        /// <param name="value">The optional Java parameter name.</param>
        /// <param name="index">The zero-based descriptor position used for fallback names.</param>
        /// <returns>A valid camelCase identifier, escaped when it is a C# keyword.</returns>
        internal static string ParameterName(string value, int index)
        {
            if (string.IsNullOrEmpty(value) || value.StartsWith("__", StringComparison.Ordinal))
            {
                return "argument" + index.ToString(CultureInfo.InvariantCulture);
            }
            var result = new StringBuilder();
            foreach (var character in value)
            {
                if (IsAsciiLetter(character) || character == '_' || character >= '0' && character <= '9')
                {
                    if (result.Length == 0 && character >= '0' && character <= '9')
                    {
                        result.Append('_');
                    }
                    result.Append(result.Length == 0 ? char.ToLowerInvariant(character) : character);
                }
                else
                {
                    result.Append("_u");
                    result.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            var identifier = result.ToString();
            return s_keywords.Contains(identifier) ? "@" + identifier : identifier;
        }

        /// <summary>Creates a stable suffix independent of process hash randomization.</summary>
        /// <param name="value">The complete JVM identity being disambiguated.</param>
        /// <returns>A sixteen-digit hexadecimal FNV-1a suffix.</returns>
        internal static string StableSuffix(string value)
        {
            var hash = 14695981039346656037UL;
            foreach (var character in value)
            {
                unchecked
                {
                    hash = (hash ^ character) * 1099511628211UL;
                }
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>Removes annotations that do not distinguish C# overload signatures.</summary>
        /// <param name="typeName">A fully qualified, nullable C# type.</param>
        /// <returns>The normalized overload identity.</returns>
        internal static string SignatureType(string typeName)
        {
            return typeName.Replace("global::", string.Empty).Replace("?", string.Empty);
        }

        /// <summary>Quotes JVM metadata without allowing it to inject generated C# syntax.</summary>
        /// <param name="value">The exact unescaped metadata text.</param>
        /// <returns>A regular C# string literal.</returns>
        internal static string StringLiteral(string value)
        {
            var result = new StringBuilder("\"");
            foreach (var character in value)
            {
                switch (character)
                {
                    case '"':
                        result.Append("\\\"");
                        break;
                    case '\\':
                        result.Append("\\\\");
                        break;
                    default:
                        if (character < ' ' || character > '~')
                        {
                            result.Append("\\u");
                            result.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            result.Append(character);
                        }
                        break;
                }
            }
            result.Append('"');
            return result.ToString();
        }

        /// <summary>Escapes metadata included in XML documentation text or attributes.</summary>
        /// <param name="value">The unescaped metadata.</param>
        /// <returns>Well-formed XML character data.</returns>
        internal static string Xml(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        /// <summary>Checks the portable Latin letters accepted by emitted identifiers.</summary>
        /// <param name="character">The character to inspect.</param>
        /// <returns>Whether the character is an ASCII letter.</returns>
        private static bool IsAsciiLetter(char character)
        {
            return character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
        }
    }
}

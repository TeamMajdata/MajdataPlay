#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Maps erased Java descriptor types to nullable C# signatures and raw JNI result types.</summary>
    internal static class JavaTypeMapping
    {
        /// <summary>Identifies the Unity reference type used for unmapped Java objects.</summary>
        internal const string JavaObjectType = "global::UnityEngine.AndroidJavaObject";

        /// <summary>Identifies the descriptor-based runtime invocation helper.</summary>
        internal const string JniType = "global::MajdataPlay.Platform.Android.AndroidJni";

        /// <summary>Maps a descriptor to a C# type, including every nullable jagged-array dimension.</summary>
        /// <param name="type">The parsed Java descriptor type.</param>
        /// <param name="mappings">The annotated binary-name to qualified-wrapper mapping.</param>
        /// <param name="raw">Whether wrapper leaves must remain Unity Java references.</param>
        /// <returns>The C# signature type.</returns>
        /// <exception cref="GeneratorException">The parsed type is unsupported or incomplete.</exception>
        internal static string GetTypeName(JavaTypeReference type, IReadOnlyDictionary<string, string> mappings, bool raw = false)
        {
            switch (type.Code)
            {
                case 'V':
                    return "void";
                case 'Z':
                    return "bool";
                case 'B':
                    return "sbyte";
                case 'C':
                    return "char";
                case 'S':
                    return "short";
                case 'I':
                    return "int";
                case 'J':
                    return "long";
                case 'F':
                    return "float";
                case 'D':
                    return "double";
                case 'L':
                    if (type.Name == "java.lang.String")
                    {
                        return "string?";
                    }
                    if (!raw && mappings.TryGetValue(type.Name, out var wrapper))
                    {
                        return wrapper + "?";
                    }
                    return JavaObjectType + "?";
                case '[':
                    if (type.Element != null)
                    {
                        return GetTypeName(type.Element, mappings, raw) + "[]?";
                    }
                    break;
            }
            throw new GeneratorException(GeneratorDiagnostics.InvalidMetadata, "Unsupported parsed Java type '" + type.Code + "'.");
        }

        /// <summary>Wraps declared reference results while preserving null arrays and null wrapper leaves.</summary>
        /// <param name="type">The declared result descriptor type.</param>
        /// <param name="expression">The raw JNI result expression.</param>
        /// <param name="mappings">The annotated binary-name mappings.</param>
        /// <param name="depth">The array depth used for unique lambda parameter names.</param>
        /// <returns>The result expression with any required recursive wrapping.</returns>
        internal static string WrapResult(JavaTypeReference type, string expression, IReadOnlyDictionary<string, string> mappings, int depth = 0)
        {
            if (type.Code == 'L' && type.Name != "java.lang.String" && mappings.TryGetValue(type.Name, out var wrapper))
            {
                var parameter = "javaValue" + depth.ToString(CultureInfo.InvariantCulture);
                return JniType + ".Wrap<" + wrapper + ">(" + expression + ", " + parameter + " => new " + wrapper + "(" + parameter + ", ownsReference: true))";
            }
            if (type.Code == '[' && type.Element != null && ContainsWrapper(type.Element, mappings))
            {
                var parameter = "javaElement" + depth.ToString(CultureInfo.InvariantCulture);
                return JniType + ".MapArray<" + GetTypeName(type.Element, mappings, true) + ", " + GetTypeName(type.Element, mappings) +
                    ">(" + expression + ", " + parameter + " => " + WrapResult(type.Element, parameter, mappings, depth + 1) + ")";
            }
            return expression;
        }

        /// <summary>Checks whether a reference or jagged array contains an annotated wrapper leaf.</summary>
        /// <param name="type">The declared result type.</param>
        /// <param name="mappings">The wrapper mappings.</param>
        /// <returns>Whether recursive wrapper conversion is needed.</returns>
        private static bool ContainsWrapper(JavaTypeReference type, IReadOnlyDictionary<string, string> mappings)
        {
            if (type.Code == '[' && type.Element != null)
            {
                return ContainsWrapper(type.Element, mappings);
            }
            return type.Code == 'L' && type.Name != "java.lang.String" && mappings.ContainsKey(type.Name);
        }
    }

    /// <summary>Converts Java identifiers to deterministic C# member and parameter identifiers.</summary>
    internal static class JavaNames
    {
        /// <summary>Converts Java camelCase and UPPER_SNAKE names to PascalCase.</summary>
        /// <param name="name">The original Java identifier.</param>
        /// <returns>A valid C# identifier without a keyword escape.</returns>
        internal static string PascalCase(string name)
        {
            var result = new StringBuilder();
            var tokens = name.Split(new[] { '_', '$' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var letters = token.Where(char.IsLetter).ToArray();
                var uppercase = letters.Length != 0 && letters.All(char.IsUpper);
                for (var index = 0; index < token.Length; index++)
                {
                    var character = token[index];
                    if (!SyntaxFacts.IsIdentifierPartCharacter(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format)
                    {
                        continue;
                    }
                    result.Append(index == 0 ? char.ToUpperInvariant(character) : uppercase ? char.ToLowerInvariant(character) : character);
                }
            }
            if (result.Length == 0)
            {
                result.Append("JavaMember");
            }
            if (!SyntaxFacts.IsIdentifierStartCharacter(result[0]))
            {
                result.Insert(0, "Java");
            }
            return result.ToString();
        }

        /// <summary>Converts a Java parameter name to camelCase, or creates a stable positional fallback.</summary>
        /// <param name="name">The Java parameter name, when available.</param>
        /// <param name="index">The zero-based descriptor parameter position.</param>
        /// <returns>A valid unescaped C# parameter name.</returns>
        internal static string Parameter(string name, int index)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "argument" + index.ToString(CultureInfo.InvariantCulture);
            }
            var identifier = PascalCase(name);
            return char.ToLowerInvariant(identifier[0]) + identifier.Substring(1);
        }

        /// <summary>Escapes reserved and contextual C# keywords.</summary>
        /// <param name="identifier">The valid unescaped identifier.</param>
        /// <returns>The identifier as it must appear in generated C#.</returns>
        internal static string Escape(string identifier)
        {
            return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
                ? "@" + identifier : identifier;
        }

        /// <summary>Normalizes C# type names for signature collision checking.</summary>
        /// <param name="typeName">The generated or symbol-displayed type name.</param>
        /// <returns>The type name without reference-nullability or global qualification.</returns>
        internal static string SignatureType(string typeName)
        {
            return typeName.Replace("global::", string.Empty).Replace("?", string.Empty).Replace(" ", string.Empty);
        }
    }

    /// <summary>Formats exact C# literals for JVM compile-time primitive and String constants.</summary>
    internal static class JavaLiterals
    {
        /// <summary>Quotes a string without allowing metadata to inject C# tokens or line breaks.</summary>
        /// <param name="value">The unescaped string value.</param>
        /// <returns>The exact C# string literal.</returns>
        internal static string String(string value)
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
                    case '\0':
                        result.Append("\\0");
                        break;
                    case '\r':
                        result.Append("\\r");
                        break;
                    case '\n':
                        result.Append("\\n");
                        break;
                    case '\t':
                        result.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(character) || char.IsSurrogate(character) || character == '\u2028' || character == '\u2029')
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

        /// <summary>Formats a Java constant after verifying its declared primitive or String type.</summary>
        /// <param name="field">The field's constant metadata.</param>
        /// <param name="type">The parsed field descriptor.</param>
        /// <returns>The exact C# constant expression.</returns>
        /// <exception cref="GeneratorException">The constant does not match its JVM descriptor.</exception>
        internal static string Constant(JavaApiField field, JavaTypeReference type)
        {
            var value = field.ConstantValue;
            var kind = field.ConstantKind.ToLowerInvariant();
            if (value == null)
            {
                throw Invalid(field);
            }
            if (type.Code == 'L' && type.Name == "java.lang.String" && (kind == "string" || kind == "java.lang.string"))
            {
                return String(value);
            }
            if (type.Code == 'Z' && (kind == "boolean" || kind == "bool") && bool.TryParse(value, out var boolean))
            {
                return boolean ? "true" : "false";
            }
            if (type.Code == 'C' && kind == "char")
            {
                int code;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && numeric >= 0 && numeric <= 65535)
                {
                    code = numeric;
                }
                else if (value.Length == 1)
                {
                    code = value[0];
                }
                else
                {
                    throw Invalid(field);
                }
                return "'\\u" + code.ToString("x4", CultureInfo.InvariantCulture) + "'";
            }
            if ((type.Code == 'B' && kind == "byte") || (type.Code == 'S' && kind == "short") ||
                (type.Code == 'I' && kind == "int") || (type.Code == 'J' && kind == "long"))
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    throw Invalid(field);
                }
                if ((type.Code == 'B' && (integer < sbyte.MinValue || integer > sbyte.MaxValue)) ||
                    (type.Code == 'S' && (integer < short.MinValue || integer > short.MaxValue)) ||
                    (type.Code == 'I' && (integer < int.MinValue || integer > int.MaxValue)))
                {
                    throw Invalid(field);
                }
                return integer.ToString(CultureInfo.InvariantCulture) + (type.Code == 'J' ? "L" : string.Empty);
            }
            if ((type.Code == 'F' && kind == "float") || (type.Code == 'D' && kind == "double"))
            {
                return Floating(value, type.Code == 'F', field);
            }
            throw Invalid(field);
        }

        /// <summary>Formats floating-point constants including signed zero, NaN, and infinities.</summary>
        /// <param name="value">The Java floating-point text.</param>
        /// <param name="single">Whether the constant has float rather than double precision.</param>
        /// <param name="field">The originating field for diagnostics.</param>
        /// <returns>An exact C# floating-point constant expression.</returns>
        /// <exception cref="GeneratorException">The text is not a valid Java floating-point value.</exception>
        private static string Floating(string value, bool single, JavaApiField field)
        {
            var type = single ? "float" : "double";
            if (value == "NaN")
            {
                return type + ".NaN";
            }
            if (value == "Infinity" || value == "+Infinity")
            {
                return type + ".PositiveInfinity";
            }
            if (value == "-Infinity")
            {
                return type + ".NegativeInfinity";
            }
            if (single)
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || float.IsInfinity(parsed) || float.IsNaN(parsed))
                {
                    throw Invalid(field);
                }
                return parsed == 0 && value.StartsWith("-", StringComparison.Ordinal) ? "-0.0F" : parsed.ToString("R", CultureInfo.InvariantCulture) + "F";
            }
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || double.IsInfinity(number) || double.IsNaN(number))
            {
                throw Invalid(field);
            }
            return number == 0 && value.StartsWith("-", StringComparison.Ordinal) ? "-0.0D" : number.ToString("R", CultureInfo.InvariantCulture) + "D";
        }

        /// <summary>Creates a constant-specific metadata validation failure.</summary>
        /// <param name="field">The invalid constant field.</param>
        /// <returns>The reportable failure.</returns>
        private static GeneratorException Invalid(JavaApiField field)
        {
            return new GeneratorException(GeneratorDiagnostics.InvalidMetadata,
                "Java constant '" + field.DeclaringType + "." + field.Name + "' (" + field.Descriptor + ") has incompatible kind/value '" +
                field.ConstantKind + "/" + field.ConstantValue + "'.");
        }
    }

    /// <summary>Builds CRLF C# source with four-space indentation and escaped XML documentation.</summary>
    internal sealed class GeneratedSource
    {
        /// <summary>Stores the generated source text.</summary>
        private readonly StringBuilder _text = new StringBuilder();

        /// <summary>Stores the current four-space indentation depth.</summary>
        private int _depth;

        /// <summary>Appends an indented source line.</summary>
        /// <param name="line">The source text, or an empty line.</param>
        internal void Line(string line = "")
        {
            if (line.Length != 0)
            {
                _text.Append(' ', _depth * 4);
                _text.Append(line);
            }
            _text.Append("\r\n");
        }

        /// <summary>Opens a multiline statement or declaration block.</summary>
        internal void Open()
        {
            Line("{");
            _depth++;
        }

        /// <summary>Closes a multiline statement or declaration block.</summary>
        internal void Close()
        {
            _depth--;
            Line("}");
        }

        /// <summary>Appends escaped plain-text XML documentation with safe line prefixes.</summary>
        /// <param name="tag">The trusted documentation tag name.</param>
        /// <param name="text">The plain-text documentation content.</param>
        /// <param name="attributes">Trusted tag attributes with already escaped values.</param>
        internal void Documentation(string tag, string text, string attributes = "")
        {
            Line("/// <" + tag + attributes + ">");
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                Line("/// " + EscapeXml(line));
            }
            Line("/// </" + tag + ">");
        }

        /// <summary>Escapes a plain-text XML documentation value.</summary>
        /// <param name="value">The plain-text value.</param>
        /// <returns>The XML-safe value.</returns>
        internal static string EscapeXml(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        /// <summary>Returns the completed generated source.</summary>
        /// <returns>The complete CRLF source text.</returns>
        public override string ToString()
        {
            return _text.ToString();
        }
    }
}

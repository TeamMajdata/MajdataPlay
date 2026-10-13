using System;
using System.Globalization;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Writes official plain-text Java descriptions as safe C# XML documentation comments.</summary>
    internal static class WrapperDocumentation
    {
        /// <summary>Explains the nullable representation used for Java reference results.</summary>
        private const string NullableReturnNote = "The binding represents a null Java reference as null, including array references and reference elements.";

        /// <summary>Explains the nullable representation used for Java reference arguments.</summary>
        private const string NullableParameterNote = "The binding accepts null for a null Java reference.";

        /// <summary>Writes an official summary or the existing binding description when no summary is available.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="documentation">The optional official description of the actual declaration.</param>
        /// <param name="fallbackXml">The escaped XML binding description to use without official prose.</param>
        internal static void WriteSummary(Action<string> writeLine, JavaDocumentation? documentation, string fallbackXml)
        {
            if (string.IsNullOrWhiteSpace(documentation?.Summary))
            {
                WriteXmlLine(writeLine, "<summary>" + fallbackXml + "</summary>");
                return;
            }
            WriteTextElement(writeLine, "summary", string.Empty, documentation!.Summary);
        }

        /// <summary>Writes Java behavior and exception descriptions as remarks, followed by official attribution.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="documentation">The optional official description, including Java exception prose.</param>
        internal static void WriteDetails(Action<string> writeLine, JavaDocumentation? documentation)
        {
            if (documentation is null)
            {
                return;
            }
            if (!string.IsNullOrWhiteSpace(documentation.Remarks))
            {
                WriteTextElement(writeLine, "remarks", string.Empty, documentation.Remarks);
            }
            if (!string.IsNullOrWhiteSpace(documentation.SourceUrl))
            {
                var sourceUrl = NormalizeText(documentation.SourceUrl).Replace('\n', ' ');
                WriteXmlLine(writeLine, "<seealso href=\"" + JavaNames.Xml(sourceUrl) + "\" />");
            }
        }

        /// <summary>Gets an original Java parameter name aligned to the actual JVM descriptor.</summary>
        /// <param name="method">The method metadata and optional restored source names.</param>
        /// <param name="parameterCount">The authoritative parameter count from the JVM descriptor.</param>
        /// <param name="index">The zero-based parameter position in that descriptor.</param>
        /// <returns>The documented Java name, the metadata name, or an empty fallback name.</returns>
        internal static string OriginalParameterName(JavaMethod method, int parameterCount, int index)
        {
            var documentation = method.Documentation;
            if (documentation is not null && documentation.ParameterNames.Count != 0)
            {
                var offset = method.Name == "<init>" ? Math.Max(0, parameterCount - documentation.ParameterNames.Count) : 0;
                var sourceIndex = index - offset;
                if (sourceIndex >= 0 && sourceIndex < documentation.ParameterNames.Count)
                {
                    return documentation.ParameterNames[sourceIndex];
                }
            }
            return index < method.ParameterNames.Count ? method.ParameterNames[index] : string.Empty;
        }

        /// <summary>Documents one final C# argument using the original Java parameter at the same descriptor position.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="name">The allocated C# identifier, possibly escaped or disambiguated.</param>
        /// <param name="method">The actual Java declaration and its official parameter descriptions.</param>
        /// <param name="signature">The descriptor supplying argument positions and nullability.</param>
        /// <param name="index">The zero-based parameter position, including enclosing constructor prefixes.</param>
        internal static void WriteParameter(Action<string> writeLine, string name, JavaMethod method, JavaMethodDescriptor signature, int index)
        {
            var attributes = " name=\"" + JavaNames.Xml(name.TrimStart('@')) + "\"";
            var nullable = signature.Parameters[index].Code == 'L' || signature.Parameters[index].Code == '[';
            var javaName = OriginalParameterName(method, signature.Parameters.Count, index);
            if (method.Documentation is not null && method.Documentation.Parameters.TryGetValue(javaName, out var description)
                && !string.IsNullOrWhiteSpace(description))
            {
                WriteTextElement(writeLine, "param", attributes, description, nullable ? NullableParameterNote : string.Empty);
                return;
            }
            var fallback = "The Java argument at position " + (index + 1).ToString(CultureInfo.InvariantCulture)
                + (nullable ? ", or null for a null Java reference" : string.Empty) + ".";
            WriteXmlLine(writeLine, "<param" + attributes + ">" + fallback + "</param>");
        }

        /// <summary>Documents a result using official prose and the binding's nullable reference representation.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="documentation">The optional official result description of the actual method or field.</param>
        /// <param name="descriptor">The authoritative result or field descriptor.</param>
        internal static void WriteReturn(Action<string> writeLine, JavaDocumentation? documentation, JavaTypeDescriptor descriptor)
        {
            var nullable = descriptor.Code == 'L' || descriptor.Code == '[';
            if (!string.IsNullOrWhiteSpace(documentation?.Returns))
            {
                WriteTextElement(writeLine, "returns", string.Empty, documentation!.Returns, nullable ? NullableReturnNote : string.Empty);
                return;
            }
            WriteXmlLine(writeLine, "<returns>The Java result" + (nullable ? ", or null for a null Java reference" : string.Empty) + ".</returns>");
        }

        /// <summary>Writes one escaped text element with paragraph boundaries and an optional binding note.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="element">The fixed XML element name.</param>
        /// <param name="attributes">The already escaped XML attributes.</param>
        /// <param name="text">The original plain English description.</param>
        /// <param name="note">An optional additional binding paragraph.</param>
        private static void WriteTextElement(Action<string> writeLine, string element, string attributes, string text, string note = "")
        {
            WriteXmlLine(writeLine, "<" + element + attributes + ">");
            WriteParagraphs(writeLine, text);
            if (note.Length != 0)
            {
                WriteParagraphs(writeLine, note);
            }
            WriteXmlLine(writeLine, "</" + element + ">");
        }

        /// <summary>Escapes plain text and preserves every paragraph without allowing raw source-line injection.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="text">The description whose line breaks separate paragraphs or comment lines.</param>
        private static void WriteParagraphs(Action<string> writeLine, string text)
        {
            var paragraphOpen = false;
            foreach (var line in NormalizeText(text).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (paragraphOpen)
                    {
                        WriteXmlLine(writeLine, "</para>");
                        paragraphOpen = false;
                    }
                    continue;
                }
                if (!paragraphOpen)
                {
                    WriteXmlLine(writeLine, "<para>");
                    paragraphOpen = true;
                }
                WriteXmlLine(writeLine, JavaNames.Xml(line.Trim()));
            }
            if (paragraphOpen)
            {
                WriteXmlLine(writeLine, "</para>");
            }
        }

        /// <summary>Prefixes every physical XML comment line, including all C# line terminators.</summary>
        /// <param name="writeLine">The output callback for a single indented source line.</param>
        /// <param name="xml">An escaped XML fragment that may contain line breaks.</param>
        private static void WriteXmlLine(Action<string> writeLine, string xml)
        {
            foreach (var line in NormalizeText(xml).Split('\n'))
            {
                writeLine("/// " + line);
            }
        }

        /// <summary>Normalizes line terminators and replaces characters forbidden in XML 1.0 text.</summary>
        /// <param name="text">The original plain text or escaped XML fragment.</param>
        /// <returns>XML-compatible text with only LF as a line terminator and valid surrogate pairs.</returns>
        private static string NormalizeText(string text)
        {
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u0085', '\n')
                .Replace('\u2028', '\n').Replace('\u2029', '\n');
            var result = new StringBuilder(normalized.Length);
            for (var index = 0; index < normalized.Length; index++)
            {
                var character = normalized[index];
                if (char.IsHighSurrogate(character) && index + 1 < normalized.Length && char.IsLowSurrogate(normalized[index + 1]))
                {
                    result.Append(character);
                    result.Append(normalized[++index]);
                }
                else if (char.IsSurrogate(character) || character < ' ' && character != '\t' && character != '\n'
                    || character == '\uFFFE' || character == '\uFFFF')
                {
                    result.Append('\uFFFD');
                }
                else
                {
                    result.Append(character);
                }
            }
            return result.ToString();
        }
    }
}

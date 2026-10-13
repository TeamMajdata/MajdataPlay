using System;
using System.Collections.Generic;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Matches official source signatures to unambiguous erased JVM descriptors.</summary>
    internal static class DocumentationHtmlSignature
    {
        /// <summary>Contains unqualified java.lang types commonly used without links in source signatures.</summary>
        private static readonly HashSet<string> s_javaLangTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Object", "String", "StringBuilder", "StringBuffer", "CharSequence", "Class", "Enum", "Number", "Boolean",
            "Byte", "Character", "Short", "Integer", "Long", "Float", "Double", "Void", "Throwable", "Exception",
            "RuntimeException", "Error", "Comparable", "Iterable", "Cloneable", "Runnable", "Thread"
        };

        /// <summary>Matches a documented member only when all known parameter types and array ranks agree.</summary>
        /// <param name="type">The immutable metadata used as the matching authority.</param>
        /// <param name="name">The original Java method name, or &lt;init&gt;.</param>
        /// <param name="signature">The official signature subtree.</param>
        /// <param name="anchor">The official member anchor, if present.</param>
        /// <param name="sourceUrl">The page URL used to resolve relative type links.</param>
        /// <param name="typeVariables">The type-level generic parameter names.</param>
        /// <param name="parameterNames">Receives source names in descriptor order.</param>
        /// <returns>The unique matching metadata member, or null for missing or ambiguous signatures.</returns>
        internal static JavaMethod? Match(JavaType type, string name, DocumentationHtmlNode signature, string anchor, string sourceUrl,
            HashSet<string> typeVariables, out List<string> parameterNames)
        {
            parameterNames = new List<string>();
            var variables = new HashSet<string>(typeVariables, StringComparer.Ordinal);
            CollectVariables(signature, sourceUrl, false, variables);
            var text = SignatureText(signature, sourceUrl);
            var displayName = name == "<init>" ? SimpleName(type.Name) : name;
            var open = FindParameterStart(text, displayName);
            if (open < 0)
            {
                return null;
            }
            var close = MatchingDelimiter(text, open, '(', ')');
            if (close < 0)
            {
                return null;
            }
            var parameters = new List<ParameterType>();
            var names = new List<string>();
            var declarations = SplitParameters(text.Substring(open + 1, close - open - 1));
            foreach (var declaration in declarations)
            {
                if (!ReadParameter(declaration, type, variables, out var parameter, out var parameterName))
                {
                    return null;
                }
                parameters.Add(parameter);
                names.Add(parameterName);
            }
            var anchorTypes = ReadAnchorTypes(anchor, name, SimpleName(type.Name));
            if (anchorTypes != null && anchorTypes.Count == parameters.Count)
            {
                for (var index = 0; index < parameters.Count; index++)
                {
                    var anchored = ReadType(anchorTypes[index], type, variables);
                    if (anchored != null)
                    {
                        if (anchored.ArrayDepth != parameters[index].ArrayDepth)
                        {
                            return null;
                        }
                        parameters[index] = anchored;
                    }
                }
            }
            JavaMethod? match = null;
            var matchedPrefix = false;
            foreach (var method in type.Methods)
            {
                if (method.Name != name || method.IsBridge)
                {
                    continue;
                }
                JavaMethodDescriptor descriptor;
                try
                {
                    descriptor = JavaDescriptors.ParseMethod(method.Descriptor);
                }
                catch (FormatException)
                {
                    continue;
                }
                var enclosingName = type.EnclosingType;
                var nestedSeparator = type.Name.LastIndexOf('$');
                if (enclosingName == null && nestedSeparator >= 0)
                {
                    enclosingName = type.Name.Substring(0, nestedSeparator);
                }
                var prefix = name == "<init>" && (type.AccessFlags & 0x0008) == 0 && enclosingName != null
                    && descriptor.Parameters.Count == parameters.Count + 1 && descriptor.Parameters[0].Code == 'L'
                    && descriptor.Parameters[0].BinaryName == enclosingName;
                var offset = prefix ? 1 : 0;
                if (descriptor.Parameters.Count != parameters.Count + offset)
                {
                    continue;
                }
                var matches = true;
                for (var index = 0; index < parameters.Count; index++)
                {
                    if (!parameters[index].Matches(descriptor.Parameters[index + offset]))
                    {
                        matches = false;
                        break;
                    }
                }
                if (!matches)
                {
                    continue;
                }
                if (match != null)
                {
                    return null;
                }
                match = method;
                matchedPrefix = prefix;
            }
            if (match != null)
            {
                if (matchedPrefix)
                {
                    parameterNames.Add(match.ParameterNames.Count != 0 && (match.ParameterNames[0] == "this$0" || match.ParameterNames[0] == "enclosingInstance")
                        ? match.ParameterNames[0] : "enclosingInstance");
                }
                parameterNames.AddRange(names);
            }
            return match;
        }

        /// <summary>Collects declared generic variables and explicitly labeled type-parameter links.</summary>
        /// <param name="signature">The type or member signature.</param>
        /// <param name="sourceUrl">The official page URL.</param>
        /// <param name="typeDeclaration">Whether this is a class or interface declaration.</param>
        /// <param name="variables">The set receiving source type-variable names.</param>
        internal static void CollectVariables(DocumentationHtmlNode signature, string sourceUrl, bool typeDeclaration, HashSet<string> variables)
        {
            foreach (var element in signature.Elements())
            {
                if (element.Name == "a" && (element.Attribute("title").IndexOf("type parameter", StringComparison.OrdinalIgnoreCase) >= 0
                    || element.Attribute("href").Contains("#type-param-", StringComparison.Ordinal)))
                {
                    var variable = DocumentationHtml.Text(element).Trim();
                    if (IsIdentifier(variable))
                    {
                        variables.Add(variable);
                    }
                }
            }
            var text = SignatureText(signature, sourceUrl);
            var start = 0;
            if (typeDeclaration)
            {
                var keyword = text.IndexOf("class ", StringComparison.Ordinal);
                var keywordLength = 6;
                if (keyword < 0)
                {
                    keyword = text.IndexOf("interface ", StringComparison.Ordinal);
                    keywordLength = 10;
                }
                if (keyword < 0)
                {
                    keyword = text.IndexOf("record ", StringComparison.Ordinal);
                    keywordLength = 7;
                }
                if (keyword < 0)
                {
                    return;
                }
                start = keyword + keywordLength;
                while (start < text.Length && (char.IsWhiteSpace(text[start]) || IsIdentifierCharacter(text[start]) || text[start] == '.'))
                {
                    start++;
                }
                if (start >= text.Length || text[start] != '<')
                {
                    return;
                }
            }
            for (var index = start; index < text.Length; index++)
            {
                if (text[index] != '<')
                {
                    continue;
                }
                var precedingSpace = index == 0 || char.IsWhiteSpace(text[index - 1]);
                if (!typeDeclaration && !precedingSpace)
                {
                    continue;
                }
                var end = MatchingDelimiter(text, index, '<', '>');
                if (end < 0)
                {
                    break;
                }
                foreach (var declaration in SplitParameters(text.Substring(index + 1, end - index - 1)))
                {
                    var length = 0;
                    while (length < declaration.Length && IsIdentifierCharacter(declaration[length]))
                    {
                        length++;
                    }
                    if (length != 0)
                    {
                        var variable = declaration.Substring(0, length);
                        var remainder = declaration.Substring(length).Trim();
                        if (remainder.Length == 0 || remainder.StartsWith("extends ", StringComparison.Ordinal))
                        {
                            variables.Add(variable);
                        }
                    }
                }
                index = end;
                if (typeDeclaration)
                {
                    break;
                }
            }
        }

        /// <summary>Resolves a class hyperlink to its binary name, including nested classes and Javadoc modules.</summary>
        /// <param name="link">The official type hyperlink.</param>
        /// <param name="sourceUrl">The page URL used for relative paths.</param>
        /// <returns>The qualified binary name, or null for member and type-variable links.</returns>
        internal static string? LinkedType(DocumentationHtmlNode link, string sourceUrl)
        {
            var href = link.Attribute("href");
            var title = link.Attribute("title");
            if (href.Length == 0 || href[0] == '#' || title.IndexOf("type parameter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var pageUri) || !Uri.TryCreate(pageUri, href, out var target))
            {
                return null;
            }
            var path = Uri.UnescapeDataString(target.AbsolutePath);
            var reference = path.IndexOf("/reference/", StringComparison.Ordinal);
            if (reference >= 0)
            {
                var relative = path.Substring(reference + "/reference/".Length);
                if (relative.StartsWith("kotlin/", StringComparison.Ordinal))
                {
                    return null;
                }
                return BinaryNameFromPath(relative);
            }
            var api = path.IndexOf("/api/", StringComparison.Ordinal);
            if (api >= 0 && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                var relative = path.Substring(api + "/api/".Length);
                var separator = relative.IndexOf('/');
                if (separator >= 0 && relative.Substring(0, separator).Contains('.', StringComparison.Ordinal))
                {
                    relative = relative.Substring(separator + 1);
                }
                return BinaryNameFromPath(relative.Substring(0, relative.Length - 5));
            }
            var packageMarker = title.LastIndexOf(" in ", StringComparison.Ordinal);
            if (packageMarker >= 0 && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                var package = title.Substring(packageMarker + 4).Trim();
                var file = path.Substring(path.LastIndexOf('/') + 1);
                return package + "." + file.Substring(0, file.Length - 5).Replace('.', '$');
            }
            return null;
        }

        /// <summary>Builds a source signature with qualified type links while omitting annotations.</summary>
        /// <param name="signature">The signature subtree.</param>
        /// <param name="sourceUrl">The official page URL.</param>
        /// <returns>A whitespace-normalized signature retaining original parameter names.</returns>
        private static string SignatureText(DocumentationHtmlNode signature, string sourceUrl)
        {
            var text = DocumentationHtml.Text(signature,
                node => node.Name == "a" ? LinkedType(node, sourceUrl) : null,
                node => node.HasClass("annotations"));
            return RemoveAnnotations(text).Replace('\n', ' ').Trim();
        }

        /// <summary>Finds the parameter list belonging to an exact source member name.</summary>
        /// <param name="text">The normalized source signature.</param>
        /// <param name="name">The source method or constructor name.</param>
        /// <returns>The opening parenthesis position, or -1.</returns>
        private static int FindParameterStart(string text, string name)
        {
            var position = 0;
            while (position < text.Length)
            {
                var found = text.IndexOf(name, position, StringComparison.Ordinal);
                if (found < 0)
                {
                    return -1;
                }
                var after = found + name.Length;
                while (after < text.Length && char.IsWhiteSpace(text[after]))
                {
                    after++;
                }
                if ((found == 0 || !IsIdentifierCharacter(text[found - 1])) && after < text.Length && text[after] == '(')
                {
                    return after;
                }
                position = found + name.Length;
            }
            return -1;
        }

        /// <summary>Parses one named source parameter, including post-name arrays and varargs.</summary>
        /// <param name="declaration">The source parameter declaration.</param>
        /// <param name="type">The declaring metadata type.</param>
        /// <param name="variables">Known generic variables.</param>
        /// <param name="parameter">Receives the exact or generic-erased matching constraint.</param>
        /// <param name="name">Receives the original Java parameter name.</param>
        /// <returns>Whether both the type and name could be parsed.</returns>
        private static bool ReadParameter(string declaration, JavaType type, HashSet<string> variables, out ParameterType parameter, out string name)
        {
            parameter = new ParameterType('\0', string.Empty, 0, false);
            name = string.Empty;
            var text = RemoveAnnotations(declaration).Trim();
            var end = text.Length;
            var extraArrays = 0;
            while (end > 0)
            {
                while (end > 0 && char.IsWhiteSpace(text[end - 1]))
                {
                    end--;
                }
                if (end == 0 || text[end - 1] != ']')
                {
                    break;
                }
                var start = end - 2;
                while (start >= 0 && char.IsWhiteSpace(text[start]))
                {
                    start--;
                }
                if (start < 0 || text[start] != '[')
                {
                    return false;
                }
                extraArrays++;
                end = start;
            }
            var nameStart = end;
            while (nameStart > 0 && IsIdentifierCharacter(text[nameStart - 1]))
            {
                nameStart--;
            }
            if (nameStart == end || nameStart == 0)
            {
                return false;
            }
            name = text.Substring(nameStart, end - nameStart);
            var typeText = text.Substring(0, nameStart).Trim();
            if (typeText.StartsWith("final ", StringComparison.Ordinal))
            {
                typeText = typeText.Substring(6).Trim();
            }
            for (var index = 0; index < extraArrays; index++)
            {
                typeText += "[]";
            }
            var parsed = ReadType(typeText, type, variables);
            if (parsed == null)
            {
                return false;
            }
            parameter = parsed;
            return true;
        }

        /// <summary>Parses a source or anchor type after erasing generic arguments.</summary>
        /// <param name="text">The qualified or source type spelling.</param>
        /// <param name="type">The declaring type for same-package resolution.</param>
        /// <param name="variables">The known generic variable names.</param>
        /// <returns>A parameter matching constraint, or null for malformed types.</returns>
        private static ParameterType? ReadType(string text, JavaType type, HashSet<string> variables)
        {
            text = RemoveAnnotations(text).Replace(":A", "[]", StringComparison.Ordinal).Replace("...", "[]", StringComparison.Ordinal);
            var erased = new StringBuilder();
            var genericDepth = 0;
            foreach (var character in text)
            {
                if (character == '<')
                {
                    genericDepth++;
                }
                else if (character == '>')
                {
                    genericDepth--;
                }
                else if (genericDepth == 0 && !char.IsWhiteSpace(character))
                {
                    erased.Append(character);
                }
                if (genericDepth < 0)
                {
                    return null;
                }
            }
            if (genericDepth != 0)
            {
                return null;
            }
            var name = erased.ToString();
            var arrays = 0;
            while (name.EndsWith("[]", StringComparison.Ordinal))
            {
                arrays++;
                name = name.Substring(0, name.Length - 2);
            }
            if (name.Length == 0 || name.IndexOf('[') >= 0 || name.IndexOf(']') >= 0)
            {
                return null;
            }
            var primitive = PrimitiveCode(name);
            if (primitive != '\0')
            {
                return new ParameterType(primitive, string.Empty, arrays, false);
            }
            if (variables.Contains(name))
            {
                return new ParameterType('L', string.Empty, arrays, true);
            }
            if (name.IndexOf('.') < 0 && name.IndexOf('$') < 0)
            {
                if (name == SimpleName(type.Name))
                {
                    name = type.Name;
                }
                else if (s_javaLangTypes.Contains(name))
                {
                    name = "java.lang." + name;
                }
                else
                {
                    var packageSeparator = type.Name.LastIndexOf('.');
                    name = packageSeparator >= 0 ? type.Name.Substring(0, packageSeparator + 1) + name : name;
                }
            }
            return new ParameterType('L', name.Replace('$', '.'), arrays, false);
        }

        /// <summary>Reads parameter identities from modern anchors or Java 8 dash-encoded anchors.</summary>
        /// <param name="anchor">The observed official anchor.</param>
        /// <param name="name">The JVM member name.</param>
        /// <param name="constructorName">The simple source constructor name.</param>
        /// <returns>The ordered anchor type spellings, or null for non-signature anchors.</returns>
        private static List<string>? ReadAnchorTypes(string anchor, string name, string constructorName)
        {
            anchor = Uri.UnescapeDataString(anchor);
            var open = anchor.IndexOf('(');
            var close = anchor.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                return SplitParameters(anchor.Substring(open + 1, close - open - 1));
            }
            var prefix = (name == "<init>" ? constructorName : name) + "-";
            if (!anchor.StartsWith(prefix, StringComparison.Ordinal) || !anchor.EndsWith("-", StringComparison.Ordinal))
            {
                return null;
            }
            var body = anchor.Substring(prefix.Length, anchor.Length - prefix.Length - 1);
            return body.Length == 0 ? new List<string>() : new List<string>(body.Split('-'));
        }

        /// <summary>Splits source parameters or generic declarations without splitting nested syntax.</summary>
        /// <param name="text">The comma-separated source text.</param>
        /// <returns>Trimmed, ordered declarations.</returns>
        private static List<string> SplitParameters(string text)
        {
            var result = new List<string>();
            var depth = 0;
            var start = 0;
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '<' || text[index] == '(' || text[index] == '[')
                {
                    depth++;
                }
                else if (text[index] == '>' || text[index] == ')' || text[index] == ']')
                {
                    depth--;
                }
                else if (text[index] == ',' && depth == 0)
                {
                    result.Add(text.Substring(start, index - start).Trim());
                    start = index + 1;
                }
            }
            var last = text.Substring(start).Trim();
            if (last.Length != 0)
            {
                result.Add(last);
            }
            return result;
        }

        /// <summary>Finds a balanced closing delimiter without recursive parsing.</summary>
        /// <param name="text">The source signature.</param>
        /// <param name="start">The opening delimiter position.</param>
        /// <param name="open">The opening delimiter.</param>
        /// <param name="close">The closing delimiter.</param>
        /// <returns>The matching closing position, or -1.</returns>
        private static int MatchingDelimiter(string text, int start, char open, char close)
        {
            var depth = 0;
            for (var index = start; index < text.Length; index++)
            {
                if (text[index] == open)
                {
                    depth++;
                }
                else if (text[index] == close && --depth == 0)
                {
                    return index;
                }
            }
            return -1;
        }

        /// <summary>Removes source annotations and balanced annotation arguments.</summary>
        /// <param name="text">The plain source signature.</param>
        /// <returns>The signature without annotations.</returns>
        private static string RemoveAnnotations(string text)
        {
            var result = new StringBuilder();
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != '@')
                {
                    result.Append(text[index]);
                    continue;
                }
                index++;
                while (index < text.Length && (IsIdentifierCharacter(text[index]) || text[index] == '.'))
                {
                    index++;
                }
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }
                if (index < text.Length && text[index] == '(')
                {
                    var end = MatchingDelimiter(text, index, '(', ')');
                    index = end < 0 ? text.Length : end + 1;
                }
                result.Append(' ');
                index--;
            }
            return result.ToString();
        }

        /// <summary>Converts package paths and dot-separated nested class names to binary names.</summary>
        /// <param name="path">A relative type path without an HTML extension.</param>
        /// <returns>The qualified binary class name.</returns>
        private static string BinaryNameFromPath(string path)
        {
            var separator = path.LastIndexOf('/');
            return separator < 0 ? path.Replace('.', '$') : path.Substring(0, separator + 1).Replace('/', '.') + path.Substring(separator + 1).Replace('.', '$');
        }

        /// <summary>Returns a Java type's innermost source name.</summary>
        /// <param name="name">The Java binary name.</param>
        /// <returns>The simple source class name.</returns>
        private static string SimpleName(string name)
        {
            return name.Substring(Math.Max(name.LastIndexOf('.'), name.LastIndexOf('$')) + 1);
        }

        /// <summary>Maps source primitive names to JVM descriptor codes.</summary>
        /// <param name="name">The source primitive name.</param>
        /// <returns>The JVM primitive code, or a null character for reference types.</returns>
        private static char PrimitiveCode(string name)
        {
            switch (name)
            {
                case "boolean":
                    return 'Z';
                case "byte":
                    return 'B';
                case "char":
                    return 'C';
                case "short":
                    return 'S';
                case "int":
                    return 'I';
                case "long":
                    return 'J';
                case "float":
                    return 'F';
                case "double":
                    return 'D';
                default:
                    return '\0';
            }
        }

        /// <summary>Tests whether a complete string is a source identifier.</summary>
        /// <param name="text">The possible identifier.</param>
        /// <returns>Whether the string contains only identifier characters and does not start with a digit.</returns>
        private static bool IsIdentifier(string text)
        {
            if (text.Length == 0 || char.IsDigit(text[0]))
            {
                return false;
            }
            foreach (var character in text)
            {
                if (!IsIdentifierCharacter(character))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Tests a source identifier character while preserving dollar signs and Unicode names.</summary>
        /// <param name="character">The character to inspect.</param>
        /// <returns>Whether the character may occur in a Java identifier.</returns>
        private static bool IsIdentifierCharacter(char character)
        {
            return char.IsLetterOrDigit(character) || character == '_' || character == '$';
        }

        /// <summary>Contains the known erased type and array rank of one source parameter.</summary>
        private sealed class ParameterType
        {
            /// <summary>Gets the erased primitive or reference descriptor code.</summary>
            internal char Code { get; }

            /// <summary>Gets the qualified reference identity with normalized nested separators.</summary>
            internal string Name { get; }

            /// <summary>Gets the exact source array rank, including a varargs dimension.</summary>
            internal int ArrayDepth { get; }

            /// <summary>Gets whether a declared generic variable permits an unknown reference erasure.</summary>
            internal bool IsVariable { get; }

            /// <summary>Creates a source parameter constraint.</summary>
            /// <param name="code">The erased descriptor category.</param>
            /// <param name="name">The qualified reference identity.</param>
            /// <param name="arrayDepth">The exact number of array dimensions.</param>
            /// <param name="isVariable">Whether the reference erasure is a declared type variable.</param>
            internal ParameterType(char code, string name, int arrayDepth, bool isVariable)
            {
                Code = code;
                Name = name;
                ArrayDepth = arrayDepth;
                IsVariable = isVariable;
            }

            /// <summary>Tests a class-file parameter against all known source constraints.</summary>
            /// <param name="descriptor">The authoritative erased JVM parameter.</param>
            /// <returns>Whether primitive/reference identity and array rank match.</returns>
            internal bool Matches(JavaTypeDescriptor descriptor)
            {
                var arrays = 0;
                while (descriptor.Code == '[' && descriptor.Element != null)
                {
                    arrays++;
                    descriptor = descriptor.Element;
                }
                return arrays == ArrayDepth && descriptor.Code == Code
                    && (Code != 'L' || IsVariable || descriptor.BinaryName.Replace('$', '.') == Name);
            }
        }
    }
}

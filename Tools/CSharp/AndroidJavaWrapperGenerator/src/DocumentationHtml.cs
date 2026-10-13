using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Represents a tolerant HTML element or decoded text node without executing page content.</summary>
    internal sealed class DocumentationHtmlNode
    {
        /// <summary>Gets the lowercase element name, or an empty string for text nodes.</summary>
        internal string Name { get; }

        /// <summary>Gets the decoded text of a text node.</summary>
        internal string Text { get; }

        /// <summary>Gets the source offset used to associate legacy member anchors.</summary>
        internal int Offset { get; }

        /// <summary>Gets the parent element, or null for the document root.</summary>
        internal DocumentationHtmlNode? Parent { get; }

        /// <summary>Gets child nodes in source order.</summary>
        internal List<DocumentationHtmlNode> Children { get; } = new List<DocumentationHtmlNode>();

        /// <summary>Gets decoded attribute values using case-insensitive HTML names.</summary>
        internal Dictionary<string, string> Attributes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Creates an element or text node.</summary>
        /// <param name="name">The lowercase element name, or an empty string for text.</param>
        /// <param name="text">The decoded text, if any.</param>
        /// <param name="offset">The original source offset.</param>
        /// <param name="parent">The containing element.</param>
        internal DocumentationHtmlNode(string name, string text, int offset, DocumentationHtmlNode? parent)
        {
            Name = name;
            Text = text;
            Offset = offset;
            Parent = parent;
        }

        /// <summary>Reads a decoded attribute without requiring it to exist.</summary>
        /// <param name="name">The HTML attribute name.</param>
        /// <returns>The attribute value, or an empty string.</returns>
        internal string Attribute(string name)
        {
            return Attributes.TryGetValue(name, out var value) ? value : string.Empty;
        }

        /// <summary>Tests an exact token in the whitespace-separated class attribute.</summary>
        /// <param name="name">The CSS class token.</param>
        /// <returns>Whether the element has the class.</returns>
        internal bool HasClass(string name)
        {
            var classes = Attribute("class");
            var start = 0;
            while (start < classes.Length)
            {
                while (start < classes.Length && char.IsWhiteSpace(classes[start]))
                {
                    start++;
                }
                var end = start;
                while (end < classes.Length && !char.IsWhiteSpace(classes[end]))
                {
                    end++;
                }
                if (end - start == name.Length && string.CompareOrdinal(classes, start, name, 0, name.Length) == 0)
                {
                    return true;
                }
                start = end;
            }
            return false;
        }

        /// <summary>Checks whether a node belongs to this subtree.</summary>
        /// <param name="node">The possible descendant.</param>
        /// <returns>Whether this node is the node itself or one of its ancestors.</returns>
        internal bool Contains(DocumentationHtmlNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (ReferenceEquals(this, current))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Enumerates visible elements in document order using a bounded-depth iterative traversal.</summary>
        /// <returns>This element and its descendants, excluding navigation and hidden page content.</returns>
        internal IEnumerable<DocumentationHtmlNode> Elements()
        {
            var pending = new Stack<DocumentationHtmlNode>();
            pending.Push(this);
            while (pending.Count != 0)
            {
                var node = pending.Pop();
                if (DocumentationHtml.IsIgnored(node))
                {
                    continue;
                }
                if (node.Name.Length != 0)
                {
                    yield return node;
                }
                for (var index = node.Children.Count - 1; index >= 0; index--)
                {
                    pending.Push(node.Children[index]);
                }
            }
        }
    }

    /// <summary>Tokenizes official documentation HTML and renders selected subtrees as plain text.</summary>
    internal static class DocumentationHtml
    {
        /// <summary>Contains elements that have no HTML end tag.</summary>
        private static readonly HashSet<string> s_voidElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr"
        };

        /// <summary>Contains elements that close an omitted paragraph end tag.</summary>
        private static readonly HashSet<string> s_blockElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "address", "article", "aside", "blockquote", "dd", "div", "dl", "dt", "fieldset", "figcaption", "figure",
            "h1", "h2", "h3", "h4", "h5", "h6", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "table", "ul"
        };

        /// <summary>Parses markup without interpreting scripts, styles, comments, or declarations.</summary>
        /// <param name="html">The complete downloaded HTML.</param>
        /// <returns>A tolerant document tree with decoded text and attributes.</returns>
        internal static DocumentationHtmlNode Parse(string html)
        {
            var root = new DocumentationHtmlNode("document", string.Empty, 0, null);
            var open = new List<DocumentationHtmlNode> { root };
            var position = 0;
            while (position < html.Length)
            {
                var start = position;
                if (html[position] != '<')
                {
                    var next = html.IndexOf('<', position);
                    position = next < 0 ? html.Length : next;
                    var parent = open[open.Count - 1];
                    parent.Children.Add(new DocumentationHtmlNode(string.Empty, WebUtility.HtmlDecode(html.Substring(start, position - start)), start, parent));
                    continue;
                }
                if (html.AsSpan(position).StartsWith("<!--".AsSpan(), StringComparison.Ordinal))
                {
                    var end = html.IndexOf("-->", position + 4, StringComparison.Ordinal);
                    position = end < 0 ? html.Length : end + 3;
                    continue;
                }
                position++;
                if (position < html.Length && (html[position] == '!' || html[position] == '?'))
                {
                    var end = html.IndexOf('>', position);
                    position = end < 0 ? html.Length : end + 1;
                    continue;
                }
                var closing = position < html.Length && html[position] == '/';
                if (closing)
                {
                    position++;
                }
                SkipWhitespace(html, ref position);
                var nameStart = position;
                while (position < html.Length && (char.IsLetterOrDigit(html[position]) || html[position] == '-' || html[position] == ':'))
                {
                    position++;
                }
                if (position == nameStart)
                {
                    var parent = open[open.Count - 1];
                    parent.Children.Add(new DocumentationHtmlNode(string.Empty, "<", start, parent));
                    position = start + 1;
                    continue;
                }
                var name = html.Substring(nameStart, position - nameStart).ToLowerInvariant();
                if (closing)
                {
                    var end = html.IndexOf('>', position);
                    position = end < 0 ? html.Length : end + 1;
                    Close(open, name);
                    continue;
                }
                AutoClose(open, name);
                var element = new DocumentationHtmlNode(name, string.Empty, start, open[open.Count - 1]);
                var selfClosing = ReadAttributes(html, ref position, element);
                element.Parent!.Children.Add(element);
                if (name == "script" || name == "style" || name == "textarea")
                {
                    position = SkipRawElement(html, position, name);
                    continue;
                }
                if (!selfClosing && !s_voidElements.Contains(name) && open.Count < 256)
                {
                    open.Add(element);
                }
            }
            return root;
        }

        /// <summary>Identifies page chrome and executable or hidden content that cannot become documentation.</summary>
        /// <param name="node">The element to inspect.</param>
        /// <returns>Whether the complete subtree should be excluded.</returns>
        internal static bool IsIgnored(DocumentationHtmlNode node)
        {
            return node.Name == "script" || node.Name == "style" || node.Name == "nav" || node.Name == "header"
                || node.Name == "footer" || node.Name == "noscript" || node.Name == "template" || node.Name == "svg"
                || node.Name == "textarea" || node.Name == "devsite-toc" || node.Name == "devsite-nav"
                || node.HasClass("nocontent") || node.HasClass("devsite-nav") || node.HasClass("api-level")
                || node.Attribute("role") == "navigation" || node.Attribute("aria-hidden") == "true"
                || node.Attributes.ContainsKey("hidden");
        }

        /// <summary>Renders an element as entity-decoded plain text with optional signature-link replacement.</summary>
        /// <param name="node">The selected description or signature subtree.</param>
        /// <param name="replacement">An optional replacement for complete elements, such as qualified type links.</param>
        /// <param name="exclude">An optional additional subtree exclusion.</param>
        /// <returns>Plain text with collapsed spaces and preserved paragraph boundaries.</returns>
        internal static string Text(DocumentationHtmlNode node, Func<DocumentationHtmlNode, string?>? replacement = null, Func<DocumentationHtmlNode, bool>? exclude = null)
        {
            var result = new StringBuilder();
            var pending = new Stack<(DocumentationHtmlNode Node, bool End, bool Preformatted)>();
            pending.Push((node, false, false));
            while (pending.Count != 0)
            {
                var item = pending.Pop();
                var current = item.Node;
                if (IsIgnored(current) || exclude != null && exclude(current))
                {
                    continue;
                }
                var block = s_blockElements.Contains(current.Name) || current.Name == "br" || current.Name == "tr";
                if (item.End)
                {
                    if (block)
                    {
                        Newline(result);
                    }
                    continue;
                }
                if (current.Name.Length == 0)
                {
                    AppendText(result, current.Text, item.Preformatted);
                    continue;
                }
                var substituted = replacement?.Invoke(current);
                if (substituted != null)
                {
                    AppendText(result, substituted, false);
                    continue;
                }
                if (block)
                {
                    Newline(result);
                }
                pending.Push((current, true, item.Preformatted));
                for (var index = current.Children.Count - 1; index >= 0; index--)
                {
                    pending.Push((current.Children[index], false, item.Preformatted || current.Name == "pre"));
                }
            }
            return result.ToString().Trim();
        }

        /// <summary>Reads quoted or unquoted attributes through the end of a start tag.</summary>
        /// <param name="html">The HTML source.</param>
        /// <param name="position">The current cursor, advanced past the tag.</param>
        /// <param name="node">The element receiving decoded attributes.</param>
        /// <returns>Whether the start tag explicitly closes itself.</returns>
        private static bool ReadAttributes(string html, ref int position, DocumentationHtmlNode node)
        {
            while (position < html.Length)
            {
                SkipWhitespace(html, ref position);
                if (position >= html.Length)
                {
                    break;
                }
                if (html[position] == '>')
                {
                    position++;
                    return false;
                }
                if (html[position] == '/' && position + 1 < html.Length && html[position + 1] == '>')
                {
                    position += 2;
                    return true;
                }
                var start = position;
                while (position < html.Length && !char.IsWhiteSpace(html[position]) && html[position] != '=' && html[position] != '>' && html[position] != '/')
                {
                    position++;
                }
                if (position == start)
                {
                    position++;
                    continue;
                }
                var name = html.Substring(start, position - start);
                SkipWhitespace(html, ref position);
                var value = string.Empty;
                if (position < html.Length && html[position] == '=')
                {
                    position++;
                    SkipWhitespace(html, ref position);
                    var quote = position < html.Length && (html[position] == '\'' || html[position] == '"') ? html[position++] : '\0';
                    start = position;
                    while (position < html.Length && (quote != '\0' ? html[position] != quote : !char.IsWhiteSpace(html[position]) && html[position] != '>'))
                    {
                        position++;
                    }
                    value = WebUtility.HtmlDecode(html.Substring(start, position - start));
                    if (quote != '\0' && position < html.Length)
                    {
                        position++;
                    }
                }
                node.Attributes[name] = value;
            }
            return false;
        }

        /// <summary>Skips raw text until an actual matching script or style end tag.</summary>
        /// <param name="html">The HTML source.</param>
        /// <param name="position">The position after the start tag.</param>
        /// <param name="name">The raw-text element name.</param>
        /// <returns>The position after its end tag, or the end of the input.</returns>
        private static int SkipRawElement(string html, int position, string name)
        {
            var marker = "</" + name;
            while (position < html.Length)
            {
                var end = html.IndexOf(marker, position, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    return html.Length;
                }
                var afterName = end + marker.Length;
                if (afterName == html.Length || html[afterName] == '>' || char.IsWhiteSpace(html[afterName]))
                {
                    var close = html.IndexOf('>', afterName);
                    return close < 0 ? html.Length : close + 1;
                }
                position = afterName;
            }
            return html.Length;
        }

        /// <summary>Closes an explicit or omitted ancestor end tag, ignoring stray end tags.</summary>
        /// <param name="open">The bounded stack of open elements.</param>
        /// <param name="name">The ancestor element to close.</param>
        private static void Close(List<DocumentationHtmlNode> open, string name)
        {
            for (var index = open.Count - 1; index > 0; index--)
            {
                if (open[index].Name == name)
                {
                    open.RemoveRange(index, open.Count - index);
                    return;
                }
            }
        }

        /// <summary>Handles common optional paragraph, list, and table end tags in generated documentation.</summary>
        /// <param name="open">The stack of open elements.</param>
        /// <param name="name">The incoming start tag.</param>
        private static void AutoClose(List<DocumentationHtmlNode> open, string name)
        {
            if (s_blockElements.Contains(name))
            {
                CloseWithin(open, "p", "table", "div", "section", "li");
            }
            if (name == "li")
            {
                CloseWithin(open, "li", "ul", "ol");
            }
            if (name == "dt" || name == "dd")
            {
                CloseWithin(open, "dt", "dl");
                CloseWithin(open, "dd", "dl");
            }
            if (name == "tr")
            {
                CloseWithin(open, "tr", "table");
            }
            if (name == "td" || name == "th")
            {
                CloseWithin(open, "td", "tr", "table");
                CloseWithin(open, "th", "tr", "table");
            }
        }

        /// <summary>Closes an optional end tag only within its nearest structural scope.</summary>
        /// <param name="open">The stack of open elements.</param>
        /// <param name="name">The optionally closed element.</param>
        /// <param name="boundaries">The elements that prevent closing unrelated ancestors.</param>
        private static void CloseWithin(List<DocumentationHtmlNode> open, string name, params string[] boundaries)
        {
            for (var index = open.Count - 1; index > 0; index--)
            {
                var candidate = open[index].Name;
                if (candidate == name)
                {
                    open.RemoveRange(index, open.Count - index);
                    return;
                }
                if (Array.IndexOf(boundaries, candidate) >= 0)
                {
                    return;
                }
            }
        }

        /// <summary>Moves an HTML cursor past source whitespace.</summary>
        /// <param name="html">The HTML source.</param>
        /// <param name="position">The cursor to advance.</param>
        private static void SkipWhitespace(string html, ref int position)
        {
            while (position < html.Length && char.IsWhiteSpace(html[position]))
            {
                position++;
            }
        }

        /// <summary>Appends text while collapsing source wrapping and retaining preformatted line breaks.</summary>
        /// <param name="result">The accumulated plain text.</param>
        /// <param name="text">The already decoded text.</param>
        /// <param name="preformatted">Whether literal source line breaks describe a code example.</param>
        private static void AppendText(StringBuilder result, string text, bool preformatted)
        {
            foreach (var character in text)
            {
                if (preformatted && (character == '\r' || character == '\n'))
                {
                    Newline(result);
                }
                else if (char.IsWhiteSpace(character))
                {
                    if (result.Length != 0 && result[result.Length - 1] != ' ' && result[result.Length - 1] != '\n')
                    {
                        result.Append(' ');
                    }
                }
                else if (character != '\u200B' && character != '\uFEFF')
                {
                    result.Append(character);
                }
            }
        }

        /// <summary>Adds a paragraph boundary without trailing spaces or repeated empty lines.</summary>
        /// <param name="result">The accumulated plain text.</param>
        private static void Newline(StringBuilder result)
        {
            while (result.Length != 0 && result[result.Length - 1] == ' ')
            {
                result.Length--;
            }
            if (result.Length != 0 && result[result.Length - 1] != '\n')
            {
                result.Append('\n');
            }
        }
    }
}

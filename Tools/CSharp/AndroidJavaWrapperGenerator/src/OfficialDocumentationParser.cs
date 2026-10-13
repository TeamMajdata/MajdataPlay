using System;
using System.Collections.Generic;
using System.Text;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Extracts plain English API documentation from official Android and Oracle Javadoc pages.</summary>
    internal static class OfficialDocumentationParser
    {
        /// <summary>Parses one official type page without changing any supplied JVM metadata.</summary>
        /// <param name="type">The authoritative Java type and exact member descriptors.</param>
        /// <param name="html">The downloaded official HTML, bounded by the documentation provider.</param>
        /// <param name="sourceUrl">The official page URL used for attribution and relative type links.</param>
        /// <returns>A separate descriptor-keyed documentation page, empty for unrecognized layouts.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        internal static JavaDocumentationPage Parse(JavaType type, string html, string sourceUrl)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }
            if (html == null)
            {
                throw new ArgumentNullException(nameof(html));
            }
            if (sourceUrl == null)
            {
                throw new ArgumentNullException(nameof(sourceUrl));
            }
            var page = new JavaDocumentationPage { TypeName = type.Name };
            var document = DocumentationHtml.Parse(html);
            var android = First(document, node => node.Attribute("id") == "jd-content");
            var description = android == null ? First(document, node => node.HasClass("class-description") || node.HasClass("description")) : null;
            if (android == null && (description == null || !description.HasClass("class-description")
                && First(document, node => node.HasClass("member-signature") || node.HasClass("blockList")) == null))
            {
                return page;
            }
            var root = android ?? OracleRoot(description!);
            var typeVariables = new HashSet<string>(StringComparer.Ordinal);
            var typeSignature = First(android ?? description!, node => node.HasClass("type-signature")
                || node.HasClass("api-signature") || node.Name == "pre");
            if (typeSignature != null)
            {
                DocumentationHtmlSignature.CollectVariables(typeSignature, sourceUrl, true, typeVariables);
            }
            page.Type = new JavaDocumentation
            {
                Summary = android != null ? AndroidTypeSummary(android) : BlockSummary(description!),
                SourceUrl = PageUrl(sourceUrl)
            };
            if (description != null)
            {
                ReadNotes(description, page.Type);
            }
            var anchors = new List<DocumentationHtmlNode>();
            var headings = new List<DocumentationHtmlNode>();
            foreach (var node in root.Elements())
            {
                if (node.Attribute("id").Length != 0 || node.Name == "a" && node.Attribute("name").Length != 0)
                {
                    anchors.Add(node);
                }
                if ((node.Name == "h3" || node.Name == "h4") && (android != null || description == null || !description.Contains(node)))
                {
                    headings.Add(node);
                }
            }
            var visited = new HashSet<DocumentationHtmlNode>();
            foreach (var heading in headings)
            {
                var block = MemberBlock(heading, root, android != null);
                if (block == null || !visited.Add(block))
                {
                    continue;
                }
                var signature = First(block, IsSignature);
                if (signature == null)
                {
                    continue;
                }
                var headingName = DocumentationHtml.Text(heading).Trim();
                var anchor = MemberAnchor(block, heading, anchors, headingName, type.Name);
                var name = MemberName(anchor, headingName, type.Name);
                var documentation = new JavaDocumentation
                {
                    Summary = android != null ? FollowingSummary(block, signature) : BlockSummary(block),
                    SourceUrl = MemberUrl(sourceUrl, anchor)
                };
                if (documentation.Summary.Length == 0)
                {
                    documentation.Summary = FollowingSummary(block, signature);
                }
                ReadNotes(block, documentation);
                var isField = false;
                foreach (var field in type.Fields)
                {
                    if (field.Name == name && anchor.IndexOf('(') < 0 && anchor.IndexOf('-') < 0)
                    {
                        page.Fields[field.Name] = documentation;
                        isField = true;
                        break;
                    }
                }
                if (isField)
                {
                    continue;
                }
                var method = DocumentationHtmlSignature.Match(type, name, signature, anchor, sourceUrl, typeVariables, out var parameterNames);
                if (method == null)
                {
                    continue;
                }
                documentation.ParameterNames = parameterNames;
                page.Methods[method.Name + method.Descriptor] = documentation;
            }
            return page;
        }

        /// <summary>Finds the first visible element satisfying a predicate.</summary>
        /// <param name="root">The subtree to inspect.</param>
        /// <param name="predicate">The element selection rule.</param>
        /// <returns>The first matching element, or null.</returns>
        private static DocumentationHtmlNode? First(DocumentationHtmlNode root, Func<DocumentationHtmlNode, bool> predicate)
        {
            foreach (var node in root.Elements())
            {
                if (predicate(node))
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>Finds the main Oracle API content containing the class description and member details.</summary>
        /// <param name="description">The recognized class-description subtree.</param>
        /// <returns>The main content element, or the description's containing document.</returns>
        private static DocumentationHtmlNode OracleRoot(DocumentationHtmlNode description)
        {
            var root = description;
            for (var current = description.Parent; current != null; current = current.Parent)
            {
                root = current;
                if (current.Name == "main" || current.HasClass("contentContainer"))
                {
                    break;
                }
            }
            return root;
        }

        /// <summary>Extracts Android type prose before the generated API summary and details.</summary>
        /// <param name="root">The jd-content subtree.</param>
        /// <returns>The type's paragraph-preserving English description.</returns>
        private static string AndroidTypeSummary(DocumentationHtmlNode root)
        {
            var explicitDescription = First(root, node => node.HasClass("jd-descr"));
            if (explicitDescription != null)
            {
                return DocumentationHtml.Text(explicitDescription, exclude: IsNotesElement);
            }
            var result = new StringBuilder();
            var content = First(root, node => node.HasClass("api")) ?? root;
            foreach (var child in content.Children)
            {
                if (child.Name == "h2" || child.Name == "h3" || child.Name == "h4" || child.Attribute("id") == "summary")
                {
                    break;
                }
                if (child.Name != "p" && child.Name != "div" && child.Name != "blockquote" && child.Name != "pre")
                {
                    continue;
                }
                if (First(child, IsSignature) != null || child.HasClass("jd-inheritance-table") || DocumentationHtml.IsIgnored(child))
                {
                    continue;
                }
                AppendParagraph(result, DocumentationHtml.Text(child, exclude: IsNotesElement));
            }
            return result.ToString();
        }

        /// <summary>Identifies generated signatures without treating examples as member declarations.</summary>
        /// <param name="node">The element to inspect.</param>
        /// <returns>Whether the element contains an API signature.</returns>
        private static bool IsSignature(DocumentationHtmlNode node)
        {
            if (node.HasClass("member-signature") || node.HasClass("api-signature"))
            {
                return true;
            }
            if (node.Name != "pre")
            {
                return false;
            }
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.HasClass("block") || parent.HasClass("jd-details-descr"))
                {
                    return false;
                }
                if (parent.HasClass("detail") || parent.HasClass("blockList") || parent.HasClass("jd-details"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Locates a member's smallest structural container with one generated signature.</summary>
        /// <param name="heading">The member heading.</param>
        /// <param name="root">The recognized API content boundary.</param>
        /// <param name="android">Whether the page uses the Android layout.</param>
        /// <returns>The member block, or null for summary and unrelated headings.</returns>
        private static DocumentationHtmlNode? MemberBlock(DocumentationHtmlNode heading, DocumentationHtmlNode root, bool android)
        {
            for (var current = heading.Parent; current != null && !ReferenceEquals(current, root); current = current.Parent)
            {
                if (current.HasClass("summary") || current.HasClass("method-summary") || current.HasClass("field-summary")
                    || current.Name == "table")
                {
                    return null;
                }
                var count = 0;
                foreach (var element in current.Elements())
                {
                    if (IsSignature(element) && element.Offset > heading.Offset)
                    {
                        count++;
                        if (count > 1)
                        {
                            break;
                        }
                    }
                }
                if (count == 1)
                {
                    return current;
                }
                if (count > 1)
                {
                    return null;
                }
            }
            if (!android || !ReferenceEquals(heading.Parent, root))
            {
                return null;
            }
            var slice = new DocumentationHtmlNode("member", string.Empty, heading.Offset, null);
            var started = false;
            foreach (var child in root.Children)
            {
                if (ReferenceEquals(child, heading))
                {
                    started = true;
                }
                else if (started && (child.Name == "h2" || child.Name == "h3" || child.Name == "h4"))
                {
                    break;
                }
                if (started)
                {
                    slice.Children.Add(child);
                }
            }
            return First(slice, IsSignature) != null ? slice : null;
        }

        /// <summary>Reads paragraph-preserving descriptions from Oracle block elements.</summary>
        /// <param name="root">The class or member block.</param>
        /// <returns>The English description without generated signatures or notes.</returns>
        private static string BlockSummary(DocumentationHtmlNode root)
        {
            var result = new StringBuilder();
            foreach (var node in root.Elements())
            {
                if (!node.HasClass("block"))
                {
                    continue;
                }
                var nested = false;
                for (var parent = node.Parent; parent != null && !ReferenceEquals(parent, root); parent = parent.Parent)
                {
                    if (parent.HasClass("block") || IsNotesElement(parent) || IsSignature(parent))
                    {
                        nested = true;
                        break;
                    }
                }
                if (!nested)
                {
                    AppendParagraph(result, DocumentationHtml.Text(node, exclude: IsNotesElement));
                }
            }
            return result.ToString();
        }

        /// <summary>Reads Android prose or legacy prose after a signature, excluding generated notes.</summary>
        /// <param name="root">The member block.</param>
        /// <param name="signature">The generated signature.</param>
        /// <returns>The member description with paragraph boundaries.</returns>
        private static string FollowingSummary(DocumentationHtmlNode root, DocumentationHtmlNode signature)
        {
            var result = new StringBuilder();
            var started = false;
            foreach (var child in root.Children)
            {
                if (!started)
                {
                    if (child.Contains(signature))
                    {
                        started = true;
                    }
                    continue;
                }
                if (child.Name == "h2" || child.Name == "h3" || child.Name == "h4")
                {
                    break;
                }
                if (child.Name == "hr" || IsSignature(child) || IsNotesElement(child))
                {
                    continue;
                }
                AppendParagraph(result, DocumentationHtml.Text(child, exclude: node => IsNotesElement(node) || IsSignature(node)));
            }
            return result.ToString();
        }

        /// <summary>Reads Android note tables and Oracle definition-list notes.</summary>
        /// <param name="root">The type or member description subtree.</param>
        /// <param name="documentation">The separate documentation object receiving descriptions.</param>
        private static void ReadNotes(DocumentationHtmlNode root, JavaDocumentation documentation)
        {
            foreach (var element in root.Elements())
            {
                if (element.Name == "table")
                {
                    var label = TableLabel(element);
                    if (label == "Parameters" || label == "Returns" || label == "Throws")
                    {
                        ReadTableNotes(element, label, documentation);
                    }
                }
                else if (element.Name == "dl")
                {
                    ReadDefinitionNotes(element, documentation);
                }
            }
        }

        /// <summary>Classifies generated notes that must not become summary paragraphs.</summary>
        /// <param name="node">The element to inspect.</param>
        /// <returns>Whether the element is a generated notes container.</returns>
        private static bool IsNotesElement(DocumentationHtmlNode node)
        {
            if (node.Name == "dl")
            {
                return true;
            }
            if (node.Name != "table")
            {
                return false;
            }
            var label = TableLabel(node);
            return label == "Parameters" || label == "Returns" || label == "Throws";
        }

        /// <summary>Reads a generated Android table's heading.</summary>
        /// <param name="table">The possible notes table.</param>
        /// <returns>The normalized heading, or an empty string.</returns>
        private static string TableLabel(DocumentationHtmlNode table)
        {
            var heading = First(table, node => node.Name == "th");
            return heading == null ? string.Empty : DocumentationHtml.Text(heading).TrimEnd(':').Trim();
        }

        /// <summary>Extracts parameter, return, and Java exception descriptions from Android tables.</summary>
        /// <param name="table">The generated notes table.</param>
        /// <param name="label">The recognized table heading.</param>
        /// <param name="documentation">The separate member documentation.</param>
        private static void ReadTableNotes(DocumentationHtmlNode table, string label, JavaDocumentation documentation)
        {
            foreach (var row in table.Elements())
            {
                if (row.Name != "tr")
                {
                    continue;
                }
                var cells = new List<DocumentationHtmlNode>();
                foreach (var child in row.Children)
                {
                    if (child.Name == "td")
                    {
                        cells.Add(child);
                    }
                }
                if (cells.Count == 0)
                {
                    continue;
                }
                var identity = cells.Count > 1 ? DocumentationHtml.Text(cells[0]) : string.Empty;
                var body = cells[cells.Count - 1];
                var description = DocumentationHtml.Text(body);
                if (label == "Parameters" && identity.Length != 0)
                {
                    var typePrefix = First(body, node => node.Name == "code");
                    if (typePrefix != null)
                    {
                        var prefix = DocumentationHtml.Text(typePrefix);
                        if (description == prefix)
                        {
                            description = string.Empty;
                        }
                        else if (description.StartsWith(prefix + ":", StringComparison.Ordinal))
                        {
                            description = description.Substring(prefix.Length + 1).Trim();
                        }
                    }
                    documentation.Parameters[identity] = description;
                }
                else if (label == "Returns")
                {
                    documentation.Returns = Join(documentation.Returns, description);
                }
                else if (label == "Throws" && identity.Length != 0)
                {
                    documentation.Remarks = Join(documentation.Remarks, ExceptionNote(identity, description));
                }
            }
        }

        /// <summary>Extracts Oracle Java 8 and Java 17 notes while preserving original parameter names.</summary>
        /// <param name="list">The generated definition list.</param>
        /// <param name="documentation">The separate member or type documentation.</param>
        private static void ReadDefinitionNotes(DocumentationHtmlNode list, JavaDocumentation documentation)
        {
            var label = string.Empty;
            foreach (var child in list.Children)
            {
                if (child.Name == "dt")
                {
                    label = DocumentationHtml.Text(child).Trim().TrimEnd(':');
                    continue;
                }
                if (child.Name != "dd")
                {
                    continue;
                }
                var text = DocumentationHtml.Text(child);
                if (label == "Parameters" || label == "Throws")
                {
                    var identifier = First(child, node => node.Name == "code");
                    if (identifier == null)
                    {
                        continue;
                    }
                    var name = DocumentationHtml.Text(identifier).Trim();
                    var description = RemoveNoteName(text, name);
                    if (label == "Parameters")
                    {
                        documentation.Parameters[name] = description;
                    }
                    else
                    {
                        documentation.Remarks = Join(documentation.Remarks, ExceptionNote(name, description));
                    }
                }
                else if (label == "Returns")
                {
                    documentation.Returns = Join(documentation.Returns, text);
                }
                else if (label == "API Note" || label == "Implementation Requirements" || label == "Implementation Note" || label == "Deprecated")
                {
                    documentation.Remarks = Join(documentation.Remarks, label + ": " + text);
                }
            }
        }

        /// <summary>Removes a definition-list parameter or exception name and its separator.</summary>
        /// <param name="text">The complete definition text.</param>
        /// <param name="name">The original source identifier.</param>
        /// <returns>The remaining English description.</returns>
        private static string RemoveNoteName(string text, string name)
        {
            if (!text.StartsWith(name, StringComparison.Ordinal))
            {
                return text;
            }
            return text.Substring(name.Length).TrimStart(' ', '\n', '-', '\u2013', '\u2014', ':');
        }

        /// <summary>Formats documented Java throws information as remarks rather than CLR exception claims.</summary>
        /// <param name="name">The documented Java exception name.</param>
        /// <param name="description">The documented condition.</param>
        /// <returns>A plain English Java exception note.</returns>
        private static string ExceptionNote(string name, string description)
        {
            return description.Length == 0 ? "Throws " + name + "." : "Throws " + name + ": " + description;
        }

        /// <summary>Finds a modern element ID or the nearest corresponding Java 8 member anchor.</summary>
        /// <param name="block">The member detail block.</param>
        /// <param name="heading">The member heading.</param>
        /// <param name="anchors">Visible page anchors in source order.</param>
        /// <param name="name">The source heading name.</param>
        /// <param name="typeName">The Java binary type name.</param>
        /// <returns>The decoded official anchor, or an empty string.</returns>
        private static string MemberAnchor(DocumentationHtmlNode block, DocumentationHtmlNode heading, List<DocumentationHtmlNode> anchors, string name, string typeName)
        {
            var anchor = heading.Attribute("id");
            if (anchor.Length == 0)
            {
                anchor = block.Attribute("id");
            }
            if (anchor.Length != 0)
            {
                return Uri.UnescapeDataString(anchor);
            }
            var simpleName = typeName.Substring(Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('$')) + 1);
            for (var index = anchors.Count - 1; index >= 0; index--)
            {
                var candidate = anchors[index];
                if (candidate.Offset > heading.Offset)
                {
                    continue;
                }
                anchor = candidate.Attribute("name");
                if (anchor.Length == 0)
                {
                    anchor = candidate.Attribute("id");
                }
                anchor = Uri.UnescapeDataString(anchor);
                if (anchor == name || anchor.StartsWith(name + "(", StringComparison.Ordinal) || anchor.StartsWith(name + "-", StringComparison.Ordinal)
                    || name == simpleName && (anchor.StartsWith("<init>(", StringComparison.Ordinal) || anchor.StartsWith(simpleName + "-", StringComparison.Ordinal)))
                {
                    return anchor;
                }
            }
            return string.Empty;
        }

        /// <summary>Normalizes an official heading or anchor to the original JVM member name.</summary>
        /// <param name="anchor">The official member anchor.</param>
        /// <param name="heading">The source heading text.</param>
        /// <param name="typeName">The declaring Java binary name.</param>
        /// <returns>The original method or field name, using &lt;init&gt; for constructors.</returns>
        private static string MemberName(string anchor, string heading, string typeName)
        {
            var name = heading;
            var opening = anchor.IndexOf('(');
            if (opening >= 0)
            {
                name = anchor.Substring(0, opening);
            }
            else if (anchor.Length != 0 && anchor.IndexOf('-') < 0)
            {
                name = anchor;
            }
            var simpleName = typeName.Substring(Math.Max(typeName.LastIndexOf('.'), typeName.LastIndexOf('$')) + 1);
            return name == "<init>" || name == simpleName || name == typeName.Replace('$', '.') ? "<init>" : name;
        }

        /// <summary>Removes an existing fragment while preserving the official URL and language query.</summary>
        /// <param name="url">The official page URL.</param>
        /// <returns>The page URL without a fragment.</returns>
        private static string PageUrl(string url)
        {
            var fragment = url.IndexOf('#');
            return fragment < 0 ? url : url.Substring(0, fragment);
        }

        /// <summary>Appends an observed official anchor, escaping unsafe fragment characters.</summary>
        /// <param name="url">The official page URL.</param>
        /// <param name="anchor">The decoded member anchor.</param>
        /// <returns>The official member URL, or the page URL when no anchor exists.</returns>
        private static string MemberUrl(string url, string anchor)
        {
            if (anchor.Length == 0)
            {
                return PageUrl(url);
            }
            var escaped = Uri.EscapeDataString(anchor).Replace("%28", "(", StringComparison.Ordinal).Replace("%29", ")", StringComparison.Ordinal)
                .Replace("%2C", ",", StringComparison.Ordinal).Replace("%5B", "[", StringComparison.Ordinal).Replace("%5D", "]", StringComparison.Ordinal)
                .Replace("%24", "$", StringComparison.Ordinal);
            return PageUrl(url) + "#" + escaped;
        }

        /// <summary>Joins nonempty English descriptions with an internal paragraph boundary.</summary>
        /// <param name="first">The accumulated text.</param>
        /// <param name="second">The next description.</param>
        /// <returns>The joined, trimmed description.</returns>
        private static string Join(string first, string second)
        {
            second = second.Trim();
            return second.Length == 0 ? first : first.Length == 0 ? second : first + "\n" + second;
        }

        /// <summary>Appends one nonempty paragraph to a plain-text description.</summary>
        /// <param name="result">The accumulated description.</param>
        /// <param name="paragraph">The next extracted paragraph.</param>
        private static void AppendParagraph(StringBuilder result, string paragraph)
        {
            paragraph = paragraph.Trim();
            if (paragraph.Length == 0)
            {
                return;
            }
            if (result.Length != 0)
            {
                result.Append('\n');
            }
            result.Append(paragraph);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Topten.RichTextKit;

namespace SkiaSharp.Unity.HB {
    /// <summary>
    /// Parses simple rich text tags and adds styled runs to a TextBlock.
    /// Supported tags: <b>, <i>, <u>, <s>, <sup>, <sub>,
    /// <size=N>, <color=#RRGGBB>, <color=#RRGGBBAA>, <color=name>
    /// </summary>
    public static class HBRichTextParser {

        private struct TagState {
            public bool bold;
            public bool italic;
            public UnderlineStyle underline;
            public StrikeThroughStyle strikeThrough;
            public FontVariant variant;
            public float fontSize;
            public SKColor textColor;
            public bool hasColor;
        }

        public static void Parse(TextBlock tb, string text, Style baseStyle) {
            if (string.IsNullOrEmpty(text)) return;

            var stack = new Stack<TagState>();
            var current = new TagState {
                bold = baseStyle.FontItalic ? false : false, // will read from base
                italic = baseStyle.FontItalic,
                underline = baseStyle.Underline,
                strikeThrough = baseStyle.StrikeThrough,
                variant = baseStyle.FontVariant,
                fontSize = baseStyle.FontSize,
                textColor = baseStyle.TextColor,
                hasColor = false
            };
            current.bold = baseStyle.FontWeight >= 700;
            current.italic = baseStyle.FontItalic;

            int i = 0;
            int runStart = 0;

            while (i < text.Length) {
                if (text[i] == '<') {
                    int closeIdx = text.IndexOf('>', i + 1);
                    if (closeIdx == -1) {
                        i++;
                        continue;
                    }

                    string tagContent = text.Substring(i + 1, closeIdx - i - 1).Trim();
                    string tagLower = tagContent.ToLowerInvariant();

                    bool isKnownTag = true;
                    bool isClosing = tagLower.StartsWith("/");
                    string tagName = isClosing ? tagLower.Substring(1) : tagLower;

                    // Check for value tags like size=24 or color=#FF0000
                    string tagValue = null;
                    int eqIdx = tagName.IndexOf('=');
                    if (eqIdx >= 0) {
                        tagValue = tagName.Substring(eqIdx + 1);
                        tagName = tagName.Substring(0, eqIdx);
                    }

                    switch (tagName) {
                        case "b":
                        case "i":
                        case "u":
                        case "s":
                        case "sup":
                        case "sub":
                        case "size":
                        case "color":
                            break;
                        default:
                            isKnownTag = false;
                            break;
                    }

                    if (!isKnownTag) {
                        i++;
                        continue;
                    }

                    // Flush text before this tag
                    if (i > runStart) {
                        string segment = text.Substring(runStart, i - runStart);
                        AddStyledRun(tb, segment, baseStyle, current);
                    }

                    if (isClosing) {
                        // Pop state
                        if (stack.Count > 0) {
                            current = stack.Pop();
                        }
                    } else {
                        // Push current state
                        stack.Push(current);

                        switch (tagName) {
                            case "b":
                                current.bold = true;
                                break;
                            case "i":
                                current.italic = true;
                                break;
                            case "u":
                                current.underline = UnderlineStyle.Solid;
                                break;
                            case "s":
                                current.strikeThrough = StrikeThroughStyle.Solid;
                                break;
                            case "sup":
                                current.variant = FontVariant.SuperScript;
                                break;
                            case "sub":
                                current.variant = FontVariant.SubScript;
                                break;
                            case "size":
                                if (tagValue != null && float.TryParse(tagValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sz)) {
                                    current.fontSize = sz;
                                }
                                break;
                            case "color":
                                if (tagValue != null) {
                                    if (TryParseColor(tagValue, out SKColor c)) {
                                        current.textColor = c;
                                        current.hasColor = true;
                                    }
                                }
                                break;
                        }
                    }

                    runStart = closeIdx + 1;
                    i = runStart;
                } else {
                    i++;
                }
            }

            // Flush remaining text
            if (runStart < text.Length) {
                string segment = text.Substring(runStart);
                AddStyledRun(tb, segment, baseStyle, current);
            }
        }

        private static void AddStyledRun(TextBlock tb, string text, Style baseStyle, TagState state) {
            if (string.IsNullOrEmpty(text)) return;

            var style = baseStyle.Modify(
                fontWeight: state.bold ? 700 : (int?)baseStyle.FontWeight,
                fontItalic: state.italic,
                underline: state.underline,
                strikeThrough: state.strikeThrough,
                fontVariant: state.variant,
                fontSize: state.fontSize,
                textColor: state.hasColor ? state.textColor : (SKColor?)null
            );

            tb.AddText(text, style);
        }

        private static bool TryParseColor(string value, out SKColor color) {
            color = default;

            if (value.StartsWith("#")) {
                string hex = value.Substring(1);
                if (hex.Length == 6) {
                    if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) {
                        color = new SKColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
                        return true;
                    }
                } else if (hex.Length == 8) {
                    if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint rgba)) {
                        color = new SKColor((byte)((rgba >> 24) & 0xFF), (byte)((rgba >> 16) & 0xFF), (byte)((rgba >> 8) & 0xFF), (byte)(rgba & 0xFF));
                        return true;
                    }
                }
            }

            // Named colors
            switch (value.ToLowerInvariant()) {
                case "red": color = new SKColor(255, 0, 0); return true;
                case "green": color = new SKColor(0, 128, 0); return true;
                case "blue": color = new SKColor(0, 0, 255); return true;
                case "white": color = new SKColor(255, 255, 255); return true;
                case "black": color = new SKColor(0, 0, 0); return true;
                case "yellow": color = new SKColor(255, 255, 0); return true;
                case "cyan": color = new SKColor(0, 255, 255); return true;
                case "magenta": color = new SKColor(255, 0, 255); return true;
                case "orange": color = new SKColor(255, 165, 0); return true;
                case "grey": case "gray": color = new SKColor(128, 128, 128); return true;
            }

            return false;
        }
    }
}

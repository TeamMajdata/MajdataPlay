#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// A scrollable, read-only text view that draws only its visible lines, so megabyte-sized bodies stay responsive.
    /// IMGUI cannot draw such text as one label, because a label's mesh is limited to about 16,000 characters.
    /// </summary>
    internal sealed class CurlNetworkTextView
    {
        /// <summary>
        /// The length at which long lines are wrapped, keeping every drawn label well below the IMGUI mesh limit.
        /// </summary>
        public const int MaxLineLength = 400;

        /// <summary>
        /// The space left of each line.
        /// </summary>
        private const float Padding = 4f;

        /// <summary>
        /// Monospace fonts tried in order.
        /// </summary>
        private static readonly string[] s_monospaceFonts = { "Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" };

        /// <summary>
        /// The shared monospace font, created on first use.
        /// </summary>
        private static Font? s_monospaceFont;

        /// <summary>
        /// The style used to draw lines, created on first draw.
        /// </summary>
        private GUIStyle? _style;

        /// <summary>
        /// Produces the text of a line on demand.
        /// </summary>
        private Func<int, string> _lineProvider = static _ => string.Empty;

        /// <summary>
        /// The scroll position.
        /// </summary>
        private Vector2 _scroll;

        /// <summary>
        /// The widest line drawn so far, which sets the horizontal scroll range.
        /// </summary>
        private float _contentWidth;

        /// <summary>
        /// Gets the number of lines.
        /// </summary>
        public int LineCount { get; private set; }

        /// <summary>
        /// Shows text, wrapping lines longer than <see cref="MaxLineLength"/>.
        /// </summary>
        /// <param name="text">The text to show.</param>
        /// <param name="keepScroll">Whether to keep the scroll position, for text that only grew.</param>
        public void SetText(string text, bool keepScroll)
        {
            var lines = SplitLines(text);
            SetLines(lines.Count, index => lines[index], keepScroll);
        }

        /// <summary>
        /// Shows lines produced on demand.
        /// </summary>
        /// <param name="count">The number of lines.</param>
        /// <param name="lineProvider">Produces the text of a line from its index.</param>
        /// <param name="keepScroll">Whether to keep the scroll position.</param>
        public void SetLines(int count, Func<int, string> lineProvider, bool keepScroll)
        {
            LineCount = count;
            _lineProvider = lineProvider;
            if (!keepScroll)
            {
                _scroll = Vector2.zero;
                _contentWidth = 0;
            }
        }

        /// <summary>
        /// Draws the visible lines.
        /// </summary>
        /// <param name="rect">The area of the view.</param>
        public void OnGUI(Rect rect)
        {
            _style ??= CreateStyle();
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var contentRect = new Rect(0, 0, Math.Max(_contentWidth, rect.width - 16), (LineCount * lineHeight) + Padding);
            _scroll = GUI.BeginScrollView(rect, _scroll, contentRect);
            var first = Math.Max(0, (int)(_scroll.y / lineHeight));
            var last = Math.Min(LineCount, first + (int)Math.Ceiling(rect.height / lineHeight) + 1);
            for (var i = first; i < last; i++)
            {
                var content = new GUIContent(_lineProvider(i));
                var width = _style.CalcSize(content).x + (Padding * 2);
                _contentWidth = Math.Max(_contentWidth, width);
                GUI.Label(new Rect(Padding, i * lineHeight, width, lineHeight), content, _style);
            }
            GUI.EndScrollView();
        }

        /// <summary>
        /// Splits text into lines, wrapping lines longer than <see cref="MaxLineLength"/>.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The lines without terminators.</returns>
        internal static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            var start = 0;
            while (start <= text.Length)
            {
                var end = text.IndexOf('\n', start);
                if (end < 0)
                {
                    end = text.Length;
                }
                var lineEnd = end > start && text[end - 1] == '\r' ? end - 1 : end;
                for (var chunk = start; chunk < lineEnd; chunk += MaxLineLength)
                {
                    lines.Add(text.Substring(chunk, Math.Min(MaxLineLength, lineEnd - chunk)));
                }
                if (lineEnd == start)
                {
                    lines.Add(string.Empty);
                }
                start = end + 1;
            }

            return lines;
        }

        /// <summary>
        /// Creates the line style with a monospace font when the system has one.
        /// </summary>
        /// <returns>The style.</returns>
        private static GUIStyle CreateStyle()
        {
            if (s_monospaceFont == null)
            {
                s_monospaceFont = Font.CreateDynamicFontFromOSFont(s_monospaceFonts, 12);
            }
            var style = new GUIStyle(EditorStyles.label)
            {
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Overflow,
            };
            if (s_monospaceFont != null)
            {
                style.font = s_monospaceFont;
            }

            return style;
        }
    }
}

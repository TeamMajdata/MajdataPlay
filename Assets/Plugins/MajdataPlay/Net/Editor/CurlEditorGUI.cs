#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// IMGUI building blocks shared by the Curl Debugger panels: collapsible sections, name and value rows, and
    /// simple tables of selectable cells.
    /// </summary>
    internal static class CurlEditorGUI
    {
        /// <summary>
        /// The widest the name column of name and value rows gets.
        /// </summary>
        private const float KeyColumnWidth = 190f;

        /// <summary>
        /// The longest value drawn in one selectable label, keeping it below the IMGUI mesh limit.
        /// </summary>
        private const int MaxLabelLength = 8000;

        /// <summary>
        /// The style of section headers, created on first use.
        /// </summary>
        private static GUIStyle? s_sectionStyle;

        /// <summary>
        /// The style of names and table headings, created on first use.
        /// </summary>
        private static GUIStyle? s_keyStyle;

        /// <summary>
        /// The style of wrapped values, created on first use.
        /// </summary>
        private static GUIStyle? s_valueStyle;

        /// <summary>
        /// The style of single-line cells, created on first use.
        /// </summary>
        private static GUIStyle? s_cellStyle;

        /// <summary>
        /// Gets the style of section headers: a bold foldout.
        /// </summary>
        public static GUIStyle SectionStyle
        {
            get => s_sectionStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
        }

        /// <summary>
        /// Gets the style of names and table headings: a bold, clipped label.
        /// </summary>
        public static GUIStyle KeyStyle
        {
            get => s_keyStyle ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
        }

        /// <summary>
        /// Gets the style of wrapped values.
        /// </summary>
        public static GUIStyle ValueStyle
        {
            get => s_valueStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        }

        /// <summary>
        /// Gets the style of single-line cells.
        /// </summary>
        public static GUIStyle CellStyle
        {
            get => s_cellStyle ??= new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip, richText = false };
        }

        /// <summary>
        /// Draws a collapsible section header.
        /// </summary>
        /// <param name="isExpanded">Whether the section is expanded.</param>
        /// <param name="title">The section title.</param>
        /// <returns>Whether the section is expanded after the user's input.</returns>
        public static bool DrawSection(bool isExpanded, string title)
        {
            return EditorGUILayout.Foldout(isExpanded, title, true, SectionStyle);
        }

        /// <summary>
        /// Draws name and value rows; values wrap and can be selected.
        /// </summary>
        /// <param name="pairs">The rows.</param>
        /// <param name="width">The width available to the rows.</param>
        public static void DrawKeyValues(IReadOnlyList<KeyValuePair<string, string>> pairs, float width)
        {
            if (pairs.Count == 0)
            {
                EditorGUILayout.LabelField("(none)", EditorStyles.miniLabel);
                return;
            }
            var keyWidth = Math.Min(KeyColumnWidth, width * 0.4f);
            var lineHeight = EditorGUIUtility.singleLineHeight;
            foreach (var pair in pairs)
            {
                var value = Truncate(pair.Value);
                var valueWidth = Math.Max(20f, width - keyWidth);
                var height = Math.Max(lineHeight, ValueStyle.CalcHeight(new GUIContent(value), valueWidth));
                var row = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
                GUI.Label(new Rect(row.x + 12, row.y, keyWidth - 12, lineHeight), new GUIContent(pair.Key, pair.Key), KeyStyle);
                EditorGUI.SelectableLabel(new Rect(row.x + keyWidth, row.y, row.width - keyWidth, height), value, ValueStyle);
            }
        }

        /// <summary>
        /// Draws a table with a heading row and selectable single-line cells.
        /// </summary>
        /// <param name="headings">The column headings.</param>
        /// <param name="weights">The share of the width of every column.</param>
        /// <param name="rows">The cells of every row, one per column.</param>
        /// <param name="width">The width available to the table.</param>
        public static void DrawTable(IReadOnlyList<string> headings, IReadOnlyList<float> weights, IReadOnlyList<string[]> rows, float width)
        {
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var widths = new float[headings.Count];
            var tableWidth = 0f;
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = Math.Max(48f, width * weights[i]);
                tableWidth += widths[i];
            }
            var headingRow = GUILayoutUtility.GetRect(tableWidth, lineHeight, GUILayout.ExpandWidth(true));
            var x = headingRow.x;
            for (var i = 0; i < widths.Length; i++)
            {
                GUI.Label(new Rect(x, headingRow.y, widths[i] - 4, headingRow.height), headings[i], KeyStyle);
                x += widths[i];
            }
            foreach (var row in rows)
            {
                var rowRect = GUILayoutUtility.GetRect(tableWidth, lineHeight, GUILayout.ExpandWidth(true));
                x = rowRect.x;
                for (var i = 0; i < widths.Length && i < row.Length; i++)
                {
                    EditorGUI.SelectableLabel(new Rect(x, rowRect.y, widths[i] - 4, rowRect.height), Truncate(row[i]), CellStyle);
                    x += widths[i];
                }
            }
        }

        /// <summary>
        /// Draws multi-line selectable text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="width">The width available to the text.</param>
        public static void DrawSelectableBlock(string text, float width)
        {
            text = Truncate(text);
            var height = ValueStyle.CalcHeight(new GUIContent(text), width) + 4;
            EditorGUILayout.SelectableLabel(text, ValueStyle, GUILayout.Height(height));
        }

        /// <summary>
        /// Shortens text that one label cannot draw.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The text, cut at the label limit with an ellipsis.</returns>
        public static string Truncate(string text)
        {
            return text.Length <= MaxLabelLength ? text : text.Substring(0, MaxLabelLength) + "...";
        }
    }
}

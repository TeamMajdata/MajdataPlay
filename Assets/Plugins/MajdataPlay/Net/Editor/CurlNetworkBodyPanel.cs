#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MajdataPlay.Net.Editor
{
    /// <summary>
    /// Shows one captured body in the details panel as formatted text, raw text, a hex dump or an image preview, and
    /// caches the body until its record changes.
    /// </summary>
    internal sealed class CurlNetworkBodyPanel : IDisposable
    {
        /// <summary>
        /// The text view that draws text and hex dumps.
        /// </summary>
        private readonly CurlNetworkTextView _textView = new();

        /// <summary>
        /// The cached body.
        /// </summary>
        private byte[] _data = Array.Empty<byte>();

        /// <summary>
        /// The <c>Content-Type</c> of the cached body.
        /// </summary>
        private string? _contentType;

        /// <summary>
        /// The record the cached body belongs to, or -1.
        /// </summary>
        private long _recordId = -1;

        /// <summary>
        /// The record version the cached body was read at.
        /// </summary>
        private int _version = -1;

        /// <summary>
        /// The mode the text view currently shows, or <see langword="null"/> when it must be rebuilt.
        /// </summary>
        private CurlNetworkBodyMode? _renderedMode;

        /// <summary>
        /// Whether the next rebuild keeps the scroll position, because the same body only grew.
        /// </summary>
        private bool _keepScroll;

        /// <summary>
        /// The decoded image, created on demand.
        /// </summary>
        private Texture2D? _preview;

        /// <summary>
        /// Whether the cached body could not be decoded as an image, so decoding is not retried every frame.
        /// </summary>
        private bool _isPreviewInvalid;

        /// <summary>
        /// Gets the cached body.
        /// </summary>
        public byte[] Data
        {
            get => _data;
        }

        /// <summary>
        /// Gets whether the body is a PNG or JPEG image that can be previewed.
        /// </summary>
        public bool CanPreview
        {
            get
            {
                var mediaType = CurlNetworkFormatter.GetMediaType(_contentType);
                return mediaType == "image/png" || mediaType == "image/jpeg";
            }
        }

        /// <summary>
        /// Gets whether the body is <c>application/x-www-form-urlencoded</c> text.
        /// </summary>
        public bool IsForm
        {
            get => CurlNetworkFormatter.GetMediaType(_contentType) == "application/x-www-form-urlencoded";
        }

        /// <summary>
        /// Reads the body again when the record or its version changed.
        /// </summary>
        /// <param name="recordId">The record id.</param>
        /// <param name="version">The record version.</param>
        /// <param name="readBody">Copies the body from the record.</param>
        /// <param name="contentType">The <c>Content-Type</c> of the body.</param>
        public void Update(long recordId, int version, Func<byte[]> readBody, string? contentType)
        {
            if (recordId == _recordId && version == _version)
            {
                return;
            }
            _keepScroll = recordId == _recordId;
            _recordId = recordId;
            _version = version;
            _data = readBody();
            _contentType = contentType;
            _renderedMode = null;
            _isPreviewInvalid = false;
            DestroyPreview();
        }

        /// <summary>
        /// Forgets the cached body.
        /// </summary>
        public void Clear()
        {
            _recordId = -1;
            _version = -1;
            _data = Array.Empty<byte>();
            _contentType = null;
            _renderedMode = null;
            DestroyPreview();
        }

        /// <summary>
        /// Gets the body as text for copying.
        /// </summary>
        /// <param name="mode">The display mode.</param>
        /// <returns>The formatted text, the raw text, or the whole hex dump for hex and image previews.</returns>
        public string GetText(CurlNetworkBodyMode mode)
        {
            switch (ResolveMode(mode))
            {
                case CurlNetworkBodyMode.Hex:
                case CurlNetworkBodyMode.Preview:
                    var lines = new string[CurlNetworkFormatter.GetHexLineCount(_data.Length)];
                    for (var i = 0; i < lines.Length; i++)
                    {
                        lines[i] = CurlNetworkFormatter.FormatHexLine(_data, i);
                    }
                    return string.Join("\n", lines);
                case CurlNetworkBodyMode.Pretty:
                    return FormatPretty();
                default:
                    return CurlNetworkFormatter.DecodeText(_data, _contentType);
            }
        }

        /// <summary>
        /// Gets the form fields of a form body.
        /// </summary>
        /// <returns>The decoded fields.</returns>
        public List<KeyValuePair<string, string>> GetFormFields()
        {
            return CurlNetworkFormatter.ParseFormUrlEncoded(CurlNetworkFormatter.DecodeText(_data, _contentType));
        }

        /// <summary>
        /// Draws the body.
        /// </summary>
        /// <param name="rect">The area to draw in.</param>
        /// <param name="mode">The display mode.</param>
        public void OnGUI(Rect rect, CurlNetworkBodyMode mode)
        {
            var resolved = ResolveMode(mode);
            if (resolved == CurlNetworkBodyMode.Preview)
            {
                DrawPreview(rect);
                return;
            }
            if (_renderedMode != resolved)
            {
                Render(resolved);
            }
            _textView.OnGUI(rect);
        }

        /// <summary>
        /// Destroys the preview texture.
        /// </summary>
        public void Dispose()
        {
            DestroyPreview();
        }

        /// <summary>
        /// Picks the mode actually shown: a preview only for images, and a hex dump instead of pretty text for binary bodies.
        /// </summary>
        /// <param name="mode">The requested mode.</param>
        /// <returns>The mode to show.</returns>
        private CurlNetworkBodyMode ResolveMode(CurlNetworkBodyMode mode)
        {
            if (mode == CurlNetworkBodyMode.Preview && !CanPreview)
            {
                mode = CurlNetworkBodyMode.Pretty;
            }
            if (mode == CurlNetworkBodyMode.Pretty && CurlNetworkFormatter.IsBinary(_data, _contentType))
            {
                return CurlNetworkBodyMode.Hex;
            }

            return mode;
        }

        /// <summary>
        /// Fills the text view for a mode.
        /// </summary>
        /// <param name="mode">A text or hex mode.</param>
        private void Render(CurlNetworkBodyMode mode)
        {
            if (mode == CurlNetworkBodyMode.Hex)
            {
                var data = _data;
                _textView.SetLines(CurlNetworkFormatter.GetHexLineCount(data.Length), line => CurlNetworkFormatter.FormatHexLine(data, line), _keepScroll);
            }
            else
            {
                var text = mode == CurlNetworkBodyMode.Pretty ? FormatPretty() : CurlNetworkFormatter.DecodeText(_data, _contentType);
                _textView.SetText(text, _keepScroll);
            }
            _renderedMode = mode;
            _keepScroll = true;
        }

        /// <summary>
        /// Formats the body as text, re-indenting JSON.
        /// </summary>
        /// <returns>The formatted text.</returns>
        private string FormatPretty()
        {
            var text = CurlNetworkFormatter.DecodeText(_data, _contentType);

            return CurlNetworkFormatter.IsJson(_contentType, text) ? CurlNetworkFormatter.PrettyPrintJson(text) : text;
        }

        /// <summary>
        /// Draws the decoded image scaled to fit, with its size.
        /// </summary>
        /// <param name="rect">The area to draw in.</param>
        private void DrawPreview(Rect rect)
        {
            if (_preview == null && !_isPreviewInvalid)
            {
                _preview = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                if (!_preview.LoadImage(_data, false))
                {
                    DestroyPreview();
                    _isPreviewInvalid = true;
                }
            }
            if (_preview == null)
            {
                EditorGUI.HelpBox(new Rect(rect.x, rect.y, rect.width, 40), "The image could not be decoded; it may be incomplete.", MessageType.Warning);
                return;
            }
            var labelHeight = EditorGUIUtility.singleLineHeight;
            GUI.Label(new Rect(rect.x, rect.y, rect.width, labelHeight), $"{_preview.width} × {_preview.height}", EditorStyles.miniLabel);
            var imageRect = new Rect(rect.x, rect.y + labelHeight, rect.width, Math.Max(0, rect.height - labelHeight));
            GUI.DrawTexture(imageRect, _preview, ScaleMode.ScaleToFit);
        }

        /// <summary>
        /// Destroys the preview texture.
        /// </summary>
        private void DestroyPreview()
        {
            if (_preview != null)
            {
                Object.DestroyImmediate(_preview);
            }
            _preview = null;
        }
    }
}

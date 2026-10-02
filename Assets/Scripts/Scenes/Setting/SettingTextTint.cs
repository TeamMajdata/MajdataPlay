using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Scenes.Setting
{
    // Animate the renderer tint without dirtying TMP's layout or glyph mesh.
    internal sealed class SettingTextTint : IDisposable
    {
        readonly TextMeshProUGUI _text;
        readonly List<CanvasRenderer> _renderers = new(4);
        Color _color;
        bool _isDisposed;

        internal SettingTextTint(TextMeshProUGUI text)
        {
            _text = text;
            _color = text.color;
            text.color = Color.white;
            text.OnPreRenderText += OnPreRenderText;
            RefreshRenderers();
        }

        internal void SetColor(Color color)
        {
            if (_isDisposed || _color == color)
            {
                return;
            }
            _color = color;
            ApplyColor();
        }

        void OnPreRenderText(TMP_TextInfo textInfo)
        {
            // A new glyph can create another fallback-font or atlas submesh.
            RefreshRenderers();
        }

        void RefreshRenderers()
        {
            _text.GetComponentsInChildren(true, _renderers);
            ApplyColor();
        }

        void ApplyColor()
        {
            for (var i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (renderer != null)
                {
                    renderer.SetColor(_color);
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            if (_text != null)
            {
                _text.OnPreRenderText -= OnPreRenderText;
            }
            _renderers.Clear();
        }
    }
}

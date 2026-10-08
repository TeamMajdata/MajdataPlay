/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core.Unmanaged;
using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering.Util;
using UnityEngine;

public class CubismOffscreen : MonoBehaviour
{
    #region Factory Methods

    /// <summary>
    /// Creates offscreens for a <see cref="CubismModel"/>.
    /// </summary>
    /// <param name="unmanagedModel">Handle to unmanaged model.</param>
    /// <returns>Offscreens root.</returns>
    internal static GameObject CreateOffscreens(CubismUnmanagedModel unmanagedModel)
    {
        var root = new GameObject("Offscreens");


        // Create offscreens.
        var unmanagedOffscreens = unmanagedModel.Offscreens;
        var buffer = new CubismOffscreen[unmanagedOffscreens.Count];


        for (var i = 0; i < buffer.Length; ++i)
        {
            var proxy = new GameObject();


            buffer[i] = proxy.AddComponent<CubismOffscreen>();


            buffer[i].transform.SetParent(root.transform);
            buffer[i].Reset(unmanagedModel, i);
        }


        return root;
    }

    #endregion


    /// <summary>
    /// Unmanaged offscreens from unmanaged model.
    /// </summary>
    private CubismUnmanagedOffscreens UnmanagedOffscreens { get; set; }

    /// <summary>
    /// <see cref="UnmanagedIndex"/> backing field.
    /// </summary>
    [SerializeField, HideInInspector]
    private int _unmanagedIndex = -1;

    /// <summary>
    /// Position in unmanaged arrays.
    /// </summary>
    internal int UnmanagedIndex
    {
        get { return _unmanagedIndex; }
        private set { _unmanagedIndex = value; }
    }

    /// <summary>
    /// Gets the color blend mode of the Offscreen.
    /// </summary>
    public BlendTypes.ColorBlend ColorBlend
    {
        get
        {
            // Pull data.
            return (BlendTypes.ColorBlend)(UnmanagedOffscreens.BlendModes[UnmanagedIndex] & 0xFF);
        }
    }

    /// <summary>
    /// Gets the alpha blend mode of the Offscreen.
    /// </summary>
    public BlendTypes.AlphaBlend AlphaBlend
    {
        get
        {
            // Pull data.
            return (BlendTypes.AlphaBlend)((UnmanagedOffscreens.BlendModes[UnmanagedIndex] >> 8) & 0xFF);
        }
    }

    /// <summary>
    /// Gets the current opacity of the Offscreen.
    /// </summary>
    public float Opacity
    {
        get
        {
            return UnmanagedOffscreens.Opacities[UnmanagedIndex];
        }
    }

    /// <summary>
    /// Gets the index of the owner of the offscreen.
    /// </summary>
    public int OwnerIndex
    {
        get
        {
            return UnmanagedOffscreens.OwnerIndices[UnmanagedIndex];
        }
    }

    /// <summary>
    /// <see cref="MultiplyColor"/> backing field.
    /// </summary>
    private Color _multiplyColor;

    /// <summary>
    /// Copy of MultiplyColor.
    /// </summary>
    public Color MultiplyColor
    {
        get
        {
            var index = UnmanagedIndex * 4;

            // Pull data.
            _multiplyColor.r = UnmanagedOffscreens.MultiplyColors[index];
            _multiplyColor.g = UnmanagedOffscreens.MultiplyColors[index + 1];
            _multiplyColor.b = UnmanagedOffscreens.MultiplyColors[index + 2];
            _multiplyColor.a = UnmanagedOffscreens.MultiplyColors[index + 3];

            return _multiplyColor;
        }
    }

    /// <summary>
    /// <see cref="ScreenColor"/> backing field.
    /// </summary>
    private Color _screenColor;

    /// <summary>
    /// Copy of ScreenColor.
    /// </summary>
    public Color ScreenColor
    {
        get
        {
            var index = UnmanagedIndex * 4;

            // Pull data.
            _screenColor.r = UnmanagedOffscreens.ScreenColors[index];
            _screenColor.g = UnmanagedOffscreens.ScreenColors[index + 1];
            _screenColor.b = UnmanagedOffscreens.ScreenColors[index + 2];
            _screenColor.a = UnmanagedOffscreens.ScreenColors[index + 3];

            return _screenColor;
        }
    }

    /// <summary>
    /// Gets the number of masks associated with this offscreen.
    /// </summary>
    public int MaskCount
    {
        get
        {
            return UnmanagedOffscreens.MaskCounts[UnmanagedIndex];
        }
    }

    /// <summary>
    /// <see cref="Masks"/> backing field.
    /// </summary>
    private CubismDrawable[] _masks;

    /// <summary>
    /// Gets the masks associated with this offscreen.
    /// </summary>
    public CubismDrawable[] Masks
    {
        get
        {
            if (_masks == null)
            {
                var drawables = this
                    .FindCubismModel(true)
                    .Drawables;

                var indices = UnmanagedOffscreens.Masks[UnmanagedIndex];

                // Pull data.
                _masks = new CubismDrawable[MaskCount];

                for (var i = 0; i < _masks.Length; ++i)
                {
                    for (var j = 0; j < drawables.Length; ++j)
                    {
                        if (drawables[j].UnmanagedIndex != indices[i])
                        {
                            continue;
                        }

                        _masks[i] = drawables[j];

                        break;
                    }
                }
            }

            return _masks;
        }
    }

    #region Constant Flags
    /// <summary>
    /// True if double-sided.
    /// </summary>
    public bool IsDoubleSided
    {
        get
        {
            // Get address.
            var flags = UnmanagedOffscreens.ConstantFlags;


            // Pull data.
            return flags[UnmanagedIndex].HasIsDoubleSidedFlag();
        }
    }

    /// <summary>
    /// True if masking is requested.
    /// </summary>
    public bool IsMasked
    {
        get
        {
            // Get address.
            var counts = UnmanagedOffscreens.MaskCounts;


            // Pull data.
            return counts[UnmanagedIndex] > 0;
        }
    }

    /// <summary>
    /// True if inverted mask.
    /// </summary>
    public bool IsInverted
    {
        get
        {
            // Get address.
            var flags = UnmanagedOffscreens.ConstantFlags;


            // Pull data.
            return flags[UnmanagedIndex].HasIsInvertedMaskFlag();
        }
    }
    #endregion

    /// <summary>
    /// Revives instance.
    /// </summary>
    /// <param name="unmanagedModel">Handle to unmanaged model.</param>
    internal void Revive(CubismUnmanagedModel unmanagedModel)
    {
        UnmanagedOffscreens = unmanagedModel.Offscreens;
    }

    /// <summary>
    /// Restores instance to initial state.
    /// </summary>
    /// <param name="unmanagedModel">Handle to unmanaged model.</param>
    /// <param name="unmanagedIndex">Position in unmanaged arrays.</param>
    private void Reset(CubismUnmanagedModel unmanagedModel, int unmanagedIndex)
    {
        Revive(unmanagedModel);

        UnmanagedIndex = unmanagedIndex;

        name = "Offscreen_";
        name += unmanagedModel.Parts.Ids[OwnerIndex];
    }
}

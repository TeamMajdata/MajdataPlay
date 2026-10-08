/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

using Live2D.Cubism.Core.Unmanaged;

namespace Live2D.Cubism.Rendering.Util
{
    public static class BlendTypes
    {
        public enum ColorBlend
        {
            Normal = CubismCoreDll.ColorBlendType_Normal,
            Add = CubismCoreDll.ColorBlendType_AddCompatible,
            Multiply = CubismCoreDll.ColorBlendType_MultiplyCompatible,
            Add_R2 = CubismCoreDll.ColorBlendType_Add,
            Add_Glow = CubismCoreDll.ColorBlendType_AddGlow,
            Darken = CubismCoreDll.ColorBlendType_Darken,
            Multiply_R2 = CubismCoreDll.ColorBlendType_Multiply,
            ColorBurn = CubismCoreDll.ColorBlendType_ColorBurn,
            LinearBurn = CubismCoreDll.ColorBlendType_LinearBurn,
            Lighten = CubismCoreDll.ColorBlendType_Lighten,
            Screen = CubismCoreDll.ColorBlendType_Screen,
            ColorDodge = CubismCoreDll.ColorBlendType_ColorDodge,
            Overlay = CubismCoreDll.ColorBlendType_Overlay,
            SoftLight = CubismCoreDll.ColorBlendType_SoftLight,
            HardLight = CubismCoreDll.ColorBlendType_HardLight,
            LinearLight = CubismCoreDll.ColorBlendType_LinearLight,
            Hue = CubismCoreDll.ColorBlendType_Hue,
            Color = CubismCoreDll.ColorBlendType_Color,
            End
        }

        public enum AlphaBlend
        {
            Over = CubismCoreDll.AlphaBlendType_Over,
            Atop = CubismCoreDll.AlphaBlendType_Atop,
            Out = CubismCoreDll.AlphaBlendType_Out,
            Conjoint = CubismCoreDll.AlphaBlendType_ConjointOver,
            Disjoint = CubismCoreDll.AlphaBlendType_DisjointOver,
            End
        }
    }
}

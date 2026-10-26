/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using UnityEngine;


namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Default shader assets.
    /// </summary>
    public static class CubismBuiltinShaders
    {
        /// <summary>
        /// Shader for drawing masks.
        /// </summary>
        public static Shader Mask
        {
            get
            {
                return Shader.Find("Live2D Cubism/Mask");
            }
        }

        /// <summary>
        /// Shader for drawing planes for blend mode.
        /// </summary>
        public static Shader Blit
        {
            get
            {
                return Shader.Find("Unlit/BlendMode/Blit");
            }
        }

        /// <summary>
        /// Shader for blend mode compatible with versions before Cubism 5.2.
        /// </summary>
        public static Shader CompatibleBlend
        {
            get
            {
                return Shader.Find("Unlit/BlendMode/CompatibleBlend");
            }
        }

        /// <summary>
        /// Blend mode shader for versions after Cubism 5.3.
        /// </summary>
        public static Shader BlendMode
        {
            get
            {
                return Shader.Find("Unlit/BlendMode/BlendImage");
            }
        }

        /// <summary>
        /// Mask shader for Offscreen.
        /// </summary>
        public static Shader OffscreenMask
        {
            get
            {
                return Shader.Find("Unlit/BlendMode/OffscreenMask");
            }
        }

        /// <summary>
        /// Blend mode offscreen shader for versions after Cubism 5.3.
        /// </summary>
        public static Shader OffscreenBlend
        {
            get
            {
                return Shader.Find("Unlit/BlendMode/OffscreenBlend");
            }
        }

        /// <summary>
        /// Shader for offscreen rendering blend mode compatible with versions before Cubism 5.2.
        /// </summary>
        public static Shader OffscreenCompatibleBlend
        {
            get
            {
                return Shader.Find("Unlit/BlendMode/OffscreenCompatibleBlend");
            }
        }

        /// <summary>
        /// Shader for Scene View picking.
        /// </summary>
        public static Shader TransparentPicking
        {
            get
            {
                return Shader.Find("Live2D Cubism/TransparentPicking");
            }
        }
    }
}

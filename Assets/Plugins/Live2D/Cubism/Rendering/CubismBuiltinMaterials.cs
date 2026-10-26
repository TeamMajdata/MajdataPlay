/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Rendering.Util;
using UnityEngine;


namespace Live2D.Cubism.Rendering
{
    /// <summary>
    /// Default materials.
    /// </summary>
    public static class CubismBuiltinMaterials
    {
        /// <summary>
        /// Default mask material.
        /// </summary>
        public static Material Mask
        {
            get { return LoadMaskMaterial(); }
        }

        /// <summary>
        /// Default culled mask material.
        /// </summary>
        public static Material MaskCulling
        {
            get { return LoadMaskCullingMaterial(); }
        }

        /// <summary>
        /// Unlit blit material for BlendMode.
        /// </summary>
        public static Material UnlitBlit
        {
            get
            {
                return LoadBlendModeMaterial("UnlitBlit");
            }
        }

        /// <summary>
        /// <see cref="OffscreenMask"/>'s backing field.
        /// </summary>
        private static Material _offscreenMask;

        /// <summary>
        /// Unlit mask material for BlendMode.
        /// </summary>
        public static Material OffscreenMask
        {
            get
            {
                if (_offscreenMask == null)
                {
                    _offscreenMask = LoadBlendModeMaterial("OffscreenMask");
                }

                return _offscreenMask;
            }
        }

        /// <summary>
        /// <see cref="OffscreenMaskCulling"/>'s backing field.
        /// </summary>
        private static Material _offscreenMaskCulling;

        /// <summary>
        /// Unlit mask material for BlendMode.
        /// </summary>
        public static Material OffscreenMaskCulling
        {
            get
            {
                if (_offscreenMaskCulling == null)
                {
                    _offscreenMaskCulling = LoadBlendModeMaterial("OffscreenMaskCulling");
                }

                return _offscreenMaskCulling;
            }
        }

        /// <summary>
        /// <see cref="TransparentPicking"/>'s backing field.
        /// </summary>
        private static Material _transparentPicking;

        /// <summary>
        /// Transparent material for Scene View picking.
        /// </summary>
        public static Material TransparentPicking
        {
            get
            {
                if (_transparentPicking == null)
                {
                    _transparentPicking = LoadTransparentPickingMaterial();
                }

                return _transparentPicking;
            }
        }

        /// <summary>
        /// Returns a material for the given BlendMode.
        /// </summary>
        /// <param name="materialName">Name</param>
        /// <param name="colorBlend">ColorBlend type</param>
        /// <param name="alphaBlend">AlphaBlend type</param>
        /// <param name="masked">Is masked?</param>
        /// <param name="inverted">Is using invert mask?</param>
        /// <param name="isDoubleSided">Is double-sided rendering?</param>
        /// <returns></returns>
        public static Material GetBlendModeMaterial(string materialName, BlendTypes.ColorBlend colorBlend, BlendTypes.AlphaBlend alphaBlend, bool masked, bool inverted, bool isDoubleSided)
        {
            var maskedString = masked ? "Masked" : "";
            var invertedString = masked && inverted ? "Invert" : "";
            var cullingString = isDoubleSided ? "" : "Culling";

            switch (colorBlend)
            {
                case BlendTypes.ColorBlend.Add:
                case BlendTypes.ColorBlend.Multiply:
                    materialName += $"{invertedString}{maskedString}{colorBlend}{cullingString}";
                    break;
                default:
                    materialName += $"{invertedString}{maskedString}{colorBlend}{alphaBlend}{cullingString}";
                    break;
            }

            return LoadBlendModeMaterial(materialName);
        }


        #region Helper Methods

        /// <summary>
        /// Resource directory of builtin <see cref="Material"/>s.
        /// </summary>
        private const string ResourcesDirectory = "Live2D/Cubism/Materials";

        /// <summary>
        /// Loads an mask material.
        /// </summary>
        /// <returns>The material.</returns>
        private static Material LoadMaskMaterial()
        {
            return Resources.Load<Material>(ResourcesDirectory + "/Mask");
        }

        /// <summary>
        /// Loads an mask culling material.
        /// </summary>
        /// <returns>The material.</returns>
        private static Material LoadMaskCullingMaterial()
        {
            return Resources.Load<Material>(ResourcesDirectory + "/MaskCulling");
        }

        /// <summary>
        /// Loads a material for BlendMode.
        /// </summary>
        /// <param name="name">Material's name</param>
        /// <returns>Material</returns>
        private static Material LoadBlendModeMaterial(string name)
        {
            var material = Resources.Load<Material>(ResourcesDirectory + "/BlendMode/" + name);
            if (material == null)
            {
                Debug.LogError($"Could not load material '{name}'");
            }
            return material;
        }

        /// <summary>
        /// Loads a material for picking.
        /// </summary>
        private static Material LoadTransparentPickingMaterial()
        {
            return Resources.Load<Material>(ResourcesDirectory + "/TransparentPicking");
        }

        #endregion
    }
}

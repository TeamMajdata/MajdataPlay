/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */

/* THIS FILE WAS AUTO-GENERATED. ALL CHANGES WILL BE LOST UPON RE-GENERATION. */


using System;
using System.Runtime.InteropServices;


namespace Live2D.Cubism.Core.Unmanaged
{
    /// <summary>
    /// Unmanaged Offscreen interface.
    /// </summary>
    public sealed class CubismUnmanagedOffscreens
    {
        /// <summary>
        /// Number of offscreens.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Offscreen blend modes.
        /// </summary>
        public CubismUnmanagedIntArrayView BlendModes { get; private set; }

        /// <summary>
        /// Offscreen opacities.
        /// </summary>
        public CubismUnmanagedFloatArrayView Opacities { get; private set; }

        /// <summary>
        /// Offscreen's owner indices.
        /// </summary>
        public CubismUnmanagedIntArrayView OwnerIndices { get; private set; }

        /// <summary>
        /// Offscreen's multiply colors.
        /// </summary>
        public CubismUnmanagedFloatArrayView MultiplyColors { get; private set; }

        /// <summary>
        /// Offscreen's screen colors.
        /// </summary>
        public CubismUnmanagedFloatArrayView ScreenColors { get; private set; }

        /// <summary>
        /// Offscreen's mask counts.
        /// </summary>
        public CubismUnmanagedIntArrayView MaskCounts { get; private set; }

        /// <summary>
        /// Offscreen's masks.
        /// </summary>
        public CubismUnmanagedIntArrayView[] Masks { get; private set; }

        /// <summary>
        /// Offscreen's constant flags.
        /// </summary>
        public CubismUnmanagedByteArrayView ConstantFlags { get; private set; }



        #region Ctors

        /// <summary>
        /// Initializes instance.
        /// </summary>
        internal unsafe CubismUnmanagedOffscreens(IntPtr modelPtr)
        {
            var length = 0;
            CubismUnmanagedIntArrayView length2;


            Count = CubismCoreDll.GetOffscreenCount(modelPtr);



            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            BlendModes = new CubismUnmanagedIntArrayView(CubismCoreDll.GetOffscreenBlendModes(modelPtr), length * 2);

            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            Opacities = new CubismUnmanagedFloatArrayView(CubismCoreDll.GetOffscreenOpacities(modelPtr), length);

            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            OwnerIndices = new CubismUnmanagedIntArrayView(CubismCoreDll.GetOffscreenOwnerIndices(modelPtr), length);

            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            MultiplyColors = new CubismUnmanagedFloatArrayView(CubismCoreDll.GetOffscreenMultiplyColors(modelPtr), length * 4);

            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            ScreenColors = new CubismUnmanagedFloatArrayView(CubismCoreDll.GetOffscreenScreenColors(modelPtr), length * 4);

            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            MaskCounts = new CubismUnmanagedIntArrayView(CubismCoreDll.GetOffscreenMaskCounts(modelPtr), length);

            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            ConstantFlags = new CubismUnmanagedByteArrayView(CubismCoreDll.GetOffscreenConstantFlags(modelPtr), length);


            length = CubismCoreDll.GetOffscreenCount(modelPtr);
            length2 = new CubismUnmanagedIntArrayView(CubismCoreDll.GetOffscreenMaskCounts(modelPtr), length);
            Masks = new CubismUnmanagedIntArrayView[length];
            var _masks = (IntPtr *)(CubismCoreDll.GetOffscreenMasks(modelPtr));
            for (var i = 0; i < length; ++i)
            {
                Masks[i] = new CubismUnmanagedIntArrayView(_masks[i], length2[i]);
            }
        }

        #endregion
    }
}

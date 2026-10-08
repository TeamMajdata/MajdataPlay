/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


namespace Live2D.Cubism.Rendering.URP.RenderingInterceptor
{
    /// <summary>
    /// Rendering interceptor interface.
    /// </summary>
    public interface ICubismRenderingInterceptor
    {
        /// <summary>
        /// Called by CubismRenderingInterceptorsManager before rendering for Drawables and Offscreens.
        /// </summary>
        /// <param name="args"> Rendering event arguments. </param>
        internal void OnPreRenderingForPass(CubismRenderedEventArgs args);

        /// <summary>
        /// Called by CubismRenderingInterceptorsManager after rendering for Drawables and Offscreens.
        /// </summary>
        /// <param name="args"> Rendering event arguments. </param>
        internal void OnPostRenderingForPass(CubismRenderedEventArgs args);
    }
}

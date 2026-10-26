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
    /// Calls release processes for classes used system-wide.
    /// </summary>
    [ExecuteInEditMode]
    public class CubismCommonRenderTextureController : MonoBehaviour
    {
        private void Update()
        {
            // Reset the flag at the beginning of each frame.
            CubismOffscreenRenderTextureManager.GetInstance().HasResetThisFrame = false;
        }

        private void OnDestroy()
        {
            CubismOffscreenRenderTextureManager.GetInstance().Release();
        }
    }
}

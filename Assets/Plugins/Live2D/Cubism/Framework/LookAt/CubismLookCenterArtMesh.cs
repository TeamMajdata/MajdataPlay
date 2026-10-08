/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Rendering;
using UnityEngine;


namespace Live2D.Cubism.Framework.LookAt
{
    /// <summary>
    /// Provides the center position for look-at calculations using an ArtMesh (CubismDrawable).
    /// </summary>
    [DisallowMultipleComponent]
    public class CubismLookCenterArtMesh : MonoBehaviour, ICubismLookCenter
    {
        /// <summary>
        /// Target drawable to calculate the center from.
        /// </summary>
        [SerializeField]
        public CubismDrawable TargetDrawable;

        /// <summary>
        /// Model root transform.
        /// </summary>
        private Transform _modelRootTransform;

        private void Start()
        {
            _modelRootTransform = this.transform;
        }

        /// <summary>
        /// Gets the position of the center.
        /// </summary>
        public Vector3 GetCenterPosition()
        {
            if (TargetDrawable == null)
            {
                return Vector3.zero;
            }

            var cubismRenderer = TargetDrawable.GetComponent<CubismRenderer>();

            if (cubismRenderer == null || cubismRenderer.Mesh == null)
            {
                return _modelRootTransform.InverseTransformPoint(TargetDrawable.transform.position);
            }

            var centerInMeshLocal = cubismRenderer.Mesh.bounds.center;
            var centerInWorld = TargetDrawable.transform.TransformPoint(centerInMeshLocal);
            var centerInModelLocal = _modelRootTransform.InverseTransformPoint(centerInWorld);

            return centerInModelLocal;
        }
    }
}

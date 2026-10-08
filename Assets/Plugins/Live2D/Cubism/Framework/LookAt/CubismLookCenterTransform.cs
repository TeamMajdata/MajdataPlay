/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using UnityEngine;


namespace Live2D.Cubism.Framework.LookAt
{
    /// <summary>
    /// Provides the center position for look-at calculations.
    /// </summary>
    [DisallowMultipleComponent]
    public class CubismLookCenterTransform : MonoBehaviour, ICubismLookCenter
    {
        public Transform CenterReference;

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
            if (CenterReference == null)
            {
                return Vector3.zero;
            }

            var centerInWorld = CenterReference.position;

            var centerInModelLocal = _modelRootTransform.InverseTransformPoint(centerInWorld);

            return centerInModelLocal;
        }
    }
}

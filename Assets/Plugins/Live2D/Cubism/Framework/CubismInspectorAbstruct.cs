/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using System;
using UnityEngine;


namespace Live2D.Cubism.Framework
{
    public abstract class CubismInspectorAbstract : MonoBehaviour, ICubismUpdatable
    {
        /// <summary>
        /// Called by cubism update controller. Order to invoke OnLateUpdate.
        /// </summary>
        public abstract int ExecutionOrder { get; }

        /// <summary>
        /// Called by cubism update controller. Needs to invoke OnLateUpdate on Editing.
        /// </summary>
        public bool NeedsUpdateOnEditing => false;

        /// <summary>
        /// Model has cubism update controller component.
        /// </summary>
        public bool HasUpdateController { get; set; }

        /// <summary>
        /// Called by cubism update controller. Updates controller.
        /// </summary>
        public abstract void OnLateUpdate();

#if UNITY_EDITOR
        /// <summary>
        /// CubismModel cache.
        /// </summary>
        [NonSerialized, HideInInspector]
        public CubismModel Model;

        /// <summary>
        /// Override values.
        /// </summary>
        [NonSerialized, HideInInspector]
        public float[] OverrideValues;

        /// <summary>
        /// Flag whether to Ovaerride.
        /// </summary>
        [NonSerialized, HideInInspector]
        public bool[] OverrideFlags;

        /// <summary>
        /// Editor UI Update delegate.
        /// </summary>
        /// <param name="sender"></param>
        public delegate void ValueChangesHandler(CubismInspectorAbstract sender);

        /// <summary>
        /// Editor UI Update event.
        /// </summary>
        public event ValueChangesHandler OnChangedValues;

        /// <summary>
        /// Dispatch Editor UI Update event.
        /// </summary>
        protected void DispatchValueChanges()
        {
            OnChangedValues?.Invoke(this);
        }

        /// <summary>
        /// Called by Unity.
        /// </summary>
        public void OnEnable()
        {
            // Get cubism update controller.
            HasUpdateController = (GetComponent<CubismUpdateController>() != null);
        }

        /// <summary>
        /// Called by Unity.
        /// </summary>
        private void LateUpdate()
        {
            // OnLateUpdate is not called because OriginalWorkflow is assumed.
        }
#endif
    }
}

/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using UnityEngine;

namespace Live2D.Cubism.Framework.ParameretRepeat
{
    public class CubismRepeatController : MonoBehaviour, ICubismUpdatable
    {
        #region Variable
        /// <summary>
        /// Model has cubism update controller component.
        /// </summary>
        [HideInInspector]
        public bool HasUpdateController { get; set; }

        /// <summary>
        /// Destinations.
        /// </summary>
        private CubismParameter[] Destinations { get; set; }

        #endregion

        #region Function

        /// <summary>
        /// Refreshes the controller. Call this method after adding and/or removing <see cref="CubismFadeParameter"/>s.
        /// </summary>
        public void Refresh()
        {
            var model = GetComponent<CubismModel>();
            if (model == null)
            {
                return;
            }

            Destinations = model.Parameters;

            // Get cubism update controller.
            HasUpdateController = (GetComponent<CubismUpdateController>() != null);
        }

        /// <summary>
        /// Called by cubism update controller. Order to invoke OnLateUpdate.
        /// </summary>
        public int ExecutionOrder
        {
            get { return CubismUpdateExecutionOrder.CubismRepeatController; }
        }

        /// <summary>
        /// Called by cubism update controller. Needs to invoke OnLateUpdate on Editing.
        /// </summary>
        public bool NeedsUpdateOnEditing
        {
            get { return false; }
        }


        /// <summary>
        /// Called by cubism update controller. Updates controller.
        /// </summary>
        /// <remarks>
        /// Make sure this method is called after any animations are evaluated.
        /// </remarks>
        public void OnLateUpdate()
        {
            // Fail silently.
            if (!enabled || Destinations == null)
            {
                return;
            }

            for (var i = 0; i < Destinations.Length; i++)
            {
                var parameter = Destinations[i];

                if (parameter.IsRepeat())
                {
                    parameter.Value = parameter.GetParameterRepeatValue(parameter.Value);
                }
                else
                {
                    parameter.Value = parameter.GetParameterClampValue(parameter.Value);
                }
            }
        }


        #endregion

        #region Unity Events Handling

        /// <summary>
        /// Initializes instance.
        /// </summary>
        private void OnEnable()
        {
            // Initialize cache.
            Refresh();
        }


        /// <summary>
        /// Called by Unity.
        /// </summary>
        private void LateUpdate()
        {
            if (!HasUpdateController)
            {
                OnLateUpdate();
            }
        }

        #endregion
    }
}

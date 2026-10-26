/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using System;
using System.Linq;


namespace Live2D.Cubism.Framework
{
    /// <summary>
    /// Allows inspecting <see cref="Core.CubismParameter"/>s.
    /// </summary>
    public sealed class CubismParametersInspector : CubismInspectorAbstract
    {
        /// <summary>
        /// Called by cubism update controller. Order to invoke OnLateUpdate.
        /// </summary>
        public override int ExecutionOrder => CubismUpdateExecutionOrder.CubismParametersInspector;

        /// <summary>
        /// Called by cubism update controller. Updates controller.
        /// </summary>
        public override void OnLateUpdate()
        {
#if UNITY_EDITOR
            // Fail silently.
            if (!enabled)
            {
                return;
            }

            if (Model == null)
            {
                Model = this.FindCubismModel();
            }
            if (Model.Parameters == null)
            {
                return;
            }
            OverrideFlags ??= new bool[Model.Parameters.Length];
            if (OverrideFlags.Length != Model.Parameters.Length)
            {
                Array.Resize(ref OverrideValues, Model.Parameters.Length);
            }
            OverrideValues ??= new float[Model.Parameters.Length];
            if (OverrideValues.Length != Model.Parameters.Length)
            {
                Array.Resize(ref OverrideValues, Model.Parameters.Length);
            }
            for (var i = 0; i < Model.Parameters.Length; i++)
            {
                if (OverrideFlags[i])
                {
                    Model.Parameters[i].OverrideValue(OverrideValues[i]);
                }
                else
                {
                    OverrideValues[i] = Model.Parameters[i].Value;
                }
            }
            DispatchValueChanges();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Called by Inspector.
        /// </summary>
        public void Refresh()
        {
            Model = this.FindCubismModel();
            if (Model == null)
            {
                OverrideValues = new float[0];
                OverrideFlags = new bool[0];
            }
            else
            {
                OverrideValues = Model.Parameters.Select(e => e.Value).ToArray();
                OverrideFlags = new bool[Model.Parameters.Length];
            }
        }
#endif
    }
}

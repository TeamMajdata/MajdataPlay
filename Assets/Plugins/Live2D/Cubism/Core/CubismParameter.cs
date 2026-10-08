/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core.Unmanaged;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Utils;
using System;
using UnityEngine;


namespace Live2D.Cubism.Core
{
    /// <summary>
    /// Single <see cref="CubismModel"/> parameter.
    /// </summary>
    [CubismDontMoveOnReimport]
    public sealed class CubismParameter : MonoBehaviour
    {
        #region Factory Methods

        /// <summary>
        /// Creates drawables for a <see cref="CubismModel"/>.
        /// </summary>
        /// <param name="unmanagedModel">Handle to unmanaged model.</param>
        /// <returns>Drawables root.</returns>
        internal static GameObject CreateParameters(CubismUnmanagedModel unmanagedModel)
        {
            var root = new GameObject("Parameters");


            // Create parameters.
            var unmanagedParameters = unmanagedModel.Parameters;
            var buffer = new CubismParameter[unmanagedParameters.Count];


            for (var i = 0; i < buffer.Length; ++i)
            {
                var proxy = new GameObject();


                buffer[i] = proxy.AddComponent<CubismParameter>();


                buffer[i].transform.SetParent(root.transform);
                buffer[i].Reset(unmanagedModel, i);
            }


            return root;
        }

        #endregion

        /// <summary>
        /// Unmanaged parameters from unmanaged model.
        /// </summary>
        private CubismUnmanagedParameters UnmanagedParameters { get; set; }


        /// <summary>
        /// <see cref="UnmanagedIndex"/> backing field.
        /// </summary>
        [SerializeField, HideInInspector]
        private int _unmanagedIndex = -1;

        /// <summary>
        /// Position in unmanaged arrays.
        /// </summary>
        internal int UnmanagedIndex
        {
            get { return _unmanagedIndex; }
            private set { _unmanagedIndex = value; }
        }


        /// <summary>
        /// Copy of Id.
        /// </summary>
        public string Id
        {
            get
            {
                // Pull data.
                return UnmanagedParameters.Ids[UnmanagedIndex];
            }
        }

        /// <summary>
        /// Get Parameter Type.
        /// </summary>
        public int Type
        {
            get
            {
                // Pull data.
                return UnmanagedParameters.Types[UnmanagedIndex];
            }
        }

        /// <summary>
        /// Minimum value.
        /// </summary>
        public float MinimumValue
        {
            get
            {
                // Pull data.
                return UnmanagedParameters.MinimumValues[UnmanagedIndex];
            }
        }

        /// <summary>
        /// Maximum value.
        /// </summary>
        public float MaximumValue
        {
            get
            {
                // Pull data.
                return UnmanagedParameters.MaximumValues[UnmanagedIndex];
            }
        }

        /// <summary>
        /// Default value.
        /// </summary>
        public float DefaultValue
        {
            get
            {
                // Pull data.
                return UnmanagedParameters.DefaultValues[UnmanagedIndex];
            }
        }

        /// <summary>
        /// Current value.
        /// </summary>
        [SerializeField, HideInInspector]
        public float Value;

        /// <summary>
        /// CubismModel cache.
        /// </summary>
        private CubismModel _model;

        /// <summary>
        /// Whether to be overridden
        /// </summary>
        [SerializeField]
        private bool _isOverriddenUserParameterRepeat = false;

        /// <summary>
        /// Override flag for settings
        /// </summary>
        [SerializeField]
        private bool _isParameterRepeated = false;

        /// <summary>
        /// Checks whether parameter repetition is performed for the entire model.
        /// </summary>
        /// <returns>True if parameter repetition is performed for the entire model; otherwise returns false.</returns>
        public bool GetOverrideFlagForModelParameterRepeat()
        {
            if (_model == null)
            {
                _model = transform.FindCubismModel(true);

                if (_model == null)
                {
                    return false;
                }
            }

            return _model.GetOverrideFlagForModelParameterRepeat();
        }

        /// <summary>
        /// Returns the flag indicating whether to override the parameter repeat.
        /// </summary>
        /// <returns>True if the parameter repeat is overridden, false otherwise.</returns>
        public bool GetOverrideFlagForParameterRepeat()
        {
            return _isOverriddenUserParameterRepeat;
        }

        /// <summary>
        /// Sets the flag indicating whether to override the parameter repeat.
        /// </summary>
        /// <param name="value">True if it is to be overridden; otherwise, false.</param>
        public void SetOverrideFlagForParameterRepeat(bool value)
        {
            _isOverriddenUserParameterRepeat = value;
        }

        /// <summary>
        /// Returns the repeat flag.
        /// </summary>
        /// <returns>True if repeating, false otherwise.</returns>
        public bool GetRepeatFlagForParameterRepeat()
        {
            return _isParameterRepeated;
        }

        /// <summary>
        /// Sets the repeat flag.
        /// </summary>
        /// <param name="value">True to enable repeating, false otherwise.</param>
        public void SetRepeatFlagForParameterRepeat(bool value)
        {
            _isParameterRepeated = value;
        }

        /// <summary>
        /// Gets whether the parameter has the repeat setting.
        /// </summary>
        /// <returns>True if it is set, otherwise returns false.</returns>
        public bool IsRepeat()
        {
            bool isRepeat;

            // パラメータリピート処理を行うか判定
            if (GetOverrideFlagForModelParameterRepeat() || _isOverriddenUserParameterRepeat)
            {
                // SDK側で設定されたリピート情報を使用する
                isRepeat = _isParameterRepeated;
            }
            else
            {
                // Editorで設定されたリピート情報を使用する
                isRepeat = GetParameterRepeats();
            }

            return isRepeat;
        }

        /// <summary>
        /// Returns the calculated result ensuring the value falls within the parameter's range.
        /// </summary>
        /// <param name="value">Parameter value</param>
        /// <returns>A value that falls within the parameter’s range. If the parameter does not exist, returns it as is.</returns>
        public float GetParameterRepeatValue(float value)
        {
            var maxValue = MaximumValue;
            var minValue = MinimumValue;
            var valueSize = maxValue - minValue;

            if (maxValue < value)
            {
                var overValue = CubismMath.ModF(value - maxValue, valueSize);
                if (!float.IsNaN(overValue))
                {
                    value = minValue + overValue;
                }
                else
                {
                    value = maxValue;
                }
            }
            if (value < minValue)
            {
                var overValue = CubismMath.ModF(minValue - value, valueSize);
                if (!float.IsNaN(overValue))
                {
                    value = maxValue - overValue;
                }
                else
                {
                    value = minValue;
                }
            }

            return value;
        }

        /// <summary>
        /// Returns the result of clamping the value to ensure it falls within the parameter's range.
        /// </summary>
        /// <param name="value">Parameter value</param>
        /// <returns>The clamped value. If the parameter does not exist, returns it as is.</returns>
        public float GetParameterClampValue(float value)
        {
            var maxValue = MaximumValue;
            var minValue = MinimumValue;

            return CubismMath.ClampF(value, minValue, maxValue);
        }

        /// <summary>
        /// Returns the repeat of the parameter.
        /// </summary>
        /// <returns>The raw data parameter repeat from the Cubism Core.</returns>
        public bool GetParameterRepeats()
        {
            return Convert.ToBoolean(UnmanagedParameters.Repeats[UnmanagedIndex]);
        }


        /// <summary>
        /// Revives the instance.
        /// </summary>
        /// <param name="unmanagedModel">Handle to unmanaged model.</param>
        internal void Revive(CubismUnmanagedModel unmanagedModel)
        {
            UnmanagedParameters = unmanagedModel.Parameters;
        }

        /// <summary>
        /// Restores instance to initial state.
        /// </summary>
        /// <param name="unmanagedModel">Handle to unmanaged model.</param>
        /// <param name="unmanagedIndex">Position in unmanaged arrays.</param>
        private void Reset(CubismUnmanagedModel unmanagedModel, int unmanagedIndex)
        {
            Revive(unmanagedModel);


            UnmanagedIndex = unmanagedIndex;
            name = Id;
            Value = DefaultValue;
        }
    }
}

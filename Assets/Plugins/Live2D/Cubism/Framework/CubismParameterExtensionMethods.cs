/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;


namespace Live2D.Cubism.Framework
{
    /// <summary>
    /// Extensions for <see cref="CubismParameter"/>s.
    /// </summary>
    public static class CubismParameterExtensionMethods
    {
        /// <summary>
        /// Additively blends a value in.
        /// </summary>
        /// <param name="parameter"><see langword="this"/>.</param>
        /// <param name="value">Value to blend in.</param>
        /// <param name="weight">Blend weight.</param>
        public static void AddToValue(this CubismParameter parameter, float value, float weight = 1f)
        {
            if (parameter == null)
            {
                return;
            }

            parameter.OverrideValue(parameter.Value + (value * weight));
        }


        /// <summary>
        /// Multiply blends a value in.
        /// </summary>
        /// <param name="parameter"><see langword="this"/>.</param>
        /// <param name="value">Value to blend in.</param>
        /// <param name="weight">Blend weight.</param>
        public static void MultiplyValueBy(this CubismParameter parameter, float value, float weight = 1f)
        {
            if (parameter == null)
            {
                return;
            }

            parameter.OverrideValue(parameter.Value * (1f + ((value - 1f) * weight)));
        }

        /// <summary>
        /// Override blends a value in.
        /// </summary>
        /// <param name="parameter"><see langword="this"/>.</param>
        /// <param name="value">Value to blend in.</param>
        /// <param name="weight">Blend weight.</param>
        public static void OverrideValue(this CubismParameter parameter, float value, float weight = 1.0f)
        {
            if (parameter == null)
            {
                return;
            }

            if (parameter.IsRepeat())
            {
                value = parameter.GetParameterRepeatValue(value);
            }
            else
            {
                value = parameter.GetParameterClampValue(value);
            }

            parameter.Value = parameter.Value * (1 - weight) + value * weight;
        }


        /// <summary>
        /// Blends a value in.
        /// </summary>
        /// <param name="self"><see langword="this"/>.</param>
        /// <param name="value">Value to blend in.</param>
        /// <param name="mode">Blend mode to use.</param>
        public static void BlendToValue(this CubismParameter self, CubismParameterBlendMode mode, float value, float weight = 1.0f)
        {
            if (self == null)
            {
                return;
            }

            if (mode == CubismParameterBlendMode.Additive)
            {
                self.AddToValue(value, weight);


                return;
            }


            if (mode == CubismParameterBlendMode.Multiply)
            {
                self.MultiplyValueBy(value, weight);


                return;
            }



            self.OverrideValue(self.Value * (1 - weight) + value * weight);
        }

        /// <summary>
        /// Blends the same value into multiple <see cref="CubismParameter"/>s.
        /// </summary>
        /// <param name="self"><see langword="this"/>.</param>
        /// <param name="value">Value to blend in.</param>
        /// <param name="mode">Blend mode to use.</param>
        public static void BlendToValue(this CubismParameter[] self, CubismParameterBlendMode mode, float value, float weight = 1.0f)
        {
            if (self == null)
            {
                return;
            }

            if (mode == CubismParameterBlendMode.Additive)
            {
                for (var i = 0; i < self.Length; ++i)
                {
                    self[i].AddToValue(value, weight);
                }


                return;
            }


            if (mode == CubismParameterBlendMode.Multiply)
            {
                for (var i = 0; i < self.Length; ++i)
                {
                    self[i].MultiplyValueBy(value, weight);
                }


                return;
            }


            for (var i = 0; i < self.Length; ++i)
            {
                self[i].OverrideValue(value, weight);
            }
        }
    }
}

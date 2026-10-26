/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using UnityEngine;


namespace Live2D.Cubism.Framework.Utils
{
    public class CubismMath
    {
        /// <summary>
        /// Returns the remainder of the division of two numbers.
        /// If invalid dividend or divisor is passed, it returns <see cref="float.NaN"/>.
        /// </summary>
        /// <param name="dividend"><see cref="Mathf.Infinity"/>, <see cref="Mathf.NegativeInfinity"/> and <see cref="float.NaN"/> are not allowed.</param>
        /// <param name="divisor"><see cref="float.NaN"/> and Zero is not allowed.</param>
        /// <returns></returns>
        public static float ModF(float dividend, float divisor)
        {
            if (!float.IsFinite(dividend) || Mathf.Approximately(divisor, 0)
                || float.IsNaN(dividend) || float.IsNaN(divisor))
            {
                Debug.LogWarning($"Invalid dividend or divisor. divided: {dividend}, divisor: {divisor} Mod() returns 'NaN'.");
                return float.NaN;
            }

            // Calculate the remainder of the division.
            return dividend % divisor;
        }


        /// <summary>
        /// Returns the value clamped within the specified range.
        /// </summary>
        /// <param name="val">Value to be checked within the range.</param>
        /// <param name="min">Minimum value.</param>
        /// <param name="max">Maximum value.</param>
        /// <returns>Clamped value within the range.</returns>
        public static float ClampF(float val, float min, float max)
        {
            if (val < min)
            {
                return min;
            }
            else if (max < val)
            {
                return max;
            }

            return val;
        }
    }
}

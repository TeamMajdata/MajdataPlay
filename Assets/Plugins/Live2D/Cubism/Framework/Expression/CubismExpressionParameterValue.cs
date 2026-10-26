/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;

namespace Live2D.Cubism.Framework.Expression
{
    /**
     * @brief Structure that allows parameters to have expression values to be applied.
     */
    public struct CubismExpressionParameterValue
    {
        public CubismParameter Parameter;   ///< Parameter id.
        public float AdditiveValue;         ///< Additive value.
        public float MultiplyValue;         ///< Multiply value.
        public float OverwriteValue;        ///< Overwrite value.
    }
}

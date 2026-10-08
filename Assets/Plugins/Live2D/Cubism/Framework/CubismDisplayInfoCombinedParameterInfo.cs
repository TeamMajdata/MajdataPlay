/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Framework.Json;
using UnityEngine;

public class CubismDisplayInfoCombinedParameterInfo : MonoBehaviour
{
    /// <summary>
    /// Combined parameters from .cdi3.json.
    /// </summary>
    public CubismDisplayInfo3Json.CombinedParameter[] CombinedParameters
    {
        get
        {
            return combinedParameters;
        }
        set
        {
            combinedParameters = value;
        }
    }

    /// <summary>
    /// backing field for <see cref="CombinedParameters"/>.
    /// </summary>
    [SerializeField]
    private CubismDisplayInfo3Json.CombinedParameter[] combinedParameters;
}

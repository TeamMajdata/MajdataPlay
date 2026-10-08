/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using UnityEngine;
using Live2D.Cubism.Framework.Json;


namespace Live2D.Cubism.Framework.MotionFade
{
    public class CubismFadeMotionData : ScriptableObject
    {
        /// <summary>
        /// Name of motion.
        /// </summary>
        [SerializeField]
        public string MotionName;

        /// <summary>
        /// Store time to fade in from .model3.json.
        /// NOTE: It is used to save `FadeInTime` from .model3.json. Please use <see cref="FadeInTime"/> instead of using it directly.
        /// </summary>
        [HideInInspector, SerializeField]
        public float ModelFadeInTime = -1.0f;

        /// <summary>
        /// Store time to fade out from .model3.json.
        /// NOTE: It is used to save `FadeOutTime` from .model3.json. Please use <see cref="FadeOutTime"/> instead of using it directly.
        /// </summary>
        [HideInInspector, SerializeField]
        public float ModelFadeOutTime = -1.0f;

        /// <summary>
        /// Time to fade in.
        /// </summary>
        [SerializeField]
        public float FadeInTime;

        /// <summary>
        /// Time to fade out.
        /// </summary>
        [SerializeField]
        public float FadeOutTime;

        /// <summary>
        /// Parameter ids.
        /// </summary>
        [SerializeField]
        public string[] ParameterIds;

        /// <summary>
        /// Parameter curves.
        /// </summary>
        [SerializeField]
        public AnimationCurve[] ParameterCurves;

        /// <summary>
        /// Fade in time parameters.
        /// </summary>
        [SerializeField]
        public float[] ParameterFadeInTimes;

        /// <summary>
        /// Fade out time parameters.
        /// </summary>
        [SerializeField]
        public float[] ParameterFadeOutTimes;

        /// <summary>
        /// Motion length.
        /// </summary>
        [SerializeField]
        public float MotionLength;


        /// <summary>
        /// Create CubismFadeMotionData from CubismMotion3Json.
        /// </summary>
        /// <param name="motion3Json">Motion3json as the creator.</param>
        /// <param name="motionName">Motion name of interest.</param>
        /// <param name="motionLength">Length of target motion.</param>
        /// <param name="shouldImportAsOriginalWorkflow">Whether the original work flow or not.</param>
        /// <param name="isCallFromModelJson">Whether it is a call from the model json.</param>
        /// <param name="model3Json">.model3.json to retrieve the fade time.</param>
        /// <returns>Fade data created based on motion3json.</returns>
        public static CubismFadeMotionData CreateInstance(
            CubismMotion3Json motion3Json, string motionName, float motionLength,
             bool shouldImportAsOriginalWorkflow = false, bool isCallFromModelJson = false, CubismModel3Json model3Json = null)
        {
            var fadeMotion = CreateInstance<CubismFadeMotionData>();
            var curveCount = motion3Json.Curves.Length;
            fadeMotion.ParameterIds = new string[curveCount];
            fadeMotion.ParameterFadeInTimes = new float[curveCount];
            fadeMotion.ParameterFadeOutTimes = new float[curveCount];
            fadeMotion.ParameterCurves = new AnimationCurve[curveCount];

            return CreateInstance(fadeMotion, motion3Json, motionName, motionLength, shouldImportAsOriginalWorkflow, isCallFromModelJson, model3Json);
        }

        /// <summary>
        /// Put motion3json's fade information back into fade motion data.
        /// </summary>
        /// <param name="fadeMotion">Instance containing fade information.</param>
        /// <param name="motion3Json">Target motion3json.</param>
        /// <param name="motionName">Motion name of interest.</param>
        /// <param name="motionLength">Motion length.</param>
        /// <param name="shouldImportAsOriginalWorkflow">Whether the original work flow or not.</param>
        /// <param name="isCallFormModelJson">Whether it is a call from the model json.</param>
        /// <param name="model3Json">.model3.json to retrieve the fade time.</param>
        /// <returns>Fade data created based on fademotiondata.</returns>
        public static CubismFadeMotionData CreateInstance(
            CubismFadeMotionData fadeMotion, CubismMotion3Json motion3Json, string motionName, float motionLength,
             bool shouldImportAsOriginalWorkflow = false, bool isCallFormModelJson = false, CubismModel3Json model3Json = null)
        {
            if (model3Json != null)
            {
                GetFadeDataFromModel3Json(model3Json, fadeMotion);
            }

            if (motion3Json == null)
            {
                return fadeMotion;
            }

            fadeMotion.MotionName = motionName;
            fadeMotion.MotionLength = motionLength;

            if (fadeMotion.ModelFadeInTime < 0.0f)
            {
                fadeMotion.FadeInTime = (motion3Json.Meta.FadeInTime < 0.0f) ? 1.0f : motion3Json.Meta.FadeInTime;
            }
            else
            {
                fadeMotion.FadeInTime = fadeMotion.ModelFadeInTime;
            }

            if (fadeMotion.ModelFadeOutTime < 0.0f)
            {
                fadeMotion.FadeOutTime = (motion3Json.Meta.FadeOutTime < 0.0f) ? 1.0f : motion3Json.Meta.FadeOutTime;
            }
            else
            {
                fadeMotion.FadeOutTime = fadeMotion.ModelFadeOutTime;
            }

            for (var i = 0; i < motion3Json.Curves.Length; ++i)
            {
                var curve = motion3Json.Curves[i];

                // In original workflow mode, skip add part opacity curve when call not from model3.json.
                if (curve.Target == "PartOpacity" && shouldImportAsOriginalWorkflow && !isCallFormModelJson)
                {
                    continue;
                }

                fadeMotion.ParameterIds[i] = curve.Id;
                fadeMotion.ParameterFadeInTimes[i] = (curve.FadeInTime < 0.0f) ? -1.0f : curve.FadeInTime;
                fadeMotion.ParameterFadeOutTimes[i] = (curve.FadeOutTime < 0.0f) ? -1.0f : curve.FadeOutTime;
                fadeMotion.ParameterCurves[i] = new AnimationCurve(CubismMotion3Json.ConvertCurveSegmentsToKeyframes(curve.Segments));
            }

            return fadeMotion;
        }

        private static void GetFadeDataFromModel3Json(CubismModel3Json modelJson, CubismFadeMotionData fadeMotion)
        {
            var motions = modelJson.FileReferences.Motions.Motions;

            for (var groupIndex = 0; groupIndex < motions?.Length; groupIndex++)
            {
                if (motions[groupIndex] == null)
                {
                    continue;
                }

                for (var motionIndex = 0; motionIndex < motions[groupIndex]?.Length; motionIndex++)
                {
                    var motion = motions[groupIndex][motionIndex];

                    // Set FadeInTime.
                    if (!(motion.FadeInTime < 0.0f))
                    {
                        fadeMotion.ModelFadeInTime = motion.FadeInTime;
                        fadeMotion.FadeInTime = fadeMotion.ModelFadeInTime;
                    }

                    // Set FadeOutTime.
                    if (!(motion.FadeOutTime < 0.0f))
                    {
                        fadeMotion.ModelFadeOutTime = motion.FadeOutTime;
                        fadeMotion.FadeInTime = fadeMotion.ModelFadeInTime;
                    }
                }
            }
        }
    }
}

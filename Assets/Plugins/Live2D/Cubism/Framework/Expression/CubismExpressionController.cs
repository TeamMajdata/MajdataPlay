/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Framework.MotionFade;
using System.Collections.Generic;
using UnityEngine;


namespace Live2D.Cubism.Framework.Expression
{
    /// <summary>
    /// Expression controller.
    /// </summary>
    public class CubismExpressionController : MonoBehaviour, ICubismUpdatable
    {
        #region variable

        /// <summary>
        /// Expressions data list.
        /// </summary>
        [SerializeField]
        public CubismExpressionList ExpressionsList;

        /// <summary>
        /// Use the expression calculation method.
        /// </summary>
        [SerializeField]
        public bool UseLegacyBlendCalculation = false;

        /// <summary>
        /// CubismModel cache.
        /// </summary>
        private CubismModel _model = null;

        /// <summary>
        /// Playing expressions.
        /// </summary>
        private List<CubismPlayingExpression> _playingExpressions = new List<CubismPlayingExpression>();

        /// <summary>
        /// Playing expressions index.
        /// </summary>
        [SerializeField]
        public int CurrentExpressionIndex = -1;

        /// <summary>
        /// Last playing expressions index.
        /// </summary>
        private int _lastExpressionIndex = -1;

        /// <summary>
        /// Model has update controller component.
        /// </summary>
        [HideInInspector]
        public bool HasUpdateController { get; set; }

        /// <summary>
        /// Value of each parameter to be applied to the model.
        /// </summary>
        private List<CubismExpressionParameterValue> _expressionParameterValues = new List<CubismExpressionParameterValue>();

        /// <summary>
        /// Default value for applying additive.
        /// </summary>
        private const float DefaultAdditiveValue = 0.0f;

        /// <summary>
        /// Initial value of multiply applied.
        /// </summary>
        private const float DefaultMultiplyValue = 1.0f;

        #endregion

        /// <summary>
        /// Add new expression to playing expressions.
        /// </summary>
        private void StartExpression()
        {
            // Fail silently...
            if(ExpressionsList == null || ExpressionsList.CubismExpressionObjects == null)
            {
                return;
            }

            // Backup expression.
            _lastExpressionIndex = CurrentExpressionIndex;

            // Set last expression end time
            if(_playingExpressions.Count > 0)
            {
                var playingExpression = _playingExpressions[_playingExpressions.Count - 1];
                var newExpressionEndTime = playingExpression.ExpressionUserTime + playingExpression.FadeOutTime;

                if (playingExpression.ExpressionEndTime == 0.0f || newExpressionEndTime < playingExpression.ExpressionEndTime)
                {
                    playingExpression.ExpressionEndTime = newExpressionEndTime;
                }
                _playingExpressions[_playingExpressions.Count - 1] = playingExpression;
            }

            // Fail silently...
            if (CurrentExpressionIndex < 0 || CurrentExpressionIndex >= ExpressionsList.CubismExpressionObjects.Length)
            {
                return;
            }

            var palyingExpression = CubismPlayingExpression.Create(_model, ExpressionsList.CubismExpressionObjects[CurrentExpressionIndex]);

            if(palyingExpression == null)
            {
                return;
            }

            // Add to PlayingExList.
            _playingExpressions.Add(palyingExpression);
        }

        /// <summary>
        /// Called by cubism update controller. Order to invoke OnLateUpdate.
        /// </summary>
        public int ExecutionOrder
        {
            get { return CubismUpdateExecutionOrder.CubismExpressionController; }
        }

        /// <summary>
        /// Called by cubism update controller. Needs to invoke OnLateUpdate on Editing.
        /// </summary>
        public bool NeedsUpdateOnEditing
        {
            get { return false; }
        }

        /// <summary>
        /// Called by cubism update manager.
        /// </summary>
        public void OnLateUpdate()
        {
            // Fail silently...
            if(!enabled || _model == null)
            {
                return;
            }

            // Start expression when current expression changed.
            if(CurrentExpressionIndex != _lastExpressionIndex)
            {
                StartExpression();
            }

            // Update of expressions.
            if (UseLegacyBlendCalculation)
            {
                UpdateExpressionLegacy();
            }
            else
            {
                UpdateExpression();
            }
        }

        /// <summary>
        /// Update of expressions. (old method)
        /// </summary>
        private void UpdateExpressionLegacy()
        {
            // Update expression
            for (var expressionIndex = 0; expressionIndex < _playingExpressions.Count; ++expressionIndex)
            {
                var playingExpression = _playingExpressions[expressionIndex];

                UpdateFadeWeight(playingExpression);

                // Apply value.
                for (var i = 0; i < playingExpression.Destinations.Length; ++i)
                {
                    // Fail silently...
                    if (playingExpression.Destinations[i] == null)
                    {
                        continue;
                    }

                    switch (playingExpression.Blend[i])
                    {
                        case CubismParameterBlendMode.Additive:
                            playingExpression.Destinations[i].AddToValue(playingExpression.Value[i], playingExpression.FadeWeight);
                            break;
                        case CubismParameterBlendMode.Multiply:
                            playingExpression.Destinations[i].MultiplyValueBy(playingExpression.Value[i], playingExpression.FadeWeight);
                            break;
                        case CubismParameterBlendMode.Override:
                            playingExpression.Destinations[i].OverrideValue(playingExpression.Destinations[i].Value * (1 - playingExpression.FadeWeight) + (playingExpression.Value[i] * playingExpression.FadeInWeight));
                            break;
                        default:
                            // When an unspecified value is set, it is already in addition mode.
                            break;
                    }
                }

                // Apply update value
                _playingExpressions[expressionIndex] = playingExpression;
            }

            // Remove expression from playing expressions
            for (var expressionIndex = _playingExpressions.Count - 1; expressionIndex >= 0; --expressionIndex)
            {
                if (_playingExpressions[expressionIndex].FadeWeight > 0.0f)
                {
                    continue;
                }

                _playingExpressions.RemoveAt(expressionIndex);
            }
        }

        /// <summary>
        /// Update of expressions.
        /// </summary>
        private void UpdateExpression()
        {
            var expressionWeight = 0.0f;

            // Update expression
            for (var expressionIndex = 0; expressionIndex < _playingExpressions.Count; ++expressionIndex)
            {
                var playingExpression = _playingExpressions[expressionIndex];

                // List all parameters referenced by the Expression being played.
                for (var i = 0; i < playingExpression.Destinations.Length; ++i)
                {
                    var index = -1;
                    // Search for the presence of a parameter ID in the list.
                    for (var j = 0; j < _expressionParameterValues.Count; j++)
                    {
                        if (_expressionParameterValues[j].Parameter != playingExpression.Destinations[i])
                        {
                            continue;
                        }

                        index = j;
                        break;
                    }

                    if (index >= 0)
                    {
                        continue;
                    }

                    // If the parameter does not exist in the list, add a new one.
                    CubismExpressionParameterValue item = new CubismExpressionParameterValue();
                    item.Parameter = playingExpression.Destinations[i];
                    if (item.Parameter != null)
                    {
                        item.AdditiveValue = DefaultAdditiveValue;
                        item.MultiplyValue = DefaultMultiplyValue;
                        item.OverwriteValue = item.Parameter.Value;
                        _expressionParameterValues.Add(item);
                    }
                }

                // ------ Calculate value ------
                CalculateExpressionParameters(expressionIndex, playingExpression);

                expressionWeight += playingExpression.FadeInTime == 0.0f
                    ? 1.0f
                    : CubismFadeMath.GetEasingSine(
                        (playingExpression.ExpressionUserTime - playingExpression.ExpressionStartTime) /
                        playingExpression.FadeInTime);
            }

            // ----- If the latest Expression fade is complete, delete the earlier one. ------
            if (_playingExpressions.Count > 1 &&
                _playingExpressions[_playingExpressions.Count-1].FadeWeight >= 1.0f)
            {
                // The last element of the array is not deleted.
                for (var i = _playingExpressions.Count - 2; i >= 0; --i)
                {
                    _playingExpressions.RemoveAt(i);
                }
            }

            if (expressionWeight > 1.0f)
            {
                expressionWeight = 1.0f;
            }

            // Apply each value to the model.
            for (var i = 0; i < _expressionParameterValues.Count; i++)
            {
                var expressionParameterValue = _expressionParameterValues[i];
                expressionParameterValue.Parameter.OverrideValue(
                    (expressionParameterValue.OverwriteValue + expressionParameterValue.AdditiveValue)
                        * expressionParameterValue.MultiplyValue,
                    expressionWeight);

                expressionParameterValue.AdditiveValue = DefaultAdditiveValue;
                expressionParameterValue.MultiplyValue = DefaultMultiplyValue;
            }
        }

        /// <summary>
        /// Update motion weights.
        /// </summary>
        /// <param name="playingExpression">Expression motion during playback.</param>
        private void UpdateFadeWeight(CubismPlayingExpression playingExpression)
        {
            // Update expression user time.
            playingExpression.ExpressionUserTime += Time.deltaTime;

            // Update weight
            playingExpression.FadeInWeight = (Mathf.Abs(playingExpression.FadeInTime) < float.Epsilon)
                ? 1.0f
                : CubismFadeMath.GetEasingSine(playingExpression.ExpressionUserTime / playingExpression.FadeInTime);

            playingExpression.FadeOutWeight = ((Mathf.Abs(playingExpression.ExpressionEndTime) < float.Epsilon) || (playingExpression.ExpressionEndTime < 0.0f))
                ? 1.0f
                : CubismFadeMath.GetEasingSine(
                    (playingExpression.ExpressionEndTime - playingExpression.ExpressionUserTime) / playingExpression.FadeOutTime);

            playingExpression.FadeWeight = playingExpression.Weight * playingExpression.FadeInWeight * playingExpression.FadeOutWeight;
        }

        /// <summary>
        /// Calculate parameters related to the model's facial expressions.
        /// </summary>
        /// <param name="expressionIndex">Index of currently processed facial expression motion.</param>
        /// <param name="playingExpression">Expression motion during playback.</param>
        private void CalculateExpressionParameters(int expressionIndex, CubismPlayingExpression playingExpression)
        {
            UpdateFadeWeight(playingExpression);

            for (var i = 0; i < _expressionParameterValues.Count; i++)
            {
                var expressionParameterValue = _expressionParameterValues[i];

                if (expressionParameterValue.Parameter == null)
                {
                    continue;
                }

                var currentParameterValue = expressionParameterValue.OverwriteValue =
                    expressionParameterValue.Parameter.Value;

                var expressionParameters = playingExpression.Destinations;
                var parameterIndex = -1;
                for (var j = 0; j < expressionParameters.Length; j++)
                {
                    if (expressionParameterValue.Parameter != expressionParameters[j])
                    {
                        continue;
                    }

                    parameterIndex = j;

                    break;
                }

                // Parameters not referenced by the Expression being played have their initial values applied.
                if (parameterIndex < 0)
                {
                    if (expressionIndex == 0)
                    {
                        expressionParameterValue.AdditiveValue = DefaultAdditiveValue;
                        expressionParameterValue.MultiplyValue = DefaultMultiplyValue;
                        expressionParameterValue.OverwriteValue = currentParameterValue;
                    }
                    else
                    {
                        expressionParameterValue.AdditiveValue =
                            CalculateValue(
                                expressionParameterValue.AdditiveValue,
                                DefaultAdditiveValue,
                                playingExpression.FadeWeight);
                        expressionParameterValue.MultiplyValue =
                            CalculateValue(
                                expressionParameterValue.MultiplyValue,
                                DefaultMultiplyValue,
                                playingExpression.FadeWeight);
                        expressionParameterValue.OverwriteValue =
                            CalculateValue(
                                expressionParameterValue.OverwriteValue,
                                currentParameterValue,
                                playingExpression.FadeWeight);
                    }

                    _expressionParameterValues[i] = expressionParameterValue;
                    continue;
                }

                // Calculate value.
                var value = playingExpression.Value[parameterIndex];
                float newAdditiveValue, newMultiplyValue, newSetValue;
                switch (playingExpression.Blend[parameterIndex])
                {
                    case CubismParameterBlendMode.Additive:
                        newAdditiveValue = value;
                        newMultiplyValue = DefaultMultiplyValue;
                        newSetValue = currentParameterValue;
                        break;
                    case CubismParameterBlendMode.Multiply:
                        newAdditiveValue = DefaultAdditiveValue;
                        newMultiplyValue = value;
                        newSetValue = currentParameterValue;
                        break;
                    case CubismParameterBlendMode.Override:
                        newAdditiveValue = DefaultAdditiveValue;
                        newMultiplyValue = DefaultMultiplyValue;
                        newSetValue = value;
                        break;
                    default:
                        return;
                }

                if (expressionIndex == 0)
                {
                    expressionParameterValue.AdditiveValue = newAdditiveValue;
                    expressionParameterValue.MultiplyValue = newMultiplyValue;
                    expressionParameterValue.OverwriteValue = newSetValue;
                }
                else
                {
                    expressionParameterValue.AdditiveValue =
                        CalculateValue(
                            expressionParameterValue.AdditiveValue,
                            newAdditiveValue,
                            playingExpression.FadeWeight);
                    expressionParameterValue.MultiplyValue =
                        CalculateValue(
                            expressionParameterValue.MultiplyValue,
                            newMultiplyValue,
                            playingExpression.FadeWeight);
                    expressionParameterValue.OverwriteValue =
                        CalculateValue(
                            expressionParameterValue.OverwriteValue,
                            newSetValue,
                            playingExpression.FadeWeight);
                }
                _expressionParameterValues[i] = expressionParameterValue;
            }
        }

        /// <summary>
        /// Blend calculation.
        /// </summary>
        /// <param name="source">Source value.</param>
        /// <param name="destination">Destination value.</param>
        /// <param name="fadeWeight">Weight value.</param>
        /// <returns></returns>
        private float CalculateValue(float source, float destination, float fadeWeight)
        {
            return (source * (1.0f - fadeWeight)) + (destination * fadeWeight);
        }

        #region Unity Event Handling

        /// <summary>
        /// Called by Unity.
        /// </summary>
        private void OnEnable()
        {
            _model = this.FindCubismModel();

            // Get cubism update controller.
            HasUpdateController = (GetComponent<CubismUpdateController>() != null);
        }

        /// <summary>
        /// Called by Unity.
        /// </summary>
        private void LateUpdate()
        {
            if(!HasUpdateController)
            {
                OnLateUpdate();
            }
        }

        #endregion

    }
}

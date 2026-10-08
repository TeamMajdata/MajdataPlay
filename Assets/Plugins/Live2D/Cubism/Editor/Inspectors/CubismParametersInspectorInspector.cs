/**
 * Copyright(c) Live2D Inc. All rights reserved.
 *
 * Use of this source code is governed by the Live2D Open Software license
 * that can be found at https://www.live2d.com/eula/live2d-open-software-license-agreement_en.html.
 */


using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;


namespace Live2D.Cubism.Editor.Inspectors
{
    /// <summary>
    /// Allows inspecting <see cref="CubismParameter"/>s.
    /// </summary>
    [CustomEditor(typeof(CubismParametersInspector))]
    internal sealed class CubismParametersInspectorInspector : UnityEditor.Editor
    {
        #region Editor

        private struct Element
        {
            public FloatField field;
            public Slider slider;

            public Element(FloatField field, Slider slider)
            {
                this.field = field;
                this.slider = slider;
            }
        }

        private VisualElement _visualElement;
        private Button _button;
        private Element[] _elements;

        public override VisualElement CreateInspectorGUI()
        {
            var target = (CubismParametersInspector)this.target;
            target.Refresh();

            _visualElement = new VisualElement();
            if (target.Model != null)
            {
                var parameters = target.Model.Parameters;
                _elements = new Element[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    var displayInfoParameterName = parameter.GetComponent<CubismDisplayInfoParameterName>();
                    var displayName = displayInfoParameterName != null
                        ? (string.IsNullOrEmpty(displayInfoParameterName.DisplayName)
                            ? displayInfoParameterName.Name
                            : displayInfoParameterName.DisplayName)
                        : parameter.Id;

                    var field = new FloatField() { value = parameter.Value };

                    field.SetEnabled(!Application.isPlaying);
                    var slider = new Slider(displayName, parameter.MinimumValue, parameter.MaximumValue)
                    {
                        value = parameter.Value,
                        userData = this
                    };
                    slider.RegisterCallback<MouseCaptureEvent, int>(OnMouseCapture, i);
                    slider.RegisterCallback<MouseCaptureOutEvent, int>(OnMouseCaptureOut, i);
                    slider.RegisterCallback<ChangeEvent<float>, int>(OnChangeEvent, i);
                    field.style.flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row);
                    field.style.flexBasis = new StyleLength(new Length(10.0f, LengthUnit.Percent));
                    field.style.maxWidth = new StyleLength(new Length(50.0f, LengthUnit.Pixel));
                    field.style.minWidth = new StyleLength(new Length(30.0f, LengthUnit.Pixel));
                    _elements[i] = new Element(field, slider);
                    slider.Add(field);
                    _visualElement.Add(slider);
                }
                _button = new Button(() =>
                {
                    var target = this.target as CubismParametersInspector;
                    if (target == null)
                    {
                        return;
                    }
                    for (var i = 0; i < target.Model.Parameters.Length; i++)
                    {
                        var parameter = target.Model.Parameters[i];
                        parameter.OverrideValue(parameter.DefaultValue);
                        _elements[i].slider.value = parameter.DefaultValue;
                        _elements[i].field.value = parameter.DefaultValue;
                        EditorUtility.SetDirty(parameter);
                    }

                    Undo.RecordObjects(target.Model.Parameters, "Change Parameter Values");
                    target.Model.ForceUpdateNow();
                })
                { text = "Reset" };
                _button.SetEnabled(!Application.isPlaying);
                _visualElement.Add(_button);
            }
            return _visualElement;
        }

        private void OnEnable()
        {
            var target = (CubismParametersInspector)this.target;
            target.OnChangedValues += UpdateValues;
        }

        private void OnDisable()
        {
            var target = (CubismParametersInspector)this.target;
            target.OnChangedValues -= UpdateValues;
        }

        private void UpdateValues(CubismInspectorAbstract sender)
        {
            if (_elements == null)
            {
                return;
            }

            if (sender.Model == null)
            {
                Debug.LogError("sender model is null.");
                return;
            }
            var flags = sender.OverrideFlags;
            var parameters = sender.Model.Parameters;
            if (parameters == null)
            {
                Debug.LogError("parameters is null.");
                return;
            }
            if (_elements.Length != parameters.Length)
            {
                Debug.LogError("parameters count mismatch.");
                return;
            }
            for (var i = 0; i < _elements.Length; i++)
            {
                if (!flags[i])
                {
                    var value = parameters[i].Value;
                    _elements[i].field.value = value;
                    _elements[i].slider.value = value;
                }
            }
        }

        private static void OnMouseCapture(MouseCaptureEvent ev, int index)
        {
            var slider = ev.currentTarget as Slider;
            if (slider == null)
            {
                return;
            }
            var data = slider.userData as CubismParametersInspectorInspector;
            if (data == null)
            {
                return;
            }
            var target = (CubismParametersInspector)data.target;
            target.OverrideFlags[index] = true;
        }

        private static void OnMouseCaptureOut(MouseCaptureOutEvent ev, int index)
        {
            var slider = ev.currentTarget as Slider;
            if (slider == null)
            {
                return;
            }
            var data = slider.userData as CubismParametersInspectorInspector;
            if (data == null)
            {
                return;
            }
            var target = (CubismParametersInspector)data.target;
            target.OverrideFlags[index] = false;
        }

        private static void OnChangeEvent(ChangeEvent<float> ev, int index)
        {
            var slider = ev.target as Slider;
            if (slider == null)
            {
                return;
            }
            var data = slider.userData as CubismParametersInspectorInspector;
            if (data == null)
            {
                return;
            }
            var target = (CubismParametersInspector)data.target;
            data._elements[index].field.value = ev.newValue;
            target.OverrideValues[index] = ev.newValue;
            if (!Application.isPlaying)
            {
                var parameter = target.Model.Parameters[index];
                Undo.RecordObject(parameter, "Change Parameter Value");
                parameter.OverrideValue(ev.newValue);
                target.Model.ForceUpdateNow();
                parameter.OverrideValue(ev.newValue);
            }
        }

        #endregion
    }
}

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
    /// Allows inspecting <see cref="CubismPart"/>s.
    /// </summary>
    [CustomEditor(typeof(CubismPartsInspector))]
    internal sealed class CubismPartsInspectorInspector : UnityEditor.Editor
    {
        private sealed class PartProxy
        {
            public CubismPart Part;
            public PartProxy[] Children = new PartProxy[0];
            public FloatField Field;
            public Slider Slider;
        }

        private PartProxy[] _proxies;

        private VisualElement _visualElement;

        private void RecursiveCreateInspectorGUI(PartProxy item, VisualElement parent)
        {
            if (item.Children.Length > 0)
            {
                item.Slider.style.position = new StyleEnum<Position>(Position.Absolute);
                item.Slider.style.left = new StyleLength(new Length(0.0f, LengthUnit.Pixel));
                item.Slider.style.right = new StyleLength(new Length(0.0f, LengthUnit.Pixel));
                var element = new VisualElement();
                var foldout = new Foldout();
                foldout.style.marginTop = new StyleLength(new Length(0.0f, LengthUnit.Pixel));
                foldout.style.marginBottom = new StyleLength(new Length(0.0f, LengthUnit.Pixel));
                foreach (var sub in item.Children)
                {
                    RecursiveCreateInspectorGUI(sub, foldout);
                }
                element.Add(foldout);
                element.Add(item.Slider);
                parent.Add(element);
            }
            else
            {
                parent.Add(item.Slider);
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            var target = (CubismPartsInspector)this.target;
            target.Refresh();

            _visualElement = new VisualElement();
            if (target.Model != null)
            {
                var parts = target.Model.Parts;
                var data = new PartProxy[0];
                _proxies = new PartProxy[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];

                    var cdiPartName = part.GetComponent<CubismDisplayInfoPartName>();
                    var name = cdiPartName != null
                    ? (string.IsNullOrEmpty(cdiPartName.DisplayName) ? cdiPartName.Name : cdiPartName.DisplayName)
                    : part.name;

                    var slider = new Slider(name, 0.0f, 1.0f)
                    {
                        label = name,
                        value = part.Opacity,
                        userData = this
                    };
                    var field = new FloatField()
                    {
                        value = part.Opacity,
                        userData = this
                    };
                    field.style.width = new StyleLength(new Length(50.0f, LengthUnit.Pixel));
                    field.style.maxWidth = new StyleLength(new Length(50.0f, LengthUnit.Pixel));
                    field.style.minWidth = new StyleLength(new Length(30.0f, LengthUnit.Pixel));
                    slider.Add(field);

                    slider.RegisterCallback<MouseCaptureEvent, int>(OnMouseCapture, i);
                    slider.RegisterCallback<MouseCaptureOutEvent, int>(OnMouseCaptureOut, i);
                    slider.RegisterCallback<ChangeEvent<float>, int>(OnChangeEvent, i);

                    _proxies[i] = new PartProxy()
                    {
                        Part = part,
                        Field = field,
                        Slider = slider
                    };
                }
                foreach (var item in _proxies)
                {
                    var unmanagedParentIndex = item.Part.UnmanagedParentIndex;
                    if (unmanagedParentIndex < 0)
                    {
                        System.Array.Resize(ref data, data.Length + 1);
                        data[^1] = item;
                    }
                    else
                    {
                        var parent = _proxies[unmanagedParentIndex];
                        System.Array.Resize(ref parent.Children, parent.Children.Length + 1);
                        parent.Children[^1] = item;
                    }
                }
                foreach (var item in data)
                {
                    RecursiveCreateInspectorGUI(item, _visualElement);
                }
            }
            return _visualElement;
        }

        private void UpdateValues(CubismInspectorAbstract sender)
        {
            if (_proxies == null)
            {
                return;
            }

            if (sender.Model == null)
            {
                Debug.LogError("sender model is null.");
                return;
            }
            var flags = sender.OverrideFlags;
            var parts = sender.Model.Parts;
            if (parts == null)
            {
                Debug.LogError("parts is null.");
                return;
            }
            if (_proxies.Length != parts.Length)
            {
                Debug.LogError("parts count mismatch.");
                return;
            }
            for (var i = 0; i < _proxies.Length; i++)
            {
                if (!flags[i])
                {
                    var value = parts[i].Opacity;
                    _proxies[i].Field.value = value;
                    _proxies[i].Slider.value = value;
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
            var data = slider.userData as CubismPartsInspectorInspector;
            if (data == null)
            {
                return;
            }
            var target = (CubismPartsInspector)data.target;
            target.OverrideFlags[index] = true;
        }

        private static void OnMouseCaptureOut(MouseCaptureOutEvent ev, int index)
        {
            var slider = ev.currentTarget as Slider;
            if (slider == null)
            {
                return;
            }
            var data = slider.userData as CubismPartsInspectorInspector;
            if (data == null)
            {
                return;
            }
            var target = (CubismPartsInspector)data.target;
            target.OverrideFlags[index] = false;
        }

        private static void OnChangeEvent(ChangeEvent<float> ev, int index)
        {
            var slider = ev.target as Slider;
            if (slider == null)
            {
                return;
            }
            var data = slider.userData as CubismPartsInspectorInspector;
            if (data == null)
            {
                return;
            }
            var target = (CubismPartsInspector)data.target;
            data._proxies[index].Field.value = ev.newValue;
            target.OverrideValues[index] = ev.newValue;
            if (!Application.isPlaying)
            {
                var part = target.Model.Parts[index];
                Undo.RecordObject(part, "Change Part Value");
                part.Opacity = ev.newValue;
                target.Model.ForceUpdateNow();
                part.Opacity = ev.newValue;
            }
        }

        private void OnEnable()
        {
            var target = (CubismPartsInspector)this.target;
            target.OnChangedValues += UpdateValues;
        }

        private void OnDisable()
        {
            var target = (CubismPartsInspector)this.target;
            target.OnChangedValues -= UpdateValues;
        }
    }
}

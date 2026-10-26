using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#nullable enable
namespace MajdataPlay.Tests.Plugins.Utils
{
    /// <summary>
    /// An editor window that draws every serialized property of an object with the property drawers the Inspector uses,
    /// so tests can run <see cref="MajdataPlay.Utils.Editor.RuntimeEventDrawer"/> in a real IMGUI pass.
    /// </summary>
    internal sealed class RuntimeEventDrawerTestWindow : EditorWindow
    {
        /// <summary>The serialized object of <see cref="Target"/>, created on the first draw.</summary>
        private SerializedObject? _serializedObject;

        /// <summary>Gets or sets the object whose properties are drawn.</summary>
        public Object? Target { get; set; }

        /// <summary>Gets or sets the property path whose height is measured.</summary>
        public string MeasuredPropertyPath { get; set; } = string.Empty;

        /// <summary>Gets the number of completed draws.</summary>
        public int DrawCount { get; private set; }

        /// <summary>Gets the height of <see cref="MeasuredPropertyPath"/> measured in the last draw.</summary>
        public float MeasuredHeight { get; private set; }

        /// <summary>Draws every visible property of <see cref="Target"/>.</summary>
        private void OnGUI()
        {
            if (Target == null)
            {
                return;
            }
            _serializedObject ??= new SerializedObject(Target);
            _serializedObject.Update();

            var iterator = _serializedObject.GetIterator();
            var enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                EditorGUILayout.PropertyField(iterator, true);
            }
            var measuredProperty = _serializedObject.FindProperty(MeasuredPropertyPath);
            if (measuredProperty is not null)
            {
                MeasuredHeight = EditorGUI.GetPropertyHeight(measuredProperty, true);
            }
            _serializedObject.ApplyModifiedProperties();
            DrawCount++;
        }

        /// <summary>Releases the serialized object.</summary>
        private void OnDestroy()
        {
            _serializedObject?.Dispose();
        }
    }
}

using MajdataPlay.Behaviours.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MajdataPlay.Editor
{
    using Editor = UnityEditor.Editor;

    [CustomEditor(typeof(ButtonAnimation))]
    public sealed class ButtonAnimationInspector : Editor
    {
        double _previewRepaintUntil;
        bool _isPreviewing;
        Vector3 _originalScale;

        void OnEnable()
        {
            EditorApplication.update += RepaintPreview;
        }

        void OnDisable()
        {
            EditorApplication.update -= RepaintPreview;
            FinishPreview();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation Preview", EditorStyles.boldLabel);

            if (GUILayout.Button("Play Click Animation"))
            {
                StartPreview();
            }

            using (new EditorGUI.DisabledScope(!_isPreviewing))
            {
                if (GUILayout.Button("Stop Preview"))
                {
                    FinishPreview();
                }
            }

            EditorGUILayout.HelpBox(
                "Previews one press-and-release cycle using Released Scale, Pressed Scale, Animation Duration, and Ease Type.",
                MessageType.Info);
        }

        void StartPreview()
        {
            FinishPreview();

            var animation = (ButtonAnimation)target;
            _originalScale = animation.transform.localScale;
            animation.PlayClickAnimationPreview();
            _previewRepaintUntil = EditorApplication.timeSinceStartup
                                 + Mathf.Max(animation.AnimationDuration, 0f) * 2f
                                 + 0.1d;
            _isPreviewing = true;
        }

        void RepaintPreview()
        {
            if (!_isPreviewing)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup > _previewRepaintUntil)
            {
                FinishPreview();
                return;
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            InternalEditorUtility.RepaintAllViews();
            Repaint();
        }

        void FinishPreview()
        {
            if (!_isPreviewing)
            {
                return;
            }

            _isPreviewing = false;
            if (target != null)
            {
                ((ButtonAnimation)target).StopAnimationPreview(_originalScale);
            }
            EditorApplication.QueuePlayerLoopUpdate();
            InternalEditorUtility.RepaintAllViews();
            Repaint();
        }
    }
}

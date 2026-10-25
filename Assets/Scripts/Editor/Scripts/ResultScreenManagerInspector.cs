using Cysharp.Threading.Tasks;
using MajdataPlay.Scenes.Result;
using MajdataPlay.Scenes.Result.Components;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Editor
{
    using Editor = UnityEditor.Editor;

    [CustomEditor(typeof(ResultScreenManager))]
    public sealed class ResultScreenManagerInspector : Editor
    {
        double _previewRepaintUntil;
        bool _isPreviewing;
        SongFeedbackPanel _feedbackPreview;

        void OnEnable()
        {
            EditorApplication.update += RepaintPreview;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ResetPreviews;
        }

        void OnDisable()
        {
            ResetPreviews();
            EditorApplication.update -= RepaintPreview;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ResetPreviews;
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("结算页预览", EditorStyles.boldLabel);

            if (!EditorApplication.isPlaying)
            {
                var feedback = ((ResultScreenManager)target).GetComponent<SongFeedbackPanel>();
                if (feedback != null)
                {
                    if (GUILayout.Button("播放成绩 → 评价切换"))
                    {
                        ResetPreviews();
                        _feedbackPreview = feedback;
                        feedback.PreviewFeedbackTransitionAsync().SuppressCancellationThrow().Forget();
                        _previewRepaintUntil = EditorApplication.timeSinceStartup
                                             + SongFeedbackPanel.TRANSITION_DURATION_SEC + 0.1;
                        _isPreviewing = true;
                    }
                    if (GUILayout.Button("恢复成绩页"))
                    {
                        ResetPreviews();
                        feedback.ResetFeedbackPreview();
                        EditorApplication.QueuePlayerLoopUpdate();
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    }
                }
            }

            if (GUILayout.Button("Play Score Animation"))
            {
                ResetPreviews();
                var manager = (ResultScreenManager)target;
                if (!manager.PreviewAchievementRollAnimation())
                {
                    Debug.LogWarning("Unable to preview the score animation: accDX must contain a numeric percentage.");
                }
                else
                {
                    _previewRepaintUntil = EditorApplication.timeSinceStartup
                                         + ResultScreenManager.RESULT_ANIMATION_DURATION_SEC;
                    _isPreviewing = true;
                }
            }

            EditorGUILayout.Space();
            base.OnInspectorGUI();
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) ResetPreviews();
        }

        void ResetPreviews()
        {
            StopEditModePreview();
            if (_feedbackPreview != null) _feedbackPreview.ResetFeedbackPreview();
            _feedbackPreview = null;
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        void RepaintPreview()
        {
            if (!_isPreviewing)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup > _previewRepaintUntil
             && (_feedbackPreview == null || !_feedbackPreview.IsBusy))
            {
                StopEditModePreview();
                return;
            }

            EditorApplication.QueuePlayerLoopUpdate();
            Canvas.ForceUpdateCanvases();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Repaint();
        }

        void StopEditModePreview()
        {
            if (!_isPreviewing)
            {
                return;
            }
            if (_feedbackPreview == null && !EditorApplication.isPlaying
             && target is ResultScreenManager manager
             && manager != null)
            {
                manager.StopResultAnimationPreview();
            }
            _isPreviewing = false;
        }
    }
}

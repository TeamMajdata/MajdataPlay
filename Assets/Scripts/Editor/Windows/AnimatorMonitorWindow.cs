using MajdataPlay.Runtime.Monitors;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
#nullable enable
namespace MajdataPlay.Editor.Windows
{
    public sealed class AnimatorMonitorWindow : EditorWindow
    {
        private static readonly string[] CullingNames =
        {
            "Always Animate",
            "Cull Update Transforms",
            "Cull Completely"
        };

        private readonly List<Animator> _animators = new();
        private readonly Dictionary<EntityId, AnimatorCache> _cachedMinitors = new();
        private readonly List<Row> _rows = new();

        private Vector2 _scroll;

        private bool _autoRefresh = true;
        private bool _autoRefreshObjects = true;
        private bool _onlyUpdated;
        private bool _onlyActive = true;

        private double _lastRefreshTime;

        private Recorder _processAnimationsRecorder;

        [MenuItem("Window/Analysis/Animator Monitor")]
        public static void Open()
        {
            GetWindow<AnimatorMonitorWindow>("Animator Monitor");
        }

        private void OnEnable()
        {
            TryCreateRecorder();

            EditorApplication.update += EditorUpdate;
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;

            if (_processAnimationsRecorder != null)
            {
                _processAnimationsRecorder.enabled = false;
                _processAnimationsRecorder = null;
            }
        }

        private void TryCreateRecorder()
        {
            try
            {
                _processAnimationsRecorder =
                    Recorder.Get("Animator.ProcessAnimations");

                if (_processAnimationsRecorder != null &&
                    _processAnimationsRecorder.isValid)
                {
                    _processAnimationsRecorder.enabled = true;
                }
            }
            catch
            {
                _processAnimationsRecorder = null;
            }
        }

        private void EditorUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!_autoRefresh)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;

            if (now - _lastRefreshTime < 0.1)
            {
                return;
            }

            _lastRefreshTime = now;

            RefreshObjectList();

            Refresh();

            Repaint();
        }
        private void RefreshObjectList()
        {
            if (!_autoRefreshObjects)
            {
                return;
            }
            for (var i = 0; i < _animators.Count; i++)
            {
                var anim = _animators[i];
                if (!anim)
                {
                    _cachedMinitors.Remove(anim.GetEntityId());
                }
            }
            _animators.Clear();
            var animators = FindObjectsByType<Animator>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            _animators.AddRange(animators);
        }

        private void Refresh()
        {
            _rows.Clear();

            foreach (var animator in _animators)
            {
                if (!animator)
                {
                    continue;
                }
                var isActive = animator.isActiveAndEnabled && animator.gameObject.activeInHierarchy;

                if (_onlyActive && !isActive)
                {
                    continue;
                }
                var cache = default(AnimatorCache);

                if(!_cachedMinitors.TryGetValue(animator.GetEntityId(), out cache))
                {
                    cache = new AnimatorCache()
                    {
                        Monitor = animator.TryGetComponent<AnimatorMonitor>(out var existingMonitor) ? existingMonitor : animator.gameObject.AddComponent<AnimatorMonitor>(),
                        GameObject = animator.gameObject,
                        Transform = animator.transform
                    };
                    _cachedMinitors[animator.GetEntityId()] = cache;
                }

                var monitor = cache.Monitor;
                var updated = monitor.UpdatedThisFrame;

                if (_onlyUpdated && !updated)
                {
                    continue;
                }

                _rows.Add(new Row
                {
                    Animator = animator,
                    Monitor = monitor,
                    Updated = updated,
                    IsActive = isActive
                });
            }

            _rows.Sort(
                static (a, b) =>
                {
                    int result =
                        b.Updated.CompareTo(a.Updated);

                    if (result != 0)
                    {
                        return result;
                    }

                    return string.Compare(
                        a.Animator.name,
                        b.Animator.name,
                        StringComparison.Ordinal);
                });
        }

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.Space(4);

            DrawStatistics();

            EditorGUILayout.Space(4);

            DrawHeader();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var row in _rows)
            {
                DrawRow(row);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var newAuto =
                GUILayout.Toggle(
                    _autoRefresh,
                    "Auto Update",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(100));

            if (newAuto != _autoRefresh)
            {
                _autoRefresh = newAuto;
            }

            var newAutoRefreshObjects = 
                GUILayout.Toggle(
                    _autoRefreshObjects,
                    "Auto Refresh Objects",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(130));

            if (newAutoRefreshObjects != _autoRefreshObjects)
            {
                _autoRefreshObjects = newAutoRefreshObjects;

                if (_autoRefreshObjects)
                {
                    RefreshObjectList();
                }
            }

            var newOnlyUpdated =
                GUILayout.Toggle(
                    _onlyUpdated,
                    "Only Updated",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(100));

            if (newOnlyUpdated != _onlyUpdated)
            {
                _onlyUpdated = newOnlyUpdated;
                Refresh();
            }

            var newOnlyActive =
                GUILayout.Toggle(
                    _onlyActive,
                    "Only Active",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(90));

            if (newOnlyActive != _onlyActive)
            {
                _onlyActive = newOnlyActive;
                Refresh();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(
                    "Refresh",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(70)))
            {
                Refresh();
            }

            if (GUILayout.Button(
                    "Refresh Objects",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(110)))
            {
                RefreshObjectList();
                Refresh();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatistics()
        {
            int total = 0;
            int updated = 0;

            foreach (var row in _rows)
            {
                total++;

                if (row.Updated)
                    updated++;
            }

            EditorGUILayout.BeginHorizontal(
                EditorStyles.helpBox);

            GUILayout.Label(
                $"Animators: {total}",
                GUILayout.Width(110));

            GUILayout.Label(
                $"Updated: {updated}",
                GUILayout.Width(100));

            if (_processAnimationsRecorder != null &&
                _processAnimationsRecorder.isValid)
            {
                double ms =
                    _processAnimationsRecorder.elapsedNanoseconds /
                    1_000_000.0;

                GUILayout.Label(
                    $"Animator.ProcessAnimations: {ms:F3} ms",
                    GUILayout.Width(250));
            }
            else
            {
                GUILayout.Label(
                    "Animator.ProcessAnimations: N/A",
                    GUILayout.Width(250));
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(
                "Updated",
                GUILayout.Width(55));

            GUILayout.Label(
                "Is Active",
                GUILayout.Width(55));

            GUILayout.Label(
                "GameObject",
                GUILayout.Width(180));

            GUILayout.Label(
                "Controller",
                GUILayout.Width(180));

            GUILayout.Label(
                "State",
                GUILayout.Width(180));

            GUILayout.Label(
                "Layer",
                GUILayout.Width(60));

            GUILayout.Label(
                "Culling",
                GUILayout.Width(140));

            GUILayout.Label(
                "Sample",
                GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRow(Row row)
        {
            Animator animator = row.Animator;

            if (animator == null)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(
                row.Updated ? "YES" : "-",
                GUILayout.Width(55));

            GUILayout.Label(
                row.IsActive ? "YES" : "NO",
                GUILayout.Width(55));

            if (GUILayout.Button(
                    animator.gameObject.name,
                    EditorStyles.label,
                    GUILayout.Width(180)))
            {
                Selection.activeGameObject =
                    animator.gameObject;

                EditorGUIUtility.PingObject(
                    animator.gameObject);
            }

            string controllerName =
                animator.runtimeAnimatorController != null
                    ? animator.runtimeAnimatorController.name
                    : "<None>";

            GUILayout.Label(
                controllerName,
                GUILayout.Width(180));

            string stateName = "<None>";
            int layer = -1;

            if (animator.isActiveAndEnabled &&
                animator.layerCount > 0)
            {
                layer = 0;

                AnimatorStateInfo state =
                    animator.GetCurrentAnimatorStateInfo(0);

                stateName =
                    $"{state.shortNameHash} " +
                    $"({state.normalizedTime:F2})";
            }

            GUILayout.Label(
                stateName,
                GUILayout.Width(180));

            GUILayout.Label(
                layer >= 0
                    ? layer.ToString()
                    : "-",
                GUILayout.Width(60));

            GUILayout.Label(
                GetCullingMode(animator),
                GUILayout.Width(140));

            float sampleTime =
                row.Monitor != null
                    ? row.Monitor.EstimatedTimeMs
                    : 0;

            GUILayout.Label(
                $"{sampleTime:F3} ms",
                GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();
        }

        private static string GetCullingMode(
            Animator animator)
        {
            switch (animator.cullingMode)
            {
                case AnimatorCullingMode.AlwaysAnimate:
                    return "Always Update";

                case AnimatorCullingMode.CullUpdateTransforms:
                    return "Cull Update Transforms";

                case AnimatorCullingMode.CullCompletely:
                    return "Cull Completely";

                default:
                    return animator.cullingMode.ToString();
            }
        }

        private sealed class Row
        {
            public Animator Animator;
            public AnimatorMonitor Monitor;
            public bool Updated;
            public bool IsActive;
        }
        public struct AnimatorCache
        {
            public AnimatorMonitor Monitor;
            public GameObject GameObject;
            public Transform Transform;
        }
    }
}

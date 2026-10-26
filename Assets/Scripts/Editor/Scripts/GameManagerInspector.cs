using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Editor
{
    using Editor = UnityEditor.Editor;
    [CustomEditor(typeof(GameManager))]
    public class GameManagerInspector : Editor
    {
        string _selectedPath = string.Empty;
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var manager = (GameManager)target;
            EditorGUILayout.BeginHorizontal();
            _selectedPath = EditorGUILayout.TextField(_selectedPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                _selectedPath = EditorUtility.OpenFilePanel("Select archive", "", "zip");
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            if (GUILayout.Button("Import!"))
            {
                if(!EditorApplication.isPlaying)
                {
                    return;
                }
                if(!string.IsNullOrEmpty(_selectedPath))
                {
                    manager.DebugImportChartArchive(_selectedPath);
                }
            }
        }
    }
}
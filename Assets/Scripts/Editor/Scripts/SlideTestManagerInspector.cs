using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide.Utils;
using MajdataPlay.Scenes.SlideTest;
using System;
using UnityEditor;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Editor
{
    using Editor = UnityEditor.Editor;

    /// <summary>
    /// Inspector of the slide test scene manager. It shows only the settings of the selected slide source, picks
    /// predefined tables from a list, offers a button for every predefined table that generates its slide at once, and
    /// in Play Mode controls the clock and selects the generated slides.
    /// </summary>
    [CustomEditor(typeof(SlideTestManager))]
    internal sealed class SlideTestManagerInspector : Editor
    {
        /// <summary>How many predefined table buttons share a row.</summary>
        private const int TableButtonsPerRow = 5;

        /// <summary>The session key of the foldout of the predefined table buttons.</summary>
        private const string TableButtonsKey = "MajdataPlay.SlideTestManagerInspector.TableButtons";

        /// <summary>The names of the predefined tables followed by the wifi table, created on first use.</summary>
        private static string[]? s_tableNames;

        /// <summary>Repaints every frame in Play Mode so the clock stays current.</summary>
        /// <returns><see langword="true"/> while the editor is playing.</returns>
        public override bool RequiresConstantRepaint()
        {
            return EditorApplication.isPlaying;
        }

        /// <summary>Draws the settings, the predefined table buttons and, in Play Mode, the clock controls and the slides.</summary>
        public override void OnInspectorGUI()
        {
            var manager = (SlideTestManager)target;
            serializedObject.Update();
            DrawSettings();
            var isQuickGenerateRequested = DrawTableButtons();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode in this scene to generate slides. The Init scene sets up the game first and then loads this scene again. Every later generation reloads the scene with these settings.", MessageType.Info);
                return;
            }
            if (isQuickGenerateRequested)
            {
                manager.Generate();
            }
            DrawControls(manager);
            DrawSlides(manager);
        }

        /// <summary>Draws the settings of the selected source and the shared playback settings.</summary>
        private void DrawSettings()
        {
            var source = serializedObject.FindProperty("_source");
            EditorGUILayout.PropertyField(source);
            using (new EditorGUI.IndentLevelScope())
            {
                switch ((SlideTestSource)source.enumValueIndex)
                {
                    case SlideTestSource.Simai:
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_simai"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_isSimaiSlideTranslated"), new GUIContent("Translate to slide code", "Off builds simai slides natively from the slide prefabs."));
                        break;
                    case SlideTestSource.SlideCode:
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_slideCode"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_duration"));
                        break;
                    default:
                        DrawTablePopup(serializedObject.FindProperty("_tableName"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_startKey"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_isMirrored"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_duration"));
                        break;
                }
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_bpm"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_leadInSec"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_tailSec"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_playbackSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_autoplay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_isLooping"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_isGeneratingOnStart"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_loadingIndicator"));
        }

        /// <summary>Draws the predefined table as a popup of the known table names.</summary>
        /// <param name="tableName">The table name property.</param>
        private static void DrawTablePopup(SerializedProperty tableName)
        {
            var names = GetTableNames();
            var index = Array.IndexOf(names, tableName.stringValue);
            var newIndex = EditorGUILayout.Popup("Table", Math.Max(index, 0), names);
            if (newIndex != index)
            {
                tableName.stringValue = names[newIndex];
            }
        }

        /// <summary>
        /// Draws a button for every predefined table; a click selects the table as the source with the current start key,
        /// mirror and duration.
        /// </summary>
        /// <returns><see langword="true"/> when a button was clicked, so that the slide is generated.</returns>
        private bool DrawTableButtons()
        {
            var isOpen = SessionState.GetBool(TableButtonsKey, true);
            var newIsOpen = EditorGUILayout.Foldout(isOpen, "Generate a predefined table", true, EditorStyles.foldoutHeader);
            if (newIsOpen != isOpen)
            {
                SessionState.SetBool(TableButtonsKey, newIsOpen);
            }
            if (!newIsOpen)
            {
                return false;
            }
            var names = GetTableNames();
            var isClicked = false;
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                for (var row = 0; row < names.Length; row += TableButtonsPerRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (var i = row; i < Math.Min(row + TableButtonsPerRow, names.Length); i++)
                        {
                            if (GUILayout.Button(names[i], EditorStyles.miniButton))
                            {
                                serializedObject.FindProperty("_source").enumValueIndex = (int)SlideTestSource.PredefinedTable;
                                serializedObject.FindProperty("_tableName").stringValue = names[i];
                                isClicked = true;
                            }
                        }
                    }
                }
            }
            return isClicked;
        }

        /// <summary>Draws the clock, the generated chart and the buttons that generate, pause and step.</summary>
        /// <param name="manager">The manager.</param>
        private static void DrawControls(SlideTestManager manager)
        {
            EditorGUILayout.LabelField("Clock", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("State", manager.State.ToString());
            EditorGUILayout.LabelField("Time", $"{manager.ThisFrameSec:0.000} s / {manager.AudioLength:0.000} s");
            var progress = manager.AudioLength <= 0f ? 0f : Mathf.Clamp01(manager.ThisFrameSec / manager.AudioLength);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"Last note ends at {manager.EndSec:0.000} s");
            if (manager.Fumen.Length != 0)
            {
                EditorGUILayout.LabelField("Chart", manager.Fumen);
            }
            if (manager.LastError.Length != 0)
            {
                EditorGUILayout.HelpBox(manager.LastError, MessageType.Error);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    manager.Generate();
                }
                using (new EditorGUI.DisabledScope(!manager.IsStart))
                {
                    if (GUILayout.Button(manager.State == SlideTestState.Paused ? "Resume" : "Pause"))
                    {
                        manager.TogglePause();
                    }
                    if (GUILayout.Button("+1 frame"))
                    {
                        manager.StepFrames(1);
                    }
                    if (GUILayout.Button("+10 frames"))
                    {
                        manager.StepFrames(10);
                    }
                }
            }
        }

        /// <summary>Lists the generated slides with a button that selects each one, to open it in the slide inspector.</summary>
        /// <param name="manager">The manager.</param>
        private static void DrawSlides(SlideTestManager manager)
        {
            var slides = manager.FindSlides();
            if (slides.Length == 0)
            {
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Slides", EditorStyles.boldLabel);
            foreach (var slide in slides)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{slide.GetType().Name} {DescribeShape(slide)}");
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        Selection.activeGameObject = slide.gameObject;
                    }
                }
            }
        }

        /// <summary>Describes the shape of a generated slide.</summary>
        /// <param name="slide">The slide.</param>
        /// <returns>The slide code of an extend slide, otherwise the prefab slide type with its keys.</returns>
        private static string DescribeShape(SlideBase slide)
        {
            if (slide is ExtendSlideDrop extendSlide)
            {
                return extendSlide.SlideCode;
            }
            return $"{slide.SlideType} {slide.StartPos} -> {slide.EndPos}";
        }

        /// <summary>Gets the names of the predefined tables followed by the wifi table.</summary>
        /// <returns>The table names.</returns>
        private static string[] GetTableNames()
        {
            if (s_tableNames is null)
            {
                var shapes = SlideTables.PredefinedShapes;
                var names = new string[shapes.Count + 1];
                for (var i = 0; i < shapes.Count; i++)
                {
                    names[i] = shapes[i].Name;
                }
                names[shapes.Count] = SlideTestManager.WifiTableName;
                s_tableNames = names;
            }
            return s_tableNames;
        }
    }
}

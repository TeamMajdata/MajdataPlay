using MajdataPlay.IO;
using MajdataPlay.Scenes.Game.Notes.Behaviours;
using MajdataPlay.Scenes.Game.Notes.Slide;
using MajdataPlay.Scenes.Game.Parsing.Slide;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Editor
{
    using Editor = UnityEditor.Editor;

    /// <summary>
    /// Inspector of every slide behaviour. In Play Mode it adds, below the serialized fields, the judge queues with the
    /// areas the player has passed and the sensors pressed in this frame, the judge table and timings, the flags and
    /// connection of the slide, and for extend slides the slide code with its parsed metadata. It can also label the
    /// arrow indexes in the Scene view, to compare them with the arrow progress of the judge areas.
    /// </summary>
    [CustomEditor(typeof(SlideBase), true)]
    internal sealed class SlideInspector : Editor
    {
        /// <summary>The prefix of the session keys that remember the open sections.</summary>
        private const string SessionKeyPrefix = "MajdataPlay.SlideInspector.";

        /// <summary>The session key of the toggle that labels the arrows in the Scene view.</summary>
        private const string ArrowLabelsKey = SessionKeyPrefix + "ArrowLabels";

        /// <summary>The width of the narrow columns of the judge area table.</summary>
        private const float NarrowColumnWidth = 30f;

        /// <summary>The width of the wide columns of the judge area table.</summary>
        private const float WideColumnWidth = 64f;

        /// <summary>The label style that understands rich text, created on first use because it needs the editor skin.</summary>
        private static GUIStyle? s_richLabel;

        /// <summary>The Scene view style of the arrows that are still shown.</summary>
        private static GUIStyle? s_shownArrowLabel;

        /// <summary>The Scene view style of the arrows that are already hidden.</summary>
        private static GUIStyle? s_hiddenArrowLabel;

        /// <summary>Repaints every frame in Play Mode so the inspector follows the slide.</summary>
        /// <returns><see langword="true"/> while the editor is playing.</returns>
        public override bool RequiresConstantRepaint()
        {
            return EditorApplication.isPlaying;
        }

        /// <summary>Draws the serialized fields and, in Play Mode, the runtime state of the slide.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Slide Debug", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect the judge queues and the runtime state of this slide.", MessageType.Info);
                return;
            }
            var slide = (SlideBase)target;
            if (!TryCapture(slide, out var info))
            {
                EditorGUILayout.HelpBox("The judge table of this slide has already been released.", MessageType.Warning);
                return;
            }

            DrawOverview(slide, info);
            DrawJudgeQueues(info);
            DrawTiming(slide, info);
            DrawFlags(slide, info);
            DrawConnection(slide);
            if (slide is ExtendSlideDrop extendSlide)
            {
                DrawSlideCode(extendSlide);
            }
        }

        /// <summary>Labels the arrow indexes in the Scene view when the toggle in the Flags section is on.</summary>
        private void OnSceneGUI()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(ArrowLabelsKey, false))
            {
                return;
            }
            if (!TryCapture((SlideBase)target, out var info))
            {
                return;
            }
            for (var i = 0; i < info.ArrowPositions.Length; i++)
            {
                var style = i < info.HiddenArrowCount ? GetHiddenArrowLabel() : GetShownArrowLabel();
                Handles.Label(info.ArrowPositions[i], i.ToString(), style);
            }
        }

        /// <summary>Draws the shape, the table, the state and the judgment of the slide.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="info">The captured state.</param>
        private static void DrawOverview(SlideBase slide, SlideDebugInfo info)
        {
            if (!Foldout("Overview", "Overview"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Type", slide.GetType().Name);
                EditorGUILayout.LabelField("Shape", GetShape(slide));
                EditorGUILayout.LabelField("Judge table", info.TableName.Length == 0 ? "(generated)" : info.TableName);
                EditorGUILayout.LabelField("State", slide.State.ToString());
                var judgment = info.IsJudged ? $"{info.JudgeResult} ({info.JudgeDiffMSec:+0.0;-0.0;0.0} ms)" : "Not judged";
                EditorGUILayout.LabelField("Judgment", judgment);
                var passed = 0;
                var total = 0;
                foreach (var queue in info.Queues)
                {
                    passed += queue.PassedCount;
                    total += queue.Areas.Length;
                }
                var progress = total == 0 ? 0f : (float)passed / total;
                EditorGUI.ProgressBar(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect()), progress, $"Judge areas passed: {passed} / {total}");
            }
        }

        /// <summary>Draws every judge queue as a table of its areas.</summary>
        /// <param name="info">The captured state.</param>
        private static void DrawJudgeQueues(SlideDebugInfo info)
        {
            if (!Foldout("JudgeQueues", "Judge queues"))
            {
                return;
            }
            if (info.Queues.Length == 0)
            {
                EditorGUILayout.HelpBox("The slide has no judge queue yet; it is built when the slide is initialized.", MessageType.None);
                return;
            }
            foreach (var queue in info.Queues)
            {
                EditorGUILayout.LabelField($"{queue.Name}: {queue.PassedCount} / {queue.Areas.Length} passed", EditorStyles.miniBoldLabel);
                DrawAreaRow(string.Empty, "#", "Sensors", "Skip", "State", "Arrows");
                for (var i = 0; i < queue.Areas.Length; i++)
                {
                    DrawArea(i, queue.Areas[i], i < queue.PassedCount, i == queue.PassedCount);
                }
                EditorGUILayout.Space();
            }
        }

        /// <summary>Draws one judge area of a queue.</summary>
        /// <param name="index">The index of the area in its queue.</param>
        /// <param name="area">The captured area.</param>
        /// <param name="isPassed">Whether the player has passed the area.</param>
        /// <param name="isCurrent">Whether the area is the first one still waiting.</param>
        private static void DrawArea(int index, SlideDebugInfo.AreaInfo area, bool isPassed, bool isCurrent)
        {
            var state = area.IsFinished ? "done" : area.IsOn ? "on" : "-";
            var cells = new[]
            {
                isPassed ? "ok" : isCurrent ? "->" : string.Empty,
                index.ToString(),
                FormatSensors(area),
                area.IsSkippable ? "yes" : "no",
                state,
                $"{area.ArrowProgressWhenOn} / {area.ArrowProgressWhenFinished}"
            };
            for (var i = 0; i < cells.Length; i++)
            {
                if (isPassed)
                {
                    cells[i] = Colored(cells[i], EditorGUIUtility.isProSkin ? "#7F7F7F" : "#8C8C8C");
                }
                else if (isCurrent)
                {
                    cells[i] = $"<b>{cells[i]}</b>";
                }
            }
            DrawAreaRow(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]);
        }

        /// <summary>Draws one row of the judge area table.</summary>
        /// <param name="marker">The progress marker.</param>
        /// <param name="index">The area index.</param>
        /// <param name="sensors">The sensors of the area.</param>
        /// <param name="skippable">Whether the area may be skipped.</param>
        /// <param name="state">Whether the area is pressed or finished.</param>
        /// <param name="arrows">The arrow progress when pressed and when finished.</param>
        private static void DrawAreaRow(string marker, string index, string sensors, string skippable, string state, string arrows)
        {
            var style = GetRichLabel();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f + 15f);
                GUILayout.Label(marker, style, GUILayout.Width(NarrowColumnWidth));
                GUILayout.Label(index, style, GUILayout.Width(NarrowColumnWidth));
                GUILayout.Label(sensors, style, GUILayout.MinWidth(WideColumnWidth));
                GUILayout.Label(skippable, style, GUILayout.Width(NarrowColumnWidth + 6f));
                GUILayout.Label(state, style, GUILayout.Width(NarrowColumnWidth + 6f));
                GUILayout.Label(arrows, style, GUILayout.Width(WideColumnWidth));
            }
        }

        /// <summary>Draws the timings that drive the star and the judgment.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="info">The captured state.</param>
        private static void DrawTiming(SlideBase slide, SlideDebugInfo info)
        {
            if (!Foldout("Timing", "Timing"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                var now = slide.ThisFrameSec;
                EditorGUILayout.LabelField("This frame", FormatSeconds(now));
                EditorGUILayout.LabelField("Head timing", FormatSeconds(slide.Timing));
                EditorGUILayout.LabelField("Star starts", FormatSeconds(slide.StartTiming));
                EditorGUILayout.LabelField("Duration", FormatSeconds(slide.Length));
                EditorGUILayout.LabelField("Star arrives", FormatSeconds(slide.ArriveTiming));
                EditorGUILayout.LabelField("Judge timing", FormatSeconds(slide.JudgeTimingWithOffset));
                EditorGUILayout.LabelField("To judge timing", $"{now - slide.JudgeTimingWithOffset:+0.000;-0.000;0.000} s");
                EditorGUILayout.LabelField("Last wait", FormatSeconds(info.LastWaitTimeSec));
                var usedConst = slide.IsClassic ? "classic" : "modern";
                EditorGUILayout.LabelField("Constant", $"{info.TableConst:0.####} modern, {info.TableClassicConst:0.####} classic ({usedConst} used)");
                var starProgress = slide.Length <= 0f ? 0f : Mathf.Clamp01((now - slide.StartTiming) / slide.Length);
                EditorGUI.ProgressBar(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect()), starProgress, $"Star progress: {starProgress:P0}");
            }
        }

        /// <summary>Draws the keys, flags and arrows of the slide, with the Scene view arrow label toggle.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="info">The captured state.</param>
        private static void DrawFlags(SlideBase slide, SlideDebugInfo info)
        {
            if (!Foldout("Flags", "Flags and arrows"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Keys", $"{slide.StartPos} -> {slide.EndPos}");
                var flags = new List<string>();
                AddFlag(flags, slide.IsBreak, "break");
                AddFlag(flags, slide.IsEach, "each");
                AddFlag(flags, slide.IsEX, "EX");
                AddFlag(flags, slide.IsMine, "mine");
                AddFlag(flags, slide.IsJustR, "result faces right");
                AddFlag(flags, slide is SingleSlideBase { IsMirror: true }, "mirrored");
                AddFlag(flags, slide.IsClassic, "classic");
                AddFlag(flags, slide.IsSlideNoHead, "no head");
                AddFlag(flags, slide.IsSlideNoTrack, "no track");
                EditorGUILayout.LabelField("Flags", flags.Count == 0 ? "none" : string.Join(", ", flags));
                EditorGUILayout.LabelField("Reads sensors", info.IsCheckable ? "yes" : "no");
                EditorGUILayout.LabelField("Active", slide.Active ? "yes" : "no");
                EditorGUILayout.LabelField("Multiple", slide.Multiple.ToString());
                EditorGUILayout.LabelField("Sort order", slide.SortOrder.ToString());
                EditorGUILayout.LabelField("Arrows", $"{info.ArrowPositions.Length - info.HiddenArrowCount} shown, {info.HiddenArrowCount} hidden");
                var showLabels = SessionState.GetBool(ArrowLabelsKey, false);
                var newShowLabels = EditorGUILayout.Toggle("Label arrows in Scene", showLabels);
                if (newShowLabels != showLabels)
                {
                    SessionState.SetBool(ArrowLabelsKey, newShowLabels);
                    SceneView.RepaintAll();
                }
            }
        }

        /// <summary>Draws the connection slide information.</summary>
        /// <param name="slide">The slide.</param>
        private static void DrawConnection(SlideBase slide)
        {
            if (!Foldout("Connection", "Connection", false))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                var connectInfo = slide.ConnectInfo;
                if (!connectInfo.IsConnSlide)
                {
                    EditorGUILayout.LabelField("Part of a connection slide", "no");
                    return;
                }
                var position = connectInfo.IsGroupPartHead ? "head" : connectInfo.IsGroupPartEnd ? "end" : "middle";
                EditorGUILayout.LabelField("Part of a connection slide", position);
                EditorGUILayout.LabelField("Group starts", FormatSeconds(connectInfo.StartTiming));
                EditorGUILayout.LabelField("Group duration", FormatSeconds(connectInfo.TotalLength));
                EditorGUILayout.LabelField("Group judge areas", connectInfo.TotalJudgeQueueLen.ToString());
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Parent", connectInfo.Parent?.GameObject, typeof(GameObject), true);
                }
            }
        }

        /// <summary>Draws the slide code of an extend slide, its judge table source and its parsed metadata.</summary>
        /// <param name="slide">The extend slide.</param>
        private static void DrawSlideCode(ExtendSlideDrop slide)
        {
            if (!Foldout("SlideCode", "Slide code"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Slide code", slide.SlideCode.Length == 0 ? "(not set)" : slide.SlideCode);
                var tableSource = slide.PredefinedTable is null ? "generated by the slide code algorithm" : $"predefined \"{slide.PredefinedTable.Name}\"";
                EditorGUILayout.LabelField("Judge table", tableSource);
                if (slide.Metadata is not ExtendSlideMetadata metadata)
                {
                    EditorGUILayout.LabelField("Metadata", "(not set)");
                    return;
                }
                EditorGUILayout.LabelField("Path length", metadata.SlideLength.ToString("0.###"));
                EditorGUILayout.LabelField("Generated constant", metadata.SlideConst.ToString("0.####"));
                EditorGUILayout.LabelField("Arrow poses", $"{metadata.ArrowPoses.Length} with both path ends, last arrow conditional: {(metadata.ConditionalLastArrow ? "yes" : "no")}");
                EditorGUILayout.LabelField("Slide result", metadata.OkType.ToString());
                EditorGUILayout.LabelField("Generated judge areas", EditorStyles.miniBoldLabel);
                DrawAreaRow(string.Empty, "#", "Sensors", string.Empty, string.Empty, "Arrows");
                for (var i = 0; i < metadata.JudgeAreaQueue.Length; i++)
                {
                    var area = metadata.JudgeAreaQueue[i];
                    var sensors = area.SensorB is SensorArea sensorB ? $"{area.SensorA} | {sensorB}" : area.SensorA.ToString();
                    DrawAreaRow(string.Empty, i.ToString(), sensors, string.Empty, string.Empty, $"{area.ArrowProgressPush} / {area.ArrowProgressFinish}");
                }
            }
        }

        /// <summary>Captures the state of a slide, which fails once its judge table has been released.</summary>
        /// <param name="slide">The slide.</param>
        /// <param name="info">Receives the captured state.</param>
        /// <returns><see langword="true"/> when the state was captured.</returns>
        private static bool TryCapture(SlideBase slide, out SlideDebugInfo info)
        {
            try
            {
                info = slide.CaptureDebugInfo();
                return true;
            }
            catch (ObjectDisposedException)
            {
                info = new SlideDebugInfo();
                return false;
            }
        }

        /// <summary>Gets a readable name of the slide shape.</summary>
        /// <param name="slide">The slide.</param>
        /// <returns>The slide code, the prefab slide type or wifi.</returns>
        private static string GetShape(SlideBase slide)
        {
            switch (slide)
            {
                case ExtendSlideDrop extendSlide:
                    return extendSlide.SlideCode.Length == 0 ? "(slide code not set)" : extendSlide.SlideCode;
                case WifiDrop:
                    return "wifi";
                default:
                    return slide.SlideType.Length == 0 ? "(unknown)" : slide.SlideType;
            }
        }

        /// <summary>Formats the sensors of an area, highlighting the ones pressed in this frame.</summary>
        /// <param name="area">The captured area.</param>
        /// <returns>The sensors as rich text, joined by | for OR areas and &amp; for AND areas.</returns>
        private static string FormatSensors(SlideDebugInfo.AreaInfo area)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < area.Sensors.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(area.Policy == AreaPolicy.AND ? " & " : " | ");
                }
                var name = area.Sensors[i].ToString();
                var isPressed = i < area.SensorStatuses.Length && area.SensorStatuses[i] == SwitchStatus.On;
                builder.Append(isPressed ? Colored($"<b>{name}</b>", EditorGUIUtility.isProSkin ? "#5CD65C" : "#1E7D32") : name);
            }
            return builder.ToString();
        }

        /// <summary>Draws a section header that remembers whether it is open for this editor session.</summary>
        /// <param name="key">The key of the section.</param>
        /// <param name="title">The section title.</param>
        /// <param name="isOpenByDefault">Whether the section is open the first time.</param>
        /// <returns>Whether the section is open.</returns>
        private static bool Foldout(string key, string title, bool isOpenByDefault = true)
        {
            var sessionKey = SessionKeyPrefix + key;
            var isOpen = SessionState.GetBool(sessionKey, isOpenByDefault);
            var newIsOpen = EditorGUILayout.Foldout(isOpen, title, true, EditorStyles.foldoutHeader);
            if (newIsOpen != isOpen)
            {
                SessionState.SetBool(sessionKey, newIsOpen);
            }
            return newIsOpen;
        }

        /// <summary>Adds a flag name when the flag is set.</summary>
        /// <param name="flags">The flag names.</param>
        /// <param name="isSet">Whether the flag is set.</param>
        /// <param name="name">The flag name.</param>
        private static void AddFlag(List<string> flags, bool isSet, string name)
        {
            if (isSet)
            {
                flags.Add(name);
            }
        }

        /// <summary>Formats a time in seconds.</summary>
        /// <param name="seconds">The time.</param>
        /// <returns>The time with millisecond precision.</returns>
        private static string FormatSeconds(float seconds)
        {
            return $"{seconds:0.000} s";
        }

        /// <summary>Wraps text in a rich text colour tag.</summary>
        /// <param name="text">The text.</param>
        /// <param name="color">The colour, for example #808080.</param>
        /// <returns>The coloured text.</returns>
        private static string Colored(string text, string color)
        {
            return $"<color={color}>{text}</color>";
        }

        /// <summary>Gets the label style that understands rich text.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetRichLabel()
        {
            s_richLabel ??= new GUIStyle(EditorStyles.label) { richText = true };
            return s_richLabel;
        }

        /// <summary>Gets the Scene view style of the arrows that are still shown.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetShownArrowLabel()
        {
            s_shownArrowLabel ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.yellow } };
            return s_shownArrowLabel;
        }

        /// <summary>Gets the Scene view style of the arrows that are already hidden.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetHiddenArrowLabel()
        {
            s_hiddenArrowLabel ??= new GUIStyle(EditorStyles.label) { normal = { textColor = Color.gray } };
            return s_hiddenArrowLabel;
        }
    }
}

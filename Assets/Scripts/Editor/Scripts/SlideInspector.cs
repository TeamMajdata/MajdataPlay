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
    /// connection of the slide, the touches of the latest run when DJAuto plays the slide, and for extend slides the slide
    /// code with its parsed metadata. In the Scene view it can mark where the stars are at the judge timing and where they
    /// would be with a previewed slide constant, label the arrow indexes, to compare them with the arrow progress of the
    /// judge areas, and draw the DJAuto touches.
    /// </summary>
    [CustomEditor(typeof(SlideBase), true)]
    internal sealed class SlideInspector : Editor
    {
        /// <summary>The prefix of the session keys that remember the open sections.</summary>
        private const string SessionKeyPrefix = "MajdataPlay.SlideInspector.";

        /// <summary>The session key of the toggle that labels the arrows in the Scene view.</summary>
        private const string ArrowLabelsKey = SessionKeyPrefix + "ArrowLabels";

        /// <summary>The session key of the toggle that draws the DJAuto touches in the Scene view.</summary>
        private const string DJAutoTouchesKey = SessionKeyPrefix + "DJAutoTouches";

        /// <summary>The session key of the toggle that marks the star positions at the judge timing in the Scene view.</summary>
        private const string JudgeFrameKey = SessionKeyPrefix + "JudgeFrame";

        /// <summary>The radius of the dot that marks the centre of a DJAuto touch in the Scene view.</summary>
        private const float TouchCentreRadius = 0.03f;

        /// <summary>The radius of the ring that marks a star position at the judge timing in the Scene view.</summary>
        private const float JudgeFrameRadius = 0.4f;

        /// <summary>The radius of the dot that marks the centre of a star position at the judge timing in the Scene view.</summary>
        private const float JudgeFrameCentreRadius = 0.05f;

        /// <summary>The Scene view colour of the outline and centre of a star position at the judge timing.</summary>
        private static readonly Color s_judgeFrameColor = new(1f, 0.25f, 0.85f, 0.95f);

        /// <summary>The Scene view colour that fills a star position at the judge timing.</summary>
        private static readonly Color s_judgeFrameFillColor = new(1f, 0.25f, 0.85f, 0.15f);

        /// <summary>The Scene view colour of the outline and centre of a star position at the judge timing of the previewed constant.</summary>
        private static readonly Color s_constPreviewColor = new(1f, 0.8f, 0.1f, 0.95f);

        /// <summary>The Scene view colour that fills a star position at the judge timing of the previewed constant.</summary>
        private static readonly Color s_constPreviewFillColor = new(1f, 0.8f, 0.1f, 0.15f);

        /// <summary>The Scene view colour of the outline and centre of a DJAuto touch.</summary>
        private static readonly Color s_djAutoTouchColor = new(0f, 0.9f, 1f, 0.9f);

        /// <summary>The Scene view colour that fills the radius of a DJAuto touch.</summary>
        private static readonly Color s_djAutoTouchFillColor = new(0f, 0.9f, 1f, 0.12f);

        /// <summary>The Scene view colour of the outermost ring the touch panel sampling reads for a DJAuto touch.</summary>
        private static readonly Color s_djAutoSampledRingColor = new(1f, 0.6f, 0f, 0.8f);

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

        /// <summary>The Scene view style of the label beside a star position at the judge timing.</summary>
        private static GUIStyle? s_judgeFrameLabel;

        /// <summary>The Scene view style of the label beside a star position at the judge timing of the previewed constant.</summary>
        private static GUIStyle? s_constPreviewLabel;

        /// <summary>
        /// Whether the inspector previews the judge frame of <see cref="_previewConst"/>. The preview belongs to this
        /// inspector and never changes the slide, so it resets when the selection changes.
        /// </summary>
        private bool _isConstPreviewOn;

        /// <summary>The slide constant whose judge frame the inspector previews.</summary>
        private float _previewConst;

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
            if (info.IsDJAutoEnabled)
            {
                DrawDJAuto(info);
            }
            DrawConnection(slide);
            if (slide is ExtendSlideDrop extendSlide)
            {
                DrawSlideCode(extendSlide);
            }
        }

        /// <summary>
        /// Marks where the stars are at the judge timing when the toggle in the Timing section is on, labels the arrow
        /// indexes in the Scene view when the toggle in the Flags section is on, and draws the touches of the latest
        /// DJAuto run when DJAuto plays the slide and the toggle in the DJAuto section is on.
        /// </summary>
        private void OnSceneGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }
            var showJudgeFrame = SessionState.GetBool(JudgeFrameKey, true);
            var showArrowLabels = SessionState.GetBool(ArrowLabelsKey, false);
            var showTouches = SessionState.GetBool(DJAutoTouchesKey, true);
            if (!showJudgeFrame && !_isConstPreviewOn && !showArrowLabels && !showTouches)
            {
                return;
            }
            var slide = (SlideBase)target;
            if (!TryCapture(slide, out var info))
            {
                return;
            }
            if (showJudgeFrame)
            {
                DrawJudgeFrame(info.JudgeFrameStarPositions, s_judgeFrameColor, s_judgeFrameFillColor, "Judge", GetJudgeFrameLabel());
            }
            if (_isConstPreviewOn && info.HasJudgeTiming)
            {
                DrawJudgeFrame(slide.CaptureStarPositionsAt(1f - _previewConst),
                               s_constPreviewColor,
                               s_constPreviewFillColor,
                               $"Const {_previewConst:0.####}",
                               GetConstPreviewLabel());
            }
            if (showArrowLabels)
            {
                for (var i = 0; i < info.ArrowPositions.Length; i++)
                {
                    var style = i < info.HiddenArrowCount ? GetHiddenArrowLabel() : GetShownArrowLabel();
                    Handles.Label(info.ArrowPositions[i], i.ToString(), style);
                }
            }
            if (showTouches && info.IsDJAutoEnabled && IsLatestDJAutoRunCurrent(info))
            {
                DrawDJAutoTouches(info.DJAutoTouches);
            }
        }

        /// <summary>Draws each star position at a judge timing as a filled ring with a centre dot and a label.</summary>
        /// <param name="positions">The star positions at the judge timing.</param>
        /// <param name="color">The colour of the outline, the centre dot and the label.</param>
        /// <param name="fillColor">The colour that fills the ring.</param>
        /// <param name="label">The text drawn beside each ring.</param>
        /// <param name="labelStyle">The style of the label.</param>
        private static void DrawJudgeFrame(Vector3[] positions, Color color, Color fillColor, string label, GUIStyle labelStyle)
        {
            var oldColor = Handles.color;
            foreach (var position in positions)
            {
                Handles.color = fillColor;
                Handles.DrawSolidDisc(position, Vector3.forward, JudgeFrameRadius);
                Handles.color = color;
                Handles.DrawWireDisc(position, Vector3.forward, JudgeFrameRadius, 2f);
                Handles.DrawSolidDisc(position, Vector3.forward, JudgeFrameCentreRadius);
                Handles.Label(position + new Vector3(JudgeFrameRadius, JudgeFrameRadius), label, labelStyle);
            }
            Handles.color = oldColor;
        }

        /// <summary>
        /// Draws each touch as a filled disc of the radius DJAuto asked for, outlined in cyan, with an orange ring at the
        /// outermost ring the touch panel sampling reads.
        /// </summary>
        /// <param name="touches">The touches of the latest DJAuto run.</param>
        private static void DrawDJAutoTouches(SlideDebugInfo.DJAutoTouchInfo[] touches)
        {
            var oldColor = Handles.color;
            foreach (var touch in touches)
            {
                Handles.color = s_djAutoTouchFillColor;
                Handles.DrawSolidDisc(touch.Position, Vector3.forward, touch.Radius);
                Handles.color = s_djAutoTouchColor;
                Handles.DrawWireDisc(touch.Position, Vector3.forward, touch.Radius);
                Handles.DrawSolidDisc(touch.Position, Vector3.forward, TouchCentreRadius);
                Handles.color = s_djAutoSampledRingColor;
                Handles.DrawWireDisc(touch.Position, Vector3.forward, touch.SampledRadius);
            }
            Handles.color = oldColor;
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

        /// <summary>
        /// Draws the timings that drive the star and the judgment, with where the star is at the judge timing, the
        /// toggle that marks it in the Scene view and the preview of another slide constant.
        /// </summary>
        /// <param name="slide">The slide.</param>
        /// <param name="info">The captured state.</param>
        private void DrawTiming(SlideBase slide, SlideDebugInfo info)
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
                EditorGUILayout.LabelField("Judge frame", FormatJudgeFrame(info));
                var showJudgeFrame = SessionState.GetBool(JudgeFrameKey, true);
                var newShowJudgeFrame = EditorGUILayout.Toggle("Mark judge frame in Scene", showJudgeFrame);
                if (newShowJudgeFrame != showJudgeFrame)
                {
                    SessionState.SetBool(JudgeFrameKey, newShowJudgeFrame);
                    SceneView.RepaintAll();
                }
                if (info.HasJudgeTiming)
                {
                    DrawConstPreview(slide, info);
                }
            }
        }

        /// <summary>
        /// Draws the preview of another slide constant: the toggle, the constant slider and the judge timing and star
        /// progress that constant would give. The constant is kept by this inspector, so the slide keeps judging with its
        /// own table.
        /// </summary>
        /// <param name="slide">The slide.</param>
        /// <param name="info">The captured state.</param>
        private void DrawConstPreview(SlideBase slide, SlideDebugInfo info)
        {
            var usedConst = slide.IsClassic ? info.TableClassicConst : info.TableConst;
            var isPreviewOn = EditorGUILayout.Toggle("Preview another const", _isConstPreviewOn);
            if (isPreviewOn != _isConstPreviewOn)
            {
                _isConstPreviewOn = isPreviewOn;
                _previewConst = usedConst;
                SceneView.RepaintAll();
            }
            if (!_isConstPreviewOn)
            {
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var newConst = EditorGUILayout.Slider("Preview const", _previewConst, 0f, 1f);
                if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(WideColumnWidth)))
                {
                    newConst = usedConst;
                }
                if (!Mathf.Approximately(newConst, _previewConst))
                {
                    _previewConst = newConst;
                    SceneView.RepaintAll();
                }
            }
            // 判定时机 = 起点 + 时长 × (1 - const)，所以 const 每变化 1，判定时机就反向移动一个时长
            var shiftSec = (usedConst - _previewConst) * slide.Length;
            EditorGUILayout.LabelField("Preview judge timing", $"{FormatSeconds(slide.JudgeTimingWithOffset + shiftSec)} ({shiftSec * 1000f:+0.0;-0.0;0.0} ms)");
            EditorGUILayout.LabelField("Preview judge frame", $"star at {1f - _previewConst:P1} of the path");
        }

        /// <summary>Formats where the stars are at the judge timing.</summary>
        /// <param name="info">The captured state.</param>
        /// <returns>The share of the path the stars have moved at the judge timing, or why there is none.</returns>
        private static string FormatJudgeFrame(SlideDebugInfo info)
        {
            if (!info.HasJudgeTiming)
            {
                return "judged by the end part of the connection slide";
            }
            if (info.JudgeFrameStarPositions.Length == 0)
            {
                return $"star at {info.JudgeFrameProgress:P1} of the path (path not built yet)";
            }
            return $"star at {info.JudgeFrameProgress:P1} of the path";
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

        /// <summary>
        /// Draws the progress of DJAuto and the touches of its latest run with the sensors each one pressed, with the
        /// toggle that draws the touches in the Scene view.
        /// </summary>
        /// <param name="info">The captured state.</param>
        private static void DrawDJAuto(SlideDebugInfo info)
        {
            if (!Foldout("DJAuto", "DJAuto touches"))
            {
                return;
            }
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Mode", info.AutoplayMode.ToString());
                EditorGUI.ProgressBar(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect()), info.DJAutoplayProgress, $"DJAuto progress: {info.DJAutoplayProgress:P0}");
                EditorGUILayout.LabelField("Latest run", FormatDJAutoRun(info));
                var pressed = new SortedSet<SensorArea>();
                foreach (var touch in info.DJAutoTouches)
                {
                    pressed.UnionWith(touch.Sensors);
                }
                EditorGUILayout.LabelField("Pressed sensors", pressed.Count == 0 ? "none" : string.Join(", ", pressed));
                var showTouches = SessionState.GetBool(DJAutoTouchesKey, true);
                var newShowTouches = EditorGUILayout.Toggle("Draw touches in Scene", showTouches);
                if (newShowTouches != showTouches)
                {
                    SessionState.SetBool(DJAutoTouchesKey, newShowTouches);
                    SceneView.RepaintAll();
                }
                if (newShowTouches)
                {
                    EditorGUILayout.HelpBox("Cyan discs are the touch radius DJAuto asks for; orange rings are the outermost ring the touch panel sampling reads.", MessageType.None);
                }
                if (info.DJAutoTouches.Length == 0)
                {
                    return;
                }
                DrawTouchRow("#", "Position", "Radius", "Sensors");
                for (var i = 0; i < info.DJAutoTouches.Length; i++)
                {
                    var touch = info.DJAutoTouches[i];
                    var sensors = touch.Sensors.Length == 0 ? "-" : string.Join(", ", touch.Sensors);
                    DrawTouchRow(i.ToString(),
                                 $"({touch.Position.x:0.00}, {touch.Position.y:0.00})",
                                 $"{touch.Radius:0.###} / {touch.SampledRadius:0.###}",
                                 sensors);
                }
            }
        }

        /// <summary>Draws one row of the DJAuto touch table.</summary>
        /// <param name="index">The touch index.</param>
        /// <param name="position">The touch centre.</param>
        /// <param name="radius">The asked and the sampled radius.</param>
        /// <param name="sensors">The sensors the touch pressed.</param>
        private static void DrawTouchRow(string index, string position, string radius, string sensors)
        {
            var style = GetRichLabel();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f + 15f);
                GUILayout.Label(index, style, GUILayout.Width(NarrowColumnWidth));
                GUILayout.Label(position, style, GUILayout.Width(WideColumnWidth + 30f));
                GUILayout.Label(radius, style, GUILayout.Width(WideColumnWidth + 10f));
                GUILayout.Label(sensors, style, GUILayout.MinWidth(WideColumnWidth));
            }
        }

        /// <summary>Formats when DJAuto last ran and how many touches it simulated.</summary>
        /// <param name="info">The captured state.</param>
        /// <returns>The frame of the latest run relative to this frame, with the touch count.</returns>
        private static string FormatDJAutoRun(SlideDebugInfo info)
        {
            if (info.DJAutoTouchFrame < 0)
            {
                return "not run yet";
            }
            var age = Time.frameCount - info.DJAutoTouchFrame;
            var when = age <= 0 ? "this frame" : age == 1 ? "previous frame" : $"{age} frames ago";
            return $"frame {info.DJAutoTouchFrame} ({when}), {info.DJAutoTouches.Length} touches";
        }

        /// <summary>
        /// Gets whether the latest DJAuto run belongs to this or the previous frame, so that the Scene view does not keep
        /// drawing the touches of a slide DJAuto has stopped playing.
        /// </summary>
        /// <param name="info">The captured state.</param>
        /// <returns><see langword="true"/> when the touches are current.</returns>
        private static bool IsLatestDJAutoRunCurrent(SlideDebugInfo info)
        {
            return info.DJAutoTouchFrame >= 0 && Time.frameCount - info.DJAutoTouchFrame <= 1;
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
                EditorGUILayout.LabelField("Star poses", $"{metadata.StarPoses.Length} with both path ends");
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

        /// <summary>Gets the Scene view style of the label beside a star position at the judge timing.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetJudgeFrameLabel()
        {
            s_judgeFrameLabel ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = s_judgeFrameColor } };
            return s_judgeFrameLabel;
        }

        /// <summary>Gets the Scene view style of the label beside a star position at the judge timing of the previewed constant.</summary>
        /// <returns>The style.</returns>
        private static GUIStyle GetConstPreviewLabel()
        {
            s_constPreviewLabel ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = s_constPreviewColor } };
            return s_constPreviewLabel;
        }
    }
}

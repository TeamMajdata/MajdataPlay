using MajdataPlay.IO;
using MajdataPlay.Settings;
using System;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Slide
{
    /// <summary>
    /// The runtime state of a slide captured for the slide inspector: every judge queue as it was built and how many of
    /// its areas the player has passed, together with the judgment state the slide keeps protected and the touches DJAuto
    /// simulated in its latest run. Capturing allocates, so only editor tools capture it.
    /// </summary>
    internal sealed class SlideDebugInfo
    {
        /// <summary>Gets the name of the judge table, or an empty string for a table generated from slide code.</summary>
        public string TableName { get; init; } = string.Empty;
        /// <summary>Gets the share of the slide duration that follows the judge timing in modern judgment.</summary>
        public float TableConst { get; init; }
        /// <summary>Gets the share of the slide duration that follows the judge timing in classic judgment.</summary>
        public float TableClassicConst { get; init; }
        /// <summary>Gets whether the slide has been judged.</summary>
        public bool IsJudged { get; init; }
        /// <summary>Gets the judgment result; meaningful once <see cref="IsJudged"/> is true.</summary>
        public JudgeGrade JudgeResult { get; init; }
        /// <summary>Gets the difference between the judgment and the judge timing in milliseconds; negative is fast.</summary>
        public float JudgeDiffMSec { get; init; }
        /// <summary>Gets the time in seconds the slide still waits after its judgment before it ends.</summary>
        public float LastWaitTimeSec { get; init; }
        /// <summary>Gets whether the slide already reads the sensors.</summary>
        public bool IsCheckable { get; init; }
        /// <summary>Gets the world positions of the arrows, in path order.</summary>
        public Vector3[] ArrowPositions { get; init; } = Array.Empty<Vector3>();
        /// <summary>Gets how many arrows from the start of the path are hidden.</summary>
        public int HiddenArrowCount { get; init; }
        /// <summary>Gets the judge queues: one for an ordinary slide, the left, centre and right lanes for a wifi slide.</summary>
        public QueueInfo[] Queues { get; init; } = Array.Empty<QueueInfo>();
        /// <summary>Gets the autoplay mode of the slide.</summary>
        public AutoplayModeOption AutoplayMode { get; init; }
        /// <summary>Gets whether the autoplay mode is one of the DJAuto modes, which simulate touches on the touch panel.</summary>
        public bool IsDJAutoEnabled { get; init; }
        /// <summary>Gets how far along the path, from 0 to 1, DJAuto has touched.</summary>
        public float DJAutoplayProgress { get; init; }
        /// <summary>Gets the frame of the latest DJAuto run, or -1 when DJAuto has not run; only the Editor records it.</summary>
        public int DJAutoTouchFrame { get; init; } = -1;
        /// <summary>Gets the touches DJAuto simulated in its latest run, in the order it pressed them; only the Editor records them.</summary>
        public DJAutoTouchInfo[] DJAutoTouches { get; init; } = Array.Empty<DJAutoTouchInfo>();

        /// <summary>One judge queue of a slide.</summary>
        internal sealed class QueueInfo
        {
            /// <summary>Creates the information of a judge queue.</summary>
            /// <param name="name">The queue name shown by the inspector.</param>
            /// <param name="areas">Every area of the queue as it was built, in judgment order.</param>
            /// <param name="passedCount">How many areas from the start of the queue the player has passed.</param>
            public QueueInfo(string name, AreaInfo[] areas, int passedCount)
            {
                Name = name;
                Areas = areas;
                PassedCount = passedCount;
            }

            /// <summary>Gets the queue name shown by the inspector.</summary>
            public string Name { get; }
            /// <summary>Gets every area of the queue as it was built, in judgment order.</summary>
            public AreaInfo[] Areas { get; }
            /// <summary>Gets how many areas from the start of the queue the player has passed.</summary>
            public int PassedCount { get; }
        }

        /// <summary>One judge area of a queue.</summary>
        internal readonly struct AreaInfo
        {
            /// <summary>Gets the sensors that satisfy the area.</summary>
            public SensorArea[] Sensors { get; init; }
            /// <summary>
            /// Gets the state of each sensor in <see cref="Sensors"/> in the current frame, or an empty array when no note
            /// manager reads the sensors.
            /// </summary>
            public SwitchStatus[] SensorStatuses { get; init; }
            /// <summary>Gets whether the sensors are combined with OR or with AND.</summary>
            public AreaPolicy Policy { get; init; }
            /// <summary>Gets whether the player may skip the area.</summary>
            public bool IsSkippable { get; init; }
            /// <summary>Gets whether a sensor of the area is pressed.</summary>
            public bool IsOn { get; init; }
            /// <summary>Gets whether the area is finished.</summary>
            public bool IsFinished { get; init; }
            /// <summary>Gets how many arrows are hidden once the area is pressed.</summary>
            public int ArrowProgressWhenOn { get; init; }
            /// <summary>Gets how many arrows are hidden once the area is finished.</summary>
            public int ArrowProgressWhenFinished { get; init; }
        }

        /// <summary>One touch DJAuto simulated on the touch panel.</summary>
        internal readonly struct DJAutoTouchInfo
        {
            /// <summary>Gets the world position of the touch centre.</summary>
            public Vector3 Position { get; init; }
            /// <summary>Gets the touch radius DJAuto asked for.</summary>
            public float Radius { get; init; }
            /// <summary>
            /// Gets the radius of the outermost ring of points the touch panel sampling reads, which rounds
            /// <see cref="Radius"/> up to whole sampling segments.
            /// </summary>
            public float SampledRadius { get; init; }
            /// <summary>Gets the sensors the touch pressed.</summary>
            public SensorArea[] Sensors { get; init; }
        }
    }
}

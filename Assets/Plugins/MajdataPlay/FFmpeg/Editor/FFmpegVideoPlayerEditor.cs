#nullable enable
using UnityEditor;
using UnityEngine;

namespace MajdataPlay.FFmpeg.Editor
{
    /// <summary>Provides the FFmpeg player's configuration and live playback Inspector.</summary>
    [CustomEditor(typeof(FFmpegVideoPlayer))]
    internal sealed class FFmpegVideoPlayerEditor : UnityEditor.Editor
    {
        /// <summary>Draws serialized player settings and live playback controls and diagnostics.</summary>
        public override void OnInspectorGUI()
        {
            var player = (FFmpegVideoPlayer)target;
            var previousRate = player.Rate;
            var previousSource = player.Url;
            DrawDefaultInspector();
            // Keep the serialized bool for existing scenes while presenting an explicit type selector.
            serializedObject.Update();
            var preference = serializedObject.FindProperty("_preferHardwareDecoding");
            EditorGUI.BeginChangeCheck();
            var decoderType = (VideoDecoderType)EditorGUILayout.EnumPopup("Preferred Decoder Type", preference.boolValue ? VideoDecoderType.Hardware : VideoDecoderType.Software);
            if (EditorGUI.EndChangeCheck())
            {
                preference.boolValue = decoderType == VideoDecoderType.Hardware;
                serializedObject.ApplyModifiedProperties();
            }

            if (!Application.isPlaying)
            {
                return;
            }

            // Serialized fields bypass the Url setter; release the previous input
            // so the next Play / Preload opens the path now shown in the Inspector.
            if (player.Url != previousSource)
            {
                player.Close();
            }

            if (player.Rate != previousRate)
            {
                player.SetRate(player.Rate);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("State", player.State.ToString());
            EditorGUILayout.LabelField("Encoding", player.CodecName);
            EditorGUILayout.LabelField("Decoder", player.DecoderName + " (" + player.DecoderType + ")");
            EditorGUILayout.LabelField("Decode device", player.DecoderDevice);
            EditorGUILayout.LabelField("Texture transfer", player.TransferMode);
            EditorGUILayout.LabelField("Video", $"{player.Width} x {player.Height}, {player.FrameRate:F3} fps");
            EditorGUILayout.LabelField("Bitrate (current)", FormatBitRate(player.CurrentBitRate));
            EditorGUILayout.LabelField("Bitrate (average)", FormatBitRate(player.BitRate));
            EditorGUILayout.LabelField("Buffered frames", player.BufferedFrames.ToString());
            if (!string.IsNullOrEmpty(player.HardwareFallbackReason))
            {
                EditorGUILayout.HelpBox(player.HardwareFallbackReason, MessageType.Info);
            }

            if (!string.IsNullOrEmpty(player.LastError))
            {
                EditorGUILayout.HelpBox(player.LastError, MessageType.Error);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preload"))
                {
                    player.Prepare();
                }

                if (GUILayout.Button("Play"))
                {
                    player.Play();
                }

                if (GUILayout.Button("Pause"))
                {
                    player.Pause();
                }

                if (GUILayout.Button("Stop"))
                {
                    player.Stop();
                }

                if (GUILayout.Button("Close"))
                {
                    player.Close();
                }
            }

            using (new EditorGUI.DisabledScope(!player.IsSeekable || player.LengthSeconds <= 0))
            {
                EditorGUI.BeginChangeCheck();
                var seconds = EditorGUILayout.Slider("Time (seconds)", (float)player.TimeSeconds, 0, (float)player.LengthSeconds);
                if (EditorGUI.EndChangeCheck())
                {
                    player.TimeSeconds = seconds;
                }
            }

            EditorGUI.BeginChangeCheck();
            var rate = EditorGUILayout.Slider("Playback rate", player.Rate, 0.0625f, 16);
            if (EditorGUI.EndChangeCheck())
            {
                player.Rate = rate;
            }
        }

        /// <summary>Requests live Inspector updates while the application is playing.</summary>
        /// <returns>True during Play Mode; otherwise false.</returns>
        public override bool RequiresConstantRepaint() => Application.isPlaying;
        /// <summary>Formats a bit-rate estimate with readable units or an unknown marker.</summary>
        /// <param name="bitRate">The compressed video bit rate in bits per second, or zero when unknown.</param>
        /// <returns>The bit rate formatted in bps, kbps, or Mbps, or Unknown for nonpositive values.</returns>
        private static string FormatBitRate(long bitRate)
        {
            if (bitRate <= 0)
            {
                return "Unknown";
            }

            if (bitRate >= 1_000_000)
            {
                return $"{bitRate / 1_000_000d:F2} Mbps";
            }

            if (bitRate >= 1_000)
            {
                return $"{bitRate / 1_000d:F2} kbps";
            }

            return $"{bitRate} bps";
        }
    }
}

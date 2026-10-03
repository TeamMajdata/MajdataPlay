using UnityEditor;
using UnityEngine;

namespace MajdataPlay.Video.Editor
{
    [CustomEditor(typeof(FFmpegVideoPlayer))]
    sealed class FFmpegVideoPlayerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var player = (FFmpegVideoPlayer)target;
            var previousRate = player.Rate;
            var previousSource = player.Url;
            DrawDefaultInspector();
            if (!Application.isPlaying) return;
            // Serialized fields bypass the Url setter; release the previous input
            // so the next Play / Preload opens the path now shown in the Inspector.
            if (player.Url != previousSource) player.Close();
            if (player.Rate != previousRate) player.SetRate(player.Rate);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("State", player.State.ToString());
            EditorGUILayout.LabelField("Decoder", player.CodecName);
            EditorGUILayout.LabelField("Texture transfer", player.TransferMode);
            EditorGUILayout.LabelField("Video", $"{player.Width} x {player.Height}, {player.FrameRate:F3} fps");
            EditorGUILayout.LabelField("Buffered frames", player.BufferedFrames.ToString());
            if (!string.IsNullOrEmpty(player.HardwareFallbackReason)) EditorGUILayout.HelpBox(player.HardwareFallbackReason, MessageType.Info);
            if (!string.IsNullOrEmpty(player.LastError)) EditorGUILayout.HelpBox(player.LastError, MessageType.Error);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preload")) player.Prepare();
                if (GUILayout.Button("Play")) player.Play();
                if (GUILayout.Button("Pause")) player.Pause();
                if (GUILayout.Button("Stop")) player.Stop();
                if (GUILayout.Button("Close")) player.Close();
            }
            using (new EditorGUI.DisabledScope(!player.IsSeekable || player.LengthSeconds <= 0))
            {
                EditorGUI.BeginChangeCheck();
                var seconds = EditorGUILayout.Slider("Time (seconds)", (float)player.time, 0, (float)player.LengthSeconds);
                if (EditorGUI.EndChangeCheck()) player.time = seconds;
            }
            EditorGUI.BeginChangeCheck();
            var rate = EditorGUILayout.Slider("Playback rate", player.Rate, 0.0625f, 16);
            if (EditorGUI.EndChangeCheck()) player.Rate = rate;
        }
        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}

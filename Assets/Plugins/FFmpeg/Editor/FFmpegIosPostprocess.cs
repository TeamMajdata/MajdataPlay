#if UNITY_EDITOR && UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace MajdataPlay.Video.Editor
{
    /// <summary>System frameworks used by the pinned VideoToolbox/AudioToolbox static build.</summary>
    internal static class FFmpegIosPostprocess
    {
        [PostProcessBuild(100)]
        private static void AddFrameworks(BuildTarget target, string outputPath)
        {
            if (target != BuildTarget.iOS) return;
            string path = PBXProject.GetPBXProjectPath(outputPath);
            var project = new PBXProject();
            project.ReadFromFile(path);
            string framework = project.GetUnityFrameworkTargetGuid();
            foreach (string name in new[] { "VideoToolbox.framework", "AudioToolbox.framework", "CoreMedia.framework",
                         "CoreVideo.framework", "CoreFoundation.framework", "Foundation.framework", "Metal.framework", "Security.framework" })
                project.AddFrameworkToProject(framework, name, false);
            project.WriteToFile(path);
        }
    }
}
#endif

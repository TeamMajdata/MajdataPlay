#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Unity
{
    /// <summary>Provides typed access to the Android activity owned by the Unity player.</summary>
    [JavaClass("com.unity3d.player.UnityPlayer", ApiLevel = 36, IncludeInheritedMembers = false,
        ClassPath = new[] { "{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar" })]
    public partial class UnityPlayer
    {
    }
}

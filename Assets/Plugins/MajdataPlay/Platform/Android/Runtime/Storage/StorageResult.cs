#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Storage
{
    /// <summary>Owns a Java SAF result envelope with typed metadata, cursor, stream, and signed-byte fields.</summary>
    [JavaClass("net.majdata.majdataplay.StorageAccess$Result", ApiLevel = 36, IncludeInheritedMembers = false,
        Sources = new[] { "Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java" },
        ClassPath = new[] { "{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar" })]
    public partial class StorageResult
    {
    }
}

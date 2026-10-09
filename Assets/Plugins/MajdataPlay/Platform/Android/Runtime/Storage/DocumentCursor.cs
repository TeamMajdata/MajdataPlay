#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Storage
{
    /// <summary>Owns a Java cursor reference whose provider resource must be closed before disposal.</summary>
    [JavaClass("net.majdata.majdataplay.StorageAccess$CursorHandle", ApiLevel = 36, IncludeInheritedMembers = false,
        Sources = new[] { "Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java" },
        ClassPath = new[] { "{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar" })]
    public partial class DocumentCursor
    {
    }
}

#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Storage
{
    /// <summary>Wraps authoritative SAF metadata, including the grant-preserving URI and identity-only resource ID.</summary>
    [JavaClass("net.majdata.majdataplay.StorageAccess$Entry", ApiLevel = 36, IncludeInheritedMembers = false,
        Sources = new[] { "Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java" },
        ClassPath = new[] { "{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar" })]
    public partial class DocumentEntry
    {
    }
}

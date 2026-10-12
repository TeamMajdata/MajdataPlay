#nullable enable

namespace MajdataPlay.Platform.Android.Runtime.Validation
{
    /// <summary>Exercises generated JNI conversions using Java code packaged only in the device validation app.</summary>
    [JavaClass("net.majdata.validation.DeviceJniProbe", ApiLevel = 36, IncludeInheritedMembers = false,
        Sources = new[]
        {
            "Assets/Plugins/Android/src/java/net/majdata/validation/DeviceJniProbe.java",
            "Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java"
        },
        ClassPath = new[] { "{UnityData}/PlaybackEngines/AndroidPlayer/Variations/mono/Release/Classes/classes.jar" })]
    public partial class DeviceJniProbe
    {
    }
}

using UnityEngine.Scripting;

namespace MajdataPlay.Settings
{
    [Preserve]
    public enum RecordModeOption
    {
        Disable,
        [DisableValues(Platforms = SettingPlatform.Mobile)]
        OBSTrigger,
        FFmpeg
    }
}

using UnityEngine;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class EngineNumberSettingEnumerator : DefaultNumberEnumerator, IOptionEnumerator
{
    int _lastValue = 0;

    public override void Refresh()
    {
        base.Refresh();
        if(_lastValue == CurrentValue)
        {
            return;
        }
        switch (Name)
        {
            case "FPSLimit":
                Application.targetFrameRate = (int)CurrentValue;
#if UNITY_ANDROID && !UNITY_EDITOR
                MajdataPlay.Platform.Android.AndroidRuntime.RequestXiaomiRefreshRate(Application.targetFrameRate);
#endif
                break;
        }
        _lastValue = (int)CurrentValue;
    }
    protected override void InitInternal()
    {
        base.InitInternal();
        _lastValue = (int)CurrentValue;
    }
}

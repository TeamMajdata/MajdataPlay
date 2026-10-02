using UnityEngine;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class EngineBooleanSettingEnumerator : DefaultBooleanEnumerator, IOptionEnumerator
{
    bool _lastValue = false;

    public override void Refresh()
    {
        base.Refresh();
        var currentValue = (bool)Value;
        if (_lastValue == currentValue)
        {
            return;
        }
        switch (Name)
        {
            case "VSync":
                QualitySettings.vSyncCount = currentValue ? 1 : 0;
                break;
        }
        _lastValue = currentValue;
    }
    protected override void InitInternal()
    {
        base.InitInternal();
        _lastValue = (bool)Value;
    }
}

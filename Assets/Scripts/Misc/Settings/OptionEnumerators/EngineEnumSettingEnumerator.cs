using System;
using UnityEngine;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class EngineEnumSettingEnumerator: DefaultEnumEnumerator, IOptionEnumerator
{
    object _lastValue;
    public override void Refresh()
    {
        if (Current == _lastValue)
        {
            return;
        }
        switch (PropertyInfo.Name)
        {
            case "RenderQuality":
                QualitySettings.SetQualityLevel(Convert.ToInt32(Current), true);
                GameManager.ApplyRenderScale(((DisplayOptions)Target).RenderScale);
                break;
        }
        _lastValue = Current;
    }
    protected override void InitInternal()
    {
        base.InitInternal();
        _lastValue = Current;
    }
}

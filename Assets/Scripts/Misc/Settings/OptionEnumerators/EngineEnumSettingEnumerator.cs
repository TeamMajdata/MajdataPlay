using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
                var displayOptions = (DisplayOptions)Target;
                QualitySettings.SetQualityLevel(Convert.ToInt32(Current), true);
                displayOptions.RenderScale = (int)((GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset).renderScale * 100);
                displayOptions.VSync = QualitySettings.vSyncCount is 1 ? true : false;
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

using UnityEngine;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class EngineNumberSettingEnumerator : DefaultNumberEnumerator, IOptionEnumerator
{
    int _lastValue = 0;

    public override void Refresh()
    {
        if(_lastValue == CurrentValue)
        {
            return;
        }
        switch (Name)
        {
            case "FPSLimit":
                Application.targetFrameRate = (int)CurrentValue;
                break;
            case "RenderScale":
                GameManager.ApplyRenderScale((int)CurrentValue);
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

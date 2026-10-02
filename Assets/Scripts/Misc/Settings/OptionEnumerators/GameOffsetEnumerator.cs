using System;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class GameOffsetEnumerator : DefaultNumberEnumerator, IOptionEnumerator
{
    OffsetUnitOption _lastOffsetUnit;
    public override void Refresh()
    {
        base.Refresh();
        CheckOffsetUnit();
    }
    protected override void InitInternal()
    {
        base.InitInternal();
        _lastOffsetUnit = MajEnv.Settings.Debug.OffsetUnit;
        UpdateOptionStep();
    }
    void CheckOffsetUnit()
    {
        var currentOffsetUnit = MajEnv.Settings.Debug.OffsetUnit;
        if (currentOffsetUnit == _lastOffsetUnit)
        {
            return;
        }
        CurrentValue = Convert.ToDecimal(Value);
        UpdateOptionStep();
        OptionValues[0] = CurrentValue;
        UpdateValueText();
        _lastOffsetUnit = currentOffsetUnit;
    }
    void UpdateOptionStep()
    {
        MaxValue = null;
        MinValue = Name == "DisplayOffset" ? 0 : null;
        Step = MajEnv.Settings.Debug.OffsetUnit == OffsetUnitOption.Second ? 0.001m : 0.1m;
    }
}

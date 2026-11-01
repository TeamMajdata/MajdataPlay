namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class GameOffsetEnumerator : DefaultNumberEnumerator, IOptionEnumerator
{
    OffsetUnitOption _lastOffsetUnit;

    /// <summary>
    /// Follows a change of the offset unit, then reads the stored offset again and snaps it onto the grid of the unit.
    /// </summary>
    public override void Refresh()
    {
        CheckOffsetUnit();
        base.Refresh();
    }

    /// <summary>
    /// Uses the grid of the current offset unit instead of the step and range attributes.
    /// </summary>
    protected override void InitStepAndRange()
    {
        _lastOffsetUnit = MajEnv.UserSettings.Judge.OffsetUnit;
        UpdateOptionStep();
    }

    /// <summary>
    /// Switches the step to the current offset unit when the unit changed since the last refresh.
    /// </summary>
    void CheckOffsetUnit()
    {
        var currentOffsetUnit = MajEnv.UserSettings.Judge.OffsetUnit;
        if (currentOffsetUnit == _lastOffsetUnit)
        {
            return;
        }
        UpdateOptionStep();
        _lastOffsetUnit = currentOffsetUnit;
    }
    void UpdateOptionStep()
    {
        MaxValue = null;
        MinValue = Name == "DisplayOffset" ? 0 : null;
        Step = MajEnv.UserSettings.Judge.OffsetUnit == OffsetUnitOption.Second ? 0.001m : 0.1m;
    }
}

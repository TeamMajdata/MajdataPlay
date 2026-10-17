using UnityEngine.Scripting;

namespace MajdataPlay.Settings.OptionEnumerators;

[Preserve]
public sealed class RenderingBooleanSettingEnumerator : DefaultBooleanEnumerator, IOptionEnumerator
{
    [Preserve]
    public RenderingBooleanSettingEnumerator() { }

    public override bool MoveNext() => Move(true);
    public override bool MovePrevious() => Move(false);

    bool Move(bool moveNext)
    {
        var previousValue = Value;
        var moved = moveNext ? base.MoveNext() : base.MovePrevious();
        if (!moved || Equals(previousValue, Value))
        {
            return false;
        }

        // Compatibility is a temporary override; the settings model retains
        // the selected preset / custom values while this switch is enabled.
        GameManager.ApplyRenderingSettings();
        return true;
    }
}

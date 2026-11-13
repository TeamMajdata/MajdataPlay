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

        // Apply boolean rendering choices once after a real edit. Compatibility
        // remains a temporary override without replacing the stored custom values.
        GameManager.ApplyRenderingSettings();
        return true;
    }
}

using UnityEngine.Scripting;

namespace MajdataPlay.Settings.OptionEnumerators;

// These enumerators are created through OptionEnumeratorAttribute on IL2CPP, too.
[Preserve]
public sealed class RenderingEnumSettingEnumerator : DefaultEnumEnumerator, IOptionEnumerator
{
    [Preserve]
    public RenderingEnumSettingEnumerator() { }

    public override bool MoveNext() => Move(true);
    public override bool MovePrevious() => Move(false);

    bool Move(bool moveNext)
    {
        if (IsReadOnly)
        {
            return false;
        }
        var previousValue = Value;
        var nextIndex = ValueIndex;
        do
        {
            nextIndex = (nextIndex + (moveNext ? 1 : OptionValues.Length - 1)) % OptionValues.Length;
        }
        while (Target is RenderingOptions && Name == nameof(RenderingOptions.RenderQuality) &&
            Equals(OptionValues[nextIndex], RenderingQualityOption.Custom));
        // Keep all five labels for localization/font warmup, but only the secret gesture
        // enters Custom. Leaving Custom by choosing a preset hides the detailed controls again.
        ValueIndex = nextIndex;
        Value = OptionValues[ValueIndex];
        // The model may reject Direct output at a scaled resolution. Keep Current / labels
        // synchronized with the accepted value, including an input that becomes a no-op.
        base.Refresh();
        if (Equals(previousValue, Value))
        {
            return false;
        }

        // Init / Refresh only synchronize labels. Applying here prevents menu opening,
        // pooled view binding or localization changes from resetting a chosen preset.
        GameManager.ApplyRenderingSettings();
        return true;
    }
}

using MajdataPlay.Diagnostics;
using System;

#nullable enable
namespace MajdataPlay.Settings.OptionEnumerators;
public class DefaultNumberEnumerator : OptionEnumeratorBase, IOptionEnumerator
{
    protected decimal Step = 0;
    protected decimal CurrentValue = 0;

    protected decimal? MaxValue = null;
    protected decimal? MinValue = null;

    public override void Refresh()
    {
        CurrentValue = Convert.ToDecimal(Value);
        OptionValues[0] = CurrentValue;
        UpdateValueText();
    }

    /// <summary>
    /// Steps the value up to the next enabled number.
    /// </summary>
    /// <remarks>
    /// Numbers disabled by <see cref="DisableValuesAttribute"/> are skipped; the value does not move when only disabled
    /// numbers remain before the maximum.
    /// </remarks>
    /// <returns>True when the value was changed; false when the option is read-only or no enabled number remains.</returns>
    public override bool MoveNext()
    {
        if (IsReadOnly)
        {
            return false;
        }
        if (!TryFindEnabledValue(CurrentValue, true, out var nextValue))
        {
            return false;
        }
        SetCurrentValue(nextValue);
        return true;
    }
    /// <summary>
    /// Steps the value down to the previous enabled number.
    /// </summary>
    /// <remarks>
    /// Numbers disabled by <see cref="DisableValuesAttribute"/> are skipped; the value does not move when only disabled
    /// numbers remain before the minimum.
    /// </remarks>
    /// <returns>True when the value was changed; false when the option is read-only or no enabled number remains.</returns>
    public override bool MovePrevious()
    {
        if (IsReadOnly)
        {
            return false;
        }
        if (!TryFindEnabledValue(CurrentValue, false, out var nextValue))
        {
            return false;
        }
        SetCurrentValue(nextValue);
        return true;
    }

    protected override void InitInternal()
    {
        var isNum = IsIntType || IsFloatType;
        if (!isNum)
        {
            MajDebug.LogError($"[SettingUI]Invalid number type: {Type}");
            throw new InvalidOperationException("Type provided must be an Number");
        }
        var rangeAttribute = GetCustomAttribute<RangeAttribute>();
        var stepAttribute = GetCustomAttribute<StepAttribute>();

        if (rangeAttribute is not null)
        {
            MinValue = rangeAttribute.Min;
            MaxValue = rangeAttribute.Max;
            if (!rangeAttribute.HasMin)
            {
                MinValue = null;
            }
            if (!rangeAttribute.HasMax)
            {
                MaxValue = null;
            }
        }
        if (stepAttribute is not null)
        {
            Step = stepAttribute.Value;
        }

        if (stepAttribute is null && rangeAttribute is null)
        {
            MaxValue = null;
            MinValue = null;
            if (IsIntType)
            {
                Step = 1m;
            }
            else
            {
                Step = 0.001m;
            }
        }

        OptionValues = new object[1];
        ValueIndex = 0;

        var currentValue = Value;
        CurrentValue = Convert.ToDecimal(currentValue);
        OptionValues[0] = CurrentValue;
        InitValueTexts();
        ReplaceDisabledValue();
        UpdateValueText();
    }

    /// <summary>
    /// Replaces a stored value that <see cref="DisableValuesAttribute"/> disables with the nearest enabled number, looking
    /// up first and then down.
    /// </summary>
    /// <remarks>
    /// Read-only options and options without an enabled number in reach keep the stored value.
    /// </remarks>
    private void ReplaceDisabledValue()
    {
        if (IsReadOnly || !DisabledValues.IsDisabled(CurrentValue))
        {
            return;
        }
        if (!TryFindEnabledValue(CurrentValue, true, out var enabledValue) &&
            !TryFindEnabledValue(CurrentValue, false, out enabledValue))
        {
            MajDebug.LogWarning(nameof(DefaultNumberEnumerator), $"{Name} holds the disabled value {CurrentValue} and no enabled value is in range; keeping it");
            return;
        }
        MajDebug.LogWarning(nameof(DefaultNumberEnumerator), $"{Name} holds the disabled value {CurrentValue}; selecting {enabledValue}");
        SetCurrentValue(enabledValue);
    }

    /// <summary>
    /// Finds the first number reached by stepping from a value that is not disabled by <see cref="DisableValuesAttribute"/>.
    /// </summary>
    /// <param name="value">The value to step from.</param>
    /// <param name="moveNext">True to step up towards the maximum; false to step down towards the minimum.</param>
    /// <param name="enabledValue">Receives the enabled number, clamped to the range.</param>
    /// <returns>True when an enabled number was found; false when the range bound is reached first.</returns>
    private bool TryFindEnabledValue(decimal value, bool moveNext, out decimal enabledValue)
    {
        var candidate = value;
        while (true)
        {
            var previous = candidate;
            if (!TryStep(previous, moveNext, out candidate))
            {
                enabledValue = value;
                return false;
            }
            if (!DisabledValues.IsDisabled(candidate))
            {
                enabledValue = candidate;
                return true;
            }
            // A zero step or a clamped bound cannot reach another number.
            if (candidate == previous)
            {
                enabledValue = value;
                return false;
            }
        }
    }

    /// <summary>
    /// Steps a value once, clamped to the range.
    /// </summary>
    /// <param name="value">The value to step from.</param>
    /// <param name="moveNext">True to step up towards the maximum; false to step down towards the minimum.</param>
    /// <param name="nextValue">Receives the stepped value.</param>
    /// <returns>False when <paramref name="value"/> already lies on or beyond the bound in that direction; otherwise true.</returns>
    private bool TryStep(decimal value, bool moveNext, out decimal nextValue)
    {
        if (moveNext)
        {
            nextValue = value + Step;
            if (MaxValue is decimal maxValue)
            {
                if (value >= maxValue)
                {
                    return false;
                }
                nextValue = Math.Min(nextValue, maxValue);
            }
            return true;
        }
        nextValue = value - Step;
        if (MinValue is decimal minValue)
        {
            if (value <= minValue)
            {
                return false;
            }
            nextValue = Math.Max(nextValue, minValue);
        }
        return true;
    }

    /// <summary>
    /// Stores a new value in the option and refreshes its text.
    /// </summary>
    /// <param name="value">The new value.</param>
    private void SetCurrentValue(decimal value)
    {
        CurrentValue = value;
        var valueToSet = (object)value;
        OptionValues[0] = valueToSet;
        Value = Convert.ChangeType(valueToSet, Type);
        UpdateValueText();
    }

    protected void UpdateValueText()
    {
        var currentValue = Value;
        ValueTexts[0] = currentValue?.ToString() ?? (IsOptional ? "UNSET" : "NULL");
        LocalizedValueTexts[0] = LocalizeValueText(currentValue, ValueTexts[0]);
    }
}

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

    /// <summary>
    /// Reads the stored value again and snaps it onto the step grid and into the range.
    /// </summary>
    /// <remarks>
    /// A value written elsewhere, for example by another option or a preset, is snapped like the value read on
    /// initialization (see <see cref="SnapStoredValue"/>).
    /// </remarks>
    public override void Refresh()
    {
        CurrentValue = Convert.ToDecimal(Value);
        OptionValues[0] = CurrentValue;
        SnapStoredValue();
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

    /// <summary>
    /// Sets up the step grid and the range, reads the stored value and snaps it onto the grid.
    /// </summary>
    /// <exception cref="InvalidOperationException">The option is not a numeric member.</exception>
    protected override void InitInternal()
    {
        var isNum = IsIntType || IsFloatType;
        if (!isNum)
        {
            MajDebug.LogError($"[SettingUI]Invalid number type: {Type}");
            throw new InvalidOperationException("Type provided must be an Number");
        }
        InitStepAndRange();

        OptionValues = new object[1];
        ValueIndex = 0;

        var currentValue = Value;
        CurrentValue = Convert.ToDecimal(currentValue);
        OptionValues[0] = CurrentValue;
        InitValueTexts();
        SnapStoredValue();
        ReplaceDisabledValue();
        UpdateValueText();
    }

    /// <summary>
    /// Sets <see cref="Step"/>, <see cref="MinValue"/> and <see cref="MaxValue"/> from the <see cref="StepAttribute"/>
    /// and <see cref="RangeAttribute"/> of the option.
    /// </summary>
    /// <remarks>
    /// Called during initialization before the stored value is read and snapped. Enumerators with a grid of their own
    /// override it instead of changing the grid after initialization.
    /// </remarks>
    protected virtual void InitStepAndRange()
    {
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
    }

    /// <summary>
    /// Snaps a value onto the step grid of a numeric option and clamps it into the range.
    /// </summary>
    /// <remarks>
    /// The grid starts at the lower bound, or at zero without one, and advances by <paramref name="step"/>; a value
    /// halfway between two grid values is rounded away from zero. An upper bound that is not on the grid is a valid
    /// value too, since stepping up clamps to it, so a value nearer to it than to any grid value snaps to it. Without a
    /// positive step the value is only clamped.
    /// </remarks>
    /// <param name="value">The value to snap.</param>
    /// <param name="step">The distance between two grid values.</param>
    /// <param name="minValue">The inclusive lower bound, or null for none.</param>
    /// <param name="maxValue">The inclusive upper bound, or null for none.</param>
    /// <returns>The grid value nearest to <paramref name="value"/>, clamped into the range.</returns>
    internal static decimal Snap(decimal value, decimal step, decimal? minValue, decimal? maxValue)
    {
        var snappedValue = value;
        if (step > 0m)
        {
            var origin = minValue ?? 0m;
            var stepCount = decimal.Round((value - origin) / step, MidpointRounding.AwayFromZero);
            snappedValue = origin + stepCount * step;
        }
        if (minValue is decimal min && snappedValue < min)
        {
            return min;
        }
        if (maxValue is decimal max && (snappedValue > max || max - value < Math.Abs(snappedValue - value)))
        {
            return max;
        }
        return snappedValue;
    }

    /// <summary>
    /// Snaps the stored value onto the step grid and into the range, writing the snapped value back to the setting.
    /// </summary>
    /// <remarks>
    /// Read-only options keep the stored value. Nothing is written when the value is already on the grid.
    /// </remarks>
    private void SnapStoredValue()
    {
        if (IsReadOnly)
        {
            return;
        }
        var snappedValue = Snap(CurrentValue, Step, MinValue, MaxValue);
        if (snappedValue == CurrentValue)
        {
            return;
        }
        MajDebug.LogWarning(nameof(DefaultNumberEnumerator), $"{Name} holds {CurrentValue}, which is off its step grid or out of its range; snapping it to {snappedValue}");
        SetCurrentValue(snappedValue);
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
    /// Steps a value once to the adjacent grid value, clamped to the range.
    /// </summary>
    /// <param name="value">The value to step from.</param>
    /// <param name="moveNext">True to step up towards the maximum; false to step down towards the minimum.</param>
    /// <param name="nextValue">Receives the stepped value.</param>
    /// <returns>False when <paramref name="value"/> already lies on or beyond the bound in that direction; otherwise true.</returns>
    private bool TryStep(decimal value, bool moveNext, out decimal nextValue)
    {
        nextValue = GetAdjacentGridValue(value, moveNext);
        if (moveNext)
        {
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
    /// Gets the nearest grid value above or below a value, before clamping to the range.
    /// </summary>
    /// <remarks>
    /// For a value on the grid this is the value plus or minus <see cref="Step"/>; from an upper bound that is not on
    /// the grid, stepping down reaches the grid value below it. Without a positive step the value moves by
    /// <see cref="Step"/> as is.
    /// </remarks>
    /// <param name="value">The value to step from.</param>
    /// <param name="moveNext">True for the grid value above; false for the grid value below.</param>
    /// <returns>The adjacent grid value.</returns>
    private decimal GetAdjacentGridValue(decimal value, bool moveNext)
    {
        if (Step <= 0m)
        {
            return moveNext ? value + Step : value - Step;
        }
        var origin = MinValue ?? 0m;
        var position = (value - origin) / Step;
        var stepCount = moveNext ? decimal.Floor(position) + 1m : decimal.Ceiling(position) - 1m;
        return origin + stepCount * Step;
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

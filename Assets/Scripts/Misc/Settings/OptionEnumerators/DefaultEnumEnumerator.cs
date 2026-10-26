using MajdataPlay.Collections;
using MajdataPlay.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
#nullable enable
namespace MajdataPlay.Settings.OptionEnumerators;
public class DefaultEnumEnumerator : OptionEnumeratorBase, IOptionEnumerator
{
    /// <summary>
    /// Lists the constants of the enum that are enabled on the current platform and selects the current value.
    /// </summary>
    /// <remarks>
    /// Constants disabled by <see cref="DisableValuesAttribute"/> are left out. A value that is not a listed constant,
    /// such as an unknown number read from settings.json or a constant disabled on this platform, selects the first
    /// listed constant, which is also written back unless the option is read-only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The member is not an enum.</exception>
    protected override void InitInternal()
    {
        if (!Type.IsEnum)
        {
            throw new InvalidOperationException("Type provided must be an Enum");
        }
        // Enumerators may mutate their arrays; keep the global constants immutable.
        OptionValues = (object[])DisabledValues.Filter(SettingReflectionCache.GetEnumValues(Type), Name).Clone();
        InitValueTexts();
        var value = Value;
        ValueIndex = Array.IndexOf(OptionValues, value);
        if (ValueIndex == -1)
        {
            ValueIndex = 0;
            MajDebug.LogWarning(nameof(DefaultEnumEnumerator), $"{Name} holds the undefined or disabled value {value}; selecting {OptionValues[0]}");
            if (!IsReadOnly)
            {
                Value = OptionValues[0];
            }
        }
    }
}

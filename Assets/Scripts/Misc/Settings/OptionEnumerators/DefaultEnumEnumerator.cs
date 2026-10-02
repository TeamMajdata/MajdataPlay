using MajdataPlay.Collections;
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
    protected override void InitInternal()
    {
        if (!Type.IsEnum)
        {
            throw new InvalidOperationException("Type provided must be an Enum");
        }
        // Enumerators may mutate their arrays; keep the global constants immutable.
        OptionValues = (object[])SettingReflectionCache.GetEnumValues(Type).Clone();
        InitValueTexts();
        var value = Value;
        ValueIndex = Array.IndexOf(OptionValues, value);
    }
}

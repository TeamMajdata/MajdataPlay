using MajdataPlay.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MajdataPlay.Settings.OptionEnumerators;
public sealed class NoteMaskEnumerator : OptionEnumeratorBase, IOptionEnumerator
{
    protected override void InitInternal()
    {
        OptionValues = new string[3]
        {
            "Disable",
            "Inner",
            "Outer"
        };
        InitValueTexts();
        var current = Value;
        ValueIndex = OptionValues.FindIndex(x => Equals(x, current));
        if(ValueIndex == -1)
        {
            ValueIndex = 0;
            Value = OptionValues[ValueIndex];
        }
    }
}

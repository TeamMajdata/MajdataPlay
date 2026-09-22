using System;
using MajdataPlay.Scenes.Game.Parsing;

namespace SimaiRadar.MajSimaiAdapter;

/// <summary>Uses the same slidecode path and arrow generator as gameplay.</summary>
internal sealed class MajdataPlayExtendedSlideBarCountProvider : IExtendedSlideBarCountProvider
{
    public int ResolveBarCount(string rawContent)
    {
        var marker = rawContent.IndexOf('K');
        if (marker < 1 || marker + 1 >= rawContent.Length)
            throw new ArgumentException($"Incomplete extended Slide endpoint: {rawContent}");
        var slideCode = rawContent.Substring(0, marker + 2);
        var path = SlideCodeParser.Parse(slideCode);
        return SlideDataBuilder.BuildArrowData(path).Length - 2;
    }
}

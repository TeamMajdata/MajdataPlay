using MajdataPlay.Scenes.Game.Parsing;
using MajRadar.MajSimaiAdapter;

namespace MajdataPlay.Utils.ChartRadar;

/// <summary>Resolves extended Slide geometry with the same path used by gameplay.</summary>
internal sealed class PlayExtendedSlideBarCountProvider : IExtendedSlideBarCountProvider
{
    public int ResolveBarCount(string slideCode)
    {
        var path = SlideCodeParser.Parse(slideCode);
        return SlideDataBuilder.BuildArrowData(path).Length - 2;
    }
}

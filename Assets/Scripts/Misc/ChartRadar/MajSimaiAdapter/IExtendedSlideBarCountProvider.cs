namespace SimaiRadar.MajSimaiAdapter;

/// <summary>
/// Supplies Play's generated arrow count for a MajSimai extended K Slide.
/// Standard Slides continue to use the fixed prefab reference table.
/// </summary>
public interface IExtendedSlideBarCountProvider
{
    int ResolveBarCount(string rawContent);
}

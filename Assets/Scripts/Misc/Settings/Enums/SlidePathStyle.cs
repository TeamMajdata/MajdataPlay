using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Settings
{
    /// <summary>
    /// Selects how the arrows of a simai connection slide, such as <c>1-4q7</c> or <c>1^2^3</c>, are generated. A slide
    /// with a single segment looks the same in every style.
    /// </summary>
    [Preserve]
    public enum SlidePathStyle
    {
        /// <summary>
        /// Every segment gets the arrows of the same shape as a standalone slide, so it overlaps that slide exactly. No
        /// arrow is placed where two segments meet, which leaves a gap where two arcs of the judgment circle meet, as in
        /// the connection slides built from slide prefabs.
        /// </summary>
        Classic,

        /// <summary>
        /// Like <see cref="Classic"/>, but an arrow fills the gap where two arcs of the judgment circle meet.
        /// </summary>
        Medium,

        /// <summary>
        /// The whole chain is one path whose arrows continue across the segment boundaries, so a segment does not
        /// always overlap the standalone slide of the same shape.
        /// </summary>
        Perfect
    }
}

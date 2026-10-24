#nullable enable
namespace MajdataPlay.Scenes.SlideTest
{
    /// <summary>Where the slide test scene takes the slides it generates from.</summary>
    internal enum SlideTestSource
    {
        /// <summary>Simai notes, built natively or translated into slide code as the scene chooses.</summary>
        Simai,
        /// <summary>A slide code, built as an extend slide.</summary>
        SlideCode,
        /// <summary>The shape of a predefined slide table, rotated to a start key and optionally mirrored.</summary>
        PredefinedTable
    }
}

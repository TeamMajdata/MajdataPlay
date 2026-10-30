#nullable enable
namespace MajdataPlay.Net
{
    /// <summary>Identifies one downloadable file of an online chart, as listed by the <c>maichart/info</c> API.</summary>
    internal enum OnlineChartResourceKind
    {
        /// <summary>The chart file <c>maidata.txt</c>.</summary>
        Maidata,
        /// <summary>The audio track <c>track.mp3</c>.</summary>
        Track,
        /// <summary>The background video <c>bg.mp4</c>.</summary>
        Video,
        /// <summary>The original cover image, <c>bg.jpg</c> or else <c>bg.png</c>.</summary>
        CoverArt,
        /// <summary>The 256×256 compressed cover image <c>bg-compress.jpg</c>.</summary>
        Thumbnail,
    }
}

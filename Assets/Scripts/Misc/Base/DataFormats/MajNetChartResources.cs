using MajdataPlay.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay
{
    /// <summary>The downloadable files of an online chart, as returned by the <c>maichart/info</c> API; a file the server does not store is null.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetChartResources
    {
        /// <summary>Gets or sets the chart file <c>maidata.txt</c>.</summary>
        [Preserve]
        public MajNetChartResource? Maidata { get; set; }
        /// <summary>Gets or sets the audio track <c>track.mp3</c>.</summary>
        [Preserve]
        public MajNetChartResource? Track { get; set; }
        /// <summary>Gets or sets the background video <c>bg.mp4</c>.</summary>
        [Preserve]
        public MajNetChartResource? Video { get; set; }
        /// <summary>Gets or sets the original cover image, <c>bg.jpg</c> or else <c>bg.png</c>.</summary>
        [Preserve]
        public MajNetChartResource? CoverArt { get; set; }
        /// <summary>Gets or sets the 256×256 compressed cover image <c>bg-compress.jpg</c>.</summary>
        [Preserve]
        public MajNetChartResource? Thumbnail { get; set; }

        /// <summary>Gets the file of one resource kind.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <returns>The file, or null when the server does not store it.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined resource kind.</exception>
        internal MajNetChartResource? Get(OnlineChartResourceKind kind)
        {
            return kind switch
            {
                OnlineChartResourceKind.Maidata => Maidata,
                OnlineChartResourceKind.Track => Track,
                OnlineChartResourceKind.Video => Video,
                OnlineChartResourceKind.CoverArt => CoverArt,
                OnlineChartResourceKind.Thumbnail => Thumbnail,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }
    }
}

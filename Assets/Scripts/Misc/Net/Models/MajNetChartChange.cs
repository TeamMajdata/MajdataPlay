using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>What happened to one chart within the range of <c>GET maichart/changes</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetChartChange
    {
        /// <summary>Gets or sets the identity of the chart.</summary>
        [Preserve]
        public string ChartId { get; set; } = string.Empty;
        /// <summary>Gets or sets what happened to the chart: a deletion outweighs an upload, and an upload outweighs modifications.</summary>
        [Preserve]
        public MajNetChartChangeKind Kind { get; set; }
        /// <summary>
        /// Gets or sets the moment of the change in UTC; for <see cref="MajNetChartChangeKind.Modified"/>, the latest change
        /// of <see cref="Resources"/> and <see cref="Metadata"/>.
        /// </summary>
        [Preserve]
        public DateTime ChangedAt { get; set; }
        /// <summary>
        /// Gets or sets, for <see cref="MajNetChartChangeKind.Modified"/>, every file that changed within the range by its
        /// name in the resources of the <c>maichart/info</c> API, such as <c>coverArt</c>, mapped to the file after its
        /// latest change, or to null when that change removed it. Null when no file changed and for the other kinds.
        /// </summary>
        [Preserve]
        public Dictionary<string, MajNetChartResource?>? Resources { get; set; }
        /// <summary>
        /// Gets or sets, for <see cref="MajNetChartChangeKind.Modified"/>, the current value of every metadata field that
        /// changed within the range. Null when no metadata changed and for the other kinds.
        /// </summary>
        [Preserve]
        public MajNetChartMetadataChange? Metadata { get; set; }

        /// <summary>Gets the change of one file of the chart.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <param name="resource">Set to the file after the change, or to null when the change removed it or the file did not change.</param>
        /// <returns>True when the file changed within the range; otherwise false.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined resource kind.</exception>
        internal bool TryGetResourceChange(OnlineChartResourceKind kind, out MajNetChartResource? resource)
        {
            resource = null;
            return Resources is not null && Resources.TryGetValue(GetResourceName(kind), out resource);
        }

        /// <summary>Gets the name of a resource kind in the resources of the <c>maichart/info</c> API.</summary>
        /// <param name="kind">The resource kind.</param>
        /// <returns>The camel case name.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined resource kind.</exception>
        private static string GetResourceName(OnlineChartResourceKind kind)
        {
            return kind switch
            {
                OnlineChartResourceKind.Maidata => "maidata",
                OnlineChartResourceKind.Track => "track",
                OnlineChartResourceKind.Video => "video",
                OnlineChartResourceKind.CoverArt => "coverArt",
                OnlineChartResourceKind.Thumbnail => "thumbnail",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>
    /// The metadata fields of a chart that changed within the range of <c>GET maichart/changes</c>, each with the value
    /// of the chart when the range was queried; a field that did not change is null.
    /// </summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetChartMetadataChange
    {
        /// <summary>Gets or sets the tags maintained by the uploader, or null when they did not change.</summary>
        [Preserve]
        public string[]? Tags { get; set; }
        /// <summary>Gets or sets the tags maintained by every logged-in user, or null when they did not change.</summary>
        [Preserve]
        public string[]? PublicTags { get; set; }
    }
}

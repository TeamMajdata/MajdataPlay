using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay
{
    /// <summary>The metadata and downloadable files of an online chart, as returned by the <c>maichart/info</c> API.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetChartInfo
    {
        /// <summary>Gets or sets the metadata of the chart, whose identity matches the chart list.</summary>
        [Preserve]
        public MajnetSongDetail? Metadata { get; set; }
        /// <summary>Gets or sets the downloadable files of the chart.</summary>
        [Preserve]
        public MajNetChartResources? Resources { get; set; }
    }
}

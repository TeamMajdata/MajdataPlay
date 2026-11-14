using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.Models
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
        /// <summary>Gets or sets the level hashes indexed by zero-based difficulty; a level without inote text is null.</summary>
        /// <remarks>Older endpoints omit this field, leaving the array empty.</remarks>
        [Preserve]
        public string?[] LevelHashes { get; set; } = Array.Empty<string?>();
    }
}

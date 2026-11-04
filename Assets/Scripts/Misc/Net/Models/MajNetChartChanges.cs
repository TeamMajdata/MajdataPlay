using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The charts that changed within a time range, as returned by <c>GET maichart/changes</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetChartChanges
    {
        /// <summary>Gets or sets the end of the range, exclusive, in UTC; the next query continues from it.</summary>
        [Preserve]
        public DateTime To { get; set; }
        /// <summary>
        /// Gets or sets whether every change within the range is recorded. When false, <see cref="Changes"/> is empty and
        /// the chart list has to be read again with <c>GET maichart/list</c>, after which queries continue from <see cref="To"/>.
        /// </summary>
        [Preserve]
        public bool IsComplete { get; set; }
        /// <summary>Gets or sets one entry per changed chart, ordered by <see cref="MajNetChartChange.ChangedAt"/>.</summary>
        [Preserve]
        public MajNetChartChange[] Changes { get; set; } = Array.Empty<MajNetChartChange>();
    }
}

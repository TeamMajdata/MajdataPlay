using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>
    /// The metadata of an online chart, as returned by <c>GET maichart/list</c>, <c>GET recommend/feed</c> and inside
    /// <see cref="MajNetChartInfo"/>. A field the server leaves out keeps its empty default.
    /// </summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajnetSongDetail
    {
        /// <summary>Gets or sets the identifier of the chart, a GUID.</summary>
        [Preserve]
        public string Id { get; set; } = string.Empty;
        /// <summary>Gets or sets the title of the song.</summary>
        [Preserve]
        public string Title { get; set; } = string.Empty;
        /// <summary>Gets or sets the artist of the song.</summary>
        [Preserve]
        public string Artist { get; set; } = string.Empty;
        /// <summary>Gets or sets the designer of the chart.</summary>
        [Preserve]
        public string Designer { get; set; } = string.Empty;
        /// <summary>Gets or sets the description of the chart.</summary>
        [Preserve]
        public string Description { get; set; } = string.Empty;
        /// <summary>Gets or sets the levels of the difficulties, from Easy to UTAGE.</summary>
        [Preserve]
        public string[] Levels { get; set; } = Array.Empty<string>();
        /// <summary>Gets or sets the username of the user who uploaded the chart.</summary>
        [Preserve]
        public string Uploader { get; set; } = string.Empty;
        /// <summary>Gets or sets the moment the chart was uploaded.</summary>
        [Preserve]
        public DateTime Timestamp { get; set; }
        /// <summary>Gets or sets the hash of the chart.</summary>
        [Preserve]
        public string Hash { get; set; } = string.Empty;
        /// <summary>Gets or sets the tags set by the uploader.</summary>
        [Preserve]
        public string[] Tags { get; set; } = Array.Empty<string>();
        /// <summary>Gets or sets the tags set by other users.</summary>
        [Preserve]
        public string[] PublicTags { get; set; } = Array.Empty<string>();
    }
}

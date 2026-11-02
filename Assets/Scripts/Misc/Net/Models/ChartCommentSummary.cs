using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>A comment on an online chart with its replies, as returned inside <see cref="MajNetSongInteract"/> by <c>GET maichart/{chartId}/interact</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct ChartCommentSummary
    {
        /// <summary>Gets the username of the author.</summary>
        [Preserve]
        public string Sender { get; init; }
        /// <summary>Gets the text of the comment.</summary>
        [Preserve]
        public string Content { get; init; }
        /// <summary>Gets the moment the comment was posted.</summary>
        [Preserve]
        public DateTime Timestamp { get; init; }
        /// <summary>Gets the replies to the comment, in posting order.</summary>
        [Preserve]
        public ChartCommentSummary[] Replies { get; init; }
    }
}

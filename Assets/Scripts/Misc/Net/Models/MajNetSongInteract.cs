using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The votes, plays and comments of an online chart, as returned by <c>GET maichart/{chartId}/interact</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public struct MajNetSongInteract
    {
        /// <summary>Gets whether the current user voted the chart up.</summary>
        [Preserve]
        public bool IsLiked { get; init; }
        /// <summary>Gets whether the current user voted the chart down.</summary>
        [Preserve]
        public bool IsDisLiked { get; init; }
        /// <summary>Gets the number of times the chart was played or downloaded.</summary>
        [Preserve]
        public int Plays { get; init; }
        /// <summary>Gets the usernames of the users who voted the chart up.</summary>
        [Preserve]
        public string[] Likes { get; init; }
        /// <summary>Gets the number of users who voted the chart down.</summary>
        [Preserve]
        public int DisLikeCount { get; init; }
        /// <summary>Gets the comments on the chart as a tree, in posting order.</summary>
        [Preserve]
        public ChartCommentSummary[] Comments { get; init; }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The counts of an online chart and the vote of the current user, as returned by <c>GET maichart/{chartId}/interactsum</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct MajNetChartInteractSummary
    {
        /// <summary>Gets the identifier of the chart.</summary>
        [Preserve]
        public string ChartId { get; init; }
        /// <summary>Gets the number of times the chart was played or downloaded.</summary>
        [Preserve]
        public int Plays { get; init; }
        /// <summary>Gets the upvotes minus the downvotes.</summary>
        [Preserve]
        public int Score { get; init; }
        /// <summary>Gets the number of users who voted the chart up.</summary>
        [Preserve]
        public int Upvotes { get; init; }
        /// <summary>Gets the number of users who voted the chart down.</summary>
        [Preserve]
        public int Downvotes { get; init; }
        /// <summary>Gets the number of comments that are not deleted or removed, replies included.</summary>
        [Preserve]
        public int Comments { get; init; }
        /// <summary>Gets the vote of the current user: <c>1</c>, <c>-1</c>, or <c>0</c> for none or when not logged in.</summary>
        [Preserve]
        public int MyVote { get; init; }
    }
}

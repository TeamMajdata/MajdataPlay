using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The votes of a chart or a comment after a vote was stored, as returned by <c>PUT maichart/{chartId}/vote</c> and <c>PUT comment/{commentId}/vote</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct MajNetVoteResult
    {
        /// <summary>Gets the upvotes minus the downvotes.</summary>
        [Preserve]
        public int Score { get; init; }
        /// <summary>Gets the number of upvotes; null for a comment, which publishes its score alone.</summary>
        [Preserve]
        public int? Upvotes { get; init; }
        /// <summary>Gets the number of downvotes; null for a comment, which publishes its score alone.</summary>
        [Preserve]
        public int? Downvotes { get; init; }
        /// <summary>Gets the vote of the current user as stored: <c>1</c>, <c>-1</c>, or <c>0</c> for none.</summary>
        [Preserve]
        public int MyVote { get; init; }
    }
}

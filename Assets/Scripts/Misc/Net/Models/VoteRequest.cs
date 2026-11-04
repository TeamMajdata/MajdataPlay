using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The body of <c>PUT maichart/{chartId}/vote</c>, which stores the vote of the current user.</summary>
    [Preserve]
    internal readonly struct VoteRequest
    {
        /// <summary>Gets the vote to store: <c>1</c> for an upvote, <c>-1</c> for a downvote and <c>0</c> to withdraw the vote.</summary>
        [Preserve]
        public int Value { get; init; }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>One page of the top-level comments of an online chart with their replies, as returned by <c>GET maichart/{chartId}/comments</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct MajNetCommentPage
    {
        /// <summary>Gets the top-level comments of the page, in the requested order, with their replies nested.</summary>
        [Preserve]
        public MajNetComment[] Items { get; init; }
        /// <summary>Gets the number of top-level comments on the chart, placeholders included.</summary>
        [Preserve]
        public int Total { get; init; }
        /// <summary>Gets the number of live comments on the chart, replies included.</summary>
        [Preserve]
        public int CommentCount { get; init; }
        /// <summary>Gets the page number, starting at <c>1</c>.</summary>
        [Preserve]
        public int Page { get; init; }
        /// <summary>Gets the number of top-level comments per page.</summary>
        [Preserve]
        public int PageSize { get; init; }
        /// <summary>Gets the order of the page.</summary>
        [Preserve]
        public CommentSort Sort { get; init; }
    }
}

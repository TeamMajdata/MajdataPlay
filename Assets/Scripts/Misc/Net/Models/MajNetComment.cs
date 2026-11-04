using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>
    /// A comment on an online chart with the vote of the current user and the replies that were loaded, as returned by
    /// <c>GET maichart/{chartId}/comments</c> and <c>POST maichart/{chartId}/comments</c>. A placeholder of a deleted or
    /// removed comment keeps its place in the thread but has no author and no text.
    /// </summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct MajNetComment
    {
        /// <summary>Gets the identifier of the comment.</summary>
        [Preserve]
        public string Id { get; init; }
        /// <summary>Gets the identifier of the chart the comment is on, or null when the chart no longer exists.</summary>
        [Preserve]
        public string? ChartId { get; init; }
        /// <summary>Gets the hash of the chart the comment is on.</summary>
        [Preserve]
        public string ChartHash { get; init; }
        /// <summary>Gets the identifier of the comment replied to, or null for a top-level comment.</summary>
        [Preserve]
        public string? ParentId { get; init; }
        /// <summary>Gets the number of replies between the comment and the top-level comment of its thread.</summary>
        [Preserve]
        public int Depth { get; init; }
        /// <summary>Gets the identifier of the author, or null for a placeholder.</summary>
        [Preserve]
        public string? AuthorId { get; init; }
        /// <summary>Gets the username of the author; empty for a placeholder or when the account no longer exists.</summary>
        [Preserve]
        public string Author { get; init; }
        /// <summary>Gets the text of the comment; empty for a placeholder.</summary>
        [Preserve]
        public string Body { get; init; }
        /// <summary>Gets the moment the comment was posted.</summary>
        [Preserve]
        public DateTime CreatedAt { get; init; }
        /// <summary>Gets the moment the text was last edited, or null when it never was.</summary>
        [Preserve]
        public DateTime? EditedAt { get; init; }
        /// <summary>Gets whether the author deleted the comment.</summary>
        [Preserve]
        public bool Deleted { get; init; }
        /// <summary>Gets whether an administrator removed the comment.</summary>
        [Preserve]
        public bool Removed { get; init; }
        /// <summary>Gets the upvotes minus the downvotes.</summary>
        [Preserve]
        public int Score { get; init; }
        /// <summary>Gets the vote of the current user: <c>1</c>, <c>-1</c>, or <c>0</c> for none or when not logged in.</summary>
        [Preserve]
        public int MyVote { get; init; }
        /// <summary>Gets the number of direct replies, including placeholders.</summary>
        [Preserve]
        public int ReplyCount { get; init; }
        /// <summary>Gets the replies that were loaded, in the requested order.</summary>
        [Preserve]
        public MajNetComment[] Replies { get; init; }

        /// <summary>Gets whether the comment is a placeholder of a deleted or removed comment.</summary>
        public bool IsPlaceholder
        {
            get
            {
                return Deleted || Removed;
            }
        }
    }
}

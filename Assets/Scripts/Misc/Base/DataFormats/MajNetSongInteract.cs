using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

namespace MajdataPlay
{
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public struct MajNetSongInteract
    {
        [Preserve]
        public bool IsLiked { get; init; }
        [Preserve]
        public bool IsDisLiked { get; init; }
        [Preserve]
        public int Plays { get; init; }
        [Preserve]
        public string[] Likes { get; init; }
        [Preserve]
        public int DisLikeCount { get; init; }
        [Preserve]
        public ChartCommentSummary[] Comments { get; init; }
    }
}

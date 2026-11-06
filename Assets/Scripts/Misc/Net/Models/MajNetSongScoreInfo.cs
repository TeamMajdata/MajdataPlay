using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The rankings of every difficulty of an online chart, as returned by <c>GET maiscore/leaderboard/chart/by-id/{chartId}</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public struct MajNetSongScoreInfo
    {
        /// <summary>Gets the hash of the chart.</summary>
        public string Hash { get; init; }
        /// <summary>Gets the rankings, indexed by difficulty and then by rank.</summary>
        public MajNetSongScore[][] Scores { get; init; }
    }
}

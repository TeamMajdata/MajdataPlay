using MajdataPlay.Scenes.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The best score of the user on one difficulty of a chart, as returned by <c>GET account/scores</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct MajNetAccountSongScore
    {
        /// <summary>Gets the achievement of the score.</summary>
        public Accurate Acc { get; init; }
        /// <summary>Gets the DX score.</summary>
        public uint DXScore { get; init; }
        /// <summary>Gets the difficulty the score was played on.</summary>
        public ChartLevel ChartLevel { get; init; }
        /// <summary>Gets the combo state of the score.</summary>
        public ComboState ComboState { get; init; }
        /// <summary>Gets the hash of the chart.</summary>
        public string Hash { get; init; }
        /// <summary>Gets the moment the score was played.</summary>
        public DateTime Timestamp { get; init; }
    }
}

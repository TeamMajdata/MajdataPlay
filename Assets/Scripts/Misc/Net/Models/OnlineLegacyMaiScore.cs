using MajdataPlay.Scenes.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The body of a score submitted to the legacy <c>POST maichart/{chartId}/score</c> route.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public sealed class OnlineLegacyMaiScore
    {
        /// <summary>Gets the hash of the entire chart the player played.</summary>
        public string Hash { get; init; } = string.Empty;
        /// <summary>Gets the zero-based difficulty of the played level.</summary>
        public ChartLevel ChartLevel { get; init; }
        /// <summary>Gets the accuracies of this play.</summary>
        public Accurate Acc { get; init; }
        /// <summary>Gets the DX score of this play.</summary>
        public long DXScore { get; init; }
        /// <summary>Gets the combo state of this play.</summary>
        public ComboState ComboState { get; init; }
    }
}

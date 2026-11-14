using MajdataPlay.Scenes.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>The body and routing information of a score submitted to <c>POST maiscore/level/{levelHash}</c>.</summary>
    [Preserve]
    [JsonObject(MemberSerialization.OptIn, NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public sealed class OnlineMaiScore
    {
        /// <summary>Gets the identifier of the chart the player played, on which the play is counted.</summary>
        [JsonProperty]
        public string ChartId { get; init; } = string.Empty;
        /// <summary>Gets the accuracies of this play.</summary>
        [JsonProperty]
        public Accurate Acc { get; init; }
        /// <summary>Gets the DX score of this play.</summary>
        [JsonProperty]
        public long DXScore { get; init; }
        /// <summary>Gets the combo state of this play.</summary>
        [JsonProperty]
        public ComboState ComboState { get; init; }
        /// <summary>Gets the level hash used in the submission route; null when the level hash is unavailable.</summary>
        [JsonIgnore]
        public string? LevelHash { get; init; }
        /// <summary>Gets the played difficulty, used only to submit to an older endpoint.</summary>
        [JsonIgnore]
        public ChartLevel ChartLevel { get; init; }

        /// <summary>Creates the online submission of one play without sharing the local score persistence model.</summary>
        /// <param name="result">The finished game on an online chart.</param>
        /// <param name="level">The played difficulty.</param>
        /// <returns>The online submission, including its level hash and legacy routing information.</returns>
        /// <exception cref="ArgumentException">The result does not name an online chart.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The difficulty is outside the seven supported chart levels.</exception>
        public static OnlineMaiScore CreateFromResult(in GameResult result, ChartLevel level)
        {
            if (result.SongDetail is not OnlineSongDetail song)
            {
                throw new ArgumentException("An online score requires an online chart.", nameof(result));
            }
            return new OnlineMaiScore
            {
                ChartId = song.Id,
                LevelHash = song.GetLevelHash(level),
                ChartLevel = level,
                Acc = result.Acc,
                DXScore = result.DXScore,
                ComboState = result.ComboState
            };
        }
    }
}

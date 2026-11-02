using MajdataPlay.Scenes.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>One entry of the ranking of an online chart, as returned inside <see cref="MajNetSongScoreInfo"/>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public struct MajNetSongScore
    {
        /// <summary>Gets the player who set the score; it comes without <see cref="UserProfile.Id"/> and <see cref="UserProfile.AvatarId"/>.</summary>
        public UserProfile Player { get; init; }
        /// <summary>Gets the achievement of the score.</summary>
        public float Acc { get; init; }
        /// <summary>Gets the combo state of the score.</summary>
        public ComboState ComboState { get; init; }
        /// <summary>Gets the moment the score was played.</summary>
        public DateTime Timestamp { get; init; }
    }
}

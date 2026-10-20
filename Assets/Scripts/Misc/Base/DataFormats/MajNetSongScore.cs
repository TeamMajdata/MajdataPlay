using MajdataPlay.Scenes.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;

namespace MajdataPlay
{
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public struct MajNetSongScore
    {
        public UserProfile Player { get; init; }
        public float Acc { get; init; }
        public ComboState ComboState { get; init; }
        public DateTime Timestamp { get; init; }
    }
}

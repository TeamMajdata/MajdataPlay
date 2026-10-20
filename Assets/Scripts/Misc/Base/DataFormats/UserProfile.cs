using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay
{
    /// <summary>
    /// GET account/profile of the server. Nickname is for display; Username identifies the user.
    /// The players in a chart ranking come without Id and AvatarId.
    /// </summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct UserProfile
    {
        [Preserve]
        public string? Id { get; init; }
        [Preserve]
        public string Username { get; init; }
        [Preserve]
        public string? Nickname { get; init; }
        [Preserve]
        public string? Email { get; init; }
        [Preserve]
        public string? AvatarId { get; init; }
        [Preserve]
        public DateTime JoinDate { get; init; }

        public string DisplayName => string.IsNullOrEmpty(Nickname) ? Username : Nickname;
    }
}

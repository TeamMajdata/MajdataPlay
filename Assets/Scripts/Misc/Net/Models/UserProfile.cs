using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>
    /// GET account/profile of the server. Nickname is for display; Username identifies the user.
    /// The players in a chart ranking come without Id and AvatarId.
    /// </summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct UserProfile
    {
        /// <summary>Gets the identifier of the account.</summary>
        [Preserve]
        public string? Id { get; init; }
        /// <summary>Gets the username, which identifies the user.</summary>
        [Preserve]
        public string Username { get; init; }
        /// <summary>Gets the nickname shown instead of the username when it is set.</summary>
        [Preserve]
        public string? Nickname { get; init; }
        /// <summary>Gets the email address; only the profile of the logged-in user carries it.</summary>
        [Preserve]
        public string? Email { get; init; }
        /// <summary>Gets the identifier of the avatar image for <c>GET avatar/{avatarId}</c>; null or empty when the user has no avatar.</summary>
        [Preserve]
        public string? AvatarId { get; init; }
        /// <summary>Gets the moment the account was created.</summary>
        [Preserve]
        public DateTime JoinDate { get; init; }

        /// <summary>Gets the name shown for the user: the nickname, or the username when no nickname is set.</summary>
        public string DisplayName => string.IsNullOrEmpty(Nickname) ? Username : Nickname;
    }
}

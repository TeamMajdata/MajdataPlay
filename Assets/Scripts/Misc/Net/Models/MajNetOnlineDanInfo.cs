using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;
#nullable enable
namespace MajdataPlay.Net.Models
{
    /// <summary>A favorite chart collection of the user, as returned by <c>GET account/favorite/collection/list</c>.</summary>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public readonly struct MajNetOnlineDanInfo
    {
        /// <summary>Gets the identifier of the collection.</summary>
        public required string Id { get; init; }
        /// <summary>Gets the display name of the collection.</summary>
        public required string Name { get; init; }
        /// <summary>Gets the username of the user who created the collection.</summary>
        public required string CreatedBy { get; init; }
        /// <summary>Gets the description of the collection.</summary>
        public required string Description { get; init; }
        /// <summary>Gets the hashes of the charts in the collection.</summary>
        public required string[] SongHashs { get; init; }
        /// <summary>Gets whether the collection is played as a playlist.</summary>
        public bool IsPlayList { get; init; }
        /// <summary>Gets whether a playthrough of the collection ends when the life runs out.</summary>
        public bool IsForceGameover { get; init; }
    }
}

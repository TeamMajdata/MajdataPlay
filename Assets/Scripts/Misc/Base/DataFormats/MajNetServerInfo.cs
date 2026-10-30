using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay
{
    /// <summary>The identity of a server instance, as returned by the <c>GET info</c> API.</summary>
    /// <remarks>Every address of the same server, such as a reverse proxy, reports the same identity.</remarks>
    [Preserve]
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MajNetServerInfo
    {
        /// <summary>Gets or sets the identifier of the server instance, or an empty string when the server has none configured.</summary>
        [Preserve]
        public string Id { get; set; } = string.Empty;
        /// <summary>Gets or sets the display name of the server instance, or an empty string when the server has none configured.</summary>
        [Preserve]
        public string Name { get; set; } = string.Empty;
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Text;
using UnityEngine.Scripting;

#nullable enable
namespace MajdataPlay.Net.LanSync
{
    [Preserve]
    internal enum LanSyncMessageType
    {
        /// <summary>Host heartbeat, used by clients to discover the room.</summary>
        Announce,
        /// <summary>Client to host, carries the send stamp used for the clock offset estimate.</summary>
        Ping,
        /// <summary>Host to client, echoes a ping stamp.</summary>
        Pong,
        /// <summary>Host to client, carries the instant the chart must start at.</summary>
        Start
    }

    /// <summary>
    /// One datagram of the LAN start synchronization protocol. Every message carries the room
    /// code and the sender id, so a room never reacts to a foreign or stale packet.
    /// </summary>
    [Preserve]
    internal sealed class LanSyncMessage
    {
        [Preserve, JsonProperty("v")]
        public int Version { get; set; } = LanSyncService.PROTOCOL_VERSION;

        [Preserve, JsonProperty("type"), JsonConverter(typeof(StringEnumConverter))]
        public LanSyncMessageType Type { get; set; }

        [Preserve, JsonProperty("room")]
        public int RoomCode { get; set; }

        [Preserve, JsonProperty("mid")]
        public string MachineId { get; set; } = string.Empty;

        /// <summary>Stamp of this packet in the sender clock.</summary>
        [Preserve, JsonProperty("sent")]
        public long SentUtcTicks { get; set; }

        /// <summary>Stamp of the ping that this pong answers.</summary>
        [Preserve, JsonProperty("echo")]
        public long EchoUtcTicks { get; set; }

        /// <summary>Start instant in the host clock.</summary>
        [Preserve, JsonProperty("start")]
        public long StartUtcTicks { get; set; }

        [Preserve, JsonProperty("song")]
        public LanSyncSongIdentity? Song { get; set; }
    }

    /// <summary>
    /// Description of the chart that is about to start. It is only used to warn about a room that
    /// does not play the same chart, the score and the input are never shared.
    /// </summary>
    [Preserve]
    public sealed class LanSyncSongIdentity
    {
        [Preserve, JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [Preserve, JsonProperty("artist")]
        public string Artist { get; set; } = string.Empty;

        [Preserve, JsonProperty("hash")]
        public string ChartHash { get; set; } = string.Empty;

        [Preserve, JsonProperty("level")]
        public int Level { get; set; }

        [Preserve, JsonProperty("designer")]
        public string Designer { get; set; } = string.Empty;

        public bool IsSameChartAs(LanSyncSongIdentity? other)
        {
            return other is not null &&
                   Level == other.Level &&
                   string.Equals(ChartHash, other.ChartHash, System.StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return $"{Title} [{Level}] ({ChartHash})";
        }
    }

    internal static class LanSyncJson
    {
        static readonly JsonSerializerSettings _settings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            TypeNameHandling = TypeNameHandling.None
        };

        public static byte[] Serialize(LanSyncMessage message)
        {
            var json = JsonConvert.SerializeObject(message, _settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public static LanSyncMessage? Deserialize(byte[] payload)
        {
            if (payload.Length == 0)
            {
                return null;
            }
            var json = Encoding.UTF8.GetString(payload);
            return JsonConvert.DeserializeObject<LanSyncMessage>(json, _settings);
        }
    }
}

using MajdataPlay.Diagnostics;
using MajdataPlay.Settings;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace MajdataPlay.Net.LanSync
{
    /// <summary>
    /// Shares a single piece of state between the machines of a LAN room: the absolute instant at
    /// which the chart is allowed to start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing else is transported. The chart, the difficulty and every adjustment stay local, so a
    /// machine that runs a different chart only produces a warning, never a broken room.
    /// </para>
    /// <para>
    /// The host answers clock probes (ping/pong) so a client can convert the broadcast instant into
    /// its own clock without relying on the system clocks being in sync. On a wired LAN the offset
    /// estimate is a fraction of a millisecond, which is far below the audio output latency that a
    /// machine adds on its own.
    /// </para>
    /// <para>
    /// Packets are sent to the subnet broadcast address, so every machine of a room has to live in
    /// the same broadcast domain. Routed rooms, VLAN separation and IPv6-only networks are not
    /// supported yet.
    /// </para>
    /// </remarks>
    public static class LanSyncService
    {
        public const int PROTOCOL_VERSION = 1;

        const int ANNOUNCE_INTERVAL_MS = 500;
        const int CLIENT_PROBE_INTERVAL_MS = 200;
        const int CLIENT_IDLE_PROBE_INTERVAL_MS = 2000;
        const int CLOCK_PROBE_TARGET = 8;
        const int RECEIVE_TIMEOUT_MS = 250;
        const int START_PACKET_REPEAT = 4;
        const int START_PACKET_REPEAT_GAP_MS = 20;
        const int PEER_TIMEOUT_MS = 10000;
        const int MAX_ACCEPTED_ROUND_TRIP_MS = 2000;
        const int CLOCK_SAMPLE_LIFETIME_MS = 30000;

        static readonly object _sendLock = new();
        static readonly object _lifecycleLock = new();

        static UdpClient? _client;
        static Thread? _worker;
        static CancellationTokenSource? _cts;
        static string _machineId = string.Empty;
        static int _port;

        // 0 means "no schedule received yet". Written from the network thread, read from the
        // main thread, so every access goes through Interlocked.
        static long _scheduledStartTicks;
        static long _clockOffsetTicks;
        static long _bestRoundTripTicks;
        static long _bestSampleTicks;
        static int _peerCount;
        static LanSyncSongIdentity? _lastAnnouncedSong;

        /// <summary>Role of this machine. <see cref="LanSyncRoleOption.Disabled"/> until <see cref="Init"/> succeeded.</summary>
        public static LanSyncRoleOption Role { get; private set; } = LanSyncRoleOption.Disabled;
        public static bool IsActive => Role != LanSyncRoleOption.Disabled;
        /// <summary>Number of peers heard from during the last <see cref="PEER_TIMEOUT_MS"/>.</summary>
        public static int PeerCount => Volatile.Read(ref _peerCount);
        /// <summary>Estimated difference between the host clock and the local clock.</summary>
        public static double ClockOffsetMilliseconds => LanSyncClock.TicksToMilliseconds(Interlocked.Read(ref _clockOffsetTicks));
        /// <summary>Round trip time of the best clock sample, 0 when no sample was taken yet.</summary>
        public static double ClockRoundTripMilliseconds => LanSyncClock.TicksToMilliseconds(Interlocked.Read(ref _bestRoundTripTicks));
        public static string MachineId => _machineId;
        public static bool HasScheduledStart => Interlocked.Read(ref _scheduledStartTicks) > 0;

        /// <summary>
        /// Starts the background receiver. Safe to call more than once, the previous session is
        /// closed first. <paramref name="options"/> is read once, a role change needs a restart.
        /// </summary>
        public static void Init(GameOptions options)
        {
            lock (_lifecycleLock)
            {
                Shutdown();

                if (options is null || options.LanSyncRole == LanSyncRoleOption.Disabled)
                {
                    return;
                }
                var port = options.LanSyncPort;
                if (port <= IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
                {
                    MajDebug.LogError($"[LanSync] invalid port: {port}");
                    return;
                }

                var client = new UdpClient();
                try
                {
                    // ReuseAddress keeps two instances on a single machine usable for testing.
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    client.EnableBroadcast = true;
                    client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    client.Client.ReceiveTimeout = RECEIVE_TIMEOUT_MS;
                }
                catch (Exception e)
                {
                    MajDebug.LogError($"[LanSync] failed to open udp :{port}\n{e}");
                    try
                    {
                        client.Close();
                    }
                    catch
                    {
                        // ignored, the socket is unusable either way
                    }
                    return;
                }

                _client = client;
                _port = port;
                _machineId = CreateMachineId();
                Role = options.LanSyncRole;
                RoomCode = options.LanSyncRoomCode;

                var cts = new CancellationTokenSource();
                _cts = cts;
                var worker = new Thread(() => Run(cts.Token))
                {
                    IsBackground = true,
                    Name = "MajdataPlay.LanSync"
                };
                _worker = worker;
                worker.Start();

                MajDebug.LogInfo($"[LanSync] started as {Role}, room {RoomCode}, udp :{port}, id {_machineId}");
            }
        }

        public static int RoomCode { get; private set; }

        /// <summary>
        /// Stops the background receiver and clears the negotiated state.
        /// </summary>
        public static void Shutdown()
        {
            lock (_lifecycleLock)
            {
                try
                {
                    _cts?.Cancel();
                }
                catch
                {
                    // ignored
                }
                try
                {
                    _client?.Close();
                }
                catch
                {
                    // ignored
                }
                try
                {
                    _worker?.Join(RECEIVE_TIMEOUT_MS * 2);
                }
                catch
                {
                    // ignored
                }

                _cts?.Dispose();
                _cts = null;
                _worker = null;
                _client = null;
                Role = LanSyncRoleOption.Disabled;
                RoomCode = 0;
                _port = 0;
                Interlocked.Exchange(ref _scheduledStartTicks, 0);
                Interlocked.Exchange(ref _clockOffsetTicks, 0);
                Interlocked.Exchange(ref _bestRoundTripTicks, 0);
                Interlocked.Exchange(ref _bestSampleTicks, 0);
                Volatile.Write(ref _peerCount, 0);
                Volatile.Write(ref _lastAnnouncedSong, null);
            }
        }

        /// <summary>
        /// Host only. Broadcasts the instant the chart has to start at and returns it in the local
        /// clock, or 0 when this machine is not a host.
        /// </summary>
        public static long AnnounceStart(LanSyncSongIdentity? song, int delayMilliseconds)
        {
            if (Role != LanSyncRoleOption.Host || _client is null)
            {
                return 0;
            }

            var now = LanSyncClock.NowTicks();
            var startTicks = now + LanSyncClock.MillisecondsToTicks(Math.Max(0, delayMilliseconds));
            var message = new LanSyncMessage
            {
                Type = LanSyncMessageType.Start,
                RoomCode = RoomCode,
                MachineId = _machineId,
                SentUtcTicks = now,
                StartUtcTicks = startTicks,
                Song = song
            };
            var payload = LanSyncJson.Serialize(message);

            // Later announces carry the chart as well, so a client that joins mid-announce still
            // knows what the room is playing.
            Volatile.Write(ref _lastAnnouncedSong, song);

            // The first packet goes out immediately, the repeats only cover a lost datagram.
            SendBroadcast(payload);
            Task.Run(() =>
            {
                for (var i = 1; i < START_PACKET_REPEAT; i++)
                {
                    try
                    {
                        Thread.Sleep(START_PACKET_REPEAT_GAP_MS);
                    }
                    catch
                    {
                        // ignored
                    }
                    SendBroadcast(payload);
                }
            });

            MajDebug.LogInfo($"[LanSync] start broadcast at +{delayMilliseconds}ms: {song?.ToString() ?? "unknown chart"}");
            return startTicks;
        }

        /// <summary>
        /// Client only. Takes the negotiated start instant, in the local clock, exactly once.
        /// </summary>
        public static bool TryTakeScheduledStart(out long localStartTicks)
        {
            localStartTicks = Interlocked.Exchange(ref _scheduledStartTicks, 0);
            return localStartTicks > 0;
        }

        static void Run(CancellationToken token)
        {
            var peers = new Dictionary<string, long>(StringComparer.Ordinal);
            var isHost = Role == LanSyncRoleOption.Host;
            var lastAnnounce = 0L;
            var lastProbe = 0L;
            var lastPrune = 0L;
            var probeReplies = 0;

            while (!token.IsCancellationRequested)
            {
                var client = _client;
                if (client is null)
                {
                    break;
                }

                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var payload = client.Receive(ref remote);
                    var message = LanSyncJson.Deserialize(payload);
                    if (message is null ||
                        message.Version != PROTOCOL_VERSION ||
                        message.RoomCode != RoomCode ||
                        message.MachineId == _machineId)
                    {
                        continue;
                    }
                    Handle(message, remote, peers, ref probeReplies, isHost);
                }
                catch (SocketException e) when (e.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
                {
                    // Receive timeout, only used to keep the timers below running.
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    MajDebug.LogWarning($"[LanSync] receive failed: {e.GetType().Name}: {e.Message}");
                }

                var now = LanSyncClock.NowTicks();
                if (isHost)
                {
                    if (lastAnnounce == 0 || now - lastAnnounce >= LanSyncClock.MillisecondsToTicks(ANNOUNCE_INTERVAL_MS))
                    {
                        lastAnnounce = now;
                        SendBroadcast(LanSyncJson.Serialize(new LanSyncMessage
                        {
                            Type = LanSyncMessageType.Announce,
                            RoomCode = RoomCode,
                            MachineId = _machineId,
                            SentUtcTicks = now,
                            Song = Volatile.Read(ref _lastAnnouncedSong)
                        }));
                    }
                    if (lastPrune == 0 || now - lastPrune >= LanSyncClock.MillisecondsToTicks(PEER_TIMEOUT_MS))
                    {
                        lastPrune = now;
                        Prune(peers, now);
                    }
                }
                else
                {
                    var interval = probeReplies >= CLOCK_PROBE_TARGET ? CLIENT_IDLE_PROBE_INTERVAL_MS : CLIENT_PROBE_INTERVAL_MS;
                    if (lastProbe == 0 || now - lastProbe >= LanSyncClock.MillisecondsToTicks(interval))
                    {
                        lastProbe = now;
                        SendBroadcast(LanSyncJson.Serialize(new LanSyncMessage
                        {
                            Type = LanSyncMessageType.Ping,
                            RoomCode = RoomCode,
                            MachineId = _machineId,
                            SentUtcTicks = LanSyncClock.NowTicks()
                        }));
                    }
                }
            }
        }

        static void Handle(LanSyncMessage message, IPEndPoint remote, Dictionary<string, long> peers, ref int probeReplies, bool isHost)
        {
            switch (message.Type)
            {
                case LanSyncMessageType.Announce:
                case LanSyncMessageType.Ping:
                    {
                        peers[message.MachineId] = LanSyncClock.NowTicks();
                        if (message.Type == LanSyncMessageType.Ping && isHost)
                        {
                            SendTo(remote, new LanSyncMessage
                            {
                                Type = LanSyncMessageType.Pong,
                                RoomCode = RoomCode,
                                MachineId = _machineId,
                                EchoUtcTicks = message.SentUtcTicks,
                                SentUtcTicks = LanSyncClock.NowTicks()
                            });
                        }
                    }
                    break;
                case LanSyncMessageType.Pong:
                    {
                        if (isHost)
                        {
                            break;
                        }
                        if (TryApplyClockSample(message))
                        {
                            probeReplies++;
                        }
                    }
                    break;
                case LanSyncMessageType.Start:
                    {
                        if (isHost || message.StartUtcTicks <= 0)
                        {
                            break;
                        }
                        // The host clock is offset by the measured difference, so the schedule is
                        // converted into this machine's clock before it is handed to the gate.
                        var localStartTicks = message.StartUtcTicks - Interlocked.Read(ref _clockOffsetTicks);
                        Interlocked.Exchange(ref _scheduledStartTicks, localStartTicks);
                        Volatile.Write(ref _lastAnnouncedSong, message.Song);
                        MajDebug.LogInfo($"[LanSync] start scheduled: {message.Song?.ToString() ?? "unknown chart"}, " +
                                         $"in {LanSyncClock.TicksToMilliseconds(localStartTicks - LanSyncClock.NowTicks()):F0}ms, " +
                                         $"clock offset {ClockOffsetMilliseconds:F2}ms, rtt {ClockRoundTripMilliseconds:F2}ms");
                    }
                    break;
            }
        }

        /// <summary>
        /// Keeps the sample with the smallest round trip, which is also the sample with the smallest
        /// estimation error. Samples are refreshed periodically so a stepped system clock is picked up.
        /// </summary>
        static bool TryApplyClockSample(LanSyncMessage pong)
        {
            var receiveTicks = LanSyncClock.NowTicks();
            var sendTicks = pong.EchoUtcTicks;
            if (sendTicks <= 0 || pong.SentUtcTicks <= 0)
            {
                return false;
            }

            var roundTripTicks = receiveTicks - sendTicks;
            if (roundTripTicks <= 0 || roundTripTicks > LanSyncClock.MillisecondsToTicks(MAX_ACCEPTED_ROUND_TRIP_MS))
            {
                return false;
            }

            var bestRoundTrip = Interlocked.Read(ref _bestRoundTripTicks);
            var bestSample = Interlocked.Read(ref _bestSampleTicks);
            var isFresh = bestSample != 0 && receiveTicks - bestSample < LanSyncClock.MillisecondsToTicks(CLOCK_SAMPLE_LIFETIME_MS);
            if (bestRoundTrip != 0 && isFresh && roundTripTicks >= bestRoundTrip)
            {
                return false;
            }

            // The one way delay is assumed to be half of the round trip, which is exact enough on a
            // wired LAN and bounded by the measured round trip on a congested one.
            var offsetTicks = pong.SentUtcTicks - (sendTicks + roundTripTicks / 2);
            Interlocked.Exchange(ref _bestRoundTripTicks, roundTripTicks);
            Interlocked.Exchange(ref _bestSampleTicks, receiveTicks);
            Interlocked.Exchange(ref _clockOffsetTicks, offsetTicks);
            return true;
        }

        static void Prune(Dictionary<string, long> peers, long now)
        {
            var timeout = LanSyncClock.MillisecondsToTicks(PEER_TIMEOUT_MS);
            List<string>? expired = null;
            foreach (var pair in peers)
            {
                if (now - pair.Value <= timeout)
                {
                    continue;
                }
                expired ??= new List<string>();
                expired.Add(pair.Key);
            }
            if (expired is null)
            {
                Volatile.Write(ref _peerCount, peers.Count);
                return;
            }
            foreach (var id in expired)
            {
                peers.Remove(id);
            }
            Volatile.Write(ref _peerCount, peers.Count);
        }

        static void SendBroadcast(byte[] payload)
        {
            var client = _client;
            if (client is null)
            {
                return;
            }
            try
            {
                lock (_sendLock)
                {
                    client.Send(payload, payload.Length, new IPEndPoint(IPAddress.Broadcast, _port));
                }
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"[LanSync] broadcast failed: {e.GetType().Name}: {e.Message}");
            }
        }

        static void SendTo(IPEndPoint remote, LanSyncMessage message)
        {
            var client = _client;
            if (client is null)
            {
                return;
            }
            try
            {
                var payload = LanSyncJson.Serialize(message);
                lock (_sendLock)
                {
                    client.Send(payload, payload.Length, remote);
                }
            }
            catch (Exception e)
            {
                MajDebug.LogWarning($"[LanSync] reply failed: {e.GetType().Name}: {e.Message}");
            }
        }

        static string CreateMachineId()
        {
            return $"{Environment.MachineName}-{Guid.NewGuid():N}";
        }
    }
}

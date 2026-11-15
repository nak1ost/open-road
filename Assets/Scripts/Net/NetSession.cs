using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using RaceGame.Cars;
using UnityEngine;

namespace RaceGame.Net
{
    /// <summary>
    /// Game-level LAN session on top of LanNetwork: player list, car states, car changes, races.
    /// The host is player 0 and relays everything between clients.
    /// </summary>
    public class NetSession : IDisposable
    {
        public const float StateRate = 20f;

        public bool IsHost { get; private set; }
        public int LocalId { get; private set; } = -1;
        public int Seed { get; private set; }
        public string GameName { get; private set; }
        public bool Joined => LocalId >= 0;
        public readonly Dictionary<int, PlayerInfo> players = new Dictionary<int, PlayerInfo>();

        public event Action JoinedGame;
        public event Action<PlayerInfo> PlayerAdded;
        public event Action<PlayerInfo> PlayerUpdated;
        public event Action<int> PlayerRemoved;
        public event Action<int, CarState> StateReceived;
        public event Action<string, Dictionary<int, int>> RaceStarted;   // routeId, playerId → grid slot
        public event Action<int, int, int, float> ProgressReceived;      // id, lap, nextCheckpoint, finishTime
        public event Action RaceCancelled;
        public event Action<string> Disconnected;                        // reason

        LanNetwork net;
        PlayerInfo me;
        float stateTimer;
        uint seq;
        Task<bool> connecting;

        // ------------------------------------------------------------------ start

        public static NetSession Host(string gameName, PlayerInfo me, int seed)
        {
            var s = new NetSession { IsHost = true, GameName = gameName, Seed = seed, me = me, LocalId = 0 };
            me.id = 0;
            s.players[0] = me;
            s.net = new LanNetwork();
            s.net.infoReply = s.BuildInfo;
            s.net.StartHost();
            return s;
        }

        /// <summary>Starts connecting in the background; JoinedGame fires once the host answers.</summary>
        public static NetSession Join(IPAddress address, PlayerInfo me)
        {
            var s = new NetSession { IsHost = false, me = me };
            s.net = new LanNetwork();
            s.connecting = Task.Run(() => s.net.Connect(address));
            return s;
        }

        public bool IsConnecting => connecting != null && !connecting.IsCompleted;

        byte[] BuildInfo()
        {
            int count;
            lock (players) count = players.Count;
            return LanProtocol.Frame(Msg.Info, w =>
            {
                w.Write(LanProtocol.Version);
                w.Write(GameName ?? "");
                w.Write(count);
            });
        }

        // ------------------------------------------------------------------ main-thread pump

        public void Poll()
        {
            if (connecting != null && connecting.IsCompleted)
            {
                bool ok = connecting.Status == TaskStatus.RanToCompletion && connecting.Result;
                connecting = null;
                if (!ok)
                {
                    Disconnected?.Invoke("Не удалось подключиться к хосту");
                    return;
                }
                net.SendToHost(LanProtocol.Frame(Msg.Hello, w =>
                {
                    w.Write(LanProtocol.Version);
                    me.Write(w);
                }));
            }
            if (net == null) return;

            while (net.events.TryDequeue(out var e))
            {
                switch (e.kind)
                {
                    case NetEventKind.State:
                        if (e.state.playerId == LocalId) break;
                        if (IsHost) net.SendState(e.raw, e.state.playerId);
                        StateReceived?.Invoke(e.state.playerId, e.state);
                        break;
                    case NetEventKind.Message:
                        if (IsHost) HostMessage(e.connId, e.type, e.payload);
                        else ClientMessage(e.type, e.payload);
                        break;
                    case NetEventKind.PeerDisconnected:
                        RemovePlayer(e.connId);
                        break;
                    case NetEventKind.ConnectionLost:
                        Disconnected?.Invoke("Соединение с хостом потеряно");
                        break;
                }
            }
        }

        void RemovePlayer(int id)
        {
            if (!players.Remove(id)) return;
            if (IsHost) net.Broadcast(LanProtocol.Frame(Msg.PlayerLeft, w => w.Write(id)));
            PlayerRemoved?.Invoke(id);
        }

        void HostMessage(int connId, Msg type, byte[] payload)
        {
            using (var r = LanProtocol.Reader(payload))
            {
                switch (type)
                {
                    case Msg.Hello:
                    {
                        int version = r.ReadInt32();
                        var info = PlayerInfo.Read(r);
                        if (version != LanProtocol.Version || players.Count >= LanProtocol.MaxPlayers)
                        {
                            string reason = version != LanProtocol.Version ? "Разные версии игры" : "Игра заполнена";
                            net.SendTo(connId, LanProtocol.Frame(Msg.Kick, w => w.Write(reason)));
                            net.Disconnect(connId);
                            return;
                        }
                        info.id = connId;
                        lock (players) players[connId] = info;
                        net.SendTo(connId, LanProtocol.Frame(Msg.Welcome, w =>
                        {
                            w.Write(connId);
                            w.Write(Seed);
                            w.Write(GameName ?? "");
                            w.Write(players.Count);
                            foreach (var p in players.Values) p.Write(w);
                        }));
                        net.Broadcast(LanProtocol.Frame(Msg.PlayerJoined, w => info.Write(w)), connId);
                        PlayerAdded?.Invoke(info);
                        break;
                    }
                    case Msg.CarChanged:
                    {
                        var info = PlayerInfo.Read(r);
                        info.id = connId;
                        if (players.TryGetValue(connId, out var old)) info.name = old.name;
                        lock (players) players[connId] = info;
                        net.Broadcast(LanProtocol.Frame(Msg.CarChanged, w => info.Write(w)), connId);
                        PlayerUpdated?.Invoke(info);
                        break;
                    }
                    case Msg.RaceProgress:
                    {
                        r.ReadInt32(); // id written by the client is ignored, the connection tells who it is
                        int lap = r.ReadInt32(), cp = r.ReadInt32();
                        float finish = r.ReadSingle();
                        net.Broadcast(ProgressFrame(connId, lap, cp, finish), connId);
                        ProgressReceived?.Invoke(connId, lap, cp, finish);
                        break;
                    }
                }
            }
        }

        void ClientMessage(Msg type, byte[] payload)
        {
            using (var r = LanProtocol.Reader(payload))
            {
                switch (type)
                {
                    case Msg.Welcome:
                    {
                        LocalId = r.ReadInt32();
                        Seed = r.ReadInt32();
                        GameName = r.ReadString();
                        int n = r.ReadInt32();
                        players.Clear();
                        for (int i = 0; i < n; i++)
                        {
                            var p = PlayerInfo.Read(r);
                            players[p.id] = p;
                        }
                        me.id = LocalId;
                        JoinedGame?.Invoke();
                        foreach (var p in players.Values)
                            if (p.id != LocalId) PlayerAdded?.Invoke(p);
                        break;
                    }
                    case Msg.PlayerJoined:
                    {
                        var p = PlayerInfo.Read(r);
                        players[p.id] = p;
                        PlayerAdded?.Invoke(p);
                        break;
                    }
                    case Msg.PlayerLeft:
                    {
                        int id = r.ReadInt32();
                        if (players.Remove(id)) PlayerRemoved?.Invoke(id);
                        break;
                    }
                    case Msg.CarChanged:
                    {
                        var p = PlayerInfo.Read(r);
                        players[p.id] = p;
                        PlayerUpdated?.Invoke(p);
                        break;
                    }
                    case Msg.RaceStart:
                    {
                        string routeId = r.ReadString();
                        RaceStarted?.Invoke(routeId, ReadSlots(r));
                        break;
                    }
                    case Msg.RaceProgress:
                    {
                        int id = r.ReadInt32(), lap = r.ReadInt32(), cp = r.ReadInt32();
                        float finish = r.ReadSingle();
                        ProgressReceived?.Invoke(id, lap, cp, finish);
                        break;
                    }
                    case Msg.RaceCancel:
                        RaceCancelled?.Invoke();
                        break;
                    case Msg.Kick:
                        Disconnected?.Invoke(r.ReadString());
                        break;
                }
            }
        }

        static Dictionary<int, int> ReadSlots(System.IO.BinaryReader r)
        {
            int n = r.ReadInt32();
            var slots = new Dictionary<int, int>();
            for (int i = 0; i < n; i++) slots[r.ReadInt32()] = r.ReadInt32();
            return slots;
        }

        static byte[] ProgressFrame(int id, int lap, int cp, float finish) =>
            LanProtocol.Frame(Msg.RaceProgress, w => { w.Write(id); w.Write(lap); w.Write(cp); w.Write(finish); });

        // ------------------------------------------------------------------ sending

        /// <summary>Call every frame with the local car; sends its state 20 times a second.</summary>
        public void TickLocalState(CarController car)
        {
            if (!Joined || car == null) return;
            stateTimer -= Time.unscaledDeltaTime;
            if (stateTimer > 0f) return;
            stateTimer = 1f / StateRate;
            var s = new CarState
            {
                playerId = LocalId,
                seq = ++seq,
                position = car.transform.position,
                rotation = car.transform.rotation,
                velocity = car.Body != null && !car.Body.isKinematic ? car.Body.linearVelocity : Vector3.zero,
                steer = car.steerInput,
            };
            net.SendState(s.ToBytes(), LocalId);
        }

        public void SendCarChanged(string carId, string tuningJson)
        {
            me.carId = carId;
            me.tuningJson = tuningJson;
            if (!Joined) return;
            var frame = LanProtocol.Frame(Msg.CarChanged, w => me.Write(w));
            if (IsHost) net.Broadcast(frame);
            else net.SendToHost(frame);
        }

        /// <summary>Host: start a race for everybody. Returns the grid (playerId → slot).</summary>
        public Dictionary<int, int> HostStartRace(string routeId)
        {
            var slots = new Dictionary<int, int>();
            int n = 0;
            foreach (var id in players.Keys) slots[id] = n++;
            net.Broadcast(LanProtocol.Frame(Msg.RaceStart, w =>
            {
                w.Write(routeId);
                w.Write(slots.Count);
                foreach (var kv in slots) { w.Write(kv.Key); w.Write(kv.Value); }
            }));
            return slots;
        }

        public void HostCancelRace()
        {
            if (IsHost) net.Broadcast(LanProtocol.Frame(Msg.RaceCancel, null));
        }

        public void SendProgress(int lap, int nextCheckpoint, float finishTime)
        {
            if (!Joined) return;
            var frame = ProgressFrame(LocalId, lap, nextCheckpoint, finishTime);
            if (IsHost) net.Broadcast(frame);
            else net.SendToHost(frame);
        }

        public static string LocalAddressText()
        {
            var list = LanProtocol.LocalAddresses();
            return list.Count == 0 ? "нет сети" : string.Join(", ", list);
        }

        public void Dispose()
        {
            net?.Dispose();
            net = null;
        }
    }
}

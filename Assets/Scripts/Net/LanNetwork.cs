using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace RaceGame.Net
{
    public enum NetEventKind { Message, State, PeerDisconnected, ConnectionLost }

    public struct NetEvent
    {
        public NetEventKind kind;
        public int connId;      // host: which client; client: 0 (= host)
        public Msg type;
        public byte[] payload;
        public CarState state;
        public byte[] raw;      // raw UDP bytes (host relays them)
    }

    /// <summary>
    /// LAN transport on plain .NET sockets. Host: TCP listener + UDP socket. Client: TCP connection + UDP socket.
    /// Network threads only push events into a queue; the game reads them on the main thread.
    /// </summary>
    public class LanNetwork : IDisposable
    {
        class Conn
        {
            public int id;
            public TcpClient tcp;
            public NetworkStream stream;
            public readonly object writeLock = new object();
            public IPEndPoint udpEndpoint;
        }

        public bool IsHost { get; private set; }
        public bool Running { get; private set; }
        public readonly ConcurrentQueue<NetEvent> events = new ConcurrentQueue<NetEvent>();

        /// <summary>Host: builds the reply to a scanner's Query (called on a network thread).</summary>
        public Func<byte[]> infoReply;

        TcpListener listener;
        UdpClient udp;
        readonly List<Conn> conns = new List<Conn>();
        int nextConnId = 1;

        // client side
        Conn hostConn;
        IPEndPoint hostUdp;

        // ------------------------------------------------------------------ host

        public void StartHost()
        {
            IsHost = true;
            Running = true;
            listener = new TcpListener(IPAddress.Any, LanProtocol.TcpPort);
            listener.Start();
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, LanProtocol.UdpPort));
            new Thread(AcceptLoop) { IsBackground = true, Name = "LanAccept" }.Start();
            new Thread(UdpLoop) { IsBackground = true, Name = "LanUdp" }.Start();
        }

        void AcceptLoop()
        {
            while (Running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { break; }
                client.NoDelay = true;
                Conn c;
                lock (conns)
                {
                    c = new Conn { id = nextConnId++, tcp = client, stream = client.GetStream() };
                    conns.Add(c);
                }
                new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "LanRead" + c.id }.Start();
            }
        }

        void ReadLoop(Conn c)
        {
            try
            {
                while (Running && LanProtocol.ReadFrame(c.stream, out var type, out var payload))
                {
                    if (IsHost && type == Msg.Query)
                    {
                        // a scanner asking who we are: answer and hang up
                        var reply = infoReply?.Invoke();
                        if (reply != null) Write(c, reply);
                        break;
                    }
                    events.Enqueue(new NetEvent { kind = NetEventKind.Message, connId = c.id, type = type, payload = payload });
                }
            }
            catch (Exception) { }
            Drop(c);
        }

        void Drop(Conn c)
        {
            bool wasKnown;
            lock (conns) wasKnown = conns.Remove(c);
            try { c.tcp.Close(); } catch (Exception) { }
            if (!Running) return;
            if (IsHost && wasKnown) events.Enqueue(new NetEvent { kind = NetEventKind.PeerDisconnected, connId = c.id });
            if (!IsHost) events.Enqueue(new NetEvent { kind = NetEventKind.ConnectionLost });
        }

        public void SendTo(int connId, byte[] frame)
        {
            Conn c = null;
            lock (conns) foreach (var x in conns) if (x.id == connId) c = x;
            if (c != null) Write(c, frame);
        }

        public void Broadcast(byte[] frame, int exceptConnId = -1)
        {
            Conn[] list;
            lock (conns) list = conns.ToArray();
            foreach (var c in list)
                if (c.id != exceptConnId) Write(c, frame);
        }

        public void Disconnect(int connId)
        {
            Conn c = null;
            lock (conns) foreach (var x in conns) if (x.id == connId) c = x;
            if (c != null) Drop(c);
        }

        void Write(Conn c, byte[] frame)
        {
            try
            {
                lock (c.writeLock) c.stream.Write(frame, 0, frame.Length);
            }
            catch (Exception)
            {
                Drop(c);
            }
        }

        // ------------------------------------------------------------------ client

        /// <summary>Connects to a host (blocking, call from a worker thread or with a short timeout).</summary>
        public bool Connect(IPAddress address, int timeoutMs = 3000)
        {
            IsHost = false;
            var client = new TcpClient { NoDelay = true };
            try
            {
                var ar = client.BeginConnect(address, LanProtocol.TcpPort, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !client.Connected)
                {
                    client.Close();
                    return false;
                }
                client.EndConnect(ar);
            }
            catch (Exception)
            {
                client.Close();
                return false;
            }

            Running = true;
            hostConn = new Conn { id = 0, tcp = client, stream = client.GetStream() };
            lock (conns) conns.Add(hostConn);
            hostUdp = new IPEndPoint(address, LanProtocol.UdpPort);
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            new Thread(() => ReadLoop(hostConn)) { IsBackground = true, Name = "LanRead" }.Start();
            new Thread(UdpLoop) { IsBackground = true, Name = "LanUdp" }.Start();
            return true;
        }

        public void SendToHost(byte[] frame)
        {
            if (hostConn != null) Write(hostConn, frame);
        }

        // ------------------------------------------------------------------ UDP car states

        void UdpLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (Running)
            {
                byte[] data;
                try { data = udp.Receive(ref any); }
                catch (Exception) { if (!Running) break; continue; }
                if (!CarState.TryRead(data, data.Length, out var s)) continue;

                if (IsHost)
                {
                    // remember where this player's packets come from, so we can send states back
                    lock (conns)
                        foreach (var c in conns)
                            if (c.id == s.playerId) c.udpEndpoint = new IPEndPoint(any.Address, any.Port);
                }
                events.Enqueue(new NetEvent { kind = NetEventKind.State, connId = s.playerId, state = s, raw = data });
            }
        }

        /// <summary>Client: send own state to host. Host: send a state (own or relayed) to all clients except one.</summary>
        public void SendState(byte[] raw, int exceptPlayerId = -1)
        {
            if (udp == null) return;
            try
            {
                if (!IsHost)
                {
                    udp.Send(raw, raw.Length, hostUdp);
                    return;
                }
                IPEndPoint[] targets;
                lock (conns)
                {
                    var list = new List<IPEndPoint>();
                    foreach (var c in conns)
                        if (c.udpEndpoint != null && c.id != exceptPlayerId) list.Add(c.udpEndpoint);
                    targets = list.ToArray();
                }
                foreach (var ep in targets) udp.Send(raw, raw.Length, ep);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LAN] UDP send failed: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ shutdown

        public void Dispose()
        {
            Running = false;
            try { listener?.Stop(); } catch (Exception) { }
            try { udp?.Close(); } catch (Exception) { }
            Conn[] list;
            lock (conns) { list = conns.ToArray(); conns.Clear(); }
            foreach (var c in list) try { c.tcp.Close(); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ scanner

        public class FoundGame
        {
            public IPAddress address;
            public string name;
            public int players;
            public int version;
        }

        /// <summary>
        /// Looks for hosts on the local /24 networks by trying the TCP port on every address.
        /// Works without broadcast permissions (important on iOS). Blocking: run on a worker thread.
        /// </summary>
        public static List<FoundGame> Scan(int timeoutMs = 400)
        {
            var found = new List<FoundGame>();
            var targets = new List<IPAddress>();
            foreach (var local in LanProtocol.LocalAddresses())
            {
                var b = local.GetAddressBytes();
                for (int i = 1; i < 255; i++) targets.Add(new IPAddress(new byte[] { b[0], b[1], b[2], (byte)i }));
            }
            // probe in batches of 64 threads so phones are not flooded with threads
            const int batch = 64;
            for (int start = 0; start < targets.Count; start += batch)
            {
                var threads = new List<Thread>();
                for (int k = start; k < Mathf.Min(start + batch, targets.Count); k++)
                {
                    var target = targets[k];
                    var t = new Thread(() =>
                    {
                        var g = Probe(target, timeoutMs);
                        if (g != null) lock (found) found.Add(g);
                    }) { IsBackground = true };
                    threads.Add(t);
                    t.Start();
                }
                foreach (var t in threads) t.Join(timeoutMs + 1500);
            }
            lock (found) return new List<FoundGame>(found);
        }

        public static FoundGame Probe(IPAddress address, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(address, LanProtocol.TcpPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !client.Connected) return null;
                    client.EndConnect(ar);
                    var stream = client.GetStream();
                    stream.ReadTimeout = 1500;
                    var q = LanProtocol.Frame(Msg.Query, null);
                    stream.Write(q, 0, q.Length);
                    if (!LanProtocol.ReadFrame(stream, out var type, out var payload) || type != Msg.Info) return null;
                    using (var r = LanProtocol.Reader(payload))
                        return new FoundGame { address = address, version = r.ReadInt32(), name = r.ReadString(), players = r.ReadInt32() };
                }
            }
            catch (Exception) { return null; }
        }
    }
}

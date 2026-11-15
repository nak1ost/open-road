using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace RaceGame.Net
{
    public enum Msg : byte
    {
        Query = 1,        // scanner → host: who are you?
        Info = 2,         // host → scanner: game name, players
        Hello = 3,        // client → host: my name, car, tuning
        Welcome = 4,      // host → client: your id, map seed, all players
        PlayerJoined = 5, // host → all
        PlayerLeft = 6,   // host → all
        CarChanged = 7,   // any → host → others
        RaceStart = 8,    // host → all: route, grid
        RaceProgress = 9, // any → host → others: lap, checkpoint, finish time
        RaceCancel = 10,  // host → all
        Kick = 11,        // host → client: reason (version mismatch, full)
    }

    [Serializable]
    public class PlayerInfo
    {
        public int id;
        public string name;
        public string carId;
        public string tuningJson;

        public void Write(BinaryWriter w)
        {
            w.Write(id);
            w.Write(name ?? "");
            w.Write(carId ?? "");
            w.Write(tuningJson ?? "");
        }

        public static PlayerInfo Read(BinaryReader r) =>
            new PlayerInfo { id = r.ReadInt32(), name = r.ReadString(), carId = r.ReadString(), tuningJson = r.ReadString() };
    }

    /// <summary>Car state sent over UDP 20 times a second.</summary>
    public struct CarState
    {
        public const byte Marker = 0xCA;
        public const int Size = 1 + 4 + 4 + 12 + 16 + 12 + 4;

        public int playerId;
        public uint seq;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public float steer;

        public byte[] ToBytes()
        {
            var buf = new byte[Size];
            using (var w = new BinaryWriter(new MemoryStream(buf)))
            {
                w.Write(Marker);
                w.Write(playerId);
                w.Write(seq);
                w.Write(position.x); w.Write(position.y); w.Write(position.z);
                w.Write(rotation.x); w.Write(rotation.y); w.Write(rotation.z); w.Write(rotation.w);
                w.Write(velocity.x); w.Write(velocity.y); w.Write(velocity.z);
                w.Write(steer);
            }
            return buf;
        }

        public static bool TryRead(byte[] buf, int length, out CarState s)
        {
            s = default;
            if (length < Size || buf[0] != Marker) return false;
            using (var r = new BinaryReader(new MemoryStream(buf, 0, length)))
            {
                r.ReadByte();
                s.playerId = r.ReadInt32();
                s.seq = r.ReadUInt32();
                s.position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                s.rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                s.velocity = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                s.steer = r.ReadSingle();
            }
            return true;
        }
    }

    public static class LanProtocol
    {
        public const int Version = 1;
        public const int TcpPort = 47800;
        public const int UdpPort = 47801;
        public const int MaxPlayers = 8;

        /// <summary>Writes one framed message: [int length][byte type][payload].</summary>
        public static byte[] Frame(Msg type, Action<BinaryWriter> payload)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(0);           // length placeholder
                w.Write((byte)type);
                payload?.Invoke(w);
                w.Flush();
                int len = (int)ms.Length - 4;
                ms.Position = 0;
                w.Write(len);
                return ms.ToArray();
            }
        }

        /// <summary>Reads one framed message from a stream (blocking). Returns false when the connection closed.</summary>
        public static bool ReadFrame(Stream stream, out Msg type, out byte[] payload)
        {
            type = 0; payload = null;
            var header = new byte[4];
            if (!ReadExact(stream, header, 4)) return false;
            int len = BitConverter.ToInt32(header, 0);
            if (len < 1 || len > 1 << 20) return false;
            var body = new byte[len];
            if (!ReadExact(stream, body, len)) return false;
            type = (Msg)body[0];
            payload = new byte[len - 1];
            Buffer.BlockCopy(body, 1, payload, 0, len - 1);
            return true;
        }

        static bool ReadExact(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        public static BinaryReader Reader(byte[] payload) => new BinaryReader(new MemoryStream(payload));

        /// <summary>This device's IPv4 addresses on the local network (Wi-Fi / Ethernet).</summary>
        public static List<IPAddress> LocalAddresses()
        {
            var result = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(ua.Address))
                            result.Add(ua.Address);
                }
            }
            catch (Exception) { /* not supported on some platforms */ }
            if (result.Count == 0)
            {
                try
                {
                    foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                        if (a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a)) result.Add(a);
                }
                catch (Exception) { }
            }
            return result;
        }

        static bool IsPrivate(IPAddress a)
        {
            var b = a.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }
    }
}

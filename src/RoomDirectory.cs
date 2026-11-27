using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WheekKeys
{
    internal sealed class RoomDirectory : IDisposable
    {
        internal sealed class Entry
        {
            public string Code, Host, Name;
            public int Count;
            public double Seen;
            public string Label
            {
                get { return Count + "/" + RoomMembership.Capacity + " · " + (Name.Length == 0 ? "房间 " : Name + " · ") + Code.Substring(0, 5); }
            }
        }

        private readonly RoomTransport transport = new RoomTransport();
        private readonly Dictionary<string, Entry> rooms = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private double nextPublish;

        public bool Connected { get { return transport.Connected; } }
        public string Status { get { return transport.Status; } }

        public void Advertise(double now, string code, string host, int count, string nickname)
        {
            if (!Connected || now < nextPublish) return;
            nextPublish = now + 2;
            string name;
            if (!PetName.TryNormalize(nickname, out name)) name = "";
            transport.Publish("WD2|" + code + "|" + host + "|" + count.ToString(CultureInfo.InvariantCulture) + "|" + PetName.Encode(name));
        }

        public Entry[] Refresh(double now)
        {
            if (!Connected) rooms.Clear();
            foreach (string code in rooms.Where(pair => now - pair.Value.Seen >= 7).Select(pair => pair.Key).ToArray())
                rooms.Remove(code);
            string packet;
            for (int i = 0; i < 128 && transport.TryReceive(out packet); i++)
            {
                Entry entry = Parse(packet, now);
                if (entry != null && (rooms.Count < 64 || rooms.ContainsKey(entry.Code))) rooms[entry.Code] = entry;
            }
            return rooms.Values.OrderBy(entry => entry.Count == RoomMembership.Capacity)
                .ThenBy(entry => entry.Code, StringComparer.Ordinal).ToArray();
        }

        internal static Entry Parse(string packet, double now)
        {
            string[] fields = packet.Split('|');
            long count;
            string name;
            if (fields.Length != 5 || fields[0] != "WD2" || RoomTransport.NormalizeCode(fields[1]) != fields[1] ||
                !RoomMembership.ValidId(fields[2]) || !LocalPeers.TryCount(fields[3], out count) ||
                count < 1 || count > RoomMembership.Capacity || !PetName.TryDecode(fields[4], out name)) return null;
            return new Entry { Code = fields[1], Host = fields[2], Count = (int)count, Name = name, Seen = now };
        }

        public void Dispose() { transport.Dispose(); }
    }
}

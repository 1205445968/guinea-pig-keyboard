using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WheekKeys
{
    internal sealed class RoomMembership
    {
        public const int Capacity = 16;
        private const double Lifetime = 10;
        private readonly string self;
        private readonly bool hosting;
        private readonly Dictionary<string, double> guests = new Dictionary<string, double>(StringComparer.Ordinal);
        private string host;
        private long sequence;
        private double nextSend, lastRoster = double.NegativeInfinity;

        public RoomMembership(string self, bool hosting, bool isPublic, string expectedHost)
        {
            this.self = self;
            this.hosting = hosting;
            host = hosting ? self : expectedHost;
            IsPublic = isPublic;
            VisibilityKnown = hosting || expectedHost != null;
        }

        public bool IsPublic { get; private set; }
        public bool VisibilityKnown { get; private set; }
        public bool Joined { get { return hosting || guests.ContainsKey(self); } }
        public int Count { get { return host == null || (!hosting && double.IsNegativeInfinity(lastRoster)) ? 0 : guests.Count + 1; } }
        public bool Contains(string id) { return Joined && (host == id || guests.ContainsKey(id)); }

        internal static bool ValidId(string id)
        {
            return id.Length == 32 && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        public void Disconnected()
        {
            nextSend = 0;
            if (hosting) return;
            lastRoster = double.NegativeInfinity;
            guests.Clear();
        }

        public void Expire(double now)
        {
            if (!hosting)
            {
                if (!double.IsNegativeInfinity(lastRoster) && now - lastRoster >= Lifetime) Disconnected();
                return;
            }
            foreach (string id in guests.Where(entry => now - entry.Value >= Lifetime).Select(entry => entry.Key).ToArray())
            {
                guests.Remove(id);
                nextSend = 0;
            }
        }

        public string NextPacket(double now)
        {
            if (now < nextSend) return null;
            nextSend = now + 2;
            if (!hosting) return "WJ2|" + self;
            sequence++;
            return string.Join("|", new[] { "WM2", self, sequence.ToString(CultureInfo.InvariantCulture), IsPublic ? "1" : "0" }
                .Concat(guests.Keys));
        }

        public void Receive(string[] fields, double now)
        {
            if (fields.Length < 2 || fields[1] == self || !ValidId(fields[1])) return;
            if (hosting)
            {
                if (fields.Length != 2 || fields[0] != "WJ2") return;
                if (!guests.ContainsKey(fields[1]))
                {
                    if (guests.Count >= Capacity - 1) return;
                    nextSend = 0;
                }
                guests[fields[1]] = now;
                return;
            }
            long revision;
            if (fields[0] != "WM2" || fields.Length < 4 || fields.Length > Capacity + 3 ||
                (host != null && host != fields[1]) || !LocalPeers.TryCount(fields[2], out revision) ||
                revision <= sequence || (fields[3] != "0" && fields[3] != "1")) return;
            bool visible = fields[3] == "1";
            if (VisibilityKnown && IsPublic != visible) return;
            var members = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 4; i < fields.Length; i++)
                if (fields[i] == fields[1] || !ValidId(fields[i]) || !members.Add(fields[i])) return;
            guests.Clear();
            foreach (string id in members) guests.Add(id, now);
            host = fields[1];
            sequence = revision;
            lastRoster = now;
            IsPublic = visible;
            VisibilityKnown = true;
        }
    }
}

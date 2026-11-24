using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace WheekKeys
{
    internal sealed class LocalPeers : IDisposable
    {
        private const int Port = 41684;
        private readonly SortedDictionary<string, Peer> peers = new SortedDictionary<string, Peer>(StringComparer.Ordinal);
        private readonly byte[] buffer = new byte[129];
        private readonly List<IPEndPoint> targets = new List<IPEndPoint>();
        private readonly Queue<PeerAction> pendingActions = new Queue<PeerAction>();
        private Socket socket;
        private RoomTransport room;
        private readonly string session = Guid.NewGuid().ToString("N");
        private double nextSend, nextInterfaceCheck;
        private string problem;
        private double nextActionSend;
        private long actionSequence;
        private bool shareActivity = true;

        internal struct PeerAction
        {
            public char Kind;
            public int Scan;
            public double Received;
        }

        internal sealed class Peer
        {
            public PetSkin Skin;
            public long Keys, Clicks;
            public double Seen;
            public string Nickname = "";
            public bool SupportsActivity, SharingActivity;
            internal bool HasAppearance;
            internal long ActionSequence;
            internal readonly Queue<PeerAction> Actions = new Queue<PeerAction>();

            public bool TryTakeAction(double now, out PeerAction action)
            {
                while (Actions.Count > 0)
                {
                    action = Actions.Dequeue();
                    if (SharingActivity && now - action.Received <= 0.8) return true;
                }
                action = new PeerAction();
                return false;
            }
        }

        public bool Enabled { get { return socket != null || room != null; } }
        public string RoomCode { get; private set; }
        public bool RoomConnected { get { return room != null && room.Connected; } }
        public string RoomStatus { get { return room == null ? null : room.Status; } }
        public string Problem { get { return problem; } }
        public IReadOnlyDictionary<string, Peer> Members { get { return peers; } }
        public bool ShareActivity
        {
            get { return shareActivity; }
            set
            {
                if (shareActivity == value) return;
                shareActivity = value;
                pendingActions.Clear();
                RefreshState();
            }
        }

        public void RefreshState() { nextSend = 0; }

        public void RecordKey(int scan, double now)
        {
            if (scan > 0 && scan <= 511) RecordAction('k', scan, now);
        }

        public void RecordClick(bool right, double now) { RecordAction(right ? 'r' : 'l', 0, now); }

        private void RecordAction(char kind, int scan, double now)
        {
            if (!Enabled || !shareActivity || (room != null && !room.Connected)) return;
            if (pendingActions.Count >= 64) pendingActions.Dequeue();
            pendingActions.Enqueue(new PeerAction { Kind = kind, Scan = scan, Received = now });
        }

        public void EnterRoom(string code)
        {
            Dispose();
            room = new RoomTransport(code);
            RoomCode = code;
            nextSend = 0;
        }

        public void Start()
        {
            if (Enabled) return;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.EnableBroadcast = true;
                socket.Ttl = 1;
                socket.Bind(new IPEndPoint(IPAddress.Any, Port));
                socket.Blocking = false;
                nextSend = nextInterfaceCheck = 0;
                problem = null;
            }
            catch (SocketException e) { Fail(e); }
        }

        public void Dispose()
        {
            if (room != null) room.Dispose();
            room = null;
            RoomCode = null;
            if (socket != null) socket.Close();
            socket = null;
            peers.Clear();
            targets.Clear();
            pendingActions.Clear();
            nextActionSend = 0;
            problem = null;
        }

        private void Fail(SocketException error)
        {
            Dispose();
            problem = error.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? "联机端口已占用，请稍后重试" : "局域网连接失败，请重新开启";
        }

        public void Tick(double now, PetSkin skin, long keys, long clicks, string nickname)
        {
            if (!Enabled) return;
            double timeout = room == null ? 8 : 20;
            foreach (string id in peers.Where(p => now - p.Value.Seen >= timeout).Select(p => p.Key).ToArray())
                peers.Remove(id);
            if (room != null && !room.Connected)
            {
                peers.Clear();
                pendingActions.Clear();
                nextSend = 0;
                return;
            }
            if (now >= nextSend)
            {
                string message = string.Format(CultureInfo.InvariantCulture, "WK1|{0}|{1}|{2}|{3}", session, skin.NetworkId, keys, clicks);
                Publish(now, message);
                Publish(now, "WS1|" + session + "|" + skin.Id + "|" + (shareActivity ? "1" : "0"));
                string normalized;
                if (PetName.TryNormalize(nickname, out normalized) && normalized == nickname)
                {
                    string nameMessage = "WN1|" + session + "|" + PetName.Encode(nickname);
                    Publish(now, nameMessage);
                }
                nextSend = now + (room == null ? 2 : 5);
            }
            FlushActions(now);
            if (room != null)
            {
                string message;
                for (int i = 0; i < 32 && room.TryReceive(out message); i++) ReadPeer(message, now);
                return;
            }
            for (int i = 0; i < 32; i++)
            {
                try
                {
                    if (!socket.Poll(0, SelectMode.SelectRead)) break;
                    int size = socket.Receive(buffer);
                    if (size <= 128) ReadPeer(Encoding.ASCII.GetString(buffer, 0, size), now);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.MessageSize || e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    if (e.SocketErrorCode != SocketError.WouldBlock) Fail(e);
                    break;
                }
            }
        }

        private void Publish(double now, string message)
        {
            if (room == null) Broadcast(now, message);
            else room.Publish(message);
        }

        private void FlushActions(double now)
        {
            if (!shareActivity || now < nextActionSend || pendingActions.Count == 0 || actionSequence == long.MaxValue) return;
            List<string> actions = new List<string>();
            while (pendingActions.Count > 0 && actions.Count < 12)
            {
                PeerAction action = pendingActions.Dequeue();
                if (now - action.Received > 0.8) continue;
                actions.Add(action.Kind == 'k' ? "k" + action.Scan.ToString(CultureInfo.InvariantCulture) : action.Kind.ToString());
            }
            if (actions.Count == 0) return;
            actionSequence++;
            Publish(now, "WA1|" + session + "|" + actionSequence.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(",", actions));
            nextActionSend = now + 0.03;
        }

        private void Broadcast(double now, string message)
        {
            if (now >= nextInterfaceCheck)
            {
                FindBroadcastTargets();
                nextInterfaceCheck = now + 10;
            }
            byte[] packet = Encoding.ASCII.GetBytes(message);
            bool sent = false;
            foreach (IPEndPoint target in targets)
            {
                try { socket.SendTo(packet, target); sent = true; }
                catch (SocketException) { }
            }
            problem = sent ? null : "等待可用的局域网连接…";
        }

        private void FindBroadcastTargets()
        {
            targets.Clear();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    foreach (UnicastIPAddressInformation ip in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork || ip.IPv4Mask == null) continue;
                        byte[] addr = ip.Address.GetAddressBytes(), mask = ip.IPv4Mask.GetAddressBytes();
                        if (mask[0] == 0 || mask[3] >= 254) continue;
                        for (int i = 0; i < addr.Length; i++) addr[i] |= (byte)~mask[i];
                        IPEndPoint target = new IPEndPoint(new IPAddress(addr), Port);
                        if (!targets.Contains(target)) targets.Add(target);
                    }
                }
            }
            catch (NetworkInformationException) { }
        }

        private void ReadPeer(string message, double now)
        {
            if (message.Length > 128) return;
            string[] fields = message.Split('|');
            if (fields.Length == 4 && (fields[0] == "WS1" || fields[0] == "WA1"))
            {
                Peer existing;
                if (fields[1] == session || !ValidId(fields[1]) || !peers.TryGetValue(fields[1], out existing)) return;
                if (fields[0] == "WS1")
                {
                    if (fields[3] != "0" && fields[3] != "1") return;
                    PetSkin exact = PetSkin.Find(fields[2]);
                    if (exact != null) { existing.Skin = exact; existing.HasAppearance = true; }
                    existing.SupportsActivity = true;
                    existing.SharingActivity = fields[3] == "1";
                    if (!existing.SharingActivity) existing.Actions.Clear();
                    existing.Seen = now;
                }
                else ReadActions(fields, existing, now);
                return;
            }
            if (fields.Length == 3 && fields[0] == "WN1")
            {
                string nameId = fields[1];
                string name;
                Peer existing;
                if (message.Length <= 125 && nameId != session && ValidId(nameId) &&
                    peers.TryGetValue(nameId, out existing) && PetName.TryDecode(fields[2], out name))
                    existing.Nickname = name;
                return;
            }
            if (fields.Length != 5 || fields[0] != "WK1") return;
            string id = fields[1];
            if (id == session || !ValidId(id)) return;
            PetSkin skin = PetSkin.Find(fields[2]);
            long keys, clicks;
            if (skin == null || !TryCount(fields[3], out keys) || !TryCount(fields[4], out clicks)) return;
            Peer peer;
            if (!peers.TryGetValue(id, out peer))
            {
                if (peers.Count >= 64) return;
                peers.Add(id, peer = new Peer());
            }
            peer.Seen = now;
            if (!peer.HasAppearance) peer.Skin = skin;
            peer.Keys = keys;
            peer.Clicks = clicks;
        }

        private static void ReadActions(string[] fields, Peer peer, double now)
        {
            long sequence;
            if (!peer.SharingActivity || !TryCount(fields[2], out sequence) || sequence <= peer.ActionSequence) return;
            string[] tokens = fields[3].Split(',');
            if (tokens.Length == 0 || tokens.Length > 12) return;
            List<PeerAction> actions = new List<PeerAction>();
            foreach (string token in tokens)
            {
                long scan;
                if (token == "l" || token == "r") actions.Add(new PeerAction { Kind = token[0], Received = now });
                else if (token.Length >= 2 && token[0] == 'k' && TryCount(token.Substring(1), out scan) && scan > 0 && scan <= 511)
                    actions.Add(new PeerAction { Kind = 'k', Scan = (int)scan, Received = now });
                else return;
            }
            peer.ActionSequence = sequence;
            peer.Seen = now;
            foreach (PeerAction action in actions)
            {
                if (peer.Actions.Count >= 64) peer.Actions.Dequeue();
                peer.Actions.Enqueue(action);
            }
        }

        private static bool ValidId(string id)
        {
            return id.Length == 32 && id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        internal static bool TryCount(string value, out long result)
        {
            result = 0;
            return value.Length > 0 && value.All(c => c >= '0' && c <= '9')
                && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }
    }
}

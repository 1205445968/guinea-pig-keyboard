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
        private Socket socket;
        private readonly string session = Guid.NewGuid().ToString("N");
        private double nextSend, nextInterfaceCheck;
        private string problem;

        private sealed class Peer
        {
            public string Label;
            public double Seen;
        }

        public bool Enabled { get { return socket != null; } }
        public string Summary
        {
            get
            {
                if (!string.IsNullOrEmpty(problem)) return problem;
                if (!Enabled) return "";
                if (peers.Count == 0) return "等待局域网伙伴…";
                return string.Join("\n", peers.Values.Select(p => p.Label));
            }
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
            if (socket != null) socket.Close();
            socket = null;
            peers.Clear();
            targets.Clear();
            problem = null;
        }

        private void Fail(SocketException error)
        {
            Dispose();
            problem = error.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? "联机端口已占用，请稍后重试" : "局域网连接失败，请重新开启";
        }

        public void Tick(double now, PetSkin skin, long keys, long clicks)
        {
            if (!Enabled) return;
            if (now >= nextSend)
            {
                if (now >= nextInterfaceCheck)
                {
                    FindBroadcastTargets();
                    nextInterfaceCheck = now + 10;
                }
                byte[] packet = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                    "WK1|{0}|{1}|{2}|{3}", session, skin.Id, keys, clicks));
                bool sent = false;
                foreach (IPEndPoint target in targets)
                {
                    try { socket.SendTo(packet, target); sent = true; }
                    catch (SocketException) { }
                }
                problem = sent ? null : "等待可用的局域网连接…";
                nextSend = now + 2;
            }

            foreach (string id in peers.Where(p => now - p.Value.Seen >= 8).Select(p => p.Key).ToArray())
                peers.Remove(id);
            // Bound work on the UI thread even if the socket is continuously readable.
            for (int i = 0; i < 32; i++)
            {
                try
                {
                    if (!socket.Poll(0, SelectMode.SelectRead)) break;
                    int size = socket.Receive(buffer);
                    if (size <= 128) ReadPeer(size, now);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.MessageSize || e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    if (e.SocketErrorCode != SocketError.WouldBlock) Fail(e);
                    break;
                }
            }
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

        private void ReadPeer(int size, double now)
        {
            string[] fields = Encoding.ASCII.GetString(buffer, 0, size).Split('|');
            if (fields.Length != 5 || fields[0] != "WK1") return;
            string id = fields[1];
            if (id == session || id.Length != 32 || !id.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return;
            PetSkin skin = PetSkin.FromId(fields[2]);
            long keys, clicks;
            if (skin.Id != fields[2] || !TryCount(fields[3], out keys) || !TryCount(fields[4], out clicks)) return;
            Peer peer;
            if (!peers.TryGetValue(id, out peer))
            {
                if (peers.Count >= 64) return;
                peers.Add(id, peer = new Peer());
            }
            peer.Seen = now;
            peer.Label = string.Format("{0} · {1}　按键 {2}　点击 {3}", skin.Name, id.Substring(0, 4), keys, clicks);
        }

        internal static bool TryCount(string value, out long result)
        {
            result = 0;
            return value.Length > 0 && value.All(c => c >= '0' && c <= '9')
                && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }
    }
}

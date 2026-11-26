using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Collections.Generic;

internal static class FriendNetworkSmokeTest
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: FriendNetworkSmokeTest.exe WheekKeys.exe"); return 2; }
        Assembly assembly = Assembly.LoadFile(System.IO.Path.GetFullPath(args[0]));
        Type networkType = assembly.GetType("WheekKeys.LocalPeers", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        object a = Activator.CreateInstance(networkType, true), b = Activator.CreateInstance(networkType, true);
        Socket sa = BoundSocket(), sb = BoundSocket();
        try
        {
            Set(networkType, a, "socket", sa); Set(networkType, b, "socket", sb);
            SetTargets(networkType, a, (IPEndPoint)sb.LocalEndPoint); SetTargets(networkType, b, (IPEndPoint)sa.LocalEndPoint);
            Set(networkType, a, "nextInterfaceCheck", double.MaxValue); Set(networkType, b, "nextInterfaceCheck", double.MaxValue);
            object[] skins = (object[])skinType.GetField("All").GetValue(null);
            object skin = null;
            foreach (object candidate in skins) if ((string)skinType.GetField("Id").GetValue(candidate) == "pixel-longhair-chocolate") skin = candidate;
            Check(skin != null, "test skin exists"); string skinId = "pixel-longhair-chocolate";
            MethodInfo tick = networkType.GetMethod("Tick");
            tick.Invoke(a, new object[] { 1.0, skin, 31L, 7L, "花生" });
            while (sb.Poll(0, SelectMode.SelectRead)) sb.Receive(new byte[129]);
            tick.Invoke(b, new object[] { 1.1, skin, 0L, 0L, "小豆" });
            tick.Invoke(a, new object[] { 1.1, skin, 31L, 7L, "花生" });
            tick.Invoke(a, new object[] { 1.13, skin, 31L, 7L, "花生" });
            tick.Invoke(b, new object[] { 1.13, skin, 0L, 0L, "小豆" });
            IDictionary peers = (IDictionary)networkType.GetProperty("Members").GetValue(b, null);
            Check(peers.Count == 1, "real UDP peer discovery");
            object peer = null; foreach (DictionaryEntry item in peers) peer = item.Value;
            Check((string)skinType.GetField("Id").GetValue(peer.GetType().GetField("Skin").GetValue(peer)) == skinId, "exact skin update");
            Check((long)peer.GetType().GetField("Keys").GetValue(peer) == 31L && (long)peer.GetType().GetField("Clicks").GetValue(peer) == 7L, "legacy WK1 counters");
            Check((string)peer.GetType().GetField("Nickname").GetValue(peer) == "花生", "WN1 nickname");
            MethodInfo recordKey = networkType.GetMethod("RecordKey"), recordClick = networkType.GetMethod("RecordClick");
            recordKey.Invoke(a, new object[] { 30, 1.2 }); recordKey.Invoke(a, new object[] { 31, 1.2 });
            recordClick.Invoke(a, new object[] { false, 1.2 }); recordClick.Invoke(a, new object[] { true, 1.2 });
            tick.Invoke(a, new object[] { 1.2, skin, 31L, 7L, "花生" }); tick.Invoke(b, new object[] { 1.2, skin, 0L, 0L, "小豆" });
            MethodInfo take = peer.GetType().GetMethod("TryTakeAction"); string[] expected = { "k30", "k31", "l", "r" };
            Check(Take(take, peer, 1.2) == expected[0] && Take(take, peer, 1.23) == null, "batch leaves key animation visible");
            Check(Take(take, peer, 1.26) == expected[1] && Take(take, peer, 1.32) == expected[2], "keys and left mouse replay in order");
            Check(Take(take, peer, 1.45) == null && Take(take, peer, 1.51) == expected[3], "clicks have separate animation time");
            Check(Take(take, peer, 1.68) == null, "no extra action while click finishes");

            object reversePeer = null;
            foreach (DictionaryEntry item in (IDictionary)networkType.GetProperty("Members").GetValue(a, null)) reversePeer = item.Value;
            Check(reversePeer != null, "both sides discovered after staggered join");
            MethodInfo read = networkType.GetMethod("ReadPeer", PrivateInstance);
            string session = (string)networkType.GetField("session", PrivateInstance).GetValue(a);
            int queued = QueueCount(peer);
            long nextSequence = (long)peer.GetType().GetField("ActionSequence", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(peer) + 1;
            read.Invoke(b, new object[] { "WA1|" + session + "|" + nextSequence + "|k30", 1.3 });
            read.Invoke(b, new object[] { "WA1|" + session + "|" + nextSequence + "|k31", 1.3 });
            read.Invoke(b, new object[] { "WA1|" + session + "|" + (nextSequence - 1) + "|k31", 1.3 });
            Check(QueueCount(peer) == queued + 1 && Take(take, peer, 1.7) == "k30", "duplicate and out-of-order sequences ignored");
            Reject(read, b, "WA1|ffffffffffffffffffffffffffffffff|9|k30", 1.4, peer, "unknown session");
            Reject(read, b, "WA1|" + session + "|" + (nextSequence + 1) + "|k999", 1.4, peer, "invalid scan");
            Reject(read, b, "WA1|" + session + "|" + (nextSequence + 1) + "|l,l,l,l,l,l,l,l,l,l,l,l,l", 1.4, peer, "too many actions");
            Reject(read, b, "WA1|" + session + "|" + (nextSequence + 1) + "|" + new string('x', 130), 1.4, peer, "oversized message");
            read.Invoke(b, new object[] { "WS1|" + session + "|" + skinId + "|0", 1.5 });
            Check(QueueCount(peer) == 0, "sharing disabled clears queue");
            read.Invoke(b, new object[] { "WA1|" + session + "|3|l", 1.5 });
            Check(QueueCount(peer) == 0, "sharing disabled rejects actions");
            read.Invoke(b, new object[] { "WS1|" + session + "|" + skinId + "|1", 1.6 });
            read.Invoke(b, new object[] { "WA1|" + session + "|4|l", 1.6 });
            Check(Take(take, peer, 2.5) == null, "expired action discarded");
            recordClick.Invoke(b, new object[] { true, 2.5 });
            recordKey.Invoke(b, new object[] { 328, 2.5 });
            tick.Invoke(b, new object[] { 2.5, skin, 0L, 0L, "小豆" });
            tick.Invoke(a, new object[] { 2.5, skin, 31L, 7L, "花生" });
            Check(Take(take, reversePeer, 2.5) == "r" && Take(take, reversePeer, 2.65) == null &&
                Take(take, reversePeer, 2.7) == "k328", "reverse UDP click then extended key");
            networkType.GetProperty("ShareActivity").SetValue(a, false, null);
            recordKey.Invoke(a, new object[] { 30, 2.6 });
            Check(PendingCount(a, networkType) == 0, "local sharing disabled does not queue recording");
            tick.Invoke(a, new object[] { 2.6, skin, 32L, 7L, "花生" });
            tick.Invoke(b, new object[] { 2.6, skin, 0L, 0L, "小豆" });
            Check(!(bool)peer.GetType().GetField("SharingActivity").GetValue(peer), "pause propagates to the other peer");
            Set(networkType, a, "actionSequence", 4L);
            networkType.GetProperty("ShareActivity").SetValue(a, true, null);
            recordKey.Invoke(a, new object[] { 30, 2.8 });
            tick.Invoke(a, new object[] { 2.8, skin, 33L, 7L, "花生" });
            tick.Invoke(b, new object[] { 2.8, skin, 0L, 0L, "小豆" });
            Check((bool)peer.GetType().GetField("SharingActivity").GetValue(peer) && Take(take, peer, 2.8) == "k30", "resume propagates and the first new action arrives");
            ((IDisposable)b).Dispose(); Check(((IDictionary)networkType.GetProperty("Members").GetValue(b, null)).Count == 0 && QueueCount(peer) == 0, "dispose clears peer state and actions");
            Console.WriteLine("PASS: late UDP join, bidirectional keys/clicks, paced replay, order, pause/resume and disposal");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
        finally { ((IDisposable)a).Dispose(); ((IDisposable)b).Dispose(); }
    }

    private static Socket BoundSocket()
    {
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Bind(new IPEndPoint(IPAddress.Loopback, 0)); s.Blocking = false; return s;
    }
    private static void Set(Type t, object o, string name, object value) { t.GetField(name, PrivateInstance).SetValue(o, value); }
    private static void SetTargets(Type t, object o, IPEndPoint endpoint)
    {
        IList list = (IList)t.GetField("targets", PrivateInstance).GetValue(o); list.Add(endpoint);
    }
    private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
    private static string Take(MethodInfo method, object peer, double now)
    {
        object[] args = { now, null }; if (!(bool)method.Invoke(peer, args)) return null;
        object action = args[1]; return (char)action.GetType().GetField("Kind").GetValue(action) == 'k'
            ? "k" + action.GetType().GetField("Scan").GetValue(action) : action.GetType().GetField("Kind").GetValue(action).ToString();
    }
    private static int QueueCount(object peer) { return ((ICollection)peer.GetType().GetField("Actions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(peer)).Count; }
    private static int PendingCount(object network, Type type) { return ((ICollection)type.GetField("pendingActions", PrivateInstance).GetValue(network)).Count; }
    private static void Reject(MethodInfo read, object network, string message, double now, object peer, string label)
    {
        int count = QueueCount(peer); read.Invoke(network, new object[] { message, now }); Check(QueueCount(peer) == count, label + " rejected");
    }
}

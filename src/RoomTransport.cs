using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WheekKeys
{
    internal sealed class RoomTransport : IDisposable
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const int MaxPacket = 8192;
        private const int MaxQueue = 128;
        private static readonly Uri DefaultBroker = new Uri("wss://broker.emqx.io:8084/mqtt");
        private readonly object stateLock = new object();
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly Queue<string> incoming = new Queue<string>();
        private readonly Queue<string> outgoing = new Queue<string>();
        private readonly SemaphoreSlim outgoingReady = new SemaphoreSlim(0, MaxQueue);
        private readonly byte[] key;
        private readonly string topic;
        private readonly Uri broker;
        private readonly Task worker;
        private long lastPacketTicks;
        private bool connected;
        private bool disposed;
        private string status = "连接中";

        public RoomTransport() : this(null, ConfiguredBroker()) { }
        public RoomTransport(string normalizedCode) : this(normalizedCode, ConfiguredBroker()) { }

        public RoomTransport(string normalizedCode, Uri brokerUri)
        {
            if (normalizedCode != null && NormalizeCode(normalizedCode) != normalizedCode)
                throw new ArgumentException("Invalid room code.", "normalizedCode");
            broker = brokerUri;
            key = Hash("WheekKeys/v2/fernet/" + (normalizedCode ?? "directory"));
            topic = normalizedCode == null ? "wheekkeys/v2/directory" :
                "wheekkeys/v2/r/" + ToHex(Hash("WheekKeys/v2/topic/" + normalizedCode));
            if (!ValidBroker(broker))
                status = "中转地址配置错误";
            else
                worker = Task.Run((Func<Task>)RunAsync);
        }

        private static Uri ConfiguredBroker()
        {
            string value = Environment.GetEnvironmentVariable("WHEEKKEYS_ROOM_BROKER");
            if (String.IsNullOrEmpty(value)) return DefaultBroker;
            Uri uri;
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) ? uri : null;
        }

        private static bool ValidBroker(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && String.IsNullOrEmpty(uri.UserInfo) &&
                String.IsNullOrEmpty(uri.Fragment) &&
                (uri.Scheme == "wss" || (uri.Scheme == "ws" && uri.IsLoopback));
        }

        public bool Connected { get { lock (stateLock) return connected; } }
        public string Status { get { lock (stateLock) return status; } }

        public static string CreateCode()
        {
            byte[] random = new byte[10];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            string prefix = Base32(random).Substring(0, 16);
            return prefix + Base32(Hash(prefix)).Substring(0, 4);
        }

        public static string NormalizeCode(string input)
        {
            if (input == null) return null;
            StringBuilder value = new StringBuilder(20);
            foreach (char raw in input)
            {
                if (raw == ' ' || raw == '-') continue;
                char c = raw >= 'a' && raw <= 'z' ? (char)(raw - 32) : raw;
                if (Alphabet.IndexOf(c) < 0 || value.Length == 20) return null;
                value.Append(c);
            }
            if (value.Length != 20) return null;
            string result = value.ToString();
            return result.Substring(16) == Base32(Hash(result.Substring(0, 16))).Substring(0, 4) ? result : null;
        }

        public void Publish(string plaintext)
        {
            if (!ValidPlaintext(plaintext)) return;
            lock (stateLock)
            {
                if (disposed || !connected || outgoing.Count >= MaxQueue) return;
                outgoing.Enqueue(plaintext);
                outgoingReady.Release();
            }
        }

        public bool TryReceive(out string plaintext)
        {
            lock (stateLock)
            {
                if (!disposed && incoming.Count != 0)
                {
                    plaintext = incoming.Dequeue();
                    return true;
                }
            }
            plaintext = null;
            return false;
        }

        public void Dispose()
        {
            lock (stateLock)
            {
                if (disposed) return;
                disposed = true;
                connected = false;
                status = "已断开";
                incoming.Clear();
                outgoing.Clear();
            }
            shutdown.Cancel();
            if (worker == null)
            {
                outgoingReady.Dispose();
                shutdown.Dispose();
            }
            else
                worker.ContinueWith(delegate { outgoingReady.Dispose(); shutdown.Dispose(); }, TaskScheduler.Default);
        }

        private async Task RunAsync()
        {
            int delay = 1000;
            while (!shutdown.IsCancellationRequested)
            {
                SetState(false, delay == 1000 ? "连接中" : "连接失败，正在重连");
                try
                {
                    using (ClientWebSocket socket = new ClientWebSocket())
                    using (CancellationTokenSource session = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token))
                    using (SemaphoreSlim sendLock = new SemaphoreSlim(1, 1))
                    {
                        socket.Options.AddSubProtocol("mqtt");
                        session.CancelAfter(TimeSpan.FromSeconds(12));
                        await socket.ConnectAsync(broker, session.Token).ConfigureAwait(false);
                        await SendPacket(socket, sendLock, ConnectPacket(), session.Token).ConfigureAwait(false);
                        PacketReader reader = new PacketReader(socket);
                        byte[] connack = await reader.ReadAsync(session.Token).ConfigureAwait(false);
                        if (connack.Length != 4 || connack[0] != 0x20 || connack[1] != 2 || connack[2] != 0 || connack[3] != 0)
                            throw new InvalidDataException("MQTT CONNACK failed.");
                        await SendPacket(socket, sendLock, SubscribePacket(), session.Token).ConfigureAwait(false);
                        byte[] suback = await reader.ReadAsync(session.Token).ConfigureAwait(false);
                        // A broker can deliver room traffic before acknowledging the subscription.
                        while ((suback[0] >> 4) == 3)
                            suback = await reader.ReadAsync(session.Token).ConfigureAwait(false);
                        if (suback.Length != 5 || suback[0] != 0x90 || suback[1] != 3 || suback[2] != 0 || suback[3] != 1 || suback[4] != 0)
                            throw new InvalidDataException("MQTT SUBACK failed.");
                        session.CancelAfter(Timeout.Infinite);
                        Interlocked.Exchange(ref lastPacketTicks, DateTime.UtcNow.Ticks);
                        if (!SetState(true, "已连接")) break;
                        delay = 1000;
                        Task receive = ReceiveLoop(reader, session.Token);
                        Task send = SendLoop(socket, sendLock, session.Token);
                        Task ping = PingLoop(socket, sendLock, session.Token);
                        await Task.WhenAny(receive, send, ping).ConfigureAwait(false);
                        session.Cancel();
                        socket.Abort();
                        try { await Task.WhenAll(receive, send, ping).ConfigureAwait(false); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
                DisconnectAndClear();
                if (shutdown.IsCancellationRequested) break;
                try { await Task.Delay(delay, shutdown.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                delay = Math.Min(delay * 2, 30000);
            }
        }

        private bool SetState(bool value, string text)
        {
            lock (stateLock)
            {
                if (disposed) return false;
                connected = value;
                status = text;
                return true;
            }
        }

        private void DisconnectAndClear()
        {
            lock (stateLock)
            {
                if (!disposed)
                {
                    connected = false;
                    status = "连接失败，正在重连";
                }
                incoming.Clear();
                outgoing.Clear();
                while (outgoingReady.Wait(0)) { }
            }
        }

        private async Task SendLoop(ClientWebSocket socket, SemaphoreSlim sendLock, CancellationToken token)
        {
            while (true)
            {
                await outgoingReady.WaitAsync(token).ConfigureAwait(false);
                string value;
                lock (stateLock)
                {
                    if (outgoing.Count == 0) continue;
                    value = outgoing.Dequeue();
                }
                byte[] payload = Encoding.ASCII.GetBytes(Encrypt(value));
                using (MemoryStream body = new MemoryStream())
                {
                    WriteString(body, topic);
                    body.Write(payload, 0, payload.Length);
                    await SendPacket(socket, sendLock, Packet(0x30, body.ToArray()), token).ConfigureAwait(false);
                }
            }
        }

        private async Task PingLoop(ClientWebSocket socket, SemaphoreSlim sendLock, CancellationToken token)
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false);
                if (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastPacketTicks) > TimeSpan.FromSeconds(45).Ticks)
                    throw new IOException("MQTT heartbeat timed out.");
                await SendPacket(socket, sendLock, new byte[] { 0xc0, 0 }, token).ConfigureAwait(false);
            }
        }

        private async Task ReceiveLoop(PacketReader reader, CancellationToken token)
        {
            while (true)
            {
                byte[] packet = await reader.ReadAsync(token).ConfigureAwait(false);
                Interlocked.Exchange(ref lastPacketTicks, DateTime.UtcNow.Ticks);
                int offset = 1;
                int length = ReadRemaining(packet, ref offset, packet.Length);
                if (length < 0 || offset + length != packet.Length) throw new InvalidDataException();
                if (packet[0] == 0xd0 && length == 0) continue;
                if (packet[0] != 0x30 || length < 2) continue;
                int topicLength = packet[offset] * 256 + packet[offset + 1];
                offset += 2;
                if (topicLength > packet.Length - offset) throw new InvalidDataException();
                string receivedTopic = Encoding.UTF8.GetString(packet, offset, topicLength);
                offset += topicLength;
                if (receivedTopic != topic || packet.Length - offset > 2048) continue;
                string plaintext = Decrypt(Encoding.ASCII.GetString(packet, offset, packet.Length - offset));
                if (!ValidPlaintext(plaintext)) continue;
                lock (stateLock)
                {
                    if (!disposed && connected && incoming.Count < MaxQueue)
                    {
                        incoming.Enqueue(plaintext);
                    }
                }
            }
        }

        private string Encrypt(string plaintext)
        {
            byte[] iv = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(iv);
            byte[] ciphertext;
            using (Aes aes = Aes.Create())
            {
                aes.Key = Slice(key, 16, 16);
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] plain = Encoding.ASCII.GetBytes(plaintext);
                    ciphertext = enc.TransformFinalBlock(plain, 0, plain.Length);
                }
            }
            byte[] data = new byte[25 + ciphertext.Length + 32];
            data[0] = 0x80;
            long seconds = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            for (int i = 8; i >= 1; i--)
            {
                data[i] = (byte)seconds;
                seconds >>= 8;
            }
            Buffer.BlockCopy(iv, 0, data, 9, 16);
            Buffer.BlockCopy(ciphertext, 0, data, 25, ciphertext.Length);
            using (HMACSHA256 mac = new HMACSHA256(Slice(key, 0, 16)))
                Buffer.BlockCopy(mac.ComputeHash(data, 0, data.Length - 32), 0, data, data.Length - 32, 32);
            return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_');
        }

        private string Decrypt(string token)
        {
            try
            {
                if (token.Length == 0 || token.Length % 4 != 0) return null;
                foreach (char c in token)
                    if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '=') return null;
                byte[] data = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/'));
                if (data.Length < 73 || data[0] != 0x80 || (data.Length - 57) % 16 != 0) return null;
                byte[] expected;
                using (HMACSHA256 mac = new HMACSHA256(Slice(key, 0, 16))) expected = mac.ComputeHash(data, 0, data.Length - 32);
                int diff = 0;
                for (int i = 0; i < 32; i++) diff |= expected[i] ^ data[data.Length - 32 + i];
                if (diff != 0) return null;
                ulong seconds = 0;
                for (int i = 1; i <= 8; i++) seconds = (seconds << 8) | data[i];
                long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                if (seconds > (ulong)(now + 120) || seconds < (ulong)(now - 120)) return null;
                using (Aes aes = Aes.Create())
                {
                    aes.Key = Slice(key, 16, 16);
                    aes.IV = Slice(data, 9, 16);
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (ICryptoTransform dec = aes.CreateDecryptor())
                    {
                        byte[] plain = dec.TransformFinalBlock(data, 25, data.Length - 57);
                        foreach (byte b in plain) if (b > 127) return null;
                        return Encoding.ASCII.GetString(plain);
                    }
                }
            }
            catch (FormatException) { return null; }
            catch (CryptographicException) { return null; }
        }

        private static bool ValidPlaintext(string value)
        {
            if (String.IsNullOrEmpty(value) ||
                value.Length > (value.StartsWith("WM2|", StringComparison.Ordinal) ? 560 : value.StartsWith("WD2|", StringComparison.Ordinal) ? 150 : 128)) return false;
            foreach (char c in value) if (c > 127) return false;
            return true;
        }

        private static byte[] Hash(string value)
        {
            using (SHA256 hash = SHA256.Create()) return hash.ComputeHash(Encoding.ASCII.GetBytes(value));
        }
        private static byte[] Slice(byte[] input, int offset, int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(input, offset, result, 0, count);
            return result;
        }
        private static string ToHex(byte[] bytes)
        {
            StringBuilder s = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) s.Append(b.ToString("x2"));
            return s.ToString();
        }
        private static string Base32(byte[] bytes)
        {
            StringBuilder result = new StringBuilder();
            int bits = 0, buffer = 0;
            foreach (byte b in bytes)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    result.Append(Alphabet[(buffer >> bits) & 31]);
                }
            }
            if (bits > 0) result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            return result.ToString();
        }
        private byte[] ConnectPacket()
        {
            using (MemoryStream b = new MemoryStream())
            {
                WriteString(b, "MQTT");
                b.WriteByte(4);
                b.WriteByte(2);
                b.WriteByte(0);
                b.WriteByte(30);
                WriteString(b, "wk" + Guid.NewGuid().ToString("N").Substring(0, 21));
                return Packet(0x10, b.ToArray());
            }
        }
        private byte[] SubscribePacket()
        {
            using (MemoryStream b = new MemoryStream())
            {
                b.WriteByte(0);
                b.WriteByte(1);
                WriteString(b, topic);
                b.WriteByte(0);
                return Packet(0x82, b.ToArray());
            }
        }
        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.WriteByte((byte)(bytes.Length >> 8));
            stream.WriteByte((byte)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
        private static byte[] Packet(byte header, byte[] body)
        {
            using (MemoryStream packet = new MemoryStream())
            {
                packet.WriteByte(header);
                int length = body.Length;
                do
                {
                    byte b = (byte)(length % 128);
                    length /= 128;
                    if (length > 0) b |= 0x80;
                    packet.WriteByte(b);
                } while (length > 0);
                packet.Write(body, 0, body.Length);
                return packet.ToArray();
            }
        }
        private static int ReadRemaining(byte[] packet, ref int offset, int limit)
        {
            int length = 0, multiplier = 1;
            for (int i = 0; i < 4 && offset < limit; i++)
            {
                byte b = packet[offset++];
                length += (b & 127) * multiplier;
                if ((b & 128) == 0) return length;
                multiplier *= 128;
            }
            return -1;
        }
        private static async Task SendPacket(ClientWebSocket socket, SemaphoreSlim sendLock, byte[] packet, CancellationToken token)
        {
            await sendLock.WaitAsync(token).ConfigureAwait(false);
            try { await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, token).ConfigureAwait(false); }
            finally { sendLock.Release(); }
        }
        private sealed class PacketReader
        {
            private readonly ClientWebSocket socket;
            private readonly byte[] buffer = new byte[MaxPacket + 5];
            private int count;
            public PacketReader(ClientWebSocket socket) { this.socket = socket; }
            public async Task<byte[]> ReadAsync(CancellationToken token)
            {
                while (true)
                {
                    if (count >= 2)
                    {
                        int offset = 1, remaining = ReadRemaining(buffer, ref offset, count);
                        if (remaining >= 0)
                        {
                            if (remaining > MaxPacket) throw new InvalidDataException("MQTT packet too large.");
                            int total = offset + remaining;
                            if (count >= total)
                            {
                                byte[] result = Slice(buffer, 0, total);
                                Buffer.BlockCopy(buffer, total, buffer, 0, count - total);
                                count -= total;
                                return result;
                            }
                        }
                        else if (count >= 5) throw new InvalidDataException("Invalid MQTT length.");
                    }
                    if (count == buffer.Length) throw new InvalidDataException("MQTT packet too large.");
                    WebSocketReceiveResult read = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), token).ConfigureAwait(false);
                    if (read.MessageType != WebSocketMessageType.Binary || read.Count == 0) throw new IOException("WebSocket closed or invalid frame.");
                    count += read.Count;
                }
            }
        }
    }
}

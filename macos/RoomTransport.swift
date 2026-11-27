import Foundation
import CryptoKit
import CommonCrypto
import Security

// MQTT 3.1.1 over WebSocket; all mutable network state belongs to queue.
final class RoomTransport {
    private static let alphabet = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".utf8)
    private let queue = DispatchQueue(label: "WheekKeys.RoomTransport")
    private let broker: URL?
    private let topic: String
    private let key: [UInt8]
    private var session: URLSession?
    private var socket: URLSessionWebSocketTask?
    private var ticker: DispatchSourceTimer?
    private var generation = 0
    private var running = false
    private var ready = false
    private var state = "未连接"
    private var phase = 0 // 0: connecting, 1: CONNACK, 2: SUBACK, 3: ready
    private var deadline = Date.distantPast
    private var lastReceive = Date.distantPast
    private var lastSend = Date.distantPast
    private var retryAt = Date.distantPast
    private var failures = 0
    private var buffer = [UInt8]()
    private var messages = [String]()

    static func createCode() -> String {
        var random = [UInt8](repeating: 0, count: 16)
        guard SecRandomCopyBytes(kSecRandomDefault, random.count, &random) == errSecSuccess else {
            fatalError("Secure random number generator unavailable")
        }
        let prefix = String(decoding: random.map { alphabet[Int($0 & 31)] }, as: UTF8.self)
        return prefix + checksum(prefix)
    }

    static func normalizeCode(_ input: String) -> String? {
        var bytes = [UInt8]()
        for c in input.utf8 {
            if c == 32 || c == 45 { continue }
            let upper = (c >= 97 && c <= 122) ? c - 32 : c
            guard alphabet.contains(upper) else { return nil }
            bytes.append(upper)
            if bytes.count > 20 { return nil }
        }
        guard bytes.count == 20 else { return nil }
        let prefix = String(decoding: bytes.prefix(16), as: UTF8.self)
        let suffix = String(decoding: bytes.suffix(4), as: UTF8.self)
        return suffix == checksum(prefix) ? String(decoding: bytes, as: UTF8.self) : nil
    }

    private static func checksum(_ prefix: String) -> String {
        let digest = Array(SHA256.hash(data: Data(prefix.utf8)))
        let n = UInt32(digest[0]) << 12 | UInt32(digest[1]) << 4 | UInt32(digest[2]) >> 4
        return String(decoding: [alphabet[Int((n >> 15) & 31)], alphabet[Int((n >> 10) & 31)],
                                 alphabet[Int((n >> 5) & 31)], alphabet[Int(n & 31)]], as: UTF8.self)
    }

    init(code: String? = nil) {
        precondition(code.map { Self.normalizeCode($0) == $0 } ?? true, "Invalid room code")
        self.broker = Self.brokerURL()
        if let code = code {
            let topicHash = SHA256.hash(data: Data(("WheekKeys/v2/topic/" + code).utf8))
            self.topic = "wheekkeys/v2/r/" + topicHash.map { String(format: "%02x", $0) }.joined()
        } else {
            self.topic = "wheekkeys/v2/directory"
        }
        self.key = Array(SHA256.hash(data: Data(("WheekKeys/v2/fernet/" + (code ?? "directory")).utf8)))
        if broker == nil { state = "中转地址配置无效" }
    }

    private static func brokerURL() -> URL? {
        let configured = ProcessInfo.processInfo.environment["WHEEKKEYS_ROOM_BROKER"]
            ?? "wss://broker.emqx.io:8084/mqtt"
        guard let parts = URLComponents(string: configured), let scheme = parts.scheme?.lowercased(),
              let host = parts.host?.lowercased(), !host.isEmpty,
              parts.user == nil, parts.password == nil, parts.fragment == nil,
              let url = parts.url else { return nil }
        if scheme == "wss" { return url }
        guard scheme == "ws" else { return nil }
        let plainHost = host.trimmingCharacters(in: CharacterSet(charactersIn: "[]"))
        if plainHost == "localhost" || plainHost == "::1" { return url }
        let octets = plainHost.split(separator: ".", omittingEmptySubsequences: false)
        guard octets.count == 4, octets[0] == "127",
              octets.allSatisfy({ part in
                  !part.isEmpty && part.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }) &&
                  (part.count == 1 || part.first != "0") && (Int(part).map { $0 <= 255 } ?? false)
              }) else { return nil }
        return url
    }

    var connected: Bool { queue.sync { ready } }
    var status: String { queue.sync { state } }

    func start() {
        queue.async {
            guard !self.running else { return }
            guard self.broker != nil else { self.state = "中转地址配置无效"; return }
            self.running = true
            self.failures = 0
            let timer = DispatchSource.makeTimerSource(queue: self.queue)
            timer.schedule(deadline: .now() + 1, repeating: 1)
            timer.setEventHandler { [weak self] in self?.tick() }
            self.ticker = timer
            timer.resume()
            self.connect()
        }
    }

    func stop() {
        queue.sync {
            running = false
            generation += 1
            ready = false
            state = broker == nil ? "中转地址配置无效" : "未连接"
            ticker?.cancel(); ticker = nil
            socket?.cancel(with: .normalClosure, reason: nil); socket = nil
            session?.invalidateAndCancel(); session = nil
            buffer.removeAll(); messages.removeAll()
        }
    }

    func publish(_ plaintext: String) {
        guard Self.validPlaintext(plaintext) else { return }
        queue.async {
            guard self.running, self.ready, let token = self.encrypt(plaintext) else { return }
            var data = Self.field(Array(self.topic.utf8))
            data += Array(token.utf8)
            self.send(Self.packet(0x30, data), generation: self.generation)
        }
    }

    func drain() -> [String] {
        queue.sync { let result = messages; messages.removeAll(); return result }
    }

    private func tick() {
        guard running else { return }
        let now = Date()
        if socket == nil {
            if now >= retryAt { connect() }
        } else if phase != 3 && now >= deadline {
            disconnect()
        } else if phase == 3 {
            if now.timeIntervalSince(lastReceive) > 60 { disconnect() }
            else if now.timeIntervalSince(lastSend) >= 20 { send([0xC0, 0], generation: generation) }
        }
    }

    private func connect() {
        guard running, socket == nil else { return }
        generation += 1
        let current = generation
        ready = false; phase = 0; state = "连接中"; buffer.removeAll()
        deadline = Date().addingTimeInterval(12)
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 15
        let s = URLSession(configuration: config)
        session = s
        guard let broker = broker else { s.invalidateAndCancel(); session = nil; state = "中转地址配置无效"; return }
        let task = s.webSocketTask(with: broker, protocols: ["mqtt"])
        socket = task
        task.maximumMessageSize = 65536
        task.resume()
        var id = [UInt8](repeating: 0, count: 10)
        guard SecRandomCopyBytes(kSecRandomDefault, id.count, &id) == errSecSuccess else { disconnect(); return }
        let client = "wk" + id.map { String(format: "%02x", $0) }.joined()
        var connect = Self.field(Array("MQTT".utf8)) + [4, 2, 0, 30]
        connect += Self.field(Array(client.utf8))
        phase = 1
        send(Self.packet(0x10, connect), generation: current)
        receive(current)
    }

    private func disconnect() {
        generation += 1
        ready = false; phase = 0
        socket?.cancel(with: .goingAway, reason: nil); socket = nil
        session?.invalidateAndCancel(); session = nil
        buffer.removeAll(); messages.removeAll()
        guard running else { state = "未连接"; return }
        failures = min(failures + 1, 6)
        retryAt = Date().addingTimeInterval(min(30, pow(2.0, Double(failures - 1))))
        state = "连接中断，正在重试"
    }

    private func send(_ bytes: [UInt8], generation current: Int) {
        guard current == generation, let task = socket else { return }
        lastSend = Date()
        task.send(.data(Data(bytes))) { [weak self] error in
            guard let self = self else { return }
            self.queue.async {
                guard current == self.generation else { return }
                if error != nil { self.disconnect() }
            }
        }
    }

    private func receive(_ current: Int) {
        guard current == generation, let task = socket else { return }
        task.receive { [weak self] result in
            guard let self = self else { return }
            self.queue.async {
                guard current == self.generation, self.running else { return }
                switch result {
                case .failure: self.disconnect()
                case .success(let message):
                    guard case .data(let data) = message, !data.isEmpty,
                          self.buffer.count + data.count <= 65536 else { self.disconnect(); return }
                    self.lastReceive = Date()
                    self.buffer += data
                    guard self.consume() else { self.disconnect(); return }
                    if current == self.generation { self.receive(current) }
                }
            }
        }
    }

    private func consume() -> Bool {
        while buffer.count >= 2 {
            let header = buffer[0]
            var length = 0, multiplier = 1, index = 1, complete = false
            for _ in 0..<4 {
                if index >= buffer.count { return true }
                let b = buffer[index]; index += 1
                length += Int(b & 127) * multiplier
                if b & 128 == 0 {
                    if index > 2 && b == 0 { return false }
                    complete = true; break
                }
                multiplier *= 128
            }
            guard complete, length <= 8192 else { return false }
            if buffer.count < index + length { return true }
            let body = Array(buffer[index..<(index + length)])
            buffer.removeFirst(index + length)
            guard handle(header, body) else { return false }
        }
        return true
    }

    private func handle(_ header: UInt8, _ body: [UInt8]) -> Bool {
        switch header {
        case 0x20:
            guard phase == 1, body == [0, 0] else { return false }
            phase = 2; deadline = Date().addingTimeInterval(12)
            send(Self.packet(0x82, [0, 1] + Self.field(Array(topic.utf8)) + [0]), generation: generation)
            return true
        case 0x90:
            guard phase == 2, body == [0, 1, 0] else { return false }
            phase = 3; ready = true; failures = 0; state = "已连接"
            return true
        case 0x30, 0x31, 0x38, 0x39:
            if phase == 2 { return true }
            guard phase == 3, body.count >= 2 else { return false }
            let n = Int(body[0]) * 256 + Int(body[1])
            guard n > 0, n <= body.count - 2 else { return false }
            guard Array(body[2..<(2 + n)]) == Array(topic.utf8) else { return true }
            if header & 1 != 0 { return true }
            let payload = Array(body[(2 + n)...])
            guard payload.count <= 1024, let token = String(bytes: payload, encoding: .ascii),
                  let plaintext = decrypt(token), Self.validPlaintext(plaintext) else { return true }
            if messages.count == 256 { messages.removeFirst() }
            messages.append(plaintext)
            return true
        case 0xD0:
            return phase == 3 && body.isEmpty
        default:
            return false
        }
    }

    static func validPlaintext(_ value: String) -> Bool {
        let bytes = Array(value.utf8)
        guard bytes.allSatisfy({ $0 >= 32 && $0 <= 126 }) else { return false }
        let parts = value.split(separator: "|", omittingEmptySubsequences: false)
        if parts.first == "WM2" { return bytes.count <= 560 }
        if parts.first == "WD2" { return bytes.count <= 150 }
        guard bytes.count <= 128 else { return false }
        if parts.first == "WJ2" { return bytes.count == 36 }
        if parts.count == 5 && parts[0] == "WK1" {
            return parts.dropFirst().allSatisfy { !$0.isEmpty }
        }
        if parts.count == 4 && (parts[0] == "WS1" || parts[0] == "WA1") {
            return parts.dropFirst().allSatisfy { !$0.isEmpty }
        }
        guard bytes.count <= 125, parts.count == 3, parts[0] == "WN1",
              parts[1].utf8.count == 32,
              parts[1].utf8.allSatisfy({ ($0 >= 48 && $0 <= 57) || ($0 >= 97 && $0 <= 102) }),
              let name = Data(base64Encoded: String(parts[2])) else { return false }
        return name.base64EncodedString() == String(parts[2])
    }

    private static func field(_ bytes: [UInt8]) -> [UInt8] {
        [UInt8(bytes.count >> 8), UInt8(bytes.count & 255)] + bytes
    }

    private static func packet(_ header: UInt8, _ body: [UInt8]) -> [UInt8] {
        var n = body.count, encoded = [UInt8]()
        repeat {
            var b = UInt8(n % 128); n /= 128
            if n > 0 { b |= 128 }
            encoded.append(b)
        } while n > 0
        return [header] + encoded + body
    }

    private func encrypt(_ plaintext: String) -> String? {
        var iv = [UInt8](repeating: 0, count: 16)
        guard SecRandomCopyBytes(kSecRandomDefault, iv.count, &iv) == errSecSuccess,
              let encrypted = Self.aes(Array(plaintext.utf8), key: Array(key[16..<32]), iv: iv, operation: CCOperation(kCCEncrypt)) else { return nil }
        let seconds = UInt64(Date().timeIntervalSince1970)
        var data: [UInt8] = [0x80]
        for shift in stride(from: 56, through: 0, by: -8) { data.append(UInt8((seconds >> shift) & 255)) }
        data += iv + encrypted
        data += Array(HMAC<SHA256>.authenticationCode(for: Data(data), using: SymmetricKey(data: Data(key[0..<16]))))
        return Data(data).base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
    }

    private func decrypt(_ token: String) -> String? {
        guard token.utf8.allSatisfy({ ($0 >= 65 && $0 <= 90) || ($0 >= 97 && $0 <= 122) || ($0 >= 48 && $0 <= 57) || $0 == 45 || $0 == 95 || $0 == 61 }),
              let raw = Data(base64Encoded: token.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")) else { return nil }
        guard raw.base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_") == token else { return nil }
        let bytes = Array(raw)
        guard bytes.count >= 73, (bytes.count - 57) % 16 == 0, bytes[0] == 0x80 else { return nil }
        let signed = Data(bytes.dropLast(32)), tag = Data(bytes.suffix(32))
        guard HMAC<SHA256>.isValidAuthenticationCode(tag, authenticating: signed, using: SymmetricKey(data: Data(key[0..<16]))) else { return nil }
        var seconds: UInt64 = 0
        for b in bytes[1..<9] { seconds = (seconds << 8) | UInt64(b) }
        guard abs(Double(seconds) - Date().timeIntervalSince1970) <= 120 else { return nil }
        let iv = Array(bytes[9..<25]), ciphertext = Array(bytes[25..<(bytes.count - 32)])
        guard let clear = Self.aes(ciphertext, key: Array(key[16..<32]), iv: iv, operation: CCOperation(kCCDecrypt)) else { return nil }
        return String(bytes: clear, encoding: .ascii)
    }

    private static func aes(_ input: [UInt8], key: [UInt8], iv: [UInt8], operation: CCOperation) -> [UInt8]? {
        var output = [UInt8](repeating: 0, count: input.count + kCCBlockSizeAES128)
        var written = 0
        let capacity = output.count
        let result = output.withUnsafeMutableBytes { out in
            input.withUnsafeBytes { inp in
                key.withUnsafeBytes { k in
                    iv.withUnsafeBytes { v in
                        CCCrypt(operation, CCAlgorithm(kCCAlgorithmAES), CCOptions(kCCOptionPKCS7Padding),
                                k.baseAddress, kCCKeySizeAES128, v.baseAddress, inp.baseAddress, input.count,
                                out.baseAddress, capacity, &written)
                    }
                }
            }
        }
        guard result == kCCSuccess else { return nil }
        return Array(output.prefix(written))
    }
}

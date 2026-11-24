import Foundation
import Darwin

final class LocalPeers {
    struct Peer {
        let skin: String
        let keys: UInt64
        let clicks: UInt64
        let seen: TimeInterval
        var nickname: String = ""
    }
    private(set) var peers: [String: Peer] = [:]
    private(set) var active = false
    private(set) var status = ""
    private(set) var roomCode: String?
    var roomConnected: Bool { room?.connected ?? false }
    private var room: RoomTransport?
    private var socketFD: Int32 = -1
    private var timer: Timer?
    private let session = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    var payload: (() -> (String, UInt64, UInt64))?
    var nickname: (() -> String)?
    private var lastBroadcast: TimeInterval = 0

    func start() -> Bool {
        guard !active else { return true }
        let fd = socket(AF_INET, SOCK_DGRAM, 0)
        guard fd >= 0 else { return false }
        var yes: Int32 = 1
        guard setsockopt(fd, SOL_SOCKET, SO_REUSEADDR, &yes, socklen_t(MemoryLayout<Int32>.size)) == 0,
              setsockopt(fd, SOL_SOCKET, SO_BROADCAST, &yes, socklen_t(MemoryLayout<Int32>.size)) == 0 else { close(fd); return false }
        var address = sockaddr_in()
        address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(41684).bigEndian
        address.sin_addr = in_addr(s_addr: INADDR_ANY)
        let bound = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { bind(fd, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        guard bound == 0, fcntl(fd, F_SETFL, O_NONBLOCK) == 0 else { close(fd); return false }
        socketFD = fd
        active = true
        lastBroadcast = 0
        startTimer()
        return true
    }

    func startRoom(_ code: String) -> Bool {
        guard let normalized = RoomTransport.normalizeCode(code) else { return false }
        stop()
        roomCode = normalized
        let transport = RoomTransport(code: normalized)
        room = transport
        active = true
        lastBroadcast = 0
        transport.start()
        startTimer()
        return true
    }

    private func startTimer() {
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in self?.tick() }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
        tick()
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        room?.stop()
        room = nil
        roomCode = nil
        if socketFD >= 0 { close(socketFD) }
        socketFD = -1
        active = false
        peers.removeAll()
        status = ""
    }

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        if let room = room {
            status = room.status
            guard room.connected else {
                peers.removeAll()
                lastBroadcast = 0
                return
            }
            if lastBroadcast == 0 || now - lastBroadcast >= 5 {
                lastBroadcast = now
                if let message = packet() {
                    room.publish(message)
                    room.publish(nicknamePacket())
                }
            }
            for message in room.drain() {
                accept(Array(message.utf8), now: now)
            }
            peers = Self.unexpired(peers, now: now, expiry: 20)
            return
        }
        if now - lastBroadcast >= 2 {
            lastBroadcast = now
            broadcast()
        }
        var bytes = [UInt8](repeating: 0, count: 129)
        for _ in 0..<32 {
            let count = recv(socketFD, &bytes, bytes.count, 0)
            if count <= 0 { break }
            if count <= 128 { accept(Array(bytes.prefix(count)), now: now) }
        }
        peers = Self.unexpired(peers, now: now)
    }

    private func accept(_ bytes: [UInt8], now: TimeInterval) {
        Self.apply(bytes: bytes, ownID: session, now: now, peers: &peers)
    }

    static func normalizedNickname(_ input: String) -> String? {
        let value = input.trimmingCharacters(in: .whitespacesAndNewlines)
        for scalar in value.unicodeScalars {
            let code = scalar.value
            if scalar.properties.generalCategory == .control || code == 0x2028 || code == 0x2029 ||
                code == 0x061C || code == 0x200E || code == 0x200F ||
                (0x202A...0x202E).contains(code) || (0x2066...0x2069).contains(code) { return nil }
        }
        guard value.unicodeScalars.count <= 16, value.utf8.count <= 64 else { return nil }
        return value
    }

    static func apply(bytes: [UInt8], ownID: String, now: TimeInterval, peers: inout [String: Peer]) {
        if let parsed = parse(bytes: bytes, ownID: ownID, now: now) {
            let (id, received) = parsed
            guard peers[id] != nil || peers.count < 64 else { return }
            var peer = received
            peer.nickname = peers[id]?.nickname ?? ""
            peers[id] = peer
            return
        }
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count == 3, fields[0] == "WN1", fields[1].count == 32,
              fields[1].utf8.allSatisfy({ (48...57).contains($0) || (97...102).contains($0) }),
              fields[1] != ownID, let data = Data(base64Encoded: String(fields[2])),
              data.base64EncodedString() == fields[2], let text = String(data: data, encoding: .utf8),
              let value = normalizedNickname(text), value == text else { return }
        let id = String(fields[1])
        guard var peer = peers[id] else { return }
        peer.nickname = value
        peers[id] = peer
    }

    private func nicknamePacket() -> String {
        let value = Self.normalizedNickname(nickname?() ?? "") ?? ""
        return "WN1|\(session)|\(Data(value.utf8).base64EncodedString())"
    }

    private func packet() -> String? {
        guard let values = payload?() else { return nil }
        let (skin, keys, clicks) = values
        return "WK1|\(session)|\(PetSkin.named(skin).networkID)|\(keys)|\(clicks)"
    }

    static func parse(bytes: [UInt8], ownID: String, now: TimeInterval) -> (String, Peer)? {
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return nil }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count == 5, fields[0] == "WK1", fields[1].count == 32,
              fields[1].utf8.allSatisfy({ (48...57).contains($0) || (97...102).contains($0) }),
              fields[1] != ownID, let skin = PetSkin.find(String(fields[2])),
              !fields[3].isEmpty, !fields[4].isEmpty,
              fields[3].utf8.allSatisfy({ (48...57).contains($0) }),
              fields[4].utf8.allSatisfy({ (48...57).contains($0) }),
              let keys = UInt64(fields[3]), keys <= UInt64(Int64.max),
              let clicks = UInt64(fields[4]), clicks <= UInt64(Int64.max) else { return nil }
        return (String(fields[1]), Peer(skin: skin.id, keys: keys, clicks: clicks, seen: now))
    }

    static func unexpired(_ peers: [String: Peer], now: TimeInterval, expiry: TimeInterval = 8) -> [String: Peer] {
        peers.filter { now - $0.value.seen < expiry }
    }

    private func broadcast() {
        guard let data = packet()?.data(using: .ascii), let nameData = nicknamePacket().data(using: .ascii) else { return }
        let messages = [data, nameData]
        var interfaces: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&interfaces) == 0 else { status = "无法读取网络接口"; return }
        defer { freeifaddrs(interfaces) }
        var current = interfaces
        var targets = 0
        var successes = 0
        while let item = current {
            defer { current = item.pointee.ifa_next }
            let flags = Int32(item.pointee.ifa_flags)
            guard flags & IFF_UP != 0, flags & IFF_BROADCAST != 0, flags & IFF_LOOPBACK == 0,
                  let address = item.pointee.ifa_addr, let mask = item.pointee.ifa_netmask,
                  address.pointee.sa_family == sa_family_t(AF_INET) else { continue }
            targets += 1
            let ip = UnsafeRawPointer(address).assumingMemoryBound(to: sockaddr_in.self).pointee.sin_addr.s_addr
            let netmask = UnsafeRawPointer(mask).assumingMemoryBound(to: sockaddr_in.self).pointee.sin_addr.s_addr
            var destination = sockaddr_in()
            destination.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
            destination.sin_family = sa_family_t(AF_INET)
            destination.sin_port = in_port_t(41684).bigEndian
            destination.sin_addr = in_addr(s_addr: ip | ~netmask)
            let allSent = messages.allSatisfy { message in
                message.withUnsafeBytes { buffer in
                    let sent = withUnsafePointer(to: &destination) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                        sendto(socketFD, buffer.baseAddress, buffer.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                    } }
                    return sent == message.count
                }
            }
            if allSent { successes += 1 }
        }
        if targets == 0 { status = "没有可广播的 IPv4 网卡" }
        else if successes == 0 { status = "广播发送失败" }
        else if successes < targets { status = "部分网卡广播发送失败" }
        else { status = "" }
    }
}

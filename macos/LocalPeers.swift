import Foundation
import Darwin

final class LocalPeers {
    struct Peer {
        let skin: String
        let keys: UInt64
        let clicks: UInt64
        let seen: TimeInterval
    }
    private(set) var peers: [String: Peer] = [:]
    private(set) var active = false
    private(set) var status = ""
    private var socketFD: Int32 = -1
    private var timer: Timer?
    private let session = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    var payload: (() -> (String, UInt64, UInt64))?
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
        let timer = Timer(timeInterval: 0.25, repeats: true) { [weak self] _ in self?.tick() }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
        tick()
        return true
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        if socketFD >= 0 { close(socketFD) }
        socketFD = -1
        active = false
        peers.removeAll()
        status = ""
    }

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        if now - lastBroadcast >= 2 {
            lastBroadcast = now
            broadcast()
        }
        var bytes = [UInt8](repeating: 0, count: 129)
        for _ in 0..<32 {
            let count = recv(socketFD, &bytes, bytes.count, 0)
            if count <= 0 { break }
            guard count <= 128, let parsed = Self.parse(bytes: Array(bytes.prefix(count)), ownID: session, now: now) else { continue }
            let (id, peer) = parsed
            if peers[id] != nil || peers.count < 64 { peers[id] = peer }
        }
        peers = Self.unexpired(peers, now: now)
    }

    static func parse(bytes: [UInt8], ownID: String, now: TimeInterval) -> (String, Peer)? {
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return nil }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count == 5, fields[0] == "WK1", fields[1].count == 32,
              fields[1].utf8.allSatisfy({ (48...57).contains($0) || (97...102).contains($0) }),
              fields[1] != ownID, PetSkin.all.contains(where: { $0.id == fields[2] }),
              !fields[3].isEmpty, !fields[4].isEmpty,
              fields[3].utf8.allSatisfy({ (48...57).contains($0) }),
              fields[4].utf8.allSatisfy({ (48...57).contains($0) }),
              let keys = UInt64(fields[3]), keys <= UInt64(Int64.max),
              let clicks = UInt64(fields[4]), clicks <= UInt64(Int64.max) else { return nil }
        return (String(fields[1]), Peer(skin: String(fields[2]), keys: keys, clicks: clicks, seen: now))
    }

    static func unexpired(_ peers: [String: Peer], now: TimeInterval) -> [String: Peer] {
        peers.filter { now - $0.value.seen < 8 }
    }

    private func broadcast() {
        guard let values = payload?() else { return }
        let (skin, keys, clicks) = values
        guard let data = "WK1|\(session)|\(skin)|\(keys)|\(clicks)".data(using: .ascii) else { return }
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
            let sent = data.withUnsafeBytes { buffer in
                withUnsafePointer(to: &destination) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    sendto(socketFD, buffer.baseAddress, buffer.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                } }
            }
            if sent == data.count { successes += 1 }
        }
        if targets == 0 { status = "没有可广播的 IPv4 网卡" }
        else if successes == 0 { status = "广播发送失败" }
        else if successes < targets { status = "部分网卡广播发送失败" }
        else { status = "" }
    }
}

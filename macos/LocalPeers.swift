import Foundation
import Darwin

final class LocalPeers {
    enum Action: Equatable {
        case key(UInt16)
        case click(Bool)
    }

    struct Peer {
        var skin: String
        var keys: UInt64
        var clicks: UInt64
        var seen: TimeInterval
        var nickname: String = ""
        var supportsActivity = false
        var sharingActivity = false
        fileprivate var hasAppearance = false
        fileprivate var actionSequence: UInt64 = 0
        fileprivate var actions: [(action: Action, time: TimeInterval)] = []
        private var nextActionAt: TimeInterval = 0

        init(skin: String, keys: UInt64, clicks: UInt64, seen: TimeInterval) {
            self.skin = skin
            self.keys = keys
            self.clicks = clicks
            self.seen = seen
        }

        mutating func takeAction(now: TimeInterval) -> Action? {
            guard sharingActivity, now >= nextActionAt else { return nil }
            actions.removeAll { now - $0.time > LocalPeers.actionLifetime }
            guard !actions.isEmpty else { return nil }
            let action = actions.removeFirst().action
            // 给握鼠和点击动画留时间，避免被紧随的按键打断。
            if case .click = action { nextActionAt = now + 0.18 }
            else { nextActionAt = now + 0.055 }
            return action
        }

        fileprivate mutating func clearActions() {
            actions.removeAll(keepingCapacity: true)
            nextActionAt = 0
        }
    }

    private static let actionLifetime: TimeInterval = 0.8
    private static let actionBatchLimit = 12
    private static let actionQueueLimit = 64
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
    var onUpdate: (() -> Void)?
    var shareActivity = true {
        didSet {
            guard shareActivity != oldValue else { return }
            pendingActions.removeAll(keepingCapacity: true)
            refreshState()
        }
    }
    private var lastBroadcast: TimeInterval = 0
    private var actionSequence: UInt64 = 0
    private var pendingActions: [(token: String, time: TimeInterval)] = []

    func recordKey(_ code: UInt16) {
        guard let scan = NetworkKeyCodes.scan(for: code) else { return }
        recordAction("k\(scan)")
    }

    func recordClick(right: Bool) { recordAction(right ? "r" : "l") }

    private func recordAction(_ token: String) {
        guard active, shareActivity, room == nil || roomConnected else { return }
        if pendingActions.count == Self.actionQueueLimit { pendingActions.removeFirst() }
        pendingActions.append((token, ProcessInfo.processInfo.systemUptime))
    }

    func refreshState() { lastBroadcast = 0 }

    func takeAction(for id: String, visible: Bool) -> Action? {
        guard visible else { peers[id]?.clearActions(); return nil }
        return peers[id]?.takeAction(now: ProcessInfo.processInfo.systemUptime)
    }

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
        let timer = Timer(timeInterval: 0.03, repeats: true) { [weak self] _ in self?.tick() }
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
        status = ""
        clearPeers()
    }

    private func clearPeers() {
        pendingActions.removeAll(keepingCapacity: true)
        lastBroadcast = 0
        guard !peers.isEmpty else { return }
        peers.removeAll()
        onUpdate?()
    }

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        if let room = room {
            status = room.status
            guard room.connected else { clearPeers(); return }
        }

        var outgoing: [String] = []
        if lastBroadcast == 0 || now - lastBroadcast >= (room == nil ? 2 : 5) {
            lastBroadcast = now
            outgoing = statePackets()
        }
        if let packet = actionPacket(now: now) { outgoing.append(packet) }

        var changed = false
        if let room = room {
            for message in outgoing { room.publish(message) }
            for message in room.drain() {
                if accept(Array(message.utf8), now: now) { changed = true }
            }
        } else {
            if !outgoing.isEmpty { broadcast(outgoing) }
            var bytes = [UInt8](repeating: 0, count: 129)
            for _ in 0..<32 {
                let count = recv(socketFD, &bytes, bytes.count, 0)
                if count <= 0 { break }
                if count <= 128, accept(Array(bytes.prefix(count)), now: now) { changed = true }
            }
        }

        let remaining = Self.unexpired(peers, now: now, expiry: room == nil ? 8 : 20)
        if remaining.count != peers.count {
            peers = remaining
            changed = true
        }
        if changed || peers.values.contains(where: { !$0.actions.isEmpty }) { onUpdate?() }
    }

    private func accept(_ bytes: [UInt8], now: TimeInterval) -> Bool {
        let count = peers.count
        let accepted = Self.apply(bytes: bytes, ownID: session, now: now, peers: &peers)
        if peers.count > count { refreshState() }
        return accepted
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

    @discardableResult
    static func apply(bytes: [UInt8], ownID: String, now: TimeInterval, peers: inout [String: Peer]) -> Bool {
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return false }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count >= 2, fields[1] != ownID, validID(fields[1]) else { return false }
        let id = String(fields[1])
        if fields[0] == "WK1" {
            guard let state = parseState(fields, now: now), peers[id] != nil || peers.count < 64 else { return false }
            var peer = peers[id] ?? state
            if !peer.hasAppearance { peer.skin = state.skin }
            peer.keys = state.keys
            peer.clicks = state.clicks
            peer.seen = now
            peers[id] = peer
            return true
        }
        guard var peer = peers[id] else { return false }
        switch fields[0] {
        case "WN1":
            guard fields.count == 3, let data = Data(base64Encoded: String(fields[2])),
                  data.base64EncodedString() == fields[2], let text = String(data: data, encoding: .utf8),
                  let value = normalizedNickname(text), value == text else { return false }
            peer.nickname = value
        case "WS1":
            guard fields.count == 4, fields[3] == "0" || fields[3] == "1" else { return false }
            if let skin = PetSkin.find(String(fields[2])) {
                peer.skin = skin.id
                peer.hasAppearance = true
            }
            peer.supportsActivity = true
            peer.sharingActivity = fields[3] == "1"
            if !peer.sharingActivity { peer.clearActions() }
            peer.seen = now
        case "WA1":
            guard fields.count == 4, peer.sharingActivity, let sequence = count(fields[2]),
                  sequence > peer.actionSequence, let actions = parseActions(fields[3]) else { return false }
            peer.actionSequence = sequence
            peer.seen = now
            peer.actions.append(contentsOf: actions.map { ($0, now) })
            if peer.actions.count > actionQueueLimit {
                peer.actions.removeFirst(peer.actions.count - actionQueueLimit)
            }
        default:
            return false
        }
        peers[id] = peer
        return true
    }

    private func nicknamePacket() -> String {
        let value = Self.normalizedNickname(nickname?() ?? "") ?? ""
        return "WN1|\(session)|\(Data(value.utf8).base64EncodedString())"
    }

    private func statePackets() -> [String] {
        guard let (skinID, keys, clicks) = payload?() else { return [] }
        let skin = PetSkin.named(skinID)
        return ["WK1|\(session)|\(skin.networkID)|\(keys)|\(clicks)",
                "WS1|\(session)|\(skin.id)|\(shareActivity ? "1" : "0")", nicknamePacket()]
    }

    private func actionPacket(now: TimeInterval) -> String? {
        guard !pendingActions.isEmpty, actionSequence < UInt64(Int64.max) else { return nil }
        pendingActions.removeAll { now - $0.time > Self.actionLifetime }
        let batch = pendingActions.prefix(Self.actionBatchLimit)
        guard !batch.isEmpty else { return nil }
        let tokens = batch.map { $0.token }.joined(separator: ",")
        pendingActions.removeFirst(batch.count)
        actionSequence += 1
        return "WA1|\(session)|\(actionSequence)|\(tokens)"
    }

    static func parse(bytes: [UInt8], ownID: String, now: TimeInterval) -> (String, Peer)? {
        guard bytes.count <= 128, let message = String(bytes: bytes, encoding: .ascii) else { return nil }
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count == 5, fields[1] != ownID, validID(fields[1]),
              let peer = parseState(fields, now: now) else { return nil }
        return (String(fields[1]), peer)
    }

    private static func parseState(_ fields: [Substring], now: TimeInterval) -> Peer? {
        guard fields.count == 5, fields[0] == "WK1", let skin = PetSkin.find(String(fields[2])),
              let keys = count(fields[3]), let clicks = count(fields[4]) else { return nil }
        return Peer(skin: skin.id, keys: keys, clicks: clicks, seen: now)
    }

    private static func validID(_ value: Substring) -> Bool {
        value.utf8.count == 32 && value.utf8.allSatisfy { (48...57).contains($0) || (97...102).contains($0) }
    }

    private static func count(_ value: Substring) -> UInt64? {
        guard !value.isEmpty, value.utf8.allSatisfy({ (48...57).contains($0) }),
              let number = UInt64(value), number <= UInt64(Int64.max) else { return nil }
        return number
    }

    private static func parseActions(_ value: Substring) -> [Action]? {
        let tokens = value.split(separator: ",", omittingEmptySubsequences: false)
        guard !tokens.isEmpty, tokens.count <= actionBatchLimit else { return nil }
        var actions: [Action] = []
        for token in tokens {
            if token == "l" || token == "r" {
                actions.append(.click(token == "r"))
            } else {
                guard token.first == "k", let scan = count(token.dropFirst()), (1...511).contains(scan) else { return nil }
                actions.append(.key(NetworkKeyCodes.keyCode(for: UInt16(scan)) ?? UInt16.max))
            }
        }
        return actions
    }

    static func unexpired(_ peers: [String: Peer], now: TimeInterval, expiry: TimeInterval = 8) -> [String: Peer] {
        peers.filter { now - $0.value.seen < expiry }
    }

    private func broadcast(_ packets: [String]) {
        let messages = packets.map { Data($0.utf8) }
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

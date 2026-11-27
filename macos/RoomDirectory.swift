import Foundation

final class RoomDirectory {
    struct Entry {
        let code: String, host: String, name: String
        let count: Int
        let seen: TimeInterval
        var label: String {
            (name.isEmpty ? "小豚鼠" : name) + "的公开房间"
        }
    }

    private let transport = RoomTransport()
    private var rooms: [String: Entry] = [:]
    private var nextPublish: TimeInterval = 0
    var connected: Bool { transport.connected }
    var status: String { transport.status }

    init() { transport.start() }
    deinit { transport.stop() }

    func advertise(now: TimeInterval, code: String, host: String, count: Int, nickname: String) {
        guard connected, now >= nextPublish else { return }
        nextPublish = now + 2
        let name = LocalPeers.normalizedNickname(nickname) ?? ""
        transport.publish("WD2|\(code)|\(host)|\(count)|\(Data(name.utf8).base64EncodedString())")
    }

    func refresh(now: TimeInterval) -> [Entry] {
        if !connected { rooms.removeAll() }
        rooms = rooms.filter { now - $0.value.seen < 7 }
        for packet in transport.drain() {
            if let entry = Self.parse(packet, now: now), rooms.count < 64 || rooms[entry.code] != nil {
                rooms[entry.code] = entry
            }
        }
        return rooms.values.sorted {
            let full = $0.count == RoomMembership.capacity
            let otherFull = $1.count == RoomMembership.capacity
            return full == otherFull ? $0.code < $1.code : !full
        }
    }

    static func parse(_ message: String, now: TimeInterval) -> Entry? {
        let fields = message.split(separator: "|", omittingEmptySubsequences: false)
        guard fields.count == 5, fields[0] == "WD2", let code = RoomTransport.normalizeCode(String(fields[1])),
              code == fields[1], RoomMembership.validID(fields[2]), let number = LocalPeers.count(fields[3]),
              number > 0, number <= UInt64(RoomMembership.capacity), let name = LocalPeers.decodedNickname(fields[4]) else { return nil }
        return Entry(code: code, host: String(fields[2]), name: name, count: Int(number), seen: now)
    }
}

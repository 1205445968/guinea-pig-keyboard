import Foundation

final class RoomMembership {
    static let capacity = 16
    private static let lifetime: TimeInterval = 10
    private let ownID: String
    private let hosting: Bool
    private var host: String?
    private var guests: [String: TimeInterval] = [:]
    private var sequence: UInt64 = 0
    private var nextSend: TimeInterval = 0
    private var lastRoster: TimeInterval?
    private(set) var isPublic: Bool
    private(set) var visibilityKnown: Bool

    init(ownID: String, hosting: Bool, isPublic: Bool, expectedHost: String?) {
        self.ownID = ownID
        self.hosting = hosting
        self.host = hosting ? ownID : expectedHost
        self.isPublic = isPublic
        self.visibilityKnown = hosting || expectedHost != nil
    }

    var joined: Bool { hosting || guests[ownID] != nil }
    var count: Int { hosting || lastRoster != nil ? guests.count + 1 : 0 }

    func contains(_ id: String) -> Bool { joined && (host == id || guests[id] != nil) }

    static func validID(_ value: Substring) -> Bool {
        value.utf8.count == 32 && value.utf8.allSatisfy { (48...57).contains($0) || (97...102).contains($0) }
    }

    func disconnected() {
        nextSend = 0
        guard !hosting else { return }
        lastRoster = nil
        guests.removeAll()
    }

    func expire(now: TimeInterval) {
        guard hosting else {
            if let lastRoster = lastRoster, now - lastRoster >= Self.lifetime { disconnected() }
            return
        }
        let active = guests.filter { now - $0.value < Self.lifetime }
        if active.count != guests.count {
            guests = active
            nextSend = 0
        }
    }

    func nextPacket(now: TimeInterval) -> String? {
        guard now >= nextSend else { return nil }
        nextSend = now + 2
        guard hosting else { return "WJ2|\(ownID)" }
        sequence += 1
        return (["WM2", ownID, String(sequence), isPublic ? "1" : "0"] + guests.keys.sorted()).joined(separator: "|")
    }

    func receive(_ fields: [Substring], now: TimeInterval) {
        guard fields.count >= 2, fields[1] != ownID, Self.validID(fields[1]) else { return }
        let sender = String(fields[1])
        if hosting {
            guard fields.count == 2, fields[0] == "WJ2" else { return }
            if guests[sender] == nil {
                guard guests.count < Self.capacity - 1 else { return }
                nextSend = 0
            }
            guests[sender] = now
            return
        }
        guard fields[0] == "WM2", (4...(Self.capacity + 3)).contains(fields.count),
              host == nil || host == sender, let revision = LocalPeers.count(fields[2]), revision > sequence,
              fields[3] == "0" || fields[3] == "1" else { return }
        let visible = fields[3] == "1"
        guard !visibilityKnown || isPublic == visible else { return }
        var members: [String: TimeInterval] = [:]
        for id in fields.dropFirst(4) {
            guard id != fields[1], Self.validID(id), members.updateValue(now, forKey: String(id)) == nil else { return }
        }
        guests = members
        host = sender
        lastRoster = now
        sequence = revision
        isPublic = visible
        visibilityKnown = true
    }
}

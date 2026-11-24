import AppKit

// Runs against the packaged application on both Mac architectures in CI.
@MainActor func runSmokeTest() {
    let ownID = String(repeating: "a", count: 32)
    let remoteID = String(repeating: "b", count: 32)
    func parse(_ text: String) -> (String, LocalPeers.Peer)? {
        LocalPeers.parse(bytes: Array(text.utf8), ownID: ownID, now: 10)
    }
    let valid = "WK1|\(remoteID)|longhair|123|45"
    guard let parsed = parse(valid) else { fatalError("Valid peer packet rejected") }
    precondition(parsed.0 == remoteID && parsed.1.keys == 123 && parsed.1.clicks == 45)
    for invalid in ["WK1|\(ownID)|longhair|1|2", "WK1|\(remoteID)|unknown|1|2",
                    "WK1|\(remoteID)|longhair|-1|2", "WK1|\(remoteID)|longhair|9223372036854775808|2",
                    "WK1|\(remoteID)|longhair|1|2\n", valid + String(repeating: "0", count: 129)] {
        precondition(parse(invalid) == nil, "Invalid peer packet accepted")
    }
    let peerMap = [parsed.0: parsed.1]
    precondition(LocalPeers.unexpired(peerMap, now: 17.99).count == 1)
    precondition(LocalPeers.unexpired(peerMap, now: 18).isEmpty)
    print("Verified LAN packet validation and expiry")
    _ = NSApplication.shared
    let size = NSRect(x: 0, y: 0, width: 400, height: 308)
    let window = NSWindow(contentRect: size, styleMask: [.borderless], backing: .buffered, defer: false)
    let view = PetView(frame: size)
    window.contentView = view

    func snapshot() -> Data {
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            fatalError("No native bitmap context")
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        guard let png = bitmap.representation(using: .png, properties: [:]), png.count > 8000 else {
            fatalError("Empty desktop pet frame")
        }
        return png
    }

    var idleFrames = Set<Data>()
    let root = Bundle.main.resourceURL!
    for skin in PetSkin.all {
        for file in [skin.body, skin.paw, skin.id + "-eyes.png", "keyboard.png", "mouse.png"] {
            precondition(NSImage(contentsOf: root.appendingPathComponent(file)) != nil, "Missing sprite: \(file)")
        }
        view.selectSkin(skin.id)
        let idle = snapshot()
        precondition(idleFrames.insert(idle).inserted, "Identical appearance: \(skin.id)")
        print("Rendered \(skin.id)")
    }
    view.selectSkin("californian")
    let idle = snapshot()
    view.pressKey(0) // ANSI A
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.075))
    let typing = snapshot()
    precondition(typing != idle, "Key press did not animate")
    if let i = CommandLine.arguments.firstIndex(of: "--smoke-test"), i + 1 < CommandLine.arguments.count {
        do {
            try typing.write(to: URL(fileURLWithPath: CommandLine.arguments[i + 1]))
        } catch {
            fatalError("Cannot export test frame: \(error)")
        }
    }
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.42))
    view.moveMouse(dx: 140, dy: 160)
    view.clickMouse(right: false)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.085))
    precondition(snapshot() != idle, "Mouse input did not animate")
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.9))
    let parked = snapshot()
    let deadline = Date(timeIntervalSinceNow: 7)
    var closed = false
    var reopened = false
    while Date() < deadline && !reopened {
        RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.028))
        if snapshot() != parked { closed = true } else if closed { reopened = true }
    }
    precondition(reopened, "Idle pet did not blink and reopen its eyes")
    print("Verified all skins, keyboard/mouse animation and idle blinking")
}

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
    precondition(LocalPeers.normalizedNickname("  小猪  ") == "小猪")
    precondition(LocalPeers.normalizedNickname("") == "")
    precondition(LocalPeers.normalizedNickname("🐹‍🐹") == "🐹‍🐹")
    precondition(LocalPeers.normalizedNickname(String(repeating: "猪", count: 17)) == nil)
    precondition(LocalPeers.normalizedNickname("A\nB") == nil)
    precondition(LocalPeers.normalizedNickname("A\u{202E}B") == nil)
    func namePacket(_ id: String, _ name: String) -> [UInt8] {
        Array("WN1|\(id)|\(Data(name.utf8).base64EncodedString())".utf8)
    }
    var namedPeers: [String: LocalPeers.Peer] = [:]
    LocalPeers.apply(bytes: namePacket(remoteID, "小猪"), ownID: ownID, now: 11, peers: &namedPeers)
    precondition(namedPeers.isEmpty, "Name packet must not create a peer")
    LocalPeers.apply(bytes: Array(valid.utf8), ownID: ownID, now: 12, peers: &namedPeers)
    LocalPeers.apply(bytes: namePacket(remoteID, "小猪"), ownID: ownID, now: 13, peers: &namedPeers)
    precondition(namedPeers[remoteID]?.nickname == "小猪" && namedPeers[remoteID]?.seen == 12)
    LocalPeers.apply(bytes: Array(valid.utf8), ownID: ownID, now: 14, peers: &namedPeers)
    precondition(namedPeers[remoteID]?.nickname == "小猪" && namedPeers[remoteID]?.seen == 14)
    LocalPeers.apply(bytes: namePacket(remoteID, ""), ownID: ownID, now: 15, peers: &namedPeers)
    precondition(namedPeers[remoteID]?.nickname == "" && namedPeers[remoteID]?.seen == 14)
    precondition(parse(String(decoding: namePacket(remoteID, "小猪"), as: UTF8.self)) == nil)
    let peerMap = [parsed.0: parsed.1]
    precondition(LocalPeers.unexpired(peerMap, now: 17.99).count == 1)
    precondition(LocalPeers.unexpired(peerMap, now: 18).isEmpty)
    print("Verified LAN packet validation and expiry")
    let invite = "ABCDEFGHIJKLMNOP47UL"
    precondition(RoomTransport.normalizeCode("abcde-fghij-klmno-p47ul") == invite)
    precondition(RoomTransport.normalizeCode("abcde-fghij-klmno-p47um") == nil)
    precondition(LocalPeers.unexpired(peerMap, now: 29.99, expiry: 20).count == 1)
    precondition(LocalPeers.unexpired(peerMap, now: 30, expiry: 20).isEmpty)
    _ = NSApplication.shared
    let settings = PetSettingsViewController()
    let settingsView = settings.view
    func descendants(_ view: NSView) -> [NSView] {
        [view] + view.subviews.flatMap { descendants($0) }
    }
    guard let tabs = descendants(settingsView).compactMap({ $0 as? NSSegmentedControl }).first else {
        fatalError("Settings tabs missing")
    }
    precondition(tabs.selectedSegment == 0, "Settings must start on pet tab")
    tabs.selectedSegment = 1
    tabs.sendAction(tabs.action, to: tabs.target)
    let firstPeer = LocalPeers.Peer(skin: "longhair", keys: 12_345, clicks: 100_000_000, seen: 10)
    settings.refresh(skinID: "tricolor", scale: 1, showLabels: true, clickThrough: false,
                     hideForSogou: true, networking: true, keys: 98_765, clicks: 3,
                     peers: [(remoteID, firstPeer)], networkStatus: "连接中", roomCode: invite, roomConnected: false, nickname: "")
    settingsView.layoutSubtreeIfNeeded()
    func visibleText() -> [NSTextField] {
        descendants(settingsView).compactMap { $0 as? NSTextField }.filter { !$0.isHiddenOrHasHiddenAncestor }
    }
    guard let rename = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "改名" }) else {
        fatalError("Rename button missing")
    }
    rename.sendAction(rename.action, to: rename.target)
    guard let nameInput = descendants(settingsView).compactMap({ $0 as? NSTextField }).first(where: { $0.placeholderString == "留空使用毛色名" }) else {
        fatalError("Nickname editor missing")
    }
    nameInput.stringValue = "未保存的草稿"
    settings.refresh(skinID: "tricolor", scale: 1, showLabels: true, clickThrough: false,
                     hideForSogou: true, networking: true, keys: 98_766, clicks: 3,
                     peers: [(remoteID, firstPeer)], networkStatus: "连接中", roomCode: invite, roomConnected: false, nickname: "")
    precondition(nameInput.stringValue == "未保存的草稿", "Refresh replaced nickname draft")
    let firstTexts = visibleText()
    precondition(firstTexts.contains { $0.stringValue == "房间：连接中" })
    precondition(firstTexts.contains { $0.stringValue == "按键 1.2万" && $0.toolTip == "按键 12345" })
    precondition(firstTexts.contains { $0.stringValue == "点击 1亿" && $0.toolTip == "点击 100000000" })
    guard let leave = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "退出房间" }) else {
        fatalError("Leave room button missing")
    }
    let leaveBounds = settingsView.convert(leave.bounds, from: leave)
    precondition(settingsView.bounds.contains(leaveBounds), "Room controls outside settings view")
    settings.refresh(skinID: "tricolor", scale: 1, showLabels: true, clickThrough: false,
                     hideForSogou: true, networking: true, keys: 98_765, clicks: 3,
                     peers: [], networkStatus: "已连接", roomCode: invite, roomConnected: true, nickname: "")
    let nextTexts = visibleText()
    precondition(nextTexts.contains { $0.stringValue == "房间已连接 · 伙伴 0" })
    precondition(!nextTexts.contains { $0.stringValue == "长毛猪 · bbbb" })
    print("Verified settings tab, room state and peer refresh")
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

import AppKit

// Runs against the packaged application on both Mac architectures in CI.
@MainActor func runSmokeTest() {
    func releaseData(_ tag: String, asset: String = "WheekKeys-macOS.zip", host: String = "github.com", prerelease: Bool = false) -> Data {
        let json: [String: Any] = ["tag_name": tag, "draft": false, "prerelease": prerelease,
            "assets": [["name": asset, "browser_download_url": "https://\(host)/gwtt/guinea-pig-keyboard/releases/download/\(tag)/\(asset)"]]]
        return try! JSONSerialization.data(withJSONObject: json)
    }
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0"), currentVersion: "0.1.5")) != nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.1.5"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.1.4"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0", asset: "WheekKeys.exe"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0", host: "example.com"), currentVersion: "0.1.5")) == nil)
    precondition((try! PetUpdateChecker.updateURL(data: releaseData("v0.2.0", prerelease: true), currentVersion: "0.1.5")) == nil)
    precondition((try? PetUpdateChecker.updateURL(data: releaseData("v0.2.0-beta"), currentVersion: "0.1.5")) == nil)
    precondition(initialPetScale(0) == 0.58 && initialPetScale(-1) == 0.58)
    precondition(initialPetScale(1.25) == 1.25 && initialPetScale(0.1) == 0.5 && initialPetScale(3) == 2)
    let ownID = String(repeating: "a", count: 32)
    let remoteID = String(repeating: "b", count: 32)
    func parse(_ text: String) -> (String, LocalPeers.Peer)? {
        LocalPeers.parse(bytes: Array(text.utf8), ownID: ownID, now: 10)
    }
    let valid = "WK1|\(remoteID)|longhair|123|45"
    guard let parsed = parse(valid) else { fatalError("Valid peer packet rejected") }
    precondition(parsed.0 == remoteID && parsed.1.keys == 123 && parsed.1.clicks == 45)
    precondition(parsed.1.skin == "pixel-longhair-gold-white", "Old peer coat was not mapped")
    precondition(PetSkin.named("gold-white").id == "pixel-golden-white", "Old profile coat was not mapped")
    precondition(PetSkin.all.first?.id == "pixel-tricolor", "Pixel appearance must remain the default")
    precondition(PetSkin.all.count == 14)
    for (legacy, pixel) in [("tricolor", "tricolor"), ("californian", "californian"),
                            ("gold-white", "golden-white"), ("teddy", "chocolate"),
                            ("silver-agouti", "black-white"), ("longhair", "longhair-gold-white")] {
        precondition(PetSkin.named("drawn-" + legacy).id == "pixel-" + pixel)
        precondition(parse("WK1|\(remoteID)|drawn-\(legacy)|1|2")?.1.skin == "pixel-" + pixel)
    }
    precondition(PetSkin.find("drawn-unknown") == nil)
    precondition(parse("WK1|\(remoteID)|drawn-unknown|1|2") == nil)
    precondition(PetSkin.named("longhair").id == "pixel-longhair-gold-white", "Legacy network coat was not preserved")
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
    func apply(_ message: String, now: TimeInterval) -> Bool {
        LocalPeers.apply(bytes: Array(message.utf8), ownID: ownID, now: now, peers: &namedPeers)
    }
    let appearance = "WS1|\(remoteID)|drawn-tricolor|1"
    let action = "WA1|\(remoteID)|1|k30,k328,l,r"
    precondition(RoomTransport.validPlaintext(appearance) && RoomTransport.validPlaintext(action))
    precondition(!RoomTransport.validPlaintext("WA1|\(remoteID)|1|") &&
                 !RoomTransport.validPlaintext(action + "\n"))
    precondition(namedPeers[remoteID]?.supportsActivity == false && !apply(action, now: 16))
    let previousSkin = namedPeers[remoteID]?.skin
    precondition(apply("WS1|\(remoteID)|drawn-unknown|1", now: 16))
    precondition(namedPeers[remoteID]?.skin == previousSkin && namedPeers[remoteID]?.supportsActivity == true &&
                 namedPeers[remoteID]?.sharingActivity == true)
    precondition(apply(appearance, now: 16) && apply(action, now: 16))
    precondition(apply(valid, now: 16.1) && namedPeers[remoteID]?.skin == "pixel-tricolor")
    precondition(!apply(action, now: 16.2) && !apply("WA1|\(remoteID)|2|k0,l", now: 16.2))
    precondition(namedPeers[remoteID]?.takeActions(now: 16.2) == [.key(0), .key(126), .click(false), .click(true)])
    precondition(apply("WA1|\(remoteID)|2|k57", now: 17))
    precondition(namedPeers[remoteID]?.takeActions(now: 17.9) == [])
    precondition(apply("WA1|\(remoteID)|3|r", now: 18))
    precondition(apply("WS1|\(remoteID)|drawn-tricolor|0", now: 18))
    precondition(namedPeers[remoteID]?.supportsActivity == true && namedPeers[remoteID]?.sharingActivity == false)
    precondition(!apply("WA1|\(remoteID)|4|l", now: 18) && namedPeers[remoteID]?.takeActions(now: 18) == [])
    precondition(!apply("WS1|\(ownID)|drawn-tricolor|1", now: 18) && namedPeers.count == 1)
    precondition(NetworkKeyCodes.scan(for: 0) == 30 && NetworkKeyCodes.scan(for: 49) == 57 &&
                 NetworkKeyCodes.scan(for: 62) == 285 && NetworkKeyCodes.scan(for: 76) == 284)
    print("Verified cross-platform activity packets, key codes, pause and expiry")
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
    func updateButton() -> NSButton? {
        descendants(settingsView).compactMap { $0 as? NSButton }.first { $0.title == "更新到最新版本" }
    }
    precondition(updateButton()?.isHiddenOrHasHiddenAncestor ?? true)
    settings.setUpdateAvailable(true)
    settingsView.layoutSubtreeIfNeeded()
    guard let availableUpdateButton = updateButton() else {
        fatalError("Update button missing")
    }
    precondition(!availableUpdateButton.isHidden)
    settings.setUpdateAvailable(false)
    precondition(availableUpdateButton.isHidden)
    settingsView.layoutSubtreeIfNeeded()
    let controls = descendants(settingsView).compactMap { $0 as? NSButton }
    guard let minus = controls.first(where: { $0.title == "−" }), let plus = controls.first(where: { $0.title == "+" }),
          let scale = descendants(settingsView).compactMap({ $0 as? NSTextField }).first(where: { $0.stringValue == "58%" }) else {
        fatalError("Scale controls missing")
    }
    let minusFrame = settingsView.convert(minus.bounds, from: minus)
    let scaleFrame = settingsView.convert(scale.bounds, from: scale)
    let plusFrame = settingsView.convert(plus.bounds, from: plus)
    precondition(minusFrame.minX < scaleFrame.minX && scaleFrame.minX < plusFrame.minX && plusFrame.maxX < settingsView.bounds.midX + 50)
    settings.refresh(skinID: "pixel-tricolor", scale: initialPetScale(0), showLabels: true, clickThrough: false,
                     networking: false, keys: 0, clicks: 0, peers: [], networkStatus: "", roomCode: nil, roomConnected: false, nickname: "")
    precondition(scale.stringValue == "58%")
    settings.refresh(skinID: "pixel-tricolor", scale: initialPetScale(1.25), showLabels: true, clickThrough: false,
                     networking: false, keys: 0, clicks: 0, peers: [], networkStatus: "", roomCode: nil, roomConnected: false, nickname: "")
    precondition(scale.stringValue == "125%")
    let skinButtons = descendants(settingsView).compactMap { $0 as? NSButton }.filter {
        let id = $0.identifier?.rawValue ?? ""
        return id.hasPrefix("pixel-")
    }
    precondition(skinButtons.count == 14 && skinButtons.allSatisfy { settingsView.bounds.contains(settingsView.convert($0.bounds, from: $0)) })
    precondition(tabs.segmentCount == 3 && tabs.label(forSegment: 2) == "QQ 群")
    tabs.selectedSegment = 2
    tabs.sendAction(tabs.action, to: tabs.target)
    let groupCode = descendants(settingsView).compactMap { $0 as? NSImageView }.first { $0.accessibilityLabel() == "QQ 群二维码" }
    precondition(groupCode?.isHiddenOrHasHiddenAncestor == false && groupCode?.image?.isValid == true,
                 "QQ 群页面缺少二维码")
    tabs.selectedSegment = 1
    tabs.sendAction(tabs.action, to: tabs.target)
    precondition(groupCode?.isHiddenOrHasHiddenAncestor == true)
    let firstPeer = LocalPeers.Peer(skin: "pixel-longhair-gold-white", keys: 12_345, clicks: 100_000_000, seen: 10)
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     networking: true, keys: 98_765, clicks: 3,
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
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     networking: true, keys: 98_766, clicks: 3,
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
    settings.refresh(skinID: "pixel-tricolor", scale: 1, showLabels: true, clickThrough: false,
                     networking: true, keys: 98_765, clicks: 3,
                     peers: [], networkStatus: "已连接", roomCode: invite, roomConnected: true, nickname: "")
    let nextTexts = visibleText()
    precondition(nextTexts.contains { $0.stringValue == "房间已连接 · 伙伴 0" })
    precondition(!nextTexts.contains { $0.stringValue == "像素长毛黄白 · bbbb" })
    guard let share = descendants(settingsView).compactMap({ $0 as? NSButton }).first(where: { $0.title == "同步按键和鼠标动作" }) else {
        fatalError("Activity sharing switch missing")
    }
    var shared = true
    settings.onShareActivityChange = { shared = $0 }
    share.state = .off
    share.sendAction(share.action, to: share.target)
    precondition(!shared, "Activity sharing switch did not notify owner")
    let workArea = NSRect(x: 0, y: 0, width: 1280, height: 720)
    let corner = NSRect(x: 10, y: 10, width: 310, height: 238.7)
    let friendFrames = FriendDesktop.frames(around: corner, within: workArea, count: 4)
    precondition(friendFrames.count == 4 && friendFrames.allSatisfy { workArea.contains($0) && !$0.intersects(corner) })
    for index in friendFrames.indices {
        precondition(!friendFrames[..<index].contains { $0.intersects(friendFrames[index]) })
    }
    let owner = NSPanel(contentRect: NSRect(x: 200, y: 200, width: 310, height: 238.7), styleMask: [.borderless], backing: .buffered, defer: false)
    owner.orderFrontRegardless()
    let desktop = FriendDesktop()
    var delivered = false
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: true, showLabels: true) { id in
        delivered = id == remoteID
        return [.key(0), .click(false), .click(true)]
    }
    guard let friend = desktop.window(for: remoteID) else { fatalError("Friend window missing") }
    precondition(desktop.count == 1 && friend.isVisible && friend.ignoresMouseEvents && delivered)
    precondition(friend.isMovable && friend.isMovableByWindowBackground && friend.contentView?.mouseDownCanMoveWindow == true,
                 "好友窗口未启用背景拖动")
    let initialOrigin = friend.frame.origin
    owner.setFrameOrigin(NSPoint(x: owner.frame.minX, y: owner.frame.minY + 12))
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: false) { _ in [] }
    precondition(desktop.window(for: remoteID) === friend && !friend.ignoresMouseEvents, "Refresh replaced friend")
    let arrangedOrigin = friend.frame.origin
    precondition(arrangedOrigin != initialOrigin, "未拖动的好友窗口应随本机桌宠重新排布")
    friend.setFrameOrigin(NSPoint(x: arrangedOrigin.x + 12, y: arrangedOrigin.y + 12))
    let movedOrigin = friend.frame.origin
    precondition(movedOrigin != arrangedOrigin)
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: false) { _ in [.key(0)] }
    precondition(friend.frame.origin == movedOrigin, "联机刷新重置了好友窗口位置")
    owner.setContentSize(NSSize(width: 290, height: 220))
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: false) { _ in [] }
    precondition(friend.frame.origin == movedOrigin && friend.frame.size == owner.frame.size,
                 "好友窗口应跟随缩放并保留拖动后的位置")
    owner.orderOut(nil)
    desktop.update(owner: owner, members: [remoteID: firstPeer], clickThrough: false, showLabels: true) { _ in [] }
    precondition(!friend.isVisible, "Friend remained visible after owner hid")
    desktop.update(owner: owner, members: [:], clickThrough: false, showLabels: true) { _ in [] }
    precondition(desktop.count == 0, "Leaving did not remove friend")
    desktop.close()
    owner.close()
    print("Verified settings, room state and desktop friend windows")
    let size = NSRect(x: 0, y: 0, width: 400, height: 308)
    let window = NSWindow(contentRect: size, styleMask: [.borderless], backing: .buffered, defer: false)
    let view = PetView(frame: size)
    window.contentView = view

    func snapshot() -> Data {
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            fatalError("No native bitmap context")
        }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        let visibleBody = (1..<10).contains { column in
            let color = bitmap.colorAt(x: bitmap.pixelsWide * column / 10, y: bitmap.pixelsHigh / 2)
            return (color?.alphaComponent ?? 0) > 0.9
        }
        guard visibleBody, let png = bitmap.representation(using: .png, properties: [:]) else {
            fatalError("Empty desktop pet frame")
        }
        return png
    }

    var idleFrames = Set<Data>()
    let root = Bundle.main.resourceURL!
    for skin in PetSkin.all {
        let files = [skin.body, skin.paw, "keyboard.png", "mouse.png", "mouth-peep.png", "mouth-yawn.png"]
        for file in files {
            precondition(NSImage(contentsOf: root.appendingPathComponent(file)) != nil, "Missing sprite: \(file)")
        }
        view.selectSkin(skin.id)
        let idle = snapshot()
        precondition(idleFrames.insert(idle).inserted, "Identical appearance: \(skin.id)")
        precondition(skin.portrait(size: NSSize(width: 38, height: 40)) != nil, "Missing portrait: \(skin.id)")
        print("Rendered \(skin.id)")
    }
    view.selectSkin("pixel-californian")
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
    let beforeScroll = snapshot()
    view.scrollMouse(-120)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.09))
    precondition(snapshot() != beforeScroll, "Mouse wheel did not animate")
    precondition(PetView.dozePose(idle: 29.99) == nil)
    precondition(PetView.dozePose(idle: 30)?.frame.eyes == 1)
    precondition(PetView.dozePose(idle: 31.5)?.frame.mouth == 2)
    let restingTime = Date()
    view.noteInput(ProcessInfo.processInfo.systemUptime)
    RunLoop.current.run(until: restingTime.addingTimeInterval(1))
    let resting = snapshot()
    RunLoop.current.run(until: restingTime.addingTimeInterval(31.5))
    precondition(snapshot() != resting, "Idle timer did not render the yawn")
    view.pressKey(0)
    RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.65))
    precondition(snapshot() == resting, "Key input did not restore the awake face")
    print("Verified all skins, keyboard/mouse animation, blinking, idle yawn and wake-up")
}

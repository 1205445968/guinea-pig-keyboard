import AppKit

final class PetUpdateChecker {
    private struct Release: Decodable {
        struct Asset: Decodable { let name: String; let browser_download_url: String }
        let tag_name: String
        let draft: Bool
        let prerelease: Bool
        let assets: [Asset]
    }
    private enum InvalidRelease: Error { case version }
    private let endpoint = URL(string: "https://api.github.com/repos/gwtt/guinea-pig-keyboard/releases/latest")!
    private static let releasePage = URL(string: "https://github.com/gwtt/guinea-pig-keyboard/releases/latest")!
    private var checking = false
    private var lastSuccess: TimeInterval?
    private(set) var downloadURL: URL?

    private static func version(_ value: String) -> [Int]? {
        let value = value.hasPrefix("v") ? String(value.dropFirst()) : value
        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
        guard (2...3).contains(parts.count), parts.allSatisfy({ !$0.isEmpty && $0.allSatisfy { $0.isASCII && $0.isNumber } }),
              parts.allSatisfy({ Int($0) != nil }) else { return nil }
        return parts.map { Int($0)! } + Array(repeating: 0, count: 3 - parts.count)
    }

    static func updateURL(data: Data, currentVersion: String) throws -> URL? {
        let release = try JSONDecoder().decode(Release.self, from: data)
        guard let current = version(currentVersion), let latest = version(release.tag_name) else { throw InvalidRelease.version }
        guard !release.draft, !release.prerelease, current.lexicographicallyPrecedes(latest), let asset = release.assets.first(where: { $0.name == "WheekKeys-macOS.zip" }),
              let url = URL(string: asset.browser_download_url), url.scheme == "https", url.host == "github.com",
              url.user == nil, url.password == nil, url.port == nil, url.query == nil, url.fragment == nil,
              url.path == "/gwtt/guinea-pig-keyboard/releases/download/\(release.tag_name)/WheekKeys-macOS.zip" else { return nil }
        return url
    }

    static func updateURL(releasePage page: URL, currentVersion: String) throws -> URL? {
        let tag = page.lastPathComponent
        guard page.scheme == "https", page.host == releasePage.host, page.user == nil, page.password == nil,
              page.port == nil, page.query == nil, page.fragment == nil, tag.hasPrefix("v"),
              page.path == "/gwtt/guinea-pig-keyboard/releases/tag/\(tag)",
              let latest = version(tag), let current = version(currentVersion) else { throw InvalidRelease.version }
        guard current.lexicographicallyPrecedes(latest) else { return nil }
        return URL(string: "https://github.com/gwtt/guinea-pig-keyboard/releases/download/\(tag)/WheekKeys-macOS.zip")
    }

    func check(completion: @escaping () -> Void) {
        if checking || lastSuccess.map({ ProcessInfo.processInfo.systemUptime - $0 < 300 }) == true { return }
        checking = true
        var request = URLRequest(url: endpoint, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 10)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("WheekKeys-macOS", forHTTPHeaderField: "User-Agent")
        let current = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            guard let self = self else { return }
            if let status = (response as? HTTPURLResponse)?.statusCode, status == 403 || status == 429 {
                self.checkReleasePage(current: current, completion: completion)
                return
            }
            let result: Result<URL?, Error>
            if error == nil, (response as? HTTPURLResponse)?.statusCode == 200, let data = data {
                result = Result { try Self.updateURL(data: data, currentVersion: current) }
            } else {
                result = .failure(error ?? URLError(.badServerResponse))
            }
            self.finish(result, completion: completion)
        }.resume()
    }

    private func checkReleasePage(current: String, completion: @escaping () -> Void) {
        var request = URLRequest(url: Self.releasePage, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 10)
        request.httpMethod = "HEAD"
        URLSession.shared.dataTask(with: request) { [weak self] _, response, error in
            guard let self = self else { return }
            guard let response = response as? HTTPURLResponse, response.statusCode == 200, let page = response.url else {
                self.finish(.failure(error ?? URLError(.badServerResponse)), completion: completion)
                return
            }
            self.finish(Result { try Self.updateURL(releasePage: page, currentVersion: current) }, completion: completion)
        }.resume()
    }

    private func finish(_ result: Result<URL?, Error>, completion: @escaping () -> Void) {
        DispatchQueue.main.async {
            self.checking = false
            if case .success(let url) = result {
                self.downloadURL = url
                self.lastSuccess = ProcessInfo.processInfo.systemUptime
            }
            completion()
        }
    }
}

final class PetSettingsViewController: NSViewController {
    var onSelectSkin: ((String) -> Void)?
    var onSaveNickname: ((String) -> Bool)?
    var onScaleChange: ((Double) -> Void)?
    var onShowLabelsChange: ((Bool) -> Void)?
    var onClickThroughChange: ((Bool) -> Void)?
    var onLoginAtStartupChange: ((Bool) -> Void)?
    var onShareActivityChange: ((Bool) -> Void)?
    var onCreateRoom: ((Bool) -> Void)?
    var onJoinRoom: ((String) -> Void)?
    var onJoinPublicRoom: ((RoomDirectory.Entry) -> Void)?
    var onExitRoom: (() -> Void)?
    var onQuit: (() -> Void)?
    var onClose: (() -> Void)?
    var onDownloadUpdate: (() -> Void)?

    private let skinButtons = Dictionary(uniqueKeysWithValues: PetSkin.all.map { ($0.id, NSButton()) })
    private let updateButton = NSButton(title: "更新到最新版本", target: nil, action: nil)
    private let scaleValue = NSTextField(labelWithString: "58%")
    private let labelsSwitch = NSButton(checkboxWithTitle: "显示按键气泡", target: nil, action: nil)
    private let clickThroughSwitch = NSButton(checkboxWithTitle: "鼠标穿透", target: nil, action: nil)
    private let loginSwitch = NSButton(checkboxWithTitle: "开机自启", target: nil, action: nil)
    private let loginMessage = NSTextField(labelWithString: "")
    private var loginRegistered = false

    private let shareSwitch = NSButton(checkboxWithTitle: "同步按键和鼠标动作", target: nil, action: nil)
    private let tabs = NSSegmentedControl(labels: ["桌宠", "联机", "QQ 群"], trackingMode: .selectOne, target: nil, action: nil)
    private let petScroll = NSScrollView()
    private let petPage = FlippedView()
    private let roomPage = NSView()
    private let groupPage = NSView()
    private let createRoom = NSButton(title: "创建房间", target: nil, action: nil)
    private let joinCode = NSTextField()
    private let joinRoom = NSButton(title: "加入", target: nil, action: nil)
    private let roomCodeLabel = NSTextField(labelWithString: "")
    private let copyRoom = NSButton(title: "复制", target: nil, action: nil)
    private let exitRoom = NSButton(title: "退出房间", target: nil, action: nil)
    private let roomHint = NSTextField(labelWithString: "")
    private let roomVisibility = NSPopUpButton(frame: .zero, pullsDown: false)
    private let discoverRooms = NSButton(title: "发现公开房间", target: nil, action: nil)
    private let closeDiscovery = NSButton(title: "返回", target: nil, action: nil)
    private let publicRooms = NSPopUpButton(frame: .zero, pullsDown: false)
    private let joinPublic = NSButton(title: "加入", target: nil, action: nil)
    private var directory: RoomDirectory?
    private var publicEntries: [RoomDirectory.Entry] = []
    private let networkState = NSTextField(labelWithString: "")
    private let networkMessage = NSTextField(labelWithString: "")
    private let emptyMessage = NSTextField(wrappingLabelWithString: "")
    private let localRow = PeerRow(frame: .zero)
    private let peerScroll = NSScrollView()
    private let peerDocument = FlippedView()
    private var peerRows: [String: PeerRow] = [:]
    private var portraitCache: [String: NSImage] = [:]
    private var compactConstraints: [(NSLayoutConstraint, CGFloat)] = []
    private var networkError = ""

    private let paper = NSColor(calibratedRed: 250 / 255, green: 246 / 255, blue: 235 / 255, alpha: 1)
    private let darkBrown = NSColor(calibratedRed: 77 / 255, green: 54 / 255, blue: 39 / 255, alpha: 1)
    private let mutedBrown = NSColor(calibratedRed: 143 / 255, green: 119 / 255, blue: 94 / 255, alpha: 1)
    private let sage = NSColor(calibratedRed: 224 / 255, green: 235 / 255, blue: 216 / 255, alpha: 1)

    override func loadView() {
        let root = SettingsCardView(frame: NSRect(x: 0, y: 0, width: 316, height: 508))
        root.appearance = NSAppearance(named: .aqua)
        root.wantsLayer = true
        root.layer?.backgroundColor = paper.cgColor
        root.layer?.cornerRadius = 18
        root.layer?.borderWidth = 1
        root.layer?.borderColor = NSColor(calibratedRed: 225 / 255, green: 214 / 255, blue: 194 / 255, alpha: 1).cgColor
        root.closeAction = { [weak self] in self?.onClose?() }
        view = root

        let avatar = NSImageView()
        if let url = Bundle.main.url(forResource: "avatar", withExtension: "png") {
            avatar.image = NSImage(contentsOf: url)
        }
        avatar.imageScaling = .scaleProportionallyUpOrDown
        avatar.wantsLayer = true
        avatar.layer?.cornerRadius = 10
        avatar.layer?.masksToBounds = true
        add(avatar, x: 18, top: 15, width: 42, height: 42, to: root)

        let title = NSTextField(labelWithString: "豚鼠键盘")
        title.font = .systemFont(ofSize: 17, weight: .bold)
        title.textColor = darkBrown
        add(title, x: 70, top: 12, width: 90, height: 24, to: root)

        let version = NSTextField(labelWithString: "v" + (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "未知"))
        version.font = .systemFont(ofSize: 11)
        version.textColor = mutedBrown
        add(version, x: 70, top: 37, width: 180, height: 18, to: root)
        updateButton.target = self
        updateButton.action = #selector(downloadUpdate)
        updateButton.isBordered = false
        updateButton.font = .systemFont(ofSize: 11)
        updateButton.contentTintColor = darkBrown
        updateButton.toolTip = "在浏览器下载 macOS 版本，需手动安装"
        updateButton.isHidden = true
        add(updateButton, x: 166, top: 12, width: 106, height: 24, to: root)

        let closeButton = NSButton(title: "×", target: self, action: #selector(closePanel))
        closeButton.isBordered = false
        closeButton.font = .systemFont(ofSize: 19, weight: .regular)
        closeButton.contentTintColor = mutedBrown
        add(closeButton, trailing: 12, top: 12, width: 30, height: 30, to: root)

        tabs.target = self
        tabs.action = #selector(changeTab)
        tabs.selectedSegment = 0
        add(tabs, x: 18, top: 68, width: 280, height: 27, to: root)
        petScroll.hasVerticalScroller = true
        petScroll.drawsBackground = false
        petScroll.borderType = .noBorder
        petPage.frame = NSRect(x: 0, y: 0, width: 316, height: 370)
        petScroll.documentView = petPage
        add(petScroll, x: 0, top: 100, width: 316, height: 352, to: root)
        add(roomPage, x: 0, top: 100, width: 316, height: 352, to: root)
        roomPage.isHidden = true
        add(groupPage, x: 18, top: 100, width: 280, height: 352, to: root)
        groupPage.isHidden = true

        let groupHint = NSTextField(labelWithString: "用手机 QQ 扫码加群")
        groupHint.font = .systemFont(ofSize: 12)
        groupHint.textColor = mutedBrown
        groupHint.alignment = .center
        add(groupHint, x: 0, top: 43, width: 280, height: 18, to: groupPage)
        let groupCode = NSImageView()
        if let url = Bundle.main.url(forResource: "qq-group", withExtension: "png") {
            groupCode.image = NSImage(contentsOf: url)
        }
        groupCode.imageScaling = .scaleProportionallyUpOrDown
        groupCode.setAccessibilityLabel("QQ 群二维码")
        add(groupCode, x: 28, top: 71, width: 224, height: 224, to: groupPage)

        let coatLabel = sectionLabel("毛色")
        add(coatLabel, x: 18, top: 14, width: 200, height: 18, to: petPage)

        let controlOffset = CGFloat(((PetSkin.all.count + 2) / 3 - 2) * 34)
        for (index, skin) in PetSkin.all.enumerated() {
            let button = skinButtons[skin.id]!
            button.target = self
            button.action = #selector(selectSkin(_:))
            button.identifier = NSUserInterfaceItemIdentifier(skin.id)
            button.isBordered = false
            button.wantsLayer = true
            button.layer?.cornerRadius = 8
            button.imagePosition = .imageLeading
            button.imageScaling = .scaleProportionallyUpOrDown
            button.font = .systemFont(ofSize: 12, weight: .medium)
            button.alignment = .center
            button.image = skin.portrait(size: NSSize(width: 16, height: 17))
            button.setButtonType(.momentaryPushIn)
            add(button, x: 18 + CGFloat(index % 3) * 95, top: 37 + CGFloat(index / 3) * 34,
                width: 90, height: 30, to: petPage)
        }

        let sizeLabel = sectionLabel("大小")
        add(sizeLabel, x: 18, top: 120 + controlOffset, width: 40, height: 18, to: petPage)
        scaleValue.font = .systemFont(ofSize: 12, weight: .semibold)
        scaleValue.textColor = mutedBrown
        scaleValue.alignment = .center
        add(scaleValue, x: 100, top: 120 + controlOffset, width: 45, height: 18, to: petPage)

        let minus = controlButton("−", action: #selector(decreaseScale))
        add(minus, x: 68, top: 114 + controlOffset, width: 30, height: 30, to: petPage)
        let plus = controlButton("+", action: #selector(increaseScale))
        add(plus, x: 147, top: 114 + controlOffset, width: 30, height: 30, to: petPage)

        labelsSwitch.target = self
        labelsSwitch.action = #selector(toggleLabels(_:))
        labelsSwitch.font = .systemFont(ofSize: 12)
        labelsSwitch.contentTintColor = darkBrown
        add(labelsSwitch, x: 18, top: 157 + controlOffset, width: 270, height: 22, to: petPage)
        clickThroughSwitch.target = self
        clickThroughSwitch.action = #selector(toggleClickThrough(_:))
        clickThroughSwitch.font = .systemFont(ofSize: 12)
        clickThroughSwitch.contentTintColor = darkBrown
        add(clickThroughSwitch, x: 18, top: 188 + controlOffset, width: 270, height: 22, to: petPage)

        loginSwitch.allowsMixedState = true
        loginSwitch.target = self
        loginSwitch.action = #selector(toggleLoginAtStartup(_:))
        loginSwitch.font = .systemFont(ofSize: 12)
        loginSwitch.contentTintColor = darkBrown
        add(loginSwitch, x: 18, top: 219 + controlOffset, width: 270, height: 22, to: petPage)
        loginMessage.font = .systemFont(ofSize: 11)
        loginMessage.textColor = mutedBrown
        add(loginMessage, x: 18, top: 243 + controlOffset, width: 280, height: 18, to: petPage)

        createRoom.target = self
        createRoom.action = #selector(createRoomAction)
        add(createRoom, x: 18, top: 10, width: 88, height: 26, to: roomPage)
        joinCode.placeholderString = "输入房间码"
        add(joinCode, x: 112, top: 12, width: 118, height: 24, to: roomPage)
        joinRoom.target = self
        joinRoom.action = #selector(joinRoomAction)
        add(joinRoom, x: 233, top: 10, width: 65, height: 26, to: roomPage)
        roomCodeLabel.font = .monospacedSystemFont(ofSize: 11, weight: .medium)
        add(roomCodeLabel, x: 18, top: 12, width: 163, height: 23, to: roomPage)
        copyRoom.target = self
        copyRoom.action = #selector(copyRoomAction)
        add(copyRoom, x: 180, top: 10, width: 50, height: 26, to: roomPage)
        exitRoom.target = self
        exitRoom.action = #selector(exitRoomAction)
        add(exitRoom, x: 232, top: 10, width: 66, height: 26, to: roomPage)
        roomHint.font = .systemFont(ofSize: 10)
        roomHint.textColor = mutedBrown
        add(roomHint, x: 18, top: 43, width: 280, height: 18, to: roomPage)
        roomVisibility.addItems(withTitles: ["私密", "公开"])
        roomVisibility.selectItem(at: 0)
        roomVisibility.setAccessibilityLabel("房间可见性")
        roomVisibility.toolTip = "私密房间通过房间码邀请，公开房间可被发现。每间最多 16 人。"
        add(roomVisibility, x: 18, top: 39, width: 88, height: 26, to: roomPage)
        discoverRooms.target = self
        discoverRooms.action = #selector(toggleRoomDiscovery)
        add(discoverRooms, x: 112, top: 39, width: 118, height: 26, to: roomPage)
        closeDiscovery.target = self
        closeDiscovery.action = #selector(toggleRoomDiscovery)
        add(closeDiscovery, x: 18, top: 39, width: 65, height: 26, to: roomPage)
        publicRooms.target = self
        publicRooms.action = #selector(updatePublicJoin)
        publicRooms.setAccessibilityLabel("公开房间列表")
        add(publicRooms, x: 88, top: 39, width: 142, height: 26, to: roomPage)
        joinPublic.target = self
        joinPublic.action = #selector(joinPublicRoomAction)
        add(joinPublic, x: 233, top: 39, width: 65, height: 26, to: roomPage)
        updateDiscoveryControls(enabled: true)

        networkState.font = .systemFont(ofSize: 11, weight: .semibold)
        networkState.alignment = .left
        add(networkState, x: 18, top: 65, width: 280, height: 22, to: roomPage)
        shareSwitch.target = self
        shareSwitch.action = #selector(toggleShareActivity(_:))
        shareSwitch.font = .systemFont(ofSize: 12)
        add(shareSwitch, x: 18, top: 88, width: 280, height: 22, to: roomPage)
        networkMessage.font = .systemFont(ofSize: 11)
        networkMessage.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        networkMessage.lineBreakMode = .byTruncatingTail
        add(networkMessage, x: 18, top: 111, width: 280, height: 18, to: roomPage)
        let mine = sectionLabel("本机")
        add(mine, x: 18, top: 133, width: 200, height: 18, to: roomPage)
        add(localRow, x: 18, top: 153, width: 280, height: 56, to: roomPage)
        localRow.layer?.backgroundColor = sage.cgColor
        localRow.canRename = true
        localRow.onSaveNickname = { [weak self] value in self?.onSaveNickname?(value) ?? false }
        let online = sectionLabel("联机伙伴")
        add(online, x: 18, top: 220, width: 200, height: 18, to: roomPage)
        peerScroll.hasVerticalScroller = true
        peerScroll.drawsBackground = false
        peerScroll.borderType = .noBorder
        peerScroll.documentView = peerDocument
        add(peerScroll, x: 18, top: 241, width: 280, height: 100, to: roomPage)
        emptyMessage.font = .systemFont(ofSize: 12)
        emptyMessage.textColor = mutedBrown
        add(emptyMessage, x: 26, top: 246, width: 260, height: 48, to: roomPage)
        let compactViews: [NSView] = [mine, localRow, online, peerScroll, emptyMessage]
        compactConstraints = roomPage.constraints.compactMap { constraint in
            guard constraint.firstAttribute == .top, let child = constraint.firstItem as? NSView,
                  compactViews.contains(where: { $0 === child }) else { return nil }
            return (constraint, constraint.constant)
        }

        let divider = NSBox()
        divider.boxType = .separator
        add(divider, x: 18, top: 452, width: 280, height: 1, to: root)

        let feedback = footerButton("反馈问题", action: #selector(openFeedback))
        add(feedback, x: 18, top: 465, width: 136, height: 32, to: root)
        let quit = footerButton("退出", action: #selector(quitApp))
        quit.contentTintColor = NSColor(calibratedRed: 155 / 255, green: 79 / 255, blue: 63 / 255, alpha: 1)
        add(quit, trailing: 18, top: 465, width: 136, height: 32, to: root)
    }

    func setUpdateAvailable(_ available: Bool) {
        updateButton.isHidden = !available
    }

    @objc private func downloadUpdate() { onDownloadUpdate?() }

    func refresh(skinID: String, scale: Double, showLabels: Bool, clickThrough: Bool,
                 keys: UInt64, clicks: UInt64,
                 peers: [(String, LocalPeers.Peer)], networkStatus: String,
                 roomCode: String?, roomConnected: Bool, nickname: String, shareActivity: Bool = true,
                 shareActivityAvailable: Bool = true, roomPublic: Bool = false) {
        shareSwitch.state = shareActivity ? .on : .off
        shareSwitch.isEnabled = shareActivityAvailable
        let inRoom = roomCode != nil
        if inRoom { directory = nil }
        updateDiscoveryControls(enabled: !inRoom)
        createRoom.isHidden = inRoom
        joinCode.isHidden = inRoom
        joinRoom.isHidden = inRoom
        roomCodeLabel.isHidden = !roomConnected
        copyRoom.isHidden = !roomConnected
        exitRoom.isHidden = !inRoom
        roomHint.isHidden = !roomConnected
        roomHint.stringValue = roomPublic ? "其他人可以从公开房间列表加入。" : "私密房间通过房间码邀请，每间最多 16 人。"
        if let code = roomCode {
            let chars = Array(code)
            if chars.count == 20 {
                roomCodeLabel.stringValue = stride(from: 0, to: 20, by: 5).map { String(chars[$0..<($0 + 5)]) }.joined(separator: " ")
            }
        }
        let error = inRoom ? "" : networkError
        networkMessage.stringValue = error
        networkMessage.toolTip = error
        networkMessage.isHidden = error.isEmpty
        for (constraint, original) in compactConstraints {
            let top = original - (error.isEmpty ? 16 : 0)
            if constraint.constant != top { constraint.constant = top }
        }
        if inRoom {
            networkState.stringValue = networkStatus.isEmpty ? "连接中" : networkStatus
            networkState.textColor = roomConnected ? NSColor(calibratedRed: 0.34, green: 0.48, blue: 0.29, alpha: 1) : mutedBrown
        } else if let directory = directory {
            networkState.stringValue = !directory.connected ? directory.status :
                publicEntries.isEmpty ? "正在寻找公开房间…" : "\(publicEntries.count) 个公开房间"
            networkState.textColor = mutedBrown
        } else if !error.isEmpty {
            networkState.stringValue = "联机异常"
            networkState.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        } else {
            networkState.stringValue = "未连接"
            networkState.textColor = mutedBrown
        }
        let skinName = PetSkin.named(skinID).name
        let localName = nickname.isEmpty ? skinName + " · 我" : nickname + " · " + skinName + " · 我"
        localRow.savedNickname = nickname
        localRow.update(name: localName, image: portrait(skinID), keys: keys, clicks: clicks)
        emptyMessage.isHidden = !peers.isEmpty || !error.isEmpty || inRoom
        emptyMessage.stringValue = "创建房间时选择可见性。房间最多 16 人。"
        updatePeers(peers)
        scaleValue.stringValue = "\(Int((scale * 100).rounded()))%"
        labelsSwitch.state = showLabels ? .on : .off
        clickThroughSwitch.state = clickThrough ? .on : .off
        for skin in PetSkin.all {
            guard let button = skinButtons[skin.id] else { continue }
            let selected = skin.id == skinID
            button.title = String(skin.name.dropFirst(2))
            button.toolTip = selected ? skin.name + "（当前）" : skin.name
            button.layer?.borderWidth = selected ? 1.5 : 0.5
            button.layer?.borderColor = mutedBrown.withAlphaComponent(selected ? 0.8 : 0.3).cgColor
            button.layer?.backgroundColor = (selected ? sage : paper).cgColor
            button.contentTintColor = darkBrown
        }
    }

    private func updatePeers(_ peers: [(String, LocalPeers.Peer)]) {
        let ids = Set(peers.map { $0.0 })
        for id in Array(peerRows.keys) where !ids.contains(id) {
            peerRows.removeValue(forKey: id)?.removeFromSuperview()
        }
        let height = max(peerScroll.contentSize.height, CGFloat(peers.count) * 62)
        if peerDocument.frame.size != NSSize(width: 264, height: height) {
            peerDocument.frame = NSRect(x: 0, y: 0, width: 264, height: height)
        }
        for (index, item) in peers.enumerated() {
            let (id, peer) = item
            let row: PeerRow
            if let existing = peerRows[id] { row = existing }
            else {
                row = PeerRow(frame: .zero)
                peerRows[id] = row
                peerDocument.addSubview(row)
            }
            let frame = NSRect(x: 0, y: CGFloat(index) * 62, width: 264, height: 56)
            if row.frame != frame { row.frame = frame }
            let skinName = PetSkin.named(peer.skin).name
            let displayName = peer.nickname.isEmpty ? skinName : peer.nickname + " · " + skinName
            row.update(name: displayName + " · " + String(id.prefix(4)), image: portrait(peer.skin), keys: peer.keys, clicks: peer.clicks)
        }
    }

    private func portrait(_ id: String) -> NSImage? {
        let skin = PetSkin.named(id)
        if let cached = portraitCache[skin.id] { return cached }
        let image = skin.portrait(size: NSSize(width: 38, height: 40))
        portraitCache[skin.id] = image
        return image
    }

    @objc private func changeTab() {
        petScroll.isHidden = tabs.selectedSegment != 0
        roomPage.isHidden = tabs.selectedSegment != 1
        groupPage.isHidden = tabs.selectedSegment != 2
        if roomPage.isHidden { directory = nil }
    }

    override func viewDidDisappear() {
        super.viewDidDisappear()
        directory = nil
    }

    private func updateDiscoveryControls(enabled: Bool) {
        let browsing = enabled && directory != nil
        createRoom.isEnabled = enabled && !browsing
        roomVisibility.isHidden = !enabled || browsing
        discoverRooms.isHidden = !enabled || browsing
        closeDiscovery.isHidden = !browsing
        publicRooms.isHidden = !browsing
        joinPublic.isHidden = !browsing
        guard browsing, let directory = directory else {
            publicEntries.removeAll()
            publicRooms.removeAllItems()
            return
        }
        let entries = directory.refresh(now: ProcessInfo.processInfo.systemUptime)
        if entries.map({ $0.code + $0.host + $0.label }) != publicEntries.map({ $0.code + $0.host + $0.label }) {
            let selected = publicEntry?.code
            publicEntries = entries
            var labels: Set<String> = []
            publicRooms.removeAllItems()
            publicRooms.addItems(withTitles: entries.map {
                labels.insert($0.label).inserted ? $0.label : $0.label + " · " + $0.code
            })
            if let selected = selected, let index = entries.firstIndex(where: { $0.code == selected }) {
                publicRooms.selectItem(at: index)
            }
        }
        updatePublicJoin()
    }

    private var publicEntry: RoomDirectory.Entry? {
        let index = publicRooms.indexOfSelectedItem
        return publicEntries.indices.contains(index) ? publicEntries[index] : nil
    }

    @objc private func updatePublicJoin() {
        joinPublic.isEnabled = (publicEntry?.count ?? RoomMembership.capacity) < RoomMembership.capacity
    }

    @objc private func toggleRoomDiscovery() {
        directory = directory == nil ? RoomDirectory() : nil
        updateDiscoveryControls(enabled: true)
        networkState.stringValue = directory == nil ? "未连接" : "连接中"
    }

    @objc private func joinPublicRoomAction() {
        guard let entry = publicEntry, entry.count < RoomMembership.capacity else { return }
        directory = nil
        networkError = ""
        onJoinPublicRoom?(entry)
    }

    @objc private func selectSkin(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue else { return }
        onSelectSkin?(id)
    }
    @objc private func decreaseScale() { onScaleChange?(-0.1) }
    @objc private func increaseScale() { onScaleChange?(0.1) }
    @objc private func toggleLabels(_ sender: NSButton) { onShowLabelsChange?(sender.state == .on) }
    func setLoginAtStartup(enabled: Bool, pending: Bool, message: String) {
        loginRegistered = enabled || pending
        loginSwitch.state = pending ? .mixed : (enabled ? .on : .off)
        loginMessage.stringValue = message
    }
    @objc private func toggleLoginAtStartup(_ sender: NSButton) { onLoginAtStartupChange?(!loginRegistered) }
    @objc private func toggleClickThrough(_ sender: NSButton) { onClickThroughChange?(sender.state == .on) }
    func setNetworkError(_ message: String) { networkError = message }
    @objc private func toggleShareActivity(_ sender: NSButton) { onShareActivityChange?(sender.state == .on) }
    @objc private func createRoomAction() { networkError = ""; onCreateRoom?(roomVisibility.indexOfSelectedItem == 1) }
    @objc private func joinRoomAction() { networkError = ""; onJoinRoom?(joinCode.stringValue) }
    @objc private func exitRoomAction() { onExitRoom?() }
    @objc private func copyRoomAction() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(roomCodeLabel.stringValue, forType: .string)
    }
    @objc private func openFeedback() {
        if let url = URL(string: "https://github.com/gwtt/guinea-pig-keyboard/issues/new/choose") { NSWorkspace.shared.open(url) }
    }
    @objc private func quitApp() { onQuit?() }
    @objc private func closePanel() { onClose?() }

    private func sectionLabel(_ value: String) -> NSTextField {
        let label = NSTextField(labelWithString: value)
        label.font = .systemFont(ofSize: 12, weight: .semibold)
        label.textColor = mutedBrown
        return label
    }

    private func controlButton(_ title: String, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = NSColor(calibratedRed: 239 / 255, green: 231 / 255, blue: 214 / 255, alpha: 1).cgColor
        button.layer?.cornerRadius = 8
        button.font = .systemFont(ofSize: 17, weight: .medium)
        button.contentTintColor = darkBrown
        return button
    }

    private func footerButton(_ title: String, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: self, action: action)
        button.isBordered = false
        button.wantsLayer = true
        button.layer?.backgroundColor = sage.cgColor
        button.layer?.cornerRadius = 9
        button.font = .systemFont(ofSize: 12, weight: .semibold)
        button.contentTintColor = darkBrown
        return button
    }

    private func add(_ child: NSView, x: CGFloat, top: CGFloat, width: CGFloat, height: CGFloat, to root: NSView) {
        child.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(child)
        NSLayoutConstraint.activate([
            child.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: x),
            child.topAnchor.constraint(equalTo: root.topAnchor, constant: top),
            child.widthAnchor.constraint(equalToConstant: width),
            child.heightAnchor.constraint(equalToConstant: height)
        ])
    }

    private func add(_ child: NSView, trailing: CGFloat, top: CGFloat, width: CGFloat, height: CGFloat, to root: NSView) {
        child.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(child)
        NSLayoutConstraint.activate([
            child.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -trailing),
            child.topAnchor.constraint(equalTo: root.topAnchor, constant: top),
            child.widthAnchor.constraint(equalToConstant: width),
            child.heightAnchor.constraint(equalToConstant: height)
        ])
    }
}

private final class SettingsCardView: NSView {
    var closeAction: (() -> Void)?
    override var acceptsFirstResponder: Bool { true }
    override func cancelOperation(_ sender: Any?) { closeAction?() }
}

private final class FlippedView: NSView {
    override var isFlipped: Bool { true }
}

private final class PeerRow: NSView {
    private let portrait = NSImageView()
    private let name = NSTextField(labelWithString: "")
    private let keys = NSTextField(labelWithString: "")
    private let clicks = NSTextField(labelWithString: "")
    private let rename = NSButton(title: "改名", target: nil, action: nil)
    private let nameInput = NSTextField()
    private let saveName = NSButton(title: "保存", target: nil, action: nil)
    private let cancelName = NSButton(title: "取消", target: nil, action: nil)
    private let nameError = NSTextField(labelWithString: "")
    var canRename = false { didSet { rename.isHidden = !canRename; needsLayout = true } }
    var savedNickname = ""
    var onSaveNickname: ((String) -> Bool)?
    private var editingName = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor(calibratedRed: 239/255, green: 235/255, blue: 221/255, alpha: 1).cgColor
        layer?.cornerRadius = 9
        portrait.imageScaling = .scaleProportionallyUpOrDown
        addSubview(portrait)
        for label in [name, keys, clicks] {
            label.lineBreakMode = .byTruncatingTail
            label.textColor = NSColor(calibratedRed: 77/255, green: 54/255, blue: 39/255, alpha: 1)
            addSubview(label)
        }
        name.lineBreakMode = .byTruncatingMiddle
        name.font = .systemFont(ofSize: 12, weight: .semibold)
        rename.target = self
        rename.action = #selector(beginRename)
        rename.isHidden = true
        addSubview(rename)
        nameInput.placeholderString = "留空使用毛色名"
        nameInput.target = self
        nameInput.action = #selector(saveRename)
        nameInput.isHidden = true
        addSubview(nameInput)
        saveName.target = self
        saveName.action = #selector(saveRename)
        saveName.isHidden = true
        addSubview(saveName)
        cancelName.target = self
        cancelName.action = #selector(cancelRename)
        cancelName.isHidden = true
        addSubview(cancelName)
        nameError.font = .systemFont(ofSize: 10)
        nameError.textColor = NSColor(calibratedRed: 0.65, green: 0.30, blue: 0.22, alpha: 1)
        nameError.stringValue = "名称无效或超过 16 个字符"
        nameError.isHidden = true
        addSubview(nameError)
        keys.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
        clicks.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
    }
    required init?(coder: NSCoder) { super.init(coder: coder) }
    override func layout() {
        super.layout()
        portrait.frame = NSRect(x: 8, y: 8, width: 48, height: 40)
        name.frame = NSRect(x: 62, y: bounds.height - 24, width: bounds.width - (canRename ? 124 : 70), height: 18)
        rename.frame = NSRect(x: bounds.width - 59, y: bounds.height - 28, width: 54, height: 24)
        nameInput.frame = NSRect(x: 62, y: bounds.height - 28, width: bounds.width - 174, height: 24)
        saveName.frame = NSRect(x: bounds.width - 108, y: bounds.height - 28, width: 50, height: 24)
        cancelName.frame = NSRect(x: bounds.width - 57, y: bounds.height - 28, width: 52, height: 24)
        nameError.frame = NSRect(x: 62, y: 7, width: bounds.width - 70, height: 16)
        let column = (bounds.width - 70) / 2
        keys.frame = NSRect(x: 62, y: 7, width: column - 4, height: 16)
        clicks.frame = NSRect(x: 62 + column, y: 7, width: column - 4, height: 16)
    }
    func update(name value: String, image: NSImage?, keys keyCount: UInt64, clicks clickCount: UInt64) {
        if name.stringValue != value { name.stringValue = value; name.toolTip = value }
        if portrait.image !== image { portrait.image = image }
        let keyText = "按键 \(Self.compact(keyCount))", clickText = "点击 \(Self.compact(clickCount))"
        if keys.stringValue != keyText { keys.stringValue = keyText }
        if clicks.stringValue != clickText { clicks.stringValue = clickText }
        keys.toolTip = "按键 \(keyCount)"
        clicks.toolTip = "点击 \(clickCount)"
    }
    @objc private func beginRename() {
        editingName = true
        nameInput.stringValue = savedNickname
        nameError.isHidden = true
        updateEditing()
        window?.makeFirstResponder(nameInput)
    }
    @objc private func saveRename() {
        guard onSaveNickname?(nameInput.stringValue) == true else {
            nameError.isHidden = false
            keys.isHidden = true
            clicks.isHidden = true
            return
        }
        editingName = false
        updateEditing()
    }
    @objc private func cancelRename() { editingName = false; updateEditing() }
    private func updateEditing() {
        name.isHidden = editingName
        rename.isHidden = editingName || !canRename
        nameInput.isHidden = !editingName
        saveName.isHidden = !editingName
        cancelName.isHidden = !editingName
        nameError.isHidden = true
        keys.isHidden = false
        clicks.isHidden = false
    }
    private static func compact(_ value: UInt64) -> String {
        let unit: UInt64
        let suffix: String
        if value >= 100_000_000 { unit = 100_000_000; suffix = "亿" }
        else if value >= 10_000 { unit = 10_000; suffix = "万" }
        else { return String(value) }
        let whole = value / unit, tenth = (value % unit) / (unit / 10)
        return tenth == 0 ? "\(whole)\(suffix)" : "\(whole).\(tenth)\(suffix)"
    }
}

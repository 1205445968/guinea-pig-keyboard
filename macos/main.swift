import AppKit
import ApplicationServices
import Carbon

private final class PetPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate {
    private let defaults = UserDefaults.standard
    private var window: PetPanel!
    private var pet: PetView!
    private var statusItem: NSStatusItem!
    private var settingsPopover: NSPopover?
    private var settingsController: PetSettingsViewController?
    private var eventTap: CFMachPort?
    private var eventSource: CFRunLoopSource?
    private var skinID = PetSkin.all[0].id
    private var nickname = ""
    private var scale = 1.0
    private var clickThrough = false
    private var showLabels = true
    private var hideForSogou = true
    private var manuallyHidden = false
    private var sogouActive = false
    private var inputTimer: Timer?
    private var heldModifiers = Set<UInt16>()
    private var countsDirty = false
    private var keyCount: UInt64 = 0
    private var clickCount: UInt64 = 0
    private let peers = LocalPeers()
    private let baseSize = NSSize(width: 310, height: 238.7)

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        skinID = PetSkin.named(defaults.string(forKey: "skin")).id
        nickname = LocalPeers.normalizedNickname(defaults.string(forKey: "nickname") ?? "") ?? ""
        let savedScale = defaults.double(forKey: "scale")
        scale = savedScale > 0 ? min(2.0, max(0.5, savedScale)) : 1.0
        clickThrough = defaults.bool(forKey: "clickThrough")
        showLabels = defaults.object(forKey: "showKeyLabels") as? Bool ?? true
        hideForSogou = defaults.object(forKey: "hideForSogou") as? Bool ?? true
        keyCount = min(UInt64(Int64.max), UInt64(defaults.string(forKey: "keyCount") ?? "0") ?? 0)
        clickCount = min(UInt64(Int64.max), UInt64(defaults.string(forKey: "clickCount") ?? "0") ?? 0)

        let size = NSSize(width: baseSize.width * scale, height: baseSize.height * scale)
        let visible = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        var origin = NSPoint(x: visible.maxX - size.width - 20, y: visible.minY + 20)
        if defaults.object(forKey: "windowX") != nil, defaults.object(forKey: "windowY") != nil {
            let saved = NSRect(x: defaults.double(forKey: "windowX"), y: defaults.double(forKey: "windowY"), width: size.width, height: size.height)
            if NSScreen.screens.contains(where: { $0.visibleFrame.intersects(saved) }) { origin = saved.origin }
        }
        window = PetPanel(contentRect: NSRect(origin: origin, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = false
        window.hidesOnDeactivate = false
        window.level = .floating
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        window.isMovableByWindowBackground = true
        window.ignoresMouseEvents = clickThrough
        window.delegate = self
        pet = PetView(frame: NSRect(origin: .zero, size: size))
        pet.autoresizingMask = [.width, .height]
        pet.selectSkin(skinID)
        pet.showKeyLabels = showLabels
        pet.onOpenSettings = { [weak self] in self?.showSettings() }
        window.contentView = pet
        updateVisibility()
        updateInputSource()
        let inputTimer = Timer(timeInterval: 1, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            self.updateInputSource()
            self.flushCounts()
            if self.settingsPopover?.isShown == true { self.refreshSettings() }
        }
        self.inputTimer = inputTimer
        RunLoop.main.add(inputTimer, forMode: .common)
        peers.payload = { [weak self] in
            guard let self = self else { return (PetSkin.all[0].id, 0, 0) }
            return (self.skinID, self.keyCount, self.clickCount)
        }

        peers.nickname = { [weak self] in self?.nickname ?? "" }

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let url = Bundle.main.url(forResource: "avatar", withExtension: "png"),
           let icon = NSImage(contentsOf: url) {
            icon.size = NSSize(width: 20, height: 20)
            statusItem.button?.image = icon
        } else {
            statusItem.button?.title = "桌宠"
        }
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu
        if CGPreflightListenEventAccess() { startEventTap() }

    }

    func applicationWillTerminate(_ notification: Notification) {
        savePosition()
        flushCounts()
        stopEventTap()
        inputTimer?.invalidate()
        peers.stop()
    }

    func windowDidMove(_ notification: Notification) { savePosition() }

    private func savePosition() {
        guard let window = window else { return }
        defaults.set(Double(window.frame.origin.x), forKey: "windowX")
        defaults.set(Double(window.frame.origin.y), forKey: "windowY")
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        if eventTap == nil && CGPreflightListenEventAccess() { startEventTap() }
        menu.removeAllItems()
        populate(menu)
    }

    private func add(_ title: String, _ action: Selector, to menu: NSMenu, state: Bool = false, represented: Any? = nil) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        item.state = state ? .on : .off
        item.representedObject = represented
        menu.addItem(item)
    }

    private func populate(_ menu: NSMenu) {
        add("桌宠设置…", #selector(showSettingsFromMenu(_:)), to: menu)
        menu.addItem(.separator())
        let skins = NSMenu()
        for skin in PetSkin.all {
            add(skin.name, #selector(selectSkin(_:)), to: skins, state: skin.id == skinID, represented: skin.id)
        }
        let skinItem = NSMenuItem(title: "切换外观", action: nil, keyEquivalent: "")
        skinItem.submenu = skins
        menu.addItem(skinItem)
        add("放大", #selector(enlarge), to: menu)
        add("缩小", #selector(shrink), to: menu)
        add("显示按键文字", #selector(toggleLabels), to: menu, state: showLabels)
        add(clickThrough ? "关闭鼠标穿透" : "开启鼠标穿透", #selector(toggleClickThrough), to: menu, state: clickThrough)
        add(manuallyHidden ? "显示桌宠" : "隐藏桌宠", #selector(toggleVisible), to: menu)
        menu.addItem(.separator())
        if CGPreflightListenEventAccess() {
            let item = NSMenuItem(title: eventTap == nil ? "输入监听未启动，请检查权限或重启应用" : "输入监控已授权", action: nil, keyEquivalent: "")
            item.isEnabled = false
            menu.addItem(item)
        } else {
            add("启用输入监控权限…", #selector(requestInputPermission), to: menu)
        }
        add("打开输入监控设置…", #selector(openInputSettings), to: menu)
        menu.addItem(.separator())
        add("退出", #selector(quit), to: menu)
    }

    @objc private func selectSkin(_ sender: NSMenuItem) {
        guard let id = sender.representedObject as? String else { return }
        applySkin(id)
    }
    private func applySkin(_ id: String) {
        skinID = PetSkin.named(id).id
        defaults.set(skinID, forKey: "skin")
        pet.selectSkin(skinID)
        refreshSettings()
    }
    @objc private func enlarge() { resize(to: min(2.0, scale + 0.1)) }
    @objc private func shrink() { resize(to: max(0.5, scale - 0.1)) }
    private func resize(to value: Double) {
        let oldFrame = window.frame
        scale = value
        defaults.set(scale, forKey: "scale")
        let size = NSSize(width: baseSize.width * scale, height: baseSize.height * scale)
        window.setFrame(NSRect(x: oldFrame.maxX - size.width, y: oldFrame.minY, width: size.width, height: size.height), display: true)
        savePosition()
        refreshSettings()
    }
    @objc private func toggleLabels() {
        showLabels.toggle()
        pet.showKeyLabels = showLabels
        defaults.set(showLabels, forKey: "showKeyLabels")
        refreshSettings()
    }
    @objc private func toggleClickThrough() {
        clickThrough.toggle()
        window.ignoresMouseEvents = clickThrough
        defaults.set(clickThrough, forKey: "clickThrough")
        refreshSettings()
    }
    @objc private func showSettingsFromMenu(_ sender: NSMenuItem) {
        showSettings(anchor: statusItem.button)
    }
    private func showSettings(anchor: NSView? = nil) {
        if settingsPopover?.isShown == true { return }
        if settingsController == nil {
            let created = PetSettingsViewController()
            created.onSelectSkin = { [weak self] in self?.applySkin($0) }
            created.onScaleChange = { [weak self] delta in
                guard let owner = self else { return }
                owner.resize(to: min(2.0, max(0.5, owner.scale + delta)))
            }
            created.onShowLabelsChange = { [weak self] value in
                guard let self, self.showLabels != value else { return }
                self.toggleLabels()
            }
            created.onClickThroughChange = { [weak self] value in
                guard let self, self.clickThrough != value else { return }
                self.toggleClickThrough()
            }
            created.onHideForSogouChange = { [weak self] value in
                guard let self = self else { return }
                self.hideForSogou = value
                self.defaults.set(value, forKey: "hideForSogou")
                self.updateVisibility()
                self.refreshSettings()
            }
            created.onNetworkingChange = { [weak self] value in
                guard let self = self else { return }
                if value {
                    if !self.peers.start() { self.settingsController?.setNetworkError("联机启动失败，请检查端口") }
                } else { self.peers.stop() }
                self.refreshSettings()
            }
            created.onSaveNickname = { [weak self] value in
                guard let self = self, let normalized = LocalPeers.normalizedNickname(value) else { return false }
                self.nickname = normalized
                self.defaults.set(normalized, forKey: "nickname")
                self.refreshSettings()
                return true
            }
            created.onCreateRoom = { [weak self] in
                guard let self = self else { return }
                _ = self.peers.startRoom(RoomTransport.createCode())
                self.refreshSettings()
            }
            created.onJoinRoom = { [weak self] code in
                guard let self = self else { return }
                if !self.peers.startRoom(code) { self.settingsController?.setNetworkError("房间码无效，请检查后重试") }
                self.refreshSettings()
            }
            created.onExitRoom = { [weak self] in self?.peers.stop(); self?.refreshSettings() }
            created.onReturnToCorner = { [weak self] in self?.returnToCorner() }
            created.onQuit = { [weak self] in self?.quit() }
            created.onClose = { [weak self] in self?.settingsPopover?.performClose(nil) }
            settingsController = created
            let popover = NSPopover()
            popover.behavior = .transient
            popover.animates = true
            popover.contentViewController = created
            popover.contentSize = created.view.frame.size
            settingsPopover = popover
        }
        guard let controller = settingsController else { return }
        refreshSettings()
        let source: NSView = anchor ?? pet
        settingsPopover?.show(relativeTo: source.bounds, of: source, preferredEdge: anchor == nil ? .maxY : .minY)
        if let popoverWindow = settingsPopover?.contentViewController?.view.window {
            popoverWindow.makeFirstResponder(controller.view)
        }
    }
    private func refreshSettings() {
        let rows = peers.peers.sorted { $0.key < $1.key }.map { ($0.key, $0.value) }
        settingsController?.refresh(skinID: skinID, scale: scale, showLabels: showLabels, clickThrough: clickThrough,
                                    hideForSogou: hideForSogou, networking: peers.active,
                                    keys: keyCount, clicks: clickCount, peers: rows, networkStatus: peers.status,
                                    roomCode: peers.roomCode, roomConnected: peers.roomConnected, nickname: nickname)
    }
    private func returnToCorner() {
        let screen = window.screen ?? NSScreen.main ?? NSScreen.screens[0]
        let visible = screen.visibleFrame
        window.setFrameOrigin(NSPoint(x: visible.maxX - window.frame.width - 20, y: visible.minY + 20))
        savePosition()
    }
    @objc private func toggleVisible() {
        manuallyHidden.toggle()
        updateVisibility()
    }
    private func updateVisibility() {
        let shouldShow = !manuallyHidden && !(hideForSogou && sogouActive)
        if shouldShow && !window.isVisible { window.orderFrontRegardless() }
        else if !shouldShow && window.isVisible { window.orderOut(nil) }
    }
    private func updateInputSource() {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue() else { return }
        func property(_ key: CFString) -> String {
            guard let raw = TISGetInputSourceProperty(source, key) else { return "" }
            return Unmanaged<CFString>.fromOpaque(raw).takeUnretainedValue() as String
        }
        let value = (property(kTISPropertyInputSourceID) + " " + property(kTISPropertyLocalizedName)).lowercased()
        sogouActive = value.contains("sogou") || value.contains("sougou") || value.contains("搜狗")
        updateVisibility()
    }
    @objc private func requestInputPermission() {
        if CGRequestListenEventAccess() { startEventTap() } else { openInputSettings() }
    }
    @objc private func openInputSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent") {
            NSWorkspace.shared.open(url)
        }
    }
    @objc private func quit() { NSApp.terminate(nil) }

    private func startEventTap() {
        guard eventTap == nil else { return }
        let types: [CGEventType] = [.keyDown, .flagsChanged, .mouseMoved, .leftMouseDragged, .rightMouseDragged,
                                   .otherMouseDragged, .leftMouseDown, .rightMouseDown, .scrollWheel]
        let mask = types.reduce(CGEventMask(0)) { $0 | (CGEventMask(1) << $1.rawValue) }
        let callback: CGEventTapCallBack = { _, type, event, info in
            guard let info = info else { return Unmanaged.passUnretained(event) }
            let delegate = Unmanaged<AppDelegate>.fromOpaque(info).takeUnretainedValue()
            if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
                if let tap = delegate.eventTap { CGEvent.tapEnable(tap: tap, enable: true) }
            } else {
                delegate.handleEvent(type, event)
            }
            return Unmanaged.passUnretained(event)
        }
        guard let tap = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .listenOnly, eventsOfInterest: mask, callback: callback, userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return }
        eventTap = tap
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        eventSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
    }
    private func stopEventTap() {
        if let source = eventSource { CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .commonModes) }
        if let tap = eventTap { CFMachPortInvalidate(tap) }
        eventSource = nil
        eventTap = nil
    }
    private func flushCounts() {
        guard countsDirty else { return }
        defaults.set(String(keyCount), forKey: "keyCount")
        defaults.set(String(clickCount), forKey: "clickCount")
        countsDirty = false
    }
    private func incrementKeyCount() {
        keyCount = min(UInt64(Int64.max), keyCount + 1)
        countsDirty = true
    }
    private func handleEvent(_ type: CGEventType, _ event: CGEvent) {
        switch type {
        case .keyDown:
            if event.getIntegerValueField(.keyboardEventAutorepeat) == 0 {
                incrementKeyCount()
            }
            pet.pressKey(UInt16(truncatingIfNeeded: event.getIntegerValueField(.keyboardEventKeycode)))
        case .flagsChanged:
            pet.noteInput(ProcessInfo.processInfo.systemUptime)
            let code = UInt16(truncatingIfNeeded: event.getIntegerValueField(.keyboardEventKeycode))
            if code == 57 {
                incrementKeyCount()
            } else if [54, 55, 56, 58, 59, 60, 61, 62, 63].contains(code) {
                if CGEventSource.keyState(.combinedSessionState, key: CGKeyCode(code)) {
                    if heldModifiers.insert(code).inserted { incrementKeyCount() }
                } else { heldModifiers.remove(code) }
            }
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            let dx = Double(event.getIntegerValueField(.mouseEventDeltaX))
            let dy = Double(event.getIntegerValueField(.mouseEventDeltaY))
            pet.moveMouse(dx: dx, dy: dy)
        case .leftMouseDown, .rightMouseDown:
            clickCount = min(UInt64(Int64.max), clickCount + 1)
            countsDirty = true
            pet.clickMouse(right: type == .rightMouseDown)
        case .scrollWheel:
            let points = event.getIntegerValueField(.scrollWheelEventPointDeltaAxis1)
            pet.scrollMouse(points == 0 ? event.getIntegerValueField(.scrollWheelEventDeltaAxis1) : points)
        default: break
        }
    }
}

@main
struct WheekKeysApp {
    @MainActor static func main() {
        if CommandLine.arguments.contains("--smoke-test") {
            runSmokeTest()
            return
        }
        let application = NSApplication.shared
        let delegate = AppDelegate()
        application.delegate = delegate
        withExtendedLifetime(delegate) { application.run() }
    }
}

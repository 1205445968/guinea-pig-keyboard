import AppKit
import ApplicationServices

struct PetSkin {
    let id: String
    let name: String
    let body: String
    let paw: String

    static let all: [PetSkin] = [
        PetSkin(id: "tricolor", name: "三花", body: "guinea-pig.png", paw: "paw.png"),
        PetSkin(id: "californian", name: "白加州", body: "californian-body.png", paw: "californian-paw.png"),
        PetSkin(id: "gold-white", name: "黄白英短", body: "gold-white-body.png", paw: "gold-white-paw.png"),
        PetSkin(id: "teddy", name: "美泰", body: "teddy-body.png", paw: "teddy-paw.png"),
        PetSkin(id: "silver-agouti", name: "雕灰", body: "silver-agouti-body.png", paw: "silver-agouti-paw.png")
    ]

    static func named(_ id: String) -> PetSkin {
        all.first { $0.id == id } ?? all[0]
    }
}

private final class PetPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

private final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate {
    private let defaults = UserDefaults.standard
    private var window: PetPanel!
    private var pet: PetView!
    private var statusItem: NSStatusItem!
    private var eventTap: CFMachPort?
    private var eventSource: CFRunLoopSource?
    private var skinID = "tricolor"
    private var scale = 1.0
    private var clickThrough = false
    private var showLabels = true
    private let baseSize = NSSize(width: 310, height: 238.7)

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        skinID = PetSkin.named(defaults.string(forKey: "skin") ?? "tricolor").id
        let savedScale = defaults.double(forKey: "scale")
        scale = savedScale > 0 ? min(2.0, max(0.5, savedScale)) : 1.0
        clickThrough = defaults.bool(forKey: "clickThrough")
        showLabels = defaults.object(forKey: "showKeyLabels") as? Bool ?? true

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
        window.contentView = pet
        let contextMenu = NSMenu()
        contextMenu.delegate = self
        pet.menu = contextMenu
        window.orderFrontRegardless()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let url = Bundle.main.url(forResource: "guinea-pig", withExtension: "png"),
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
        stopEventTap()
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
        add(window.isVisible ? "隐藏桌宠" : "显示桌宠", #selector(toggleVisible), to: menu)
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
        skinID = PetSkin.named(id).id
        defaults.set(skinID, forKey: "skin")
        pet.selectSkin(skinID)
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
    }
    @objc private func toggleLabels() {
        showLabels.toggle()
        pet.showKeyLabels = showLabels
        defaults.set(showLabels, forKey: "showKeyLabels")
    }
    @objc private func toggleClickThrough() {
        clickThrough.toggle()
        window.ignoresMouseEvents = clickThrough
        defaults.set(clickThrough, forKey: "clickThrough")
    }
    @objc private func toggleVisible() {
        if window.isVisible { window.orderOut(nil) } else { window.orderFrontRegardless() }
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
        let types: [CGEventType] = [.keyDown, .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged, .leftMouseDown, .rightMouseDown]
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
    private func handleEvent(_ type: CGEventType, _ event: CGEvent) {
        switch type {
        case .keyDown:
            pet.pressKey(UInt16(truncatingIfNeeded: event.getIntegerValueField(.keyboardEventKeycode)))
        case .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged:
            let dx = Double(event.getIntegerValueField(.mouseEventDeltaX))
            let dy = Double(event.getIntegerValueField(.mouseEventDeltaY))
            pet.moveMouse(dx: dx, dy: dy)
        case .leftMouseDown: pet.clickMouse(right: false)
        case .rightMouseDown: pet.clickMouse(right: true)
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

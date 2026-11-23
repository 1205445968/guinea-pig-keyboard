import AppKit

final class PetSettingsViewController: NSViewController {
    var onSelectSkin: ((String) -> Void)?
    var onScaleChange: ((Double) -> Void)?
    var onShowLabelsChange: ((Bool) -> Void)?
    var onClickThroughChange: ((Bool) -> Void)?
    var onReturnToCorner: (() -> Void)?
    var onQuit: (() -> Void)?
    var onClose: (() -> Void)?

    private let skinButtons = Dictionary(uniqueKeysWithValues: PetSkin.all.map { ($0.id, NSButton()) })
    private let scaleValue = NSTextField(labelWithString: "100%")
    private let labelsSwitch = NSButton(checkboxWithTitle: "显示按键气泡", target: nil, action: nil)
    private let clickThroughSwitch = NSButton(checkboxWithTitle: "鼠标穿透", target: nil, action: nil)

    private let paper = NSColor(calibratedRed: 250 / 255, green: 246 / 255, blue: 235 / 255, alpha: 1)
    private let darkBrown = NSColor(calibratedRed: 77 / 255, green: 54 / 255, blue: 39 / 255, alpha: 1)
    private let mutedBrown = NSColor(calibratedRed: 143 / 255, green: 119 / 255, blue: 94 / 255, alpha: 1)
    private let sage = NSColor(calibratedRed: 224 / 255, green: 235 / 255, blue: 216 / 255, alpha: 1)

    override func loadView() {
        let root = SettingsCardView(frame: NSRect(x: 0, y: 0, width: 316, height: 344))
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
        add(title, x: 70, top: 22, width: 170, height: 24, to: root)

        let closeButton = NSButton(title: "×", target: self, action: #selector(closePanel))
        closeButton.isBordered = false
        closeButton.font = .systemFont(ofSize: 19, weight: .regular)
        closeButton.contentTintColor = mutedBrown
        add(closeButton, trailing: 12, top: 12, width: 30, height: 30, to: root)

        let coatLabel = sectionLabel("毛色")
        add(coatLabel, x: 18, top: 69, width: 200, height: 18, to: root)

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
            button.image = skinThumbnail(skin)
            button.setButtonType(.momentaryPushIn)
            add(button, x: 18 + CGFloat(index % 3) * 95, top: 92 + CGFloat(index / 3) * 34,
                width: 90, height: 30, to: root)
        }

        let sizeLabel = sectionLabel("大小")
        add(sizeLabel, x: 18, top: 175, width: 100, height: 18, to: root)
        scaleValue.font = .systemFont(ofSize: 12, weight: .semibold)
        scaleValue.textColor = mutedBrown
        scaleValue.alignment = .center
        add(scaleValue, trailing: 54, top: 175, width: 45, height: 18, to: root)

        let minus = controlButton("−", action: #selector(decreaseScale))
        add(minus, trailing: 103, top: 169, width: 30, height: 30, to: root)
        let plus = controlButton("+", action: #selector(increaseScale))
        add(plus, trailing: 18, top: 169, width: 30, height: 30, to: root)

        labelsSwitch.target = self
        labelsSwitch.action = #selector(toggleLabels(_:))
        labelsSwitch.font = .systemFont(ofSize: 12)
        labelsSwitch.contentTintColor = darkBrown
        add(labelsSwitch, x: 18, top: 212, width: 270, height: 22, to: root)
        clickThroughSwitch.target = self
        clickThroughSwitch.action = #selector(toggleClickThrough(_:))
        clickThroughSwitch.font = .systemFont(ofSize: 12)
        clickThroughSwitch.contentTintColor = darkBrown
        add(clickThroughSwitch, x: 18, top: 243, width: 270, height: 22, to: root)

        let divider = NSBox()
        divider.boxType = .separator
        add(divider, x: 18, top: 277, width: 280, height: 1, to: root)

        let corner = footerButton("回到右下角", action: #selector(returnToCorner))
        add(corner, x: 18, top: 294, width: 172, height: 32, to: root)
        let quit = footerButton("退出", action: #selector(quitApp))
        quit.contentTintColor = NSColor(calibratedRed: 155 / 255, green: 79 / 255, blue: 63 / 255, alpha: 1)
        add(quit, trailing: 18, top: 294, width: 96, height: 32, to: root)
    }

    func refresh(skinID: String, scale: Double, showLabels: Bool, clickThrough: Bool) {
        scaleValue.stringValue = "\(Int((scale * 100).rounded()))%"
        labelsSwitch.state = showLabels ? .on : .off
        clickThroughSwitch.state = clickThrough ? .on : .off
        for skin in PetSkin.all {
            guard let button = skinButtons[skin.id] else { continue }
            let selected = skin.id == skinID
            button.title = skin.name
            button.toolTip = selected ? skin.name + "（当前）" : skin.name
            button.layer?.borderWidth = selected ? 1.5 : 0.5
            button.layer?.borderColor = mutedBrown.withAlphaComponent(selected ? 0.8 : 0.3).cgColor
            button.layer?.backgroundColor = (selected ? sage : paper).cgColor
            button.contentTintColor = darkBrown
        }
    }

    @objc private func selectSkin(_ sender: NSButton) {
        guard let id = sender.identifier?.rawValue else { return }
        onSelectSkin?(id)
    }
    @objc private func decreaseScale() { onScaleChange?(-0.1) }
    @objc private func increaseScale() { onScaleChange?(0.1) }
    @objc private func toggleLabels(_ sender: NSButton) { onShowLabelsChange?(sender.state == .on) }
    @objc private func toggleClickThrough(_ sender: NSButton) { onClickThroughChange?(sender.state == .on) }
    @objc private func returnToCorner() { onReturnToCorner?() }
    @objc private func quitApp() { onQuit?() }
    @objc private func closePanel() { onClose?() }

    private func skinThumbnail(_ skin: PetSkin) -> NSImage? {
        let swatch = NSImage(size: NSSize(width: 25, height: 25))
        swatch.lockFocus()
        let base: NSColor
        let patches: [NSColor]
        switch skin.id {
        case "tricolor":
            base = NSColor(calibratedRed: 248/255, green: 229/255, blue: 204/255, alpha: 1)
            patches = [NSColor(calibratedRed: 205/255, green: 135/255, blue: 83/255, alpha: 1),
                       NSColor(calibratedRed: 83/255, green: 61/255, blue: 48/255, alpha: 1)]
        case "californian":
            base = NSColor(calibratedRed: 244/255, green: 230/255, blue: 211/255, alpha: 1)
            patches = [NSColor(calibratedRed: 79/255, green: 61/255, blue: 54/255, alpha: 1)]
        case "gold-white":
            base = NSColor(calibratedRed: 250/255, green: 247/255, blue: 237/255, alpha: 1)
            patches = [NSColor(calibratedRed: 218/255, green: 165/255, blue: 91/255, alpha: 1)]
        case "teddy":
            base = NSColor(calibratedRed: 168/255, green: 105/255, blue: 70/255, alpha: 1)
            patches = [NSColor(calibratedRed: 222/255, green: 164/255, blue: 112/255, alpha: 1)]
        default:
            base = NSColor(calibratedRed: 161/255, green: 144/255, blue: 134/255, alpha: 1)
            patches = [NSColor(calibratedRed: 103/255, green: 87/255, blue: 79/255, alpha: 1)]
        }
        base.setFill()
        NSBezierPath(roundedRect: NSRect(x: 1, y: 1, width: 23, height: 23), xRadius: 8, yRadius: 8).fill()
        if skin.id == "californian" {
            patches[0].setFill()
            NSBezierPath(ovalIn: NSRect(x: 2, y: 12, width: 8, height: 11)).fill()
            NSBezierPath(ovalIn: NSRect(x: 15, y: 12, width: 8, height: 11)).fill()
            NSBezierPath(ovalIn: NSRect(x: 9, y: 3, width: 8, height: 7)).fill()
        } else {
            patches[0].setFill()
            NSBezierPath(ovalIn: NSRect(x: 2, y: 10, width: 12, height: 13)).fill()
            if patches.count > 1 {
                patches[1].setFill()
                NSBezierPath(ovalIn: NSRect(x: 14, y: 2, width: 9, height: 13)).fill()
            }
        }
        swatch.unlockFocus()
        swatch.size = NSSize(width: 17, height: 17)
        return swatch
    }

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

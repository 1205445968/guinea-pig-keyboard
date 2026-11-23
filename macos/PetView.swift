import AppKit

final class PetView: NSView {
    private struct Key {
        let code: UInt16
        let label: String
        let center: CGPoint
        let width: CGFloat
        let right: Bool
    }
    private final class Stroke {
        let rest: CGPoint
        var start: CGPoint
        var target: CGPoint
        var started: TimeInterval = 0
        var code: UInt16 = 0
        var active = false
        init(_ point: CGPoint) {
            rest = point
            start = point
            target = point
        }
        func position(_ now: TimeInterval) -> CGPoint {
            guard active else { return rest }
            let age = max(0, now - started)
            if age < 0.052 {
                let t = age / 0.052
                var p = mix(start, target, smooth(t))
                p.y -= 9 * sin(t * .pi)
                return p
            }
            if age < 0.102 { return target }
            if age >= 0.285 { return rest }
            let t = (age - 0.102) / 0.183
            var p = mix(target, rest, smooth(t))
            p.y -= 5 * sin(t * .pi)
            return p
        }
    }
    private static let rowEdges: [CGFloat] = [119, 243, 367, 490, 612, 739]
    private static let keyboardBounds = CGRect(x: 64, y: 199, width: 318, height: 88)
    private static func wellWidth(_ y: CGFloat) -> CGFloat {
        let depth = (y - rowEdges[0]) / (rowEdges[5] - rowEdges[0])
        return 1604 + 102 * depth
    }
    private static func projectRow(_ row: Int) -> (scaleX: CGFloat, scaleY: CGFloat, offsetX: CGFloat, offsetY: CGFloat) {
        let top = rowEdges[row], bottom = rowEdges[row + 1]
        let targetTop = rowEdges[0] + rowEdges[5] - bottom
        let sourceMiddle = (top + bottom) / 2
        let targetMiddle = targetTop + (bottom - top) / 2
        let scaleX = keyboardBounds.width / 1774 * wellWidth(targetMiddle) / wellWidth(sourceMiddle)
        let scaleY = keyboardBounds.height / 887
        return (scaleX, scaleY, keyboardBounds.minX + (keyboardBounds.width + 1774 * scaleX) / 2,
                keyboardBounds.minY + (targetTop - top) * scaleY)
    }
    private static func makeKeys() -> [Key] {
        var result: [Key] = []
        func row(_ index: Int, _ y: CGFloat, _ xs: [CGFloat], _ codes: [UInt16], _ labels: [String], _ right: [Bool]) {
            let projection = projectRow(index)
            for i in codes.indices {
                let wide: CGFloat = codes[i] == 49 ? 720 : ([36, 56, 60] as [UInt16]).contains(codes[i]) ? 220 : 103
                let center = CGPoint(x: projection.offsetX - xs[i] * projection.scaleX,
                                     y: projection.offsetY + y * projection.scaleY)
                result.append(Key(code: codes[i], label: labels[i], center: center,
                                  width: wide * projection.scaleX, right: right[i]))
            }
        }
        row(0, 171,
            [151,276,400,522,647,769,889,1012,1135,1253,1370,1485,1613],
            [53,18,19,20,21,23,22,26,28,25,29,27,51],
            ["Esc","1","2","3","4","5","6","7","8","9","0","-","⌫"],
            [false,false,false,false,false,false,true,true,true,true,true,true,true])
        row(1, 290,
            [160,313,437,565,691,816,944,1071,1198,1322,1449,1600],
            [48,12,13,14,15,17,16,32,34,31,35,42],
            ["Tab","Q","W","E","R","T","Y","U","I","O","P","\\"],
            [false,false,false,false,false,false,true,true,true,true,true,true])
        row(2, 416,
            [175,350,475,610,736,857,991,1121,1246,1375,1571],
            [57,0,1,2,3,5,4,38,40,37,36],
            ["Caps","A","S","D","F","G","H","J","K","L","Enter"],
            [false,false,false,false,false,false,true,true,true,true,true])
        row(3, 541,
            [193,402,537,663,797,923,1055,1181,1320,1450,1610],
            [56,6,7,8,9,11,45,46,43,47,60],
            ["Shift","Z","X","C","V","B","N","M",",",".","Shift"],
            [false,false,false,false,false,false,true,true,true,true,true])
        row(4, 662,
            [127,281,417,878,1351,1495,1647],
            [59,55,58,49,61,54,62],
            ["Ctrl","⌘","Option","","Option","⌘","Ctrl"],
            [false,false,false,false,true,true,true])
        return result
    }
    private static let keys = makeKeys()
    private static let keyLookup = Dictionary(uniqueKeysWithValues: keys.map { ($0.code, $0) })
    private static let aliases: [UInt16: (UInt16, String)] = [
        24: (27, "="), 33: (35, "["), 30: (42, "]"), 41: (37, ";"),
        39: (36, "'"), 44: (47, "/"), 76: (36, "Enter")
    ]
    private static let extraLabels: [UInt16: String] = [
        123: "←", 124: "→", 125: "↓", 126: "↑", 115: "Home", 119: "End",
        116: "PgUp", 121: "PgDn", 117: "Del", 122: "F1", 120: "F2",
        99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8",
        101: "F9", 109: "F10", 103: "F11", 111: "F12"
    ]
    private static let keyboardLabels: [UInt16: NSAttributedString] = Dictionary(
        uniqueKeysWithValues: keys.map { key in
            let font = NSFont.systemFont(ofSize: key.label.count > 1 ? 7 : 9, weight: .semibold)
            let text = NSAttributedString(string: key.label, attributes: [
                .font: font,
                .foregroundColor: NSColor(calibratedRed: 91/255, green: 56/255, blue: 35/255, alpha: 1)
            ])
            return (key.code, text)
        }
    )
    private static let pawGradient = CGGradient(
        colorsSpace: CGColorSpaceCreateDeviceRGB(),
        colors: [NSColor.clear.cgColor, NSColor.white.cgColor] as CFArray,
        locations: [0, 0.27]
    )
    private static let bubbleAttributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.boldSystemFont(ofSize: 16),
        .foregroundColor: NSColor(calibratedRed: 111/255, green: 73/255, blue: 47/255, alpha: 1)
    ]
    private let left = Stroke(CGPoint(x: 156, y: 214))
    private let right = Stroke(CGPoint(x: 263, y: 214))
    private var skin = PetSkin.all[0]
    private var images: [String: CGImage] = [:]
    private var keyboardArtwork: CGImage?
    private var eyeFrames: [String: [CGImage]] = [:]
    private var timer: Timer?
    private var blinkTimer: Timer?
    private var blinkStarted: TimeInterval?
    private var gazeFrame = 0
    private var lastGazeMove: TimeInterval = -.infinity
    private var lastEyeSample: TimeInterval = -.infinity
    private var gazeDX: Double = 0
    private var gazeDY: Double = 0
    private var lastFrame: TimeInterval = 0
    private var lastMouseUse: TimeInterval = -.infinity
    private var lastClick: TimeInterval = -.infinity
    private var mousePosition = CGPoint(x: 31, y: 243)
    private var mouseDestination = CGPoint(x: 31, y: 243)
    private var mouseGrip: CGFloat = 0
    private var rightButton = false
    var showKeyLabels = true { didSet { needsDisplay = true } }
    override var isFlipped: Bool { true }
    override var mouseDownCanMoveWindow: Bool { true }

    override init(frame frameRect: NSRect) { super.init(frame: frameRect) }
    required init?(coder: NSCoder) { super.init(coder: coder) }
    deinit {
        timer?.invalidate()
        blinkTimer?.invalidate()
    }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil {
            timer?.invalidate()
            timer = nil
            blinkTimer?.invalidate()
            blinkTimer = nil
            blinkStarted = nil
            gazeFrame = 0
            gazeDX = 0
            gazeDY = 0
        } else {
            scheduleBlink()
        }
    }
    static func blinkFrame(elapsed: TimeInterval) -> Int? {
        guard elapsed >= 0, elapsed < 0.20 else { return nil }
        return [9, 10, 11, 10, 9][min(4, Int(elapsed / 0.04))]
    }
    private func scheduleBlink() {
        guard window != nil, blinkTimer == nil, blinkStarted == nil else { return }
        let timer = Timer(timeInterval: Double.random(in: 3...6), repeats: false) { [weak self] _ in
            guard let self = self else { return }
            self.blinkTimer = nil
            let now = ProcessInfo.processInfo.systemUptime
            self.blinkStarted = now
            self.wake(now)
        }
        blinkTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }
    func selectSkin(_ id: String) {
        skin = PetSkin.named(id)
        needsDisplay = true
    }
    func pressKey(_ keyCode: UInt16) {
        let key = keyInfo(keyCode)
        let hand = key.right ? left : right
        let now = ProcessInfo.processInfo.systemUptime
        var start = hand.position(now)
        if key.right {
            start = mouseHand(start, now)
            mouseGrip = 0
            lastMouseUse = -.infinity
            lastClick = -.infinity
        }
        hand.start = start
        hand.target = key.center
        hand.started = now
        hand.code = keyCode
        hand.active = true
        wake(now)
    }
    func moveMouse(dx: Double, dy: Double) {
        guard dx.isFinite, dy.isFinite, dx != 0 || dy != 0 else { return }
        mouseDestination.x = max(10, min(46, mouseDestination.x + dx * 0.12))
        mouseDestination.y = max(210, min(270, mouseDestination.y + dy * 0.12))
        let now = ProcessInfo.processInfo.systemUptime
        lastMouseUse = now
        updateGaze(dx: dx, dy: dy, now: now)
        wake(now)
    }
    private func updateGaze(dx: Double, dy: Double, now: TimeInterval) {
        if now - lastEyeSample > 0.4 {
            gazeDX = 0
            gazeDY = 0
        }
        lastEyeSample = now
        gazeDX = gazeDX * 0.65 + dx
        gazeDY = gazeDY * 0.65 + dy
        let magnitude = hypot(gazeDX, gazeDY)
        guard magnitude >= 2 else { return }
        let horizontal = abs(gazeDX) >= magnitude * 0.38 ? (gazeDX < 0 ? -1 : 1) : 0
        let vertical = abs(gazeDY) >= magnitude * 0.38 ? (gazeDY < 0 ? -1 : 1) : 0
        switch (horizontal, vertical) {
        case (-1, 0): gazeFrame = 1
        case (1, 0): gazeFrame = 2
        case (0, -1): gazeFrame = 3
        case (0, 1): gazeFrame = 4
        case (-1, -1): gazeFrame = 5
        case (1, -1): gazeFrame = 6
        case (-1, 1): gazeFrame = 7
        case (1, 1): gazeFrame = 8
        default: gazeFrame = 0
        }
        lastGazeMove = now
    }
    func clickMouse(right: Bool) {
        rightButton = right
        let now = ProcessInfo.processInfo.systemUptime
        lastMouseUse = now
        lastClick = now
        wake(now)
    }
    private func wake(_ now: TimeInterval) {
        guard window != nil else { needsDisplay = true; return }
        if timer == nil {
            lastFrame = now
            let t = Timer(timeInterval: 0.024, repeats: true) { [weak self] _ in self?.tick() }
            timer = t
            RunLoop.main.add(t, forMode: .common)
        }
        needsDisplay = true
    }
    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        if let started = blinkStarted, Self.blinkFrame(elapsed: now - started) == nil {
            blinkStarted = nil
            scheduleBlink()
        }
        if gazeFrame != 0 && now - lastGazeMove >= 0.4 {
            gazeFrame = 0
            gazeDX = 0
            gazeDY = 0
        }
        for hand in [left, right] where hand.active && now - hand.started >= 0.395 { hand.active = false }
        let elapsed = max(0, now - lastFrame)
        lastFrame = now
        mousePosition = mix(mousePosition, mouseDestination, 1 - exp(-elapsed / 0.032))
        if hypot(mousePosition.x - mouseDestination.x, mousePosition.y - mouseDestination.y) < 0.1 { mousePosition = mouseDestination }
        let tapping = left.active && now - left.started < 0.130 && left.started > lastClick
        let target: CGFloat = !tapping && now - lastMouseUse <= 0.250 ? 1 : 0
        mouseGrip += (target - mouseGrip) * (1 - exp(-elapsed / (target > mouseGrip ? 0.030 : 0.053)))
        if abs(target - mouseGrip) < 0.004 { mouseGrip = target }
        if !left.active && !right.active && mouseGrip == 0 && mousePosition == mouseDestination
            && now - lastMouseUse > 0.250 && blinkStarted == nil && gazeFrame == 0 {
            timer?.invalidate()
            timer = nil
        }
        needsDisplay = true
    }
    private func mouseHand(_ keyboard: CGPoint, _ now: TimeInterval) -> CGPoint {
        let palm = CGPoint(x: mousePosition.x + 3, y: mousePosition.y - 2)
        return mix(keyboard, mix(palm, buttonPoint, contact(now - lastClick)), mouseGrip)
    }
    private var buttonPoint: CGPoint { CGPoint(x: mousePosition.x + (rightButton ? -6 : 6), y: mousePosition.y + 11) }
    private func keyInfo(_ code: UInt16) -> Key {
        if let key = Self.keyLookup[code] { return key }
        if let (alias, label) = Self.aliases[code], let key = Self.keyLookup[alias] {
            return Key(code: code, label: label, center: key.center, width: key.width, right: true)
        }
        let isRight = [123, 124, 125, 126, 115, 119, 116, 121, 117, 76].contains(Int(code))
            || (65...92).contains(Int(code))
        let fallback = Self.keyLookup[isRight ? 38 : 3]!
        return Key(code: code, label: Self.extraLabels[code] ?? "", center: fallback.center,
                   width: fallback.width, right: isRight)
    }
    private func image(_ name: String) -> CGImage? {
        if let cached = images[name] { return cached }
        guard let url = Bundle.main.resourceURL?.appendingPathComponent(name), let image = NSImage(contentsOf: url), let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return nil }
        images[name] = cg
        return cg
    }
    private func sprite(_ name: String, _ rect: CGRect, _ ctx: CGContext) {
        guard let image = image(name) else { return }
        drawImage(image, rect, ctx)
    }
    private func drawImage(_ image: CGImage, _ rect: CGRect, _ ctx: CGContext) {
        ctx.saveGState()
        ctx.translateBy(x: rect.minX, y: rect.maxY)
        ctx.scaleBy(x: 1, y: -1)
        ctx.draw(image, in: CGRect(x: 0, y: 0, width: rect.width, height: rect.height))
        ctx.restoreGState()
    }
    private func eyeImage(_ frame: Int) -> CGImage? {
        guard frame > 0 && frame < 12 else { return nil }
        let id = skin.id
        if eyeFrames[id] == nil {
            guard let atlas = image("\(id)-eyes.png"), atlas.width % 4 == 0,
                  atlas.height % 3 == 0 else { return nil }
            let width = atlas.width / 4, height = atlas.height / 3
            var frames: [CGImage] = []
            for index in 0..<12 {
                let rect = CGRect(x: (index % 4) * width, y: (index / 4) * height,
                                  width: width, height: height)
                guard let cropped = atlas.cropping(to: rect) else { return nil }
                frames.append(cropped)
            }
            eyeFrames[id] = frames
        }
        return eyeFrames[id]?[frame]
    }
    private func drawKeyboard(_ ctx: CGContext) {
        if let artwork = keyboardArtwork {
            drawImage(artwork, Self.keyboardBounds, ctx)
            return
        }
        guard let bitmap = image("keyboard.png"),
              let canvas = CGContext(data: nil, width: 954, height: 264, bitsPerComponent: 8,
                                     bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                                     bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
        canvas.translateBy(x: 0, y: 264)
        canvas.scaleBy(x: 3, y: -3)
        canvas.translateBy(x: -Self.keyboardBounds.minX, y: -Self.keyboardBounds.minY)
        canvas.interpolationQuality = .high
        drawImage(bitmap, Self.keyboardBounds, canvas)

        let top = Self.rowEdges[0], bottom = Self.rowEdges[5]
        let center = Self.keyboardBounds.midX
        let topHalf = Self.wellWidth(top) * 318 / 1774 / 2
        let bottomHalf = Self.wellWidth(bottom) * 318 / 1774 / 2
        let topY = Self.keyboardBounds.minY + top * 88 / 887
        let bottomY = Self.keyboardBounds.minY + bottom * 88 / 887
        canvas.saveGState()
        canvas.move(to: CGPoint(x: center - topHalf, y: topY))
        canvas.addLine(to: CGPoint(x: center + topHalf, y: topY))
        canvas.addLine(to: CGPoint(x: center + bottomHalf, y: bottomY))
        canvas.addLine(to: CGPoint(x: center - bottomHalf, y: bottomY))
        canvas.closePath()
        canvas.clip()
        canvas.setFillColor(NSColor(calibratedRed: 91/255, green: 56/255, blue: 35/255, alpha: 1).cgColor)
        canvas.fill(Self.keyboardBounds)
        for row in 0..<5 {
            let projection = Self.projectRow(row)
            canvas.saveGState()
            canvas.translateBy(x: projection.offsetX, y: projection.offsetY)
            canvas.scaleBy(x: -projection.scaleX, y: projection.scaleY)
            canvas.clip(to: CGRect(x: 0, y: Self.rowEdges[row], width: 1774,
                                   height: Self.rowEdges[row + 1] - Self.rowEdges[row]))
            drawImage(bitmap, CGRect(x: 0, y: 0, width: 1774, height: 887), canvas)
            canvas.restoreGState()
        }
        canvas.restoreGState()
        guard let artwork = canvas.makeImage() else { return }
        keyboardArtwork = artwork
        drawImage(artwork, Self.keyboardBounds, ctx)
    }
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard let ctx = NSGraphicsContext.current?.cgContext else { return }
        let scale = min(bounds.width / 400, bounds.height / 308)
        guard scale > 0 else { return }
        ctx.saveGState()
        ctx.translateBy(x: (bounds.width - 400 * scale) / 2, y: (bounds.height - 308 * scale) / 2)
        ctx.scaleBy(x: scale, y: scale)
        ctx.translateBy(x: 9, y: 0)
        ctx.interpolationQuality = .high
        let now = ProcessInfo.processInfo.systemUptime
        let lp = mouseHand(left.position(now), now), rp = right.position(now)
        let l = left.active ? contact(now - left.started) : 0, r = right.active ? contact(now - right.started) : 0
        let click = contact(now - lastClick), lean = ((lp.x - left.rest.x) + (rp.x - right.rest.x)) * 0.107
        let dip = max(click, max(l, r)) * 3.4
        let bodyRect = CGRect(x: 84 + lean, y: 12 + dip, width: 251, height: 209 - dip * 0.4)
        sprite(skin.body, bodyRect, ctx)
        let eyeFrame = blinkStarted.flatMap { Self.blinkFrame(elapsed: now - $0) } ?? gazeFrame
        if let eyes = eyeImage(eyeFrame) {
            let eyeRect = CGRect(x: bodyRect.minX + bodyRect.width * 0.16,
                                 y: bodyRect.minY + bodyRect.height * 0.19,
                                 width: bodyRect.width * 0.68, height: bodyRect.height * 0.27)
            drawImage(eyes, eyeRect, ctx)
        }
        ctx.saveGState()
        ctx.translateBy(x: mousePosition.x, y: mousePosition.y)
        ctx.rotate(by: .pi)
        sprite("mouse.png", CGRect(x: -17, y: -24 + click, width: 34, height: 48 - click), ctx)
        ctx.restoreGState()
        if click > 0 {
            ctx.setFillColor(NSColor(calibratedRed: 218/255, green: 149/255,
                                     blue: 84/255, alpha: click * 0.38).cgColor)
            ctx.fillEllipse(in: CGRect(x: buttonPoint.x - 4.5, y: buttonPoint.y - 6.2,
                                       width: 9, height: 12.4))
        }
        drawKeyboard(ctx)
        for key in Self.keys { drawKeyLabel(key, ctx) }
        if l > 0 { drawPressed(keyInfo(left.code), l, ctx) }
        if r > 0 { drawPressed(keyInfo(right.code), r, ctx) }
        drawPaw(lp, left.rest, false, lean, dip, ctx)
        drawPaw(rp, right.rest, true, lean, dip, ctx)
        if showKeyLabels {
            drawBubble(left, now, false, ctx)
            drawBubble(right, now, true, ctx)
        }
        ctx.restoreGState()
    }
    private func drawKeyLabel(_ key: Key, _ ctx: CGContext) {
        guard let text = Self.keyboardLabels[key.code], text.length > 0 else { return }
        let size = text.size()
        ctx.saveGState()
        ctx.translateBy(x: key.center.x, y: key.center.y)
        ctx.rotate(by: .pi)
        text.draw(at: CGPoint(x: -size.width / 2, y: -size.height / 2))
        ctx.restoreGState()
    }
    private func drawPressed(_ key: Key, _ amount: CGFloat, _ ctx: CGContext) {
        let rect = CGRect(x: key.center.x - key.width * 0.44, y: key.center.y - 4.3 + amount, width: key.width * 0.88, height: 8.6)
        ctx.saveGState()
        ctx.setAlpha(amount)
        ctx.setFillColor(NSColor(calibratedRed: 1, green: 185/255, blue: 74/255, alpha: 0.31).cgColor)
        ctx.setStrokeColor(NSColor(calibratedRed: 125/255, green: 75/255, blue: 32/255, alpha: 0.47).cgColor)
        ctx.setLineWidth(0.9)
        ctx.addPath(CGPath(roundedRect: rect, cornerWidth: 2.5, cornerHeight: 2.5, transform: nil))
        ctx.drawPath(using: .fillStroke)
        ctx.restoreGState()
    }
    private func drawPaw(_ tip: CGPoint, _ rest: CGPoint, _ onRight: Bool, _ lean: CGFloat, _ dip: CGFloat, _ ctx: CGContext) {
        let shoulder = CGPoint(x: rest.x + (onRight ? -8 : 8) + (tip.x - rest.x) * 0.23 + lean, y: 165 + dip + (tip.y - rest.y) * 0.22)
        let dx = tip.x - shoulder.x, dy = tip.y - shoulder.y, length = hypot(dx, dy)
        let width = 34 - min(4, max(0, length - 50) * 0.10)
        ctx.saveGState()
        ctx.translateBy(x: shoulder.x, y: shoulder.y)
        ctx.rotate(by: -atan2(dx, dy))
        if onRight { ctx.scaleBy(x: -1, y: 1) }
        let height = length + 2.5
        if let gradient = Self.pawGradient {
            ctx.beginTransparencyLayer(auxiliaryInfo: nil)
            sprite(skin.paw, CGRect(x: -width / 2, y: 0, width: width, height: height), ctx)
            ctx.setBlendMode(.destinationIn)
            ctx.drawLinearGradient(gradient, start: .zero, end: CGPoint(x: 0, y: height),
                                   options: [.drawsAfterEndLocation])
            ctx.endTransparencyLayer()
        } else {
            sprite(skin.paw, CGRect(x: -width / 2, y: 0, width: width, height: height), ctx)
        }
        ctx.restoreGState()
    }
    private func drawBubble(_ hand: Stroke, _ now: TimeInterval, _ onRight: Bool, _ ctx: CGContext) {
        guard hand.active else { return }
        let label = hand.code == 49 ? "Space" : keyInfo(hand.code).label
        let age = now - hand.started
        guard !label.isEmpty, age >= 0.022, age < 0.395 else { return }
        let t = (age - 0.022) / 0.373, opacity = min(1, min(t * 9, (1 - t) * 3.8))
        let attrs = Self.bubbleAttributes
        let text = label as NSString, size = text.size(withAttributes: attrs), width = max(29, size.width + 13)
        let x = onRight ? max(300, hand.target.x + 6) : min(90, hand.target.x - 6), y = 173 - 34 * t
        ctx.saveGState()
        ctx.setAlpha(opacity)
        ctx.translateBy(x: x, y: y)
        ctx.rotate(by: (onRight ? 7 : -7) * .pi / 180)
        ctx.setStrokeColor(NSColor(calibratedRed: 111/255, green: 73/255, blue: 47/255, alpha: 1).cgColor)
        ctx.setLineWidth(1.55)
        for (offset, color) in [(CGFloat(-11), NSColor(calibratedRed: 218/255, green: 149/255, blue: 84/255, alpha: 1)), (CGFloat(-14), NSColor(calibratedRed: 1, green: 243/255, blue: 216/255, alpha: 1))] {
            ctx.setFillColor(color.cgColor)
            ctx.addPath(CGPath(roundedRect: CGRect(x: -width/2, y: offset, width: width, height: 24), cornerWidth: 5, cornerHeight: 5, transform: nil))
            ctx.drawPath(using: .fillStroke)
        }
        text.draw(at: CGPoint(x: -size.width/2, y: -2 - size.height/2), withAttributes: attrs)
        ctx.restoreGState()
    }
}

private func smooth(_ t: CGFloat) -> CGFloat { t * t * (3 - 2 * t) }
private func mix(_ a: CGPoint, _ b: CGPoint, _ t: CGFloat) -> CGPoint { CGPoint(x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t) }
private func contact(_ age: TimeInterval) -> CGFloat {
    guard age >= 0.035, age <= 0.155 else { return 0 }
    return min(1, min((age - 0.035) / 0.017, (0.155 - age) / 0.053))
}

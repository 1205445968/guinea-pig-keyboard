import AppKit

struct PetSkin {
    let id: String
    let name: String
    let body: String
    let paw: String
    let networkID: String
    let isProne: Bool
    let furColor: CGColor
    let inkColor: CGColor
    private let eyeColor: CGColor

    static let all = [
        PetSkin("tricolor", "三花", paw: "tricolor", networkID: "tricolor", darkEyes: true),
        PetSkin("black-white", "黑白", paw: "black-white", networkID: "silver-agouti", darkEyes: true),
        PetSkin("golden-white", "黄白", paw: "golden-white", networkID: "gold-white"),
        PetSkin("buff", "浅金棕", coat: "white", paw: "white", networkID: "gold-white", fur: 0xE6C391),
        PetSkin("cream", "奶油", paw: "cream", networkID: "californian", fur: 0xF5DEAF),
        PetSkin("chocolate", "巧克力", paw: "chocolate", networkID: "teddy", fur: 0xA17B63, ink: 0x4A3127),
        PetSkin("californian", "加州", paw: "tricolor", networkID: "californian"),
        PetSkin("longhair-gold-white", "长毛黄白", coat: "longhair-template", paw: "golden-white", networkID: "longhair"),
        PetSkin("longhair-tricolor", "长毛三花", paw: "tricolor", networkID: "longhair", darkEyes: true),
        PetSkin("longhair-black-white", "长毛黑白", paw: "black-white", networkID: "longhair", darkEyes: true),
        PetSkin("longhair-buff", "长毛浅金棕", paw: "white", networkID: "longhair", fur: 0xE6C391),
        PetSkin("longhair-cream", "长毛奶油", paw: "cream", networkID: "longhair", fur: 0xF5DEAF),
        PetSkin("longhair-chocolate", "长毛巧克力", paw: "chocolate", networkID: "longhair", fur: 0xA17B63, ink: 0x4A3127),
        PetSkin("longhair-californian", "长毛加州", paw: "tricolor", networkID: "longhair"),
        PetSkin("tricolor", "趴趴三花", paw: "tricolor", networkID: "tricolor", darkEyes: true, prone: true),
        PetSkin("black-white", "趴趴黑白", paw: "black-white", networkID: "silver-agouti", darkEyes: true, prone: true),
        PetSkin("golden-white", "趴趴黄白", paw: "golden-white", networkID: "gold-white", prone: true),
        PetSkin("cream", "趴趴奶油", paw: "cream", networkID: "californian", fur: 0xF5DEAF, prone: true)
    ]

    private static let legacyIDs = [
        "tricolor": "pixel-tricolor", "californian": "pixel-californian", "gold-white": "pixel-golden-white",
        "teddy": "pixel-chocolate", "silver-agouti": "pixel-black-white", "longhair": "pixel-longhair-gold-white"
    ]
    private static let gaze = [CGPoint.zero, CGPoint(x: -2, y: 0), CGPoint(x: 2, y: 0),
                               CGPoint(x: 0, y: -2), CGPoint(x: 0, y: 2),
                               CGPoint(x: -2, y: -2), CGPoint(x: 2, y: -2),
                               CGPoint(x: -2, y: 2), CGPoint(x: 2, y: 2)]
    private static let openEye: [UInt8] = [0b011110, 0b111111, 0b111111, 0b111111, 0b111111, 0b011110]
    private static let halfEye: [UInt8] = [0, 0, 0b011110, 0b111111, 0b011110, 0]
    private static let closedEye: [UInt8] = [0, 0, 0b100001, 0b110011, 0b011110, 0]
    private static let glint = color(0xFFF0D5)

    private init(_ id: String, _ name: String, coat: String? = nil, paw: String, networkID: String,
                 fur: UInt32 = 0xFFFAF1, ink: UInt32 = 0x624737, darkEyes: Bool = false, prone: Bool = false) {
        self.isProne = prone
        self.id = (prone ? "prone-" : "pixel-") + id
        self.name = name
        self.body = "guinea-pig-" + (prone ? self.id : (coat ?? id)) + ".png"
        self.paw = "paw-\(paw).png"
        self.networkID = networkID
        self.furColor = Self.color(fur)
        self.inkColor = Self.color(ink)
        self.eyeColor = Self.color(darkEyes ? 0x302722 : ink)
    }

    static func find(_ id: String) -> PetSkin? {
        let legacyID = id.hasPrefix("drawn-") ? String(id.dropFirst(6)) : id
        let resolved = legacyIDs[legacyID] ?? id
        return all.first { $0.id == resolved }
    }

    static func named(_ id: String?) -> PetSkin { id.flatMap { find($0) } ?? all[0] }

    private static func color(_ rgb: UInt32) -> CGColor {
        NSColor(srgbRed: CGFloat((rgb >> 16) & 255) / 255, green: CGFloat((rgb >> 8) & 255) / 255,
                blue: CGFloat(rgb & 255) / 255, alpha: 1).cgColor
    }

    func drawEyes(in rect: CGRect, frame: Int, context: CGContext) {
        let offset = Self.gaze.indices.contains(frame) ? Self.gaze[frame] : .zero
        let rows = frame == 11 ? Self.closedEye : frame >= 9 ? Self.halfEye : Self.openEye
        let sx = rect.width / 100, sy = rect.height / (isProne ? 58 : 104)
        let positions: [CGFloat] = isProne ? [73] : [30, 65]
        let eyeY: CGFloat = isProne ? 19 : 30
        context.saveGState()
        context.setShouldAntialias(false)
        context.setFillColor(eyeColor)
        for x in positions {
            for (y, row) in rows.enumerated() {
                for column in 0..<6 where row & (0b100000 >> column) != 0 {
                    context.fill(CGRect(x: rect.minX + (x + CGFloat(column) + offset.x) * sx,
                                        y: rect.minY + (eyeY + CGFloat(y) + offset.y) * sy, width: sx, height: sy))
                }
            }
        }
        if frame < 9 {
            context.setFillColor(Self.glint)
            for x in positions {
                context.fill(CGRect(x: rect.minX + (x + 1 + offset.x) * sx,
                                    y: rect.minY + (eyeY + 1 + offset.y) * sy, width: sx, height: sy))
            }
        }
        context.restoreGState()
    }

    func portrait(size: NSSize) -> NSImage? {
        guard let url = Bundle.main.url(forResource: body, withExtension: nil), let image = NSImage(contentsOf: url) else { return nil }
        return NSImage(size: size, flipped: true) { rect in
            guard let context = NSGraphicsContext.current?.cgContext else { return false }
            context.saveGState()
            context.interpolationQuality = .none
            let height = self.isProne ? CGFloat(58) : 104
            let scale = min(rect.width / 100, rect.height / height)
            let bodyRect = CGRect(x: rect.midX - 50 * scale, y: rect.midY - height * scale / 2,
                                  width: 100 * scale, height: height * scale)
            image.draw(in: bodyRect, from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
            self.drawEyes(in: bodyRect, frame: 0, context: context)
            context.restoreGState()
            return true
        }
    }
}

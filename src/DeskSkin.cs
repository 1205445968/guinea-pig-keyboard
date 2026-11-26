using System;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal sealed class DeskSkin
    {
        public static readonly DeskSkin[] All = {
            new DeskSkin(
                "meadow", "牧草奶酪",
                "keyboard.png", "mouse.png", "pixel-keyboard.png", "pixel-mouse.png",
                Rgb(195, 218, 197), Rgb(255, 242, 218), Rgb(243, 194, 94), Rgb(98, 71, 55),
                Rgba(255, 185, 74, 80), Rgba(125, 75, 32, 120),
                Rgb(255, 243, 216), Rgb(241, 180, 151), Rgb(98, 71, 55),
                Rgb(255, 243, 216), Rgb(229, 188, 97), Rgb(78, 53, 30),
                Rgb(241, 180, 151)),
            new DeskSkin(
                "peach", "蜜桃奶盖",
                "keyboard-peach.png", "mouse-peach.png", "pixel-keyboard-peach.png", "pixel-mouse-peach.png",
                Rgb(245, 205, 212), Rgb(255, 246, 248), Rgb(238, 125, 153), Rgb(104, 58, 66),
                Rgba(244, 128, 158, 88), Rgba(138, 66, 84, 128),
                Rgb(255, 245, 248), Rgb(244, 158, 179), Rgb(104, 58, 66),
                Rgb(255, 245, 248), Rgb(238, 125, 153), Rgb(92, 51, 60),
                Rgb(244, 140, 164)),
            new DeskSkin(
                "mocha", "可可黑金",
                "keyboard-mocha.png", "mouse-mocha.png", "pixel-keyboard-mocha.png", "pixel-mouse-mocha.png",
                Rgb(79, 63, 57), Rgb(102, 83, 75), Rgb(224, 166, 75), Rgb(33, 23, 20),
                Rgba(235, 178, 82, 92), Rgba(255, 214, 130, 145),
                Rgb(74, 59, 53), Rgb(224, 166, 75), Rgb(245, 228, 198),
                Rgb(74, 59, 53), Rgb(224, 166, 75), Rgb(245, 228, 198),
                Rgb(224, 166, 75))
        };

        public static DeskSkin Default { get { return All[0]; } }

        public readonly string Id;
        public readonly string Name;
        public readonly string DrawnKeyboardResource;
        public readonly string DrawnMouseResource;
        public readonly string PixelKeyboardResource;
        public readonly string PixelMouseResource;
        public readonly Color SwatchShell;
        public readonly Color SwatchKey;
        public readonly Color SwatchAccent;
        public readonly Color SwatchInk;
        public readonly Brush PressGlow;
        public readonly Pen PressLine;
        public readonly Brush PixelBubblePaper;
        public readonly Brush PixelBubbleAccent;
        public readonly Color PixelBubbleInkColor;
        public readonly Brush PixelBubbleInk;
        public readonly Pen PixelBubbleLine;
        public readonly Brush DrawnBubblePaper;
        public readonly Brush DrawnBubbleAccent;
        public readonly Color DrawnBubbleInkColor;
        public readonly Brush DrawnBubbleInk;
        public readonly Pen DrawnBubbleLine;
        public readonly Brush MouseClickGlow;

        private DeskSkin(
            string id, string name,
            string drawnKb, string drawnMouse, string pixelKb, string pixelMouse,
            Color swatchShell, Color swatchKey, Color swatchAccent, Color swatchInk,
            Color pressGlow, Color pressEdge,
            Color pxPaper, Color pxAccent, Color pxInk,
            Color drPaper, Color drAccent, Color drInk,
            Color mouseGlow)
        {
            Id = id;
            Name = name;
            DrawnKeyboardResource = drawnKb;
            DrawnMouseResource = drawnMouse;
            PixelKeyboardResource = pixelKb;
            PixelMouseResource = pixelMouse;
            SwatchShell = swatchShell;
            SwatchKey = swatchKey;
            SwatchAccent = swatchAccent;
            SwatchInk = swatchInk;
            PressGlow = FreezeBrush(pressGlow);
            PressLine = FreezePen(FreezeBrush(pressEdge), 0.9);
            PixelBubblePaper = FreezeBrush(pxPaper);
            PixelBubbleAccent = FreezeBrush(pxAccent);
            PixelBubbleInkColor = pxInk;
            PixelBubbleInk = FreezeBrush(pxInk);
            PixelBubbleLine = FreezePen(FreezeBrush(id == "mocha" ? Rgb(33, 23, 20) : pxInk), 1.0);
            DrawnBubblePaper = FreezeBrush(drPaper);
            DrawnBubbleAccent = FreezeBrush(drAccent);
            DrawnBubbleInkColor = drInk;
            DrawnBubbleInk = FreezeBrush(drInk);
            DrawnBubbleLine = FreezePen(FreezeBrush(id == "mocha" ? Rgb(33, 23, 20) : drInk), 1.55);
            MouseClickGlow = FreezeBrush(mouseGlow);
        }

        public static DeskSkin FromId(string id)
        {
            if (!String.IsNullOrEmpty(id))
                foreach (DeskSkin skin in All)
                    if (String.Equals(skin.Id, id, StringComparison.OrdinalIgnoreCase)) return skin;
            return Default;
        }

        public ImageSource SwatchImage()
        {
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                Pen border = FreezePen(FreezeBrush(SwatchInk), 1.1);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchShell), border, new Rect(0.6, 1.6, 16.8, 10.8), 2.4, 2.4);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchKey), null, new Rect(2.4, 3.4, 4.2, 3.0), 0.8, 0.8);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchKey), null, new Rect(7.4, 3.4, 4.2, 3.0), 0.8, 0.8);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchAccent), null, new Rect(12.4, 3.4, 3.2, 3.0), 0.8, 0.8);
                dc.DrawRoundedRectangle(FreezeBrush(SwatchKey), null, new Rect(3.2, 7.3, 11.6, 3.0), 0.8, 0.8);
            }
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        private static Color Rgb(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }
        private static Color Rgba(byte r, byte g, byte b, byte a) { return Color.FromArgb(a, r, g, b); }
        private static SolidColorBrush FreezeBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FreezePen(Brush b, double w) { var p = new Pen(b, w); p.Freeze(); return p; }
    }
}

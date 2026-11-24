using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal static class PixelKeyboardArt
    {
        private static readonly Rect KeyboardBounds = new Rect(64, 199, 318, 88);
        // The atlas contains the resting keyboard above the pressed keyboard.
        private static readonly BitmapSource Atlas = PixelPetScene.Sprite("keyboard.png");
        private static readonly int FrameHeight = Atlas.PixelHeight / 2;
        private static readonly BitmapSource Resting = Crop(new Int32Rect(0, 0, Atlas.PixelWidth, FrameHeight));
        private static readonly Dictionary<int, Key> Lookup = IndexKeys();

        private sealed class Key
        {
            public readonly int Scan;
            public readonly string Text;
            public readonly Point Center;
            public readonly Rect Bounds;
            public readonly BitmapSource Pressed;

            public Key(int scan, string text, int x, int y, int width, int labelX)
            {
                const int height = 15;
                Scan = scan;
                Text = text;
                double sx = KeyboardBounds.Width / Atlas.PixelWidth;
                double sy = KeyboardBounds.Height / FrameHeight;
                Bounds = new Rect(KeyboardBounds.X + x * sx, KeyboardBounds.Y + y * sy, width * sx, height * sy);
                Center = new Point(KeyboardBounds.X + (labelX + 0.5) * sx, KeyboardBounds.Y + (y + 7.5) * sy);
                Pressed = Crop(new Int32Rect(x, FrameHeight + y, width, height));
            }
        }

        private static BitmapSource Crop(Int32Rect pixels)
        {
            var image = new CroppedBitmap(Atlas, pixels);
            image.Freeze();
            return image;
        }

        private static Dictionary<int, Key> IndexKeys()
        {
            // Scan code, legend, left, top, width and legend center in the native keyboard frame.
            Key[] keys = {
                new Key(1, "Esc", 324, 71, 29, 337),
                new Key(2, "1", 299, 71, 25, 311),
                new Key(3, "2", 273, 71, 26, 285),
                new Key(4, "3", 247, 71, 26, 260),
                new Key(5, "4", 222, 71, 25, 233),
                new Key(6, "5", 196, 71, 26, 208),
                new Key(7, "6", 171, 71, 24, 183),
                new Key(8, "7", 144, 71, 26, 157),
                new Key(9, "8", 119, 71, 25, 131),
                new Key(10, "9", 94, 71, 25, 106),
                new Key(11, "0", 70, 71, 24, 82),
                new Key(12, "-", 46, 71, 24, 58),
                new Key(14, "⌫", 14, 71, 32, 31),

                new Key(15, "Tab", 314, 55, 37, 332),
                new Key(16, "Q", 289, 55, 25, 300),
                new Key(17, "W", 263, 55, 26, 275),
                new Key(18, "E", 237, 55, 26, 249),
                new Key(19, "R", 211, 55, 26, 223),
                new Key(20, "T", 185, 55, 26, 198),
                new Key(21, "Y", 158, 55, 26, 171),
                new Key(22, "U", 132, 55, 26, 145),
                new Key(23, "I", 107, 55, 25, 119),
                new Key(24, "O", 81, 55, 26, 94),
                new Key(25, "P", 55, 55, 26, 68),
                new Key(43, "\\", 16, 55, 39, 37),

                new Key(58, "Caps", 305, 39, 44, 325),
                new Key(30, "A", 279, 39, 26, 290),
                new Key(31, "S", 253, 39, 26, 265),
                new Key(32, "D", 227, 39, 26, 238),
                new Key(33, "F", 202, 39, 25, 213),
                new Key(34, "G", 177, 39, 25, 189),
                new Key(35, "H", 149, 39, 27, 162),
                new Key(36, "J", 124, 39, 25, 136),
                new Key(37, "K", 98, 39, 26, 111),
                new Key(38, "L", 72, 39, 26, 86),
                new Key(28, "Enter", 18, 39, 54, 46),

                new Key(42, "Shift", 294, 23, 53, 318),
                new Key(44, "Z", 265, 23, 29, 277),
                new Key(45, "X", 240, 23, 25, 251),
                new Key(46, "C", 215, 23, 25, 227),
                new Key(47, "V", 189, 23, 26, 201),
                new Key(48, "B", 163, 23, 25, 176),
                new Key(49, "N", 138, 23, 25, 150),
                new Key(50, "M", 112, 23, 26, 126),
                new Key(51, ",", 86, 23, 26, 99),
                new Key(52, ".", 61, 23, 25, 73),
                new Key(54, "Shift", 20, 23, 41, 42),

                new Key(29, "Ctrl", 313, 7, 32, 327),
                new Key(0x15b, "◆", 286, 7, 27, 298),
                new Key(56, "Alt", 261, 7, 25, 272),
                new Key(57, "", 108, 7, 153, 185),
                new Key(0x138, "Alt", 81, 7, 26, 95),
                new Key(0x15d, "Menu", 54, 7, 27, 67),
                new Key(0x11d, "Ctrl", 22, 7, 32, 39),
            };
            var lookup = new Dictionary<int, Key>();
            foreach (Key key in keys) lookup.Add(key.Scan, key);
            lookup.Add(13, lookup[12]);
            lookup.Add(26, lookup[25]);
            lookup.Add(27, lookup[43]);
            lookup.Add(39, lookup[38]);
            lookup.Add(40, lookup[28]);
            lookup.Add(53, lookup[52]);
            lookup.Add(0x11c, lookup[28]);
            lookup.Add(0x15c, lookup[0x15d]);
            return lookup;
        }
        private static void DrawPressed(DrawingContext dc, Key key, double amount)
        {
            dc.PushOpacity(amount);
            dc.DrawImage(key.Pressed, key.Bounds);
            dc.Pop();
        }

        private static Key FindKey(int scan, bool right)
        {
            Key key;
            return scan != 0 && Lookup.TryGetValue(scan, out key) ? key : Lookup[right ? 36 : 33];
        }
        private static double Clamp(double amount)
        {
            return double.IsNaN(amount) ? 0 : Math.Max(0, Math.Min(1, amount));
        }
        public static void Draw(DrawingContext dc, int leftScan, double leftAmount, int rightScan, double rightAmount)
        {
            if (dc == null) throw new ArgumentNullException("dc");
            dc.DrawImage(Resting, KeyboardBounds);
            double left = Clamp(leftAmount);
            double right = Clamp(rightAmount);
            Key leftKey = FindKey(leftScan, false);
            Key rightKey = FindKey(rightScan, true);
            if (left > 0) DrawPressed(dc, leftKey, leftKey == rightKey ? Math.Max(left, right) : left);
            if (right > 0 && (rightKey != leftKey || left <= 0)) DrawPressed(dc, rightKey, right);
        }
        public static bool IsRight(int scan)
        {
            int make = scan & 0xff;
            if (make >= 0x47 && make <= 0x53) return true;
            switch (scan)
            {
                case 7: case 8: case 9: case 10: case 11: case 12: case 13: case 14:
                case 21: case 22: case 23: case 24: case 25: case 26: case 27: case 43:
                case 35: case 36: case 37: case 38: case 39: case 40: case 28:
                case 49: case 50: case 51: case 52: case 53: case 54:
                case 0x37: case 0x45: case 0x46: case 0x135: case 0x137:
                case 0x11c: case 0x11d: case 0x138: case 0x15c: case 0x15d:
                    return true;
                default: return false;
            }
        }
        public static string Label(int scan)
        {
            if (scan >= 0x3b && scan <= 0x44) return "F" + (scan - 0x3a);
            switch (scan)
            {
                case 13: return "=";
                case 26: return "[";
                case 27: return "]";
                case 39: return ";";
                case 40: return "'";
                case 53: return "/";
                case 57: return "Space";
                case 0x57: return "F11";
                case 0x58: return "F12";
                case 0x147: return "Home";
                case 0x148: return "↑";
                case 0x149: return "PgUp";
                case 0x14b: return "←";
                case 0x14d: return "→";
                case 0x14f: return "End";
                case 0x150: return "↓";
                case 0x151: return "PgDn";
                case 0x152: return "Ins";
                case 0x153: return "Del";
                case 0x11c: return "Enter";
                case 0x15c: return "Win";
            }
            Key key;
            return scan != 0 && Lookup.TryGetValue(scan, out key) ? key.Text : "";
        }
        public static Point Target(int scan, bool right)
        {
            return FindKey(scan, right).Center;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WheekKeys
{
    internal static class KeyboardArt
    {
        private const double TextureWidth = 1774;
        private const double TextureHeight = 887;
        private static readonly Rect KeyboardBounds = new Rect(64, 199, 318, 88);
        private static readonly double[] RowEdges = { 119, 243, 367, 490, 612, 739 };

        private sealed class Key
        {
            public int Scan;
            public string Text;
            public Point Center;
            public double Width;
            public FormattedText Label;
            public Key(int scan, string text, Point center, double width)
            {
                Scan = scan;
                Text = text;
                Center = center;
                Width = width;
                Label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"), text.Length > 1 ? 7.0 : 9.0, Ink, 1.0);
            }
        }

        private static readonly Brush Ink = BrushFrom(91, 56, 35, 255);
        private static readonly Brush PressGlow = BrushFrom(255, 185, 74, 80);
        private static readonly Brush PressEdge = BrushFrom(125, 75, 32, 120);
        private static readonly Pen PressLine = NewPen(PressEdge, 0.9);
        private static readonly Key[] Keys = CreateKeys();
        private static readonly Dictionary<int, Key> Lookup = IndexKeys();
        private static readonly DrawingGroup Artwork = CreateArtwork();

        private static Brush BrushFrom(byte r, byte g, byte b, byte a)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
        private static Pen NewPen(Brush brush, double width)
        {
            Pen pen = new Pen(brush, width);
            pen.Freeze();
            return pen;
        }
        private static Matrix ProjectRow(int row)
        {
            double top = RowEdges[row];
            double bottom = RowEdges[row + 1];
            // Reverse the row order while keeping the keycap bevels facing the viewer.
            double targetTop = RowEdges[0] + RowEdges[RowEdges.Length - 1] - bottom;
            double sourceMiddle = (top + bottom) / 2;
            double targetMiddle = targetTop + (bottom - top) / 2;
            double scaleX = KeyboardBounds.Width / TextureWidth * WellWidth(targetMiddle) / WellWidth(sourceMiddle);
            double scaleY = KeyboardBounds.Height / TextureHeight;
            return new Matrix(-scaleX, 0, 0, scaleY,
                KeyboardBounds.X + (KeyboardBounds.Width + TextureWidth * scaleX) / 2,
                KeyboardBounds.Y + (targetTop - top) * scaleY);
        }
        private static double WellWidth(double y)
        {
            double depth = (y - RowEdges[0]) / (RowEdges[RowEdges.Length - 1] - RowEdges[0]);
            return 1604 + (1706 - 1604) * depth;
        }
        private static Key[] CreateKeys()
        {
            List<Key> keys = new List<Key>();
            AddRow(keys, 0, 171, new double[] { 151,276,400,522,647,769,889,1012,1135,1253,1370,1485,1613 },
                new int[] { 1,2,3,4,5,6,7,8,9,10,11,12,14 },
                new string[] { "Esc","1","2","3","4","5","6","7","8","9","0","-","⌫" });
            AddRow(keys, 1, 290, new double[] { 160,313,437,565,691,816,944,1071,1198,1322,1449,1600 },
                new int[] { 15,16,17,18,19,20,21,22,23,24,25,43 },
                new string[] { "Tab","Q","W","E","R","T","Y","U","I","O","P","\\" });
            AddRow(keys, 2, 416, new double[] { 175,350,475,610,736,857,991,1121,1246,1375,1571 },
                new int[] { 58,30,31,32,33,34,35,36,37,38,28 },
                new string[] { "Caps","A","S","D","F","G","H","J","K","L","Enter" });
            AddRow(keys, 3, 541, new double[] { 193,402,537,663,797,923,1055,1181,1320,1450,1610 },
                new int[] { 42,44,45,46,47,48,49,50,51,52,54 },
                new string[] { "Shift","Z","X","C","V","B","N","M",",",".","Shift" });
            AddRow(keys, 4, 662, new double[] { 127,281,417,878,1351,1495,1647 },
                new int[] { 29,0x15b,56,57,0x138,0x15d,0x11d },
                new string[] { "Ctrl","◆","Alt","","Alt","Menu","Ctrl" });
            return keys.ToArray();
        }
        private static void AddRow(List<Key> keys, int row, double y, double[] x, int[] scans, string[] labels)
        {
            Matrix projection = ProjectRow(row);
            for (int i = 0; i < scans.Length; i++)
            {
                double width = scans[i] == 57 ? 720 :
                    (scans[i] == 28 || scans[i] == 42 || scans[i] == 54 ? 220 : 103);
                Point center = projection.Transform(new Point(x[i], y));
                keys.Add(new Key(scans[i], labels[i], center, width * -projection.M11));
            }
        }
        private static Dictionary<int, Key> IndexKeys()
        {
            Dictionary<int, Key> keys = new Dictionary<int, Key>();
            foreach (Key key in Keys) keys.Add(key.Scan, key);
            keys.Add(13, keys[12]);
            keys.Add(26, keys[25]);
            keys.Add(27, keys[43]);
            keys.Add(39, keys[38]);
            keys.Add(40, keys[28]);
            keys.Add(53, keys[52]);
            keys.Add(0x11c, keys[28]);
            keys.Add(0x15c, keys[0x15d]);
            return keys;
        }
        private static DrawingGroup CreateArtwork()
        {
            DrawingGroup group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                ImageSource bitmap = PetScene.Sprite("keyboard.png");
                dc.DrawImage(bitmap, KeyboardBounds);
                StreamGeometry well = CreateWell();
                dc.PushClip(well);
                dc.DrawRectangle(Ink, null, well.Bounds);
                for (int row = 0; row < RowEdges.Length - 1; row++)
                {
                    dc.PushTransform(new MatrixTransform(ProjectRow(row)));
                    dc.PushClip(new RectangleGeometry(new Rect(0, RowEdges[row], TextureWidth,
                        RowEdges[row + 1] - RowEdges[row])));
                    dc.DrawImage(bitmap, new Rect(0, 0, TextureWidth, TextureHeight));
                    dc.Pop();
                    dc.Pop();
                }
                dc.Pop();
                foreach (Key key in Keys) DrawLabel(dc, key, 0);
            }
            group.Freeze();
            return group;
        }
        private static StreamGeometry CreateWell()
        {
            double top = RowEdges[0];
            double bottom = RowEdges[RowEdges.Length - 1];
            double center = KeyboardBounds.Left + KeyboardBounds.Width / 2;
            double topHalf = WellWidth(top) * KeyboardBounds.Width / TextureWidth / 2;
            double bottomHalf = WellWidth(bottom) * KeyboardBounds.Width / TextureWidth / 2;
            double topY = KeyboardBounds.Y + top * KeyboardBounds.Height / TextureHeight;
            double bottomY = KeyboardBounds.Y + bottom * KeyboardBounds.Height / TextureHeight;
            StreamGeometry well = new StreamGeometry();
            using (StreamGeometryContext path = well.Open())
            {
                path.BeginFigure(new Point(center - topHalf, topY), true, true);
                path.LineTo(new Point(center + topHalf, topY), false, false);
                path.LineTo(new Point(center + bottomHalf, bottomY), false, false);
                path.LineTo(new Point(center - bottomHalf, bottomY), false, false);
            }
            well.Freeze();
            return well;
        }
        private static void DrawLabel(DrawingContext dc, Key key, double offset)
        {
            dc.PushTransform(new RotateTransform(180, key.Center.X, key.Center.Y));
            dc.DrawText(key.Label, new Point(key.Center.X - key.Label.Width / 2,
                key.Center.Y - key.Label.Height / 2 + offset));
            dc.Pop();
        }
        private static void DrawPressed(DrawingContext dc, Key key, double amount)
        {
            double width = key.Width * 0.88;
            double height = 10.0 * 88 / 102;
            Rect face = new Rect(key.Center.X - width / 2, key.Center.Y - height / 2 + amount, width, height);
            dc.PushOpacity(amount);
            dc.DrawRoundedRectangle(PressGlow, PressLine, face, 2.5, 2.5);
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
            dc.DrawDrawing(Artwork);
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

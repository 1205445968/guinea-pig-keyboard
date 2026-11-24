using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class CoatArt
    {
        public const string DefaultId = "californian";
        private static readonly Color Paper = Color.FromRgb(255, 250, 241);
        private static readonly Color OriginalInk = Color.FromRgb(98, 71, 55);
        private static readonly Color Grey = Color.FromRgb(164, 150, 132);
        private static readonly Color Charcoal = Color.FromRgb(119, 112, 106);
        private static readonly Color Gold = Color.FromRgb(220, 164, 94);
        private static readonly Color PaleGold = Color.FromRgb(235, 210, 155);

        public static readonly CoatArt[] Presets = {
            new CoatArt("tricolor", "三花", Paper, Grey, OriginalInk, "guinea-pig-tricolor.png"),
            new CoatArt("black-white", "黑白", Paper, Charcoal, OriginalInk),
            new CoatArt("golden-white", "黄白", Paper, PaleGold, OriginalInk, "guinea-pig-golden-white.png"),
            new CoatArt("white", "浅金棕", Color.FromRgb(230, 195, 145), Color.FromRgb(177, 137, 107), OriginalInk),
            new CoatArt("cream", "奶油", Color.FromRgb(245, 222, 175), Color.FromRgb(190, 158, 116), OriginalInk),
            new CoatArt("chocolate", "巧克力", Color.FromRgb(161, 123, 99), Color.FromRgb(117, 83, 65), Color.FromRgb(74, 49, 39)),
            new CoatArt("longhair-template", "长毛黄白（模板）", Paper, PaleGold, OriginalInk, "guinea-pig-longhair-template.png"),
            new CoatArt("longhair-tricolor", "长毛三花", Paper, Grey, OriginalInk, "guinea-pig-longhair-tricolor.png"),
            new CoatArt("longhair-black-white", "长毛黑白", Paper, Charcoal, OriginalInk, "guinea-pig-longhair-black-white.png"),
            new CoatArt("longhair-buff", "长毛浅金棕", Color.FromRgb(230, 195, 145), Color.FromRgb(177, 137, 107), OriginalInk, "guinea-pig-longhair-buff.png"),
            new CoatArt("longhair-cream", "长毛奶油", Color.FromRgb(245, 222, 175), Color.FromRgb(190, 158, 116), OriginalInk, "guinea-pig-longhair-cream.png"),
            new CoatArt("longhair-chocolate", "长毛巧克力", Color.FromRgb(161, 123, 99), Color.FromRgb(117, 83, 65), Color.FromRgb(74, 49, 39), "guinea-pig-longhair-chocolate.png"),
            new CoatArt("longhair-californian", "长毛加州重点色", Paper, Grey, OriginalInk, "guinea-pig-longhair-californian.png"),
            new CoatArt(DefaultId, "加州重点色", Paper, Grey, OriginalInk)
        };

        public string Id { get; private set; }
        public string Name { get; private set; }
        public Color Fur { get; private set; }
        public Color Ink { get; private set; }
        public string BodyAsset { get; private set; }
        private readonly Color points;

        private CoatArt(string id, string name, Color fur, Color points, Color ink, string bodyAsset = null)
        {
            Id = id;
            Name = name;
            Fur = fur;
            this.points = points;
            Ink = ink;
            BodyAsset = bodyAsset;
        }

        public static CoatArt Find(string id)
        {
            foreach (CoatArt coat in Presets)
                if (String.Equals(coat.Id, id, StringComparison.Ordinal)) return coat;
            return Presets[Presets.Length - 1];
        }

        private static bool Inside(int x, int y, double cx, double cy, double rx, double ry)
        {
            double dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy < 1;
        }

        private Color FurAt(int x, int y)
        {
            x = x / 2 * 2;
            y = y / 2 * 2;
            bool leftFace = Inside(x, y, 20, 24, 21, 29);
            bool rightFace = Inside(x, y, 85, 24, 21, 30);
            bool leftSide = Inside(x, y, 2, 101, 23, 31);
            bool rightSide = Inside(x, y, 101, 99, 22, 28);
            if (Id == "tricolor")
            {
                if (leftFace || leftSide) return Charcoal;
                if (rightFace || rightSide) return Gold;
            }
            if (Id == "black-white" && (leftFace || rightSide)) return Charcoal;
            if (Id == "golden-white" && (leftFace || rightFace || leftSide)) return PaleGold;
            return Fur;
        }

        private Color PointAt(int x)
        {
            if (Id == "tricolor") return x < 30 ? Charcoal : x > 70 ? Gold : Grey;
            if (Id == "black-white" && x > 70) return Color.FromRgb(208, 186, 167);
            return points;
        }

        public BitmapSource Body(BitmapSource source)
        {
            return Recolor(source, false);
        }

        public BitmapSource Paw(BitmapSource source)
        {
            return Recolor(source, true);
        }

        private BitmapSource Recolor(BitmapSource source, bool paw)
        {
            int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
            byte[] pixels = new byte[stride * height];
            new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
            if (!paw && (Id == "black-white" || Id == "chocolate")) RepairBasePixels(pixels, width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    if (pixels[i + 3] == 0) continue;
                    Color original = Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
                    bool eye = !paw && y >= 30 && y < 36 && ((x >= 30 && x < 36) || (x >= 65 && x < 71));
                    bool cleanNose = !paw && (Id == "tricolor" || Id == "golden-white" || Id == "white")
                        && x >= 36 && x < 66 && y >= 18 && y < 49
                        && (original == Grey || original == Color.FromRgb(210, 193, 169)
                            || original == Color.FromRgb(243, 197, 170));
                    Color color = original;
                    if (eye || original == Paper || cleanNose) color = paw ? Fur : FurAt(x, y);
                    else if (Id != DefaultId)
                    {
                        if (original == Grey) color = paw ? points : PointAt(x);
                        else if (original == OriginalInk) color = Ink;
                        else if (original == Color.FromRgb(181, 151, 121)) color = Blend(Fur, Ink, 0.40);
                        else if (original == Color.FromRgb(124, 97, 77)) color = Blend(Fur, Ink, 0.80);
                        else if (original == Color.FromRgb(210, 193, 169)) color = Blend(Fur, Ink, 0.18);
                    }
                    pixels[i] = color.B;
                    pixels[i + 1] = color.G;
                    pixels[i + 2] = color.R;
                }
            if (!paw && Id == "tricolor") UprightEars(pixels, width);
            BitmapSource result = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        private static void RepairBasePixels(byte[] pixels, int width, int height)
        {
            byte[] original = (byte[])pixels.Clone();
            int ink = OriginalInk.R << 16 | OriginalInk.G << 8 | OriginalInk.B;
            for (int y = 1; y < height - 1; y++)
                for (int x = 1; x < width - 1; x++)
                {
                    int i = (y * width + x) * 4;
                    bool hole = original[i + 3] == 0;
                    bool stray = IsPink(original, i) && (y < 48 || y >= 58);
                    if (!hole && !stray) continue;
                    bool enclosed = true;
                    var counts = new Dictionary<int, int>();
                    int best = 0, bestCount = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int n = ((y + dy) * width + x + dx) * 4;
                            if (original[n + 3] == 0) { enclosed = false; continue; }
                            if (IsPink(original, n)) continue;
                            int color = original[n + 2] << 16 | original[n + 1] << 8 | original[n];
                            int count;
                            counts.TryGetValue(color, out count);
                            counts[color] = ++count;
                            if (count > bestCount || (count == bestCount && color == ink))
                            {
                                best = color;
                                bestCount = count;
                            }
                        }
                    // Fill only enclosed holes; retain the silhouette and the pink mouth.
                    if ((hole && !enclosed) || bestCount == 0) continue;
                    pixels[i] = (byte)(best & 255);
                    pixels[i + 1] = (byte)((best >> 8) & 255);
                    pixels[i + 2] = (byte)((best >> 16) & 255);
                    pixels[i + 3] = 255;
                }
        }

        private static bool IsPink(byte[] pixels, int offset)
        {
            return pixels[offset + 3] != 0 && pixels[offset] == 170
                && pixels[offset + 1] == 197 && pixels[offset + 2] == 243;
        }

        private void UprightEars(byte[] pixels, int width)
        {
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < width; x++)
                    if (x < 27 || x >= width - 27) pixels[(y * width + x) * 4 + 3] = 0;

            Color pink = Color.FromRgb(237, 185, 165);
            for (int y = 0; y < 32; y++)
                for (int x = 8; x < 28; x++)
                {
                    int px = x / 2 * 2, py = y / 2 * 2;
                    if (!Inside(px, py, 18, 15, 10, 16)) continue;
                    bool shell = Inside(px, py, 18, 15, 8, 14);
                    bool inner = Inside(px, py, 17, 14, 5, 10);
                    Put(pixels, width, x, y, inner ? pink : shell ? Charcoal : Ink);
                    Put(pixels, width, width - 1 - x, y, inner ? pink : shell ? Gold : Ink);
                }

            // The forehead overlaps the lower ears, keeping their roots behind the face.
            for (int y = 7; y < 40; y++)
            {
                int edge = y < 12 ? 33 - y : y < 20 ? 21 - (y - 12) / 2 : y < 30 ? 17 - (y - 20) * 4 / 10 : 13 - (y - 30) / 9;
                for (int x = edge; x < 27; x++)
                {
                    Put(pixels, width, x, y, x < edge + 2 ? Ink : FurAt(x, y));
                    int other = width - 1 - x;
                    Put(pixels, width, other, y, x < edge + 2 ? Ink : FurAt(other, y));
                }
            }
        }

        private static void Put(byte[] pixels, int width, int x, int y, Color color)
        {
            int i = (y * width + x) * 4;
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        private static Color Blend(Color a, Color b, double amount)
        {
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * amount),
                (byte)Math.Round(a.G + (b.G - a.G) * amount), (byte)Math.Round(a.B + (b.B - a.B) * amount));
        }
    }
}

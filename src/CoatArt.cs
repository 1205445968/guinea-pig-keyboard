using System;
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
            new CoatArt("tricolor", "三花", Paper, Grey, OriginalInk, "pixel-guinea-pig-tricolor.png", null),
            new CoatArt("black-white", "黑白", Paper, Charcoal, OriginalInk, "coat-guinea-pig-black-white.png", null),
            new CoatArt("golden-white", "黄白", Paper, PaleGold, OriginalInk, "pixel-guinea-pig-golden-white.png", "coat-paw-golden-white.png"),
            new CoatArt("white", "浅金棕", Color.FromRgb(230, 195, 145), Color.FromRgb(177, 137, 107), OriginalInk, "coat-guinea-pig-white.png", "coat-paw-white.png"),
            new CoatArt("cream", "奶油", Color.FromRgb(245, 222, 175), Color.FromRgb(190, 158, 116), OriginalInk, "coat-guinea-pig-cream.png", "coat-paw-cream.png"),
            new CoatArt("chocolate", "巧克力", Color.FromRgb(161, 123, 99), Color.FromRgb(117, 83, 65), Color.FromRgb(74, 49, 39), "coat-guinea-pig-chocolate.png", "coat-paw-chocolate.png"),
            new CoatArt("longhair-template", "长毛黄白（模板）", Paper, PaleGold, OriginalInk, "pixel-guinea-pig-longhair-template.png", "coat-paw-golden-white.png"),
            new CoatArt("longhair-tricolor", "长毛三花", Paper, Grey, OriginalInk, "pixel-guinea-pig-longhair-tricolor.png", null),
            new CoatArt("longhair-black-white", "长毛黑白", Paper, Charcoal, OriginalInk, "pixel-guinea-pig-longhair-black-white.png", null),
            new CoatArt("longhair-buff", "长毛浅金棕", Color.FromRgb(230, 195, 145), Color.FromRgb(177, 137, 107), OriginalInk, "pixel-guinea-pig-longhair-buff.png", "coat-paw-white.png"),
            new CoatArt("longhair-cream", "长毛奶油", Color.FromRgb(245, 222, 175), Color.FromRgb(190, 158, 116), OriginalInk, "pixel-guinea-pig-longhair-cream.png", "coat-paw-cream.png"),
            new CoatArt("longhair-chocolate", "长毛巧克力", Color.FromRgb(161, 123, 99), Color.FromRgb(117, 83, 65), Color.FromRgb(74, 49, 39), "pixel-guinea-pig-longhair-chocolate.png", "coat-paw-chocolate.png"),
            new CoatArt("longhair-californian", "长毛加州重点色", Paper, Grey, OriginalInk, "pixel-guinea-pig-longhair-californian.png", "coat-paw-tricolor.png"),
            new CoatArt(DefaultId, "加州重点色", Paper, Grey, OriginalInk, "coat-guinea-pig-californian.png", "coat-paw-tricolor.png")
        };

        public string Id { get; private set; }
        public string Name { get; private set; }
        public Color Fur { get; private set; }
        public Color Ink { get; private set; }
        public string BodyResource { get; private set; }
        public string PawResource { get; private set; }
        private readonly Color points;

        private CoatArt(string id, string name, Color fur, Color points, Color ink, string bodyResource, string pawResource)
        {
            Id = id;
            Name = name;
            Fur = fur;
            this.points = points;
            Ink = ink;
            BodyResource = bodyResource;
            PawResource = pawResource;
        }

        public static CoatArt Find(string id)
        {
            foreach (CoatArt coat in Presets)
                if (String.Equals(coat.Id, id, StringComparison.Ordinal)) return coat;
            return Presets[Presets.Length - 1];
        }

        public Color DefaultEyeColor
        {
            get
            {
                return Id == "black-white" || Id == "tricolor"
                    || Id == "longhair-tricolor" || Id == "longhair-black-white"
                    ? Color.FromRgb(48, 39, 34) : Ink;
            }
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
            if (Id == "tricolor" || Id == "longhair-tricolor")
            {
                if (leftFace || leftSide) return Charcoal;
                if (rightFace || rightSide) return Gold;
            }
            if ((Id == "black-white" || Id == "longhair-black-white") && (leftFace || rightSide)) return Charcoal;
            if ((Id == "golden-white" || Id == "longhair-template") && (leftFace || rightFace || leftSide)) return PaleGold;
            return Fur;
        }

        private Color PointAt(int x)
        {
            if (Id == "tricolor" || Id == "longhair-tricolor")
                return x < 6 ? Charcoal : x > 11 ? Gold : Grey;
            if (Id == "black-white" || Id == "longhair-black-white")
                return x > 9 ? Color.FromRgb(208, 186, 167) : Charcoal;
            return points;
        }

        public BitmapSource LoadBody(BitmapSource fallback)
        {
            return BodyResource != null ? PixelPetScene.LoadSprite(BodyResource) : Recolor(fallback, false, null, null);
        }

        public BitmapSource LoadPaw(BitmapSource fallback)
        {
            return PawResource != null ? PixelPetScene.LoadSprite(PawResource) : Recolor(fallback, true, null, null);
        }

        public BitmapSource RecolorCustom(BitmapSource source, bool paw, Color? customFur, Color? customInk)
        {
            return Recolor(source, paw, customFur, customInk);
        }

        private BitmapSource Recolor(BitmapSource source, bool paw, Color? customFur, Color? customInk)
        {
            int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
            byte[] pixels = new byte[stride * height];
            new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
            Color targetFur = customFur ?? Fur;
            Color targetInk = customInk ?? Ink;
            Color mid = Blend(targetFur, targetInk, 0.35);
            Color soft = Blend(targetFur, targetInk, 0.16);
            Color dark = Blend(targetFur, targetInk, 0.62);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * stride + x * 4;
                    if (pixels[index + 3] == 0) continue;
                    Color current = Color.FromRgb(pixels[index + 2], pixels[index + 1], pixels[index]);
                    Color next = current;
                    if (Same(current, Paper) || (customFur.HasValue && !IsOutline(current)))
                        next = customFur.HasValue ? targetFur : FurAt(x, y);
                    else if (Same(current, Grey)) next = customFur.HasValue ? Blend(targetFur, targetInk, 0.28) : (paw ? PointAt(x) : PointAt(x));
                    else if (Same(current, OriginalInk) || Same(current, Color.FromRgb(74, 49, 39))) next = targetInk;
                    else if (Same(current, Color.FromRgb(181, 151, 121))) next = mid;
                    else if (Same(current, Color.FromRgb(210, 193, 169))) next = soft;
                    else if (Same(current, Color.FromRgb(124, 97, 77))) next = dark;
                    pixels[index] = next.B;
                    pixels[index + 1] = next.G;
                    pixels[index + 2] = next.R;
                }
            var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }

        private static bool IsOutline(Color c)
        {
            return (c.R < 110 && c.G < 85 && c.B < 70);
        }

        private static bool Same(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        private static Color Blend(Color a, Color b, double amount)
        {
            return Color.FromRgb(
                (byte)Math.Round(a.R + (b.R - a.R) * amount),
                (byte)Math.Round(a.G + (b.G - a.G) * amount),
                (byte)Math.Round(a.B + (b.B - a.B) * amount));
        }
    }
}

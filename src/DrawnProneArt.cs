using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class DrawnProneArt
    {
        private static readonly Dictionary<string, DrawnProneArt> Cache = new Dictionary<string, DrawnProneArt>();
        private static readonly int[] MirroredGaze = { 0, 2, 1, 3, 4, 6, 5, 8, 7 };
        private readonly BitmapSource[] eyes;
        private readonly Rect eyePlacement;
        private readonly Geometry silhouette;
        private readonly Geometry jointSilhouette;
        private readonly BitmapSource rearFeet;
        private readonly BitmapSource[] footSprites;
        private readonly Rect[] footBounds;
        private readonly Point[] footTips;
        private readonly Color rearFarFur, nearFootFur;
        private Rect clipBounds = Rect.Empty;
        private Geometry outsideBody;
        private byte[] skinPixels;
        private int[] lowerBody;
        private int firstBodyColumn, lastBodyColumn;
        private readonly Dictionary<BitmapSource, ImageSource> textures = new Dictionary<BitmapSource, ImageSource>();
        public readonly BitmapSource Body, Paw;
        public readonly BitmapSource[] BlinkEyes, SleepEyes;
        public readonly MouthArt Mouth;
        public readonly Color Fur, Ink;
        public readonly bool Compact;
        public readonly double BodyHeight;

        public static DrawnProneArt For(string coat)
        {
            DrawnProneArt art;
            if (!Cache.TryGetValue(coat, out art)) Cache.Add(coat, art = new DrawnProneArt(coat));
            return art;
        }

        private DrawnProneArt(string coat)
        {
            Compact = true;
            eyePlacement = new Rect(0.67600677, 0.26405, 0.22300079, 0.31329);
            Body = PixelPetScene.LoadSprite("drawn-" + coat + "-body.png");
            BodyHeight = 129.0 * Body.PixelHeight / Body.PixelWidth;
            if (Compact)
            {
                rearFeet = Tint(PixelPetScene.LoadSprite("drawn-tricolor-feet.png"), coat, false);
                footBounds = new[] { new Rect(366, 221, 52, 58), new Rect(414, 221, 38, 58),
                    new Rect(135, 221, 47, 58), new Rect(102, 221, 35, 58) };
                footTips = new[] { new Point(399, 267), new Point(434, 259), new Point(156, 277), new Point(119, 265) };
                footSprites = new BitmapSource[4];
                for (int i = 0; i < footSprites.Length; i++)
                {
                    Rect part = footBounds[i];
                    footSprites[i] = new CroppedBitmap(rearFeet, new Int32Rect((int)part.X - 102, 0, (int)part.Width, 58));
                    footSprites[i].Freeze();
                }
                byte[] color = new byte[4];
                new FormatConvertedBitmap(rearFeet, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(17, 29, 1, 1), color, 4, 0);
                rearFarFur = Color.FromArgb(color[3], color[2], color[1], color[0]);
                new FormatConvertedBitmap(rearFeet, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(297, 34, 1, 1), color, 4, 0);
                nearFootFur = Color.FromArgb(color[3], color[2], color[1], color[0]);
            }
            silhouette = CreateSilhouette(Body);
            if (Compact)
            {
                double inset = 1.4 * Body.PixelWidth / 129;
                Geometry edge = silhouette.GetWidenedPathGeometry(new Pen(Brushes.Black, inset * 2));
                jointSilhouette = Geometry.Combine(silhouette, edge, GeometryCombineMode.Exclude, null);
                jointSilhouette.Freeze();
            }
            string pawAsset = "drawn-paw.png";
            Paw = Tint(PixelPetScene.LoadSprite(pawAsset), coat, true);
            Fur = coat == "cream" ? Color.FromRgb(232, 210, 164)
                : coat == "teddy" ? Color.FromRgb(166, 110, 71)
                : coat == "silver-agouti" ? Color.FromRgb(170, 160, 149)
                : Color.FromRgb(252, 233, 210);
            Ink = Color.FromRgb(62, 36, 13);
            BitmapSource sheet = SolidEyes(PixelPetScene.LoadSprite("drawn-tricolor-eyes.png"));
            int width = sheet.PixelWidth / 4, height = sheet.PixelHeight / 3;
            eyes = new BitmapSource[12];
            for (int i = 0; i < eyes.Length; i++)
            {
                eyes[i] = new CroppedBitmap(sheet, new Int32Rect(i % 4 * width, i / 4 * height, width, height));
                eyes[i].Freeze();
            }
            BlinkEyes = new[] { eyes[0], eyes[9], eyes[11] };
            SleepEyes = IdleSleepAnimation.BlendEyes(eyes[0], eyes[10]);
            Mouth = new MouthArt(Body, false);
        }

        private static BitmapSource SolidEyes(BitmapSource source)
        {
            int stride = source.PixelWidth * 4;
            byte[] pixels = new byte[stride * source.PixelHeight];
            new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0) continue;
                pixels[i] = 23; pixels[i + 1] = 38; pixels[i + 2] = 57;
            }
            BitmapSource image = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            image.Freeze();
            return image;
        }

        public Point RestTip(Rect bounds, bool far)
        {
            return RestTip(bounds, far ? 1 : 0);
        }

        public Point RestTip(Rect bounds, int foot)
        {
            Point tip = footTips[foot];
            return new Point(bounds.Right - bounds.Width * tip.X / 492, bounds.Y + bounds.Height * tip.Y / 286);
        }

        public Color FootColor(int foot)
        {
            Color color = foot == 3 ? rearFarFur : nearFootFur;
            return color.A > 128 ? color : Fur;
        }

        private void CacheSkin()
        {
            if (skinPixels != null) return;
            int width = Body.PixelWidth, height = Body.PixelHeight;
            skinPixels = new byte[width * height * 4];
            new FormatConvertedBitmap(Body, PixelFormats.Bgra32, null, 0).CopyPixels(skinPixels, width * 4, 0);
            lowerBody = new int[width];
            firstBodyColumn = width; lastBodyColumn = -1;
            for (int x = 0; x < width; x++)
            {
                int run = 0, longest = 0, bottom = 0;
                for (int y = 0; y <= height; y++)
                {
                    if (y < height && skinPixels[(y * width + x) * 4 + 3] >= 128) { run++; continue; }
                    if (run > longest) { longest = run; bottom = y; }
                    run = 0;
                }
                lowerBody[x] = longest >= 8 ? bottom : -1;
                if (lowerBody[x] < 0) continue;
                firstBodyColumn = Math.Min(firstBodyColumn, x); lastBodyColumn = x;
            }
        }

        private double LowerEdge(Rect bounds, double x)
        {
            int column = Math.Max(0, Math.Min(Body.PixelWidth - 1,
                (int)Math.Round((bounds.Right - x) / bounds.Width * Body.PixelWidth - 0.5)));
            return lowerBody[column] < 0 ? bounds.Bottom - 2 : bounds.Y + bounds.Height * lowerBody[column] / Body.PixelHeight;
        }

        public Point ArmJoin(Rect bounds, double x, double radius, out Color color)
        {
            CacheSkin();
            double left = bounds.Right - bounds.Width * (lastBodyColumn + 1) / Body.PixelWidth;
            double right = bounds.Right - bounds.Width * firstBodyColumn / Body.PixelWidth;
            x = Math.Max(Math.Max(bounds.X + 17, left + radius + 1), Math.Min(right - radius - 1, x));
            double edge = Math.Min(LowerEdge(bounds, x), Math.Min(LowerEdge(bounds, x - radius), LowerEdge(bounds, x + radius)));
            int sx = (int)Math.Round((bounds.Right - x) / bounds.Width * Body.PixelWidth - 0.5);
            int sy = (int)Math.Round((edge - 6 - bounds.Y) / bounds.Height * Body.PixelHeight);
            int r = 0, g = 0, b = 0, count = 0;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int px = Math.Max(0, Math.Min(Body.PixelWidth - 1, sx + dx));
                int py = Math.Max(0, Math.Min(Body.PixelHeight - 1, sy + dy));
                int i = (py * Body.PixelWidth + px) * 4;
                if (skinPixels[i + 3] < 240 || skinPixels[i] + skinPixels[i + 1] + skinPixels[i + 2] < 165) continue;
                b += skinPixels[i]; g += skinPixels[i + 1]; r += skinPixels[i + 2]; count++;
            }
            color = count == 0 ? Fur : Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
            return new Point(x, edge - 8);
        }

        public void DrawRestFeet(DrawingContext dc, Rect bounds)
        {
            DrawFootImage(dc, bounds, rearFeet, new Rect(102, 221, 350, 58));
        }

        public void DrawRestFoot(DrawingContext dc, Rect bounds, int foot, double opacity)
        {
            if (opacity <= 0) return;
            dc.PushOpacity(opacity);
            DrawFootImage(dc, bounds, footSprites[foot], footBounds[foot]);
            dc.Pop();
        }

        private void DrawFootImage(DrawingContext dc, Rect bounds, BitmapSource image, Rect source)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, bounds.X + bounds.Width / 2, 0));
            dc.DrawImage(Texture(image), new Rect(bounds.X + bounds.Width * source.X / 492,
                bounds.Y + bounds.Height * source.Y / 286, bounds.Width * source.Width / 492, bounds.Height * source.Height / 286));
            dc.Pop();
        }

        private static Geometry CreateSilhouette(BitmapSource body)
        {
            int width = body.PixelWidth, height = body.PixelHeight, stride = width * 4;
            byte[] pixels = new byte[stride * height];
            new FormatConvertedBitmap(body, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
            var left = new List<Point>();
            var right = new List<Point>();
            for (int y = 0; y < height; y++)
            {
                int first = 0, last = width - 1;
                while (first < width && pixels[y * stride + first * 4 + 3] < 128) first++;
                if (first == width) continue;
                while (last > first && pixels[y * stride + last * 4 + 3] < 128) last--;
                left.Add(new Point(first, y)); left.Add(new Point(first, y + 1));
                right.Add(new Point(last + 1, y)); right.Add(new Point(last + 1, y + 1));
            }
            var shape = new StreamGeometry();
            using (StreamGeometryContext path = shape.Open())
                if (left.Count > 0)
                {
                    path.BeginFigure(left[0], true, true);
                    for (int i = 1; i < left.Count; i++) path.LineTo(left[i], true, false);
                    for (int i = right.Count - 1; i >= 0; i--) path.LineTo(right[i], true, false);
                }
            shape.Freeze();
            return shape;
        }

        public Geometry OutsideBody(Rect bounds)
        {
            if (outsideBody != null && clipBounds == bounds) return outsideBody;
            Geometry body = silhouette.Clone();
            body.Transform = new MatrixTransform(-bounds.Width / Body.PixelWidth, 0, 0,
                bounds.Height / Body.PixelHeight, bounds.Right, bounds.Top);
            outsideBody = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(-100, -100, 600, 508)), body);
            outsideBody.Freeze();
            clipBounds = bounds;
            return outsideBody;
        }

        public void DrawJoinedPawOutline(DrawingContext dc, Rect bounds, Geometry paw, Pen outline)
        {
            Geometry body = jointSilhouette.Clone();
            body.Transform = new MatrixTransform(-bounds.Width / Body.PixelWidth, 0, 0,
                bounds.Height / Body.PixelHeight, bounds.Right, bounds.Top);
            // Draw the shared outer contour so the paw sides turn into the belly outline.
            Geometry joined = Geometry.Combine(body, paw, GeometryCombineMode.Union, null);
            dc.PushClip(paw.GetWidenedPathGeometry(new Pen(Brushes.Black, outline.Thickness * 2)));
            dc.DrawGeometry(null, outline, joined);
            dc.Pop();
        }

        private static BitmapSource Tint(BitmapSource source, string coat, bool paw)
        {
            if (coat == "tricolor") return source;
            Color brown = coat == "black-white" ? Color.FromRgb(67, 61, 58)
                : coat == "golden-white" || coat == "longhair" ? Color.FromRgb(231, 195, 131)
                : coat == "cream" ? Color.FromRgb(232, 210, 164)
                : coat == "californian" ? Color.FromRgb(118, 96, 78)
                : coat == "teddy" ? Color.FromRgb(166, 110, 71)
                : coat == "silver-agouti" ? Color.FromRgb(170, 160, 149)
                : Color.FromRgb(231, 195, 131);
            Color white = coat == "cream" ? Color.FromRgb(232, 210, 164)
                : coat == "californian" ? Color.FromRgb(118, 96, 78)
                : coat == "teddy" ? Color.FromRgb(166, 110, 71)
                : coat == "silver-agouti" ? Color.FromRgb(170, 160, 149)
                : Color.FromRgb(253, 235, 212);
            int stride = source.PixelWidth * 4;
            byte[] pixels = new byte[stride * source.PixelHeight];
            new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                double light = r * 0.2126 + g * 0.7152 + b * 0.0722;
                if (pixels[i + 3] == 0 || (r < 90 && g < 65 && b < 45)) continue;
                bool whiteFur = light > 180;
                if (whiteFur && (coat == "black-white" || coat == "golden-white" || coat == "longhair")) continue;
                Color color = whiteFur ? white : brown;
                double amount = Math.Max(0.75, Math.Min(1.12, light / (whiteFur ? 235 : 97)));
                pixels[i] = (byte)Math.Min(255, Math.Round(color.B * amount));
                pixels[i + 1] = (byte)Math.Min(255, Math.Round(color.G * amount));
                pixels[i + 2] = (byte)Math.Min(255, Math.Round(color.R * amount));
            }
            BitmapSource image = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            image.Freeze();
            return image;
        }

        public Vector Offset(Point cursor, Rect bounds)
        {
            Vector delta = cursor - new Point(bounds.X + bounds.Width * (1 - eyePlacement.X - eyePlacement.Width / 2),
                bounds.Y + bounds.Height * (eyePlacement.Y + eyePlacement.Height / 2));
            double divisor = Math.Max(120, delta.Length);
            return new Vector(Math.Max(-2, Math.Min(2, Math.Round(delta.X / divisor * 2))),
                Math.Max(-2, Math.Min(2, Math.Round(delta.Y / divisor * 2))));
        }

        public void Draw(DrawingContext dc, Rect bounds, Vector gaze, int blink, double sleepy, double eyeScale, int mouth)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, bounds.X + bounds.Width / 2, 0));
            dc.DrawImage(Texture(Mouth.Frames[Math.Max(0, Math.Min(3, mouth))]), bounds);
            DrawEyes(dc, bounds, gaze, blink, sleepy, eyeScale);
            dc.Pop();
        }

        public void DrawEyes(DrawingContext dc, Rect bounds, Vector gaze, int blink, double sleepy, double eyeScale)
        {
            int x = gaze.X < -0.3 ? -1 : gaze.X > 0.3 ? 1 : 0;
            int y = gaze.Y < -0.3 ? -1 : gaze.Y > 0.3 ? 1 : 0;
            int direction = y < 0 ? (x < 0 ? 5 : x > 0 ? 6 : 3) : y > 0 ? (x < 0 ? 7 : x > 0 ? 8 : 4) : x < 0 ? 1 : x > 0 ? 2 : 0;
            BitmapSource image = sleepy > 0 ? SleepEyes[IdleSleepAnimation.FrameIndex(sleepy)]
                : blink > 0 ? BlinkEyes[Math.Min(2, blink)] : eyes[MirroredGaze[direction]];
            Rect eye = new Rect(bounds.X + bounds.Width * eyePlacement.X, bounds.Y + bounds.Height * eyePlacement.Y,
                bounds.Width * eyePlacement.Width, bounds.Height * eyePlacement.Height);
            eye.Inflate(0, eye.Height * (eyeScale - 1) / 2);
            dc.DrawImage(Texture(image), eye);
        }

        public ImageSource Texture(BitmapSource image)
        {
            ImageSource texture;
            if (textures.TryGetValue(image, out texture)) return texture;
            var group = new DrawingGroup();
            RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
            group.Children.Add(new ImageDrawing(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)));
            group.Freeze();
            texture = new DrawingImage(group);
            texture.Freeze();
            textures.Add(image, texture);
            return texture;
        }
    }
}

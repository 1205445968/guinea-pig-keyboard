using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class MouthArt
    {
        public readonly BitmapSource[] Frames = new BitmapSource[4];
        public readonly byte[][] Pixels = new byte[4][];
        private readonly int width, height, stride;
        private readonly Rect region;
        private readonly byte[] original;

        public MouthArt(BitmapSource source, bool pixel, BitmapSource openMouth = null, bool prone = false)
        {
            width = source.PixelWidth; height = source.PixelHeight; stride = width * 4;
            region = prone ? new Rect(87, 39, 11, 11) : pixel ? new Rect(width * 0.385, height * 48.0 / 104, width * 0.24, height * 18.0 / 104)
                : new Rect(width * 0.84, height * 0.60, width * 0.145, height * 0.15);
            original = new byte[stride * height];
            new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0).CopyPixels(original, stride, 0);
            if (pixel && openMouth != null)
            {
                CreatePixelMouths(source, openMouth, prone ? 87 : 42, prone ? 39 : 49);
                return;
            }
            Pixels[0] = original; Frames[0] = source;
            for (int frame = 1; frame < Frames.Length; frame++)
            {
                byte[] pixels = (byte[])original.Clone();
                double amount = frame / 3.0 * (pixel ? 0.22 : 0.30);
                int margin = pixel ? 2 : 5;
                for (int y = (int)Math.Ceiling(region.Top); y < region.Bottom && y < height; y++)
                    for (int x = (int)Math.Ceiling(region.Left); x < region.Right && x < width; x++)
                    {
                        if (!Interior(x, y, margin)) continue;
                        double target = y + 0.5, low = region.Top, high = region.Bottom;
                        double dx = (x + 0.5 - (region.Left + region.Width / 2)) / (region.Width / 2);
                        double horizontal = Smooth(1 - dx * dx);
                        for (int step = 0; step < 12; step++)
                        {
                            double sample = (low + high) / 2;
                            double edge = Smooth(Math.Min((sample - region.Top) / (region.Height * 0.15),
                                (region.Bottom - sample) / (region.Height * 0.22)));
                            double mapped = sample - (sample - region.Top) * amount * horizontal * edge;
                            if (mapped < target) low = sample; else high = sample;
                        }
                        int sourceY = Math.Max(0, Math.Min(height - 1, (int)Math.Round((low + high) / 2 - 0.5)));
                        int destination = y * stride + x * 4, from = sourceY * stride + x * 4;
                        int sourceAlpha = original[from + 3], destinationAlpha = original[destination + 3];
                        if (sourceAlpha < 240) continue;
                        for (int channel = 0; channel < 3; channel++)
                            pixels[destination + channel] = (byte)Math.Min(destinationAlpha,
                                (original[from + channel] * destinationAlpha + sourceAlpha / 2) / sourceAlpha);
                    }
                Pixels[frame] = pixels;
                Frames[frame] = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Pbgra32, null, pixels, stride);
                Frames[frame].Freeze();
            }
        }

        private void CreatePixelMouths(BitmapSource source, BitmapSource mouth, int left, int top)
        {
            int patchStride = mouth.PixelWidth * 4;
            byte[] patch = new byte[patchStride * mouth.PixelHeight];
            new FormatConvertedBitmap(mouth, PixelFormats.Pbgra32, null, 0).CopyPixels(patch, patchStride, 0);
            for (int frame = 0; frame < Frames.Length; frame++)
            {
                byte[] pixels = (byte[])original.Clone();
                if (frame < 3)
                {
                    int rows = Math.Max(1, mouth.PixelHeight * (3 - frame) / 3);
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < mouth.PixelWidth; x++)
                        {
                            int targetX = left + x, targetY = top + y;
                            if (!Interior(targetX, targetY, 2)) continue;
                            int sampleY = Math.Min(mouth.PixelHeight - 1, (2 * y + 1) * mouth.PixelHeight / (2 * rows));
                            int from = sampleY * patchStride + x * 4;
                            int alpha = patch[from + 3];
                            if (alpha == 0) continue;
                            int destination = targetY * stride + targetX * 4;
                            int bodyAlpha = original[destination + 3];
                            for (int channel = 0; channel < 3; channel++)
                                pixels[destination + channel] = (byte)Math.Min(bodyAlpha,
                                    (patch[from + channel] * bodyAlpha + pixels[destination + channel] * (255 - alpha) + 127) / 255);
                        }
                }
                Pixels[frame] = pixels;
                Frames[frame] = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Pbgra32, null, pixels, stride);
                Frames[frame].Freeze();
            }
        }

        public void Draw(DrawingContext dc, Rect bounds, int frame)
        {
            dc.DrawImage(Frames[Math.Max(0, Math.Min(Frames.Length - 1, frame))], bounds);
        }

        private bool Interior(int x, int y, int margin)
        {
            return x >= margin && y >= margin && x + margin < width && y + margin < height
                && original[y * stride + x * 4 + 3] >= 240
                && original[y * stride + (x - margin) * 4 + 3] >= 240
                && original[y * stride + (x + margin) * 4 + 3] >= 240
                && original[(y - margin) * stride + x * 4 + 3] >= 240
                && original[(y + margin) * stride + x * 4 + 3] >= 240;
        }

        private static double Smooth(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value * value * (3 - 2 * value);
        }
    }
}

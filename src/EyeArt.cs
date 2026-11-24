using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class EyeArt
    {
        private const int LeftX = 30;
        private const int RightX = 65;
        private const int EyeY = 30;
        private const int EyeSize = 6;
        private const int Padding = 8;
        private const int BodyLift = 4;
        private const double FacePull = 2.5;
        private const double TurnRadians = Math.PI / 120;
        private const double HeadTiltRadians = Math.PI / 100;
        private const byte BackgroundR = 255;
        private const byte BackgroundG = 250;
        private const byte BackgroundB = 241;
        private readonly BitmapSource[] _eyes;
        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private readonly byte[] _bodyPixels;
        private readonly double[] _faceWeights;
        private BitmapSource _headFrame;
        private Vector _headDirection;

        public BitmapSource Body { get; private set; }

        public EyeArt(BitmapSource source) : this(source, true, Color.FromRgb(98, 71, 55)) { }

        public EyeArt(BitmapSource source, bool clearEyes, Color eyeColor)
        {
            if (source == null) throw new ArgumentNullException("source");
            _sourceWidth = source.PixelWidth;
            _sourceHeight = source.PixelHeight;
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = _sourceWidth * 4;
            byte[] pixels = new byte[stride * _sourceHeight];
            converted.CopyPixels(pixels, stride, 0);
            byte[] body = (byte[])pixels.Clone();
            _eyes = new[] {
                CreateEye(new[] { ".####.", "######", "######", "######", "######", ".####." }, eyeColor),
                CreateEye(new[] { "......", "......", ".####.", "######", ".####.", "......" }, eyeColor),
                CreateEye(new[] { "......", "......", "#....#", ".####.", "......", "......" }, eyeColor)
            };
            if (clearEyes)
            {
                ClearEye(body, stride, LeftX, EyeY);
                ClearEye(body, stride, RightX, EyeY);
            }
            Body = BitmapSource.Create(_sourceWidth, _sourceHeight, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, body, stride);
            Body.Freeze();
            _bodyPixels = new byte[body.Length];
            new FormatConvertedBitmap(Body, PixelFormats.Pbgra32, null, 0).CopyPixels(_bodyPixels, stride, 0);
            _faceWeights = CreateFaceWeights();
        }

        public Vector Offset(Point cursor, Rect bodyBounds)
        {
            Point center = new Point(bodyBounds.X + ((LeftX + RightX + EyeSize) / 2.0) * bodyBounds.Width / _sourceWidth,
                bodyBounds.Y + (EyeY + EyeSize / 2.0 - BodyLift) * bodyBounds.Height / _sourceHeight);
            Vector delta = cursor - center;
            double divisor = Math.Max(120, delta.Length);
            return new Vector(Math.Max(-2, Math.Min(2, Math.Round(delta.X / divisor * 2))),
                Math.Max(-2, Math.Min(2, Math.Round(delta.Y / divisor * 2))));
        }

        public void Draw(DrawingContext dc, Rect bodyBounds, Vector head)
        {
            Draw(dc, bodyBounds, head, 0);
        }

        public void Draw(DrawingContext dc, Rect bodyBounds, Vector head, int blinkFrame)
        {
            if (_headFrame == null || head != _headDirection)
            {
                _headFrame = CreateHeadFrame(head);
                _headDirection = head;
            }
            double sx = bodyBounds.Width / _sourceWidth;
            double sy = bodyBounds.Height / _sourceHeight;
            dc.DrawImage(_headFrame, new Rect(bodyBounds.X - Padding * sx, bodyBounds.Y - (Padding + BodyLift) * sy,
                (_sourceWidth + Padding * 2) * sx, (_sourceHeight + Padding * 2 + BodyLift) * sy));
            DrawEyes(dc, bodyBounds, head, blinkFrame);
        }

        public void DrawEyes(DrawingContext dc, Rect bounds, Vector head)
        {
            DrawEyes(dc, bounds, head, 0);
        }

        public void DrawEyes(DrawingContext dc, Rect bounds, Vector head, int blinkFrame)
        {
            BitmapSource eye = _eyes[Math.Max(0, Math.Min(_eyes.Length - 1, blinkFrame))];
            dc.DrawImage(eye, EyeBounds(LeftX, bounds, head));
            dc.DrawImage(eye, EyeBounds(RightX, bounds, head));
        }

        private Rect EyeBounds(int x, Rect bounds, Vector head)
        {
            double angle = head.X * TurnRadians;
            Point center = DeformFace(new Point(x + EyeSize / 2.0, EyeY + EyeSize / 2.0),
                head, Math.Cos(angle), Math.Sin(angle));
            double tilt = head.X * HeadTiltRadians;
            center = TiltHead(center, head, Math.Cos(tilt), Math.Sin(tilt));
            double sx = bounds.Width / _sourceWidth;
            double sy = bounds.Height / _sourceHeight;
            return new Rect(bounds.X + Math.Round(center.X - EyeSize / 2.0) * sx,
                bounds.Y + (Math.Round(center.Y - EyeSize / 2.0) - BodyLift) * sy, EyeSize * sx, EyeSize * sy);
        }

        private static double SmoothWeight(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        private double[] CreateFaceWeights()
        {
            int[] distance = new int[_sourceWidth * _sourceHeight];
            for (int y = 0; y < _sourceHeight; y++)
                for (int x = 0; x < _sourceWidth; x++)
                {
                    int i = y * _sourceWidth + x;
                    distance[i] = _bodyPixels[i * 4 + 3] == 0 ? 0 : 20;
                    if (x == 0 || y == 0 || x == _sourceWidth - 1 || y == _sourceHeight - 1)
                        distance[i] = Math.Min(distance[i], 1);
                    if (x > 0) distance[i] = Math.Min(distance[i], distance[i - 1] + 1);
                    if (y > 0) distance[i] = Math.Min(distance[i], distance[i - _sourceWidth] + 1);
                }
            for (int y = _sourceHeight - 1; y >= 0; y--)
                for (int x = _sourceWidth - 1; x >= 0; x--)
                {
                    int i = y * _sourceWidth + x;
                    if (x + 1 < _sourceWidth) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
                    if (y + 1 < _sourceHeight) distance[i] = Math.Min(distance[i], distance[i + _sourceWidth] + 1);
                }
            double[] weights = new double[distance.Length];
            for (int y = 0; y < _sourceHeight; y++)
                for (int x = 0; x < _sourceWidth; x++)
                {
                    double dx = (x + 0.5 - 50) / 35;
                    double dy = (y + 0.5 - 40) / 42;
                    double radius = Math.Sqrt(dx * dx + dy * dy);
                    int i = y * _sourceWidth + x;
                    // Protect the original silhouette and ease the turn into the face interior.
                    weights[i] = SmoothWeight((distance[i] - 4) / 14.0) * SmoothWeight((1 - radius) / 0.45);
                }
            return weights;
        }

        private double FaceWeight(double x, double y)
        {
            double px = x - 0.5, py = y - 0.5;
            int left = (int)Math.Floor(px), top = (int)Math.Floor(py);
            if (left < 0 || top < 0 || left + 1 >= _sourceWidth || top + 1 >= _sourceHeight) return 0;
            double tx = px - left, ty = py - top;
            int i = top * _sourceWidth + left;
            double upper = _faceWeights[i] * (1 - tx) + _faceWeights[i + 1] * tx;
            double lower = _faceWeights[i + _sourceWidth] * (1 - tx) + _faceWeights[i + _sourceWidth + 1] * tx;
            return upper * (1 - ty) + lower * ty;
        }

        private Point DeformFace(Point point, Vector direction, double cosine, double sine)
        {
            double weight = FaceWeight(point.X, point.Y);
            double x = point.X - 50, y = point.Y - 40;
            double dx = x * cosine - y * sine - x + direction.X * FacePull;
            double dy = x * sine + y * cosine - y + direction.Y * FacePull;
            return point + new Vector(dx, dy) * weight;
        }

        private static Point TiltHead(Point point, Vector direction, double cosine, double sine)
        {
            double weight = SmoothWeight((88 - point.Y) / 32);
            double x = point.X - 50, y = point.Y - 76;
            double dx = x * cosine - y * sine - x;
            double dy = x * sine + y * cosine - y + direction.Y;
            return point + new Vector(dx, dy) * weight;
        }

        private BitmapSource CreateHeadFrame(Vector direction)
        {
            int width = _sourceWidth + Padding * 2;
            int height = _sourceHeight + Padding * 2;
            byte[] face = CreateFacePixels(direction);
            byte[] pixels = new byte[face.Length];
            double angle = direction.X * HeadTiltRadians;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            for (int row = 0; row < height; row++)
            {
                double y = row - Padding + 0.5;
                if (y >= 88)
                {
                    Buffer.BlockCopy(face, row * width * 4, pixels, row * width * 4, width * 4);
                    continue;
                }
                for (int col = 0; col < width; col++)
                {
                    Point target = new Point(col - Padding + 0.5, y);
                    Point sample = target;
                    for (int step = 0; step < 8; step++)
                    {
                        Vector error = target - TiltHead(sample, direction, cosine, sine);
                        sample += error;
                        if (error.LengthSquared < 0.0001) break;
                    }
                    int sourceX = (int)Math.Round(sample.X + Padding - 0.5);
                    int sourceY = (int)Math.Round(sample.Y + Padding - 0.5);
                    if (sourceX < 0 || sourceX >= width || sourceY < 0 || sourceY >= height) continue;
                    int source = (sourceY * width + sourceX) * 4;
                    int destination = (row * width + col) * 4;
                    Buffer.BlockCopy(face, source, pixels, destination, 4);
                }
            }
            pixels = ExtendTorso(pixels, width, height);
            height += BodyLift;
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            image.Freeze();
            return image;
        }

        private byte[] ExtendTorso(byte[] pixels, int width, int height)
        {
            int extendedHeight = height + BodyLift;
            byte[] extended = new byte[width * extendedHeight * 4];
            for (int row = 0; row < extendedHeight; row++)
            {
                double originalY = row;
                for (int step = 0; step < 8; step++)
                    originalY = row - BodyLift * SmoothWeight((originalY - (Padding + 80)) / (_sourceHeight - 80.0));
                int sourceY = Math.Max(0, Math.Min(height - 1, (int)Math.Round(originalY)));
                Buffer.BlockCopy(pixels, sourceY * width * 4, extended, row * width * 4, width * 4);
            }
            return extended;
        }

        private byte[] CreateFacePixels(Vector direction)
        {
            int width = _sourceWidth + Padding * 2;
            int height = _sourceHeight + Padding * 2;
            byte[] pixels = new byte[width * height * 4];
            double angle = direction.X * TurnRadians;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            for (int row = 0; row < height; row++)
            {
                double y = row - Padding + 0.5;
                int originalY = row - Padding;
                if (originalY < 0 || originalY >= _sourceHeight) continue;
                for (int col = 0; col < width; col++)
                {
                    double x = col - Padding + 0.5;
                    int originalX = col - Padding;
                    if (originalX < 0 || originalX >= _sourceWidth) continue;
                    int original = originalY * _sourceWidth + originalX;
                    int sourceX = originalX, sourceY = originalY;
                    if (_faceWeights[original] > 0)
                    {
                        // Invert the shared facial turn, leaving the protected outline untouched.
                        Point sample = new Point(x, y);
                        for (int step = 0; step < 16; step++)
                        {
                            Point mapped = DeformFace(sample, direction, cosine, sine);
                            Vector error = new Point(x, y) - mapped;
                            sample += error;
                            if (error.LengthSquared < 0.0001) break;
                        }
                        sourceX = (int)Math.Round(sample.X - 0.5);
                        sourceY = (int)Math.Round(sample.Y - 0.5);
                    }
                    if (sourceX < 0 || sourceX >= _sourceWidth || sourceY < 0 || sourceY >= _sourceHeight) continue;
                    int source = (sourceY * _sourceWidth + sourceX) * 4;
                    int target = (row * width + col) * 4;
                    Buffer.BlockCopy(_bodyPixels, source, pixels, target, 4);
                }
            }
            return pixels;
        }

        private static BitmapSource CreateEye(string[] pattern, Color color)
        {
            int eyeStride = EyeSize * 4;
            byte[] eye = new byte[eyeStride * EyeSize];
            for (int row = 0; row < EyeSize; row++)
                for (int col = 0; col < EyeSize; col++)
                {
                    int dst = row * eyeStride + col * 4;
                    if (pattern[row][col] == '#')
                    {
                        eye[dst] = color.B;
                        eye[dst + 1] = color.G;
                        eye[dst + 2] = color.R;
                        eye[dst + 3] = 255;
                    }
                }
            BitmapSource result = BitmapSource.Create(EyeSize, EyeSize, 96, 96, PixelFormats.Bgra32, null, eye, eyeStride);
            result.Freeze();
            return result;
        }

        private static void ClearEye(byte[] pixels, int stride, int x, int y)
        {
            for (int row = 0; row < EyeSize; row++)
                for (int col = 0; col < EyeSize; col++)
                {
                    int i = (y + row) * stride + (x + col) * 4;
                    pixels[i] = BackgroundB;
                    pixels[i + 1] = BackgroundG;
                    pixels[i + 2] = BackgroundR;
                    pixels[i + 3] = 255;
                }
        }
    }
}

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
        private static readonly int[] FrontEyes = { LeftX, RightX };
        private static readonly int[] ProfileEye = { 73 };
        private readonly int[] _eyePositions;
        private readonly double _eyeHeightRatio;
        private readonly double _faceCenterX;
        private const int Padding = 8;
        private const int BodyLift = 4;
        private const double FacePull = 2.5;
        private const double TurnRadians = Math.PI / 120;
        private const double HeadTiltRadians = Math.PI / 100;
        private const byte BackgroundR = 255;
        private const byte BackgroundG = 250;
        private const byte BackgroundB = 241;
        private readonly BitmapSource[] _eyes;
        private readonly BitmapSource[] _sleepEyes;
        private readonly DrawnProneArt _drawn;
        private static readonly BitmapSource OpenMouth = PixelPetScene.Sprite("mouth-smile.png");
        private static readonly BitmapSource PeepMouth = PixelPetScene.Sprite("mouth-peep.png");
        private static readonly BitmapSource YawnMouth = PixelPetScene.Sprite("mouth-yawn.png");
        private readonly int _sourceWidth;
        private readonly int _sourceHeight;
        private readonly byte[] _bodyPixels;
        private readonly MouthArt _mouth;
        private readonly byte[][] _dozeMouthPixels;
        private readonly double[] _faceWeights;
        private readonly BitmapSource[] _headFrames = new BitmapSource[4];
        private readonly BitmapSource[] _dozeHeadFrames = new BitmapSource[3];
        private Vector _headDirection;
        private Vector _dozeDirection;

        public BitmapSource Body { get; private set; }

        public EyeArt(DrawnProneArt drawn)
        {
            _drawn = drawn;
            Body = drawn.Body;
            _sourceWidth = Body.PixelWidth;
            _sourceHeight = Body.PixelHeight;
            _mouth = drawn.Mouth;
            _eyes = drawn.BlinkEyes;
            _sleepEyes = drawn.SleepEyes;
        }

        public EyeArt(BitmapSource source) : this(source, true, Color.FromRgb(98, 71, 55), null) { }

        public EyeArt(BitmapSource source, bool clearEyes, Color eyeColor) : this(source, clearEyes, eyeColor, null) { }

        public EyeArt(BitmapSource source, bool clearEyes, Color eyeColor, BitmapSource customMouth, bool prone = false)
        {
            if (source == null) throw new ArgumentNullException("source");
            _sourceWidth = source.PixelWidth;
            _sourceHeight = source.PixelHeight;
            _eyePositions = prone ? ProfileEye : FrontEyes;
            _eyeHeightRatio = prone ? 19.0 / 58 : EyeY / 104.0;
            _faceCenterX = _sourceWidth * (prone ? 0.77 : 0.50);
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = _sourceWidth * 4;
            byte[] pixels = new byte[stride * _sourceHeight];
            converted.CopyPixels(pixels, stride, 0);
            byte[] body = (byte[])pixels.Clone();
            _eyes = new[] {
                CreateEye(new[] { ".####.", "######", "######", "######", "######", ".####." }, eyeColor),
                CreateEye(new[] { "......", "......", ".####.", "######", ".####.", "......" }, eyeColor),
                CreateEye(new[] { "......", "......", "#....#", "##..##", ".####.", "......" }, eyeColor)
            };
            _sleepEyes = IdleSleepAnimation.BlendEyes(_eyes[0], _eyes[1]);
            if (clearEyes && _sourceWidth >= RightX + EyeSize && _sourceHeight >= EyeY + EyeSize)
            {
                ClearEye(body, stride, LeftX, EyeY);
                ClearEye(body, stride, RightX, EyeY);
            }
            Body = BitmapSource.Create(_sourceWidth, _sourceHeight, source.DpiX, source.DpiY,
                PixelFormats.Bgra32, null, body, stride);
            Body.Freeze();
            _bodyPixels = new byte[body.Length];
            new FormatConvertedBitmap(Body, PixelFormats.Pbgra32, null, 0).CopyPixels(_bodyPixels, stride, 0);
            _mouth = new MouthArt(Body, true, customMouth ?? (prone ? PeepMouth : OpenMouth), prone);
            _dozeMouthPixels = new[] { _mouth.Pixels[3], _bodyPixels, _bodyPixels };
            if (_sourceWidth == 100 && _sourceHeight == (prone ? 58 : 104))
            {
                _dozeMouthPixels[1] = PatchDozeMouth(_bodyPixels, _sourceWidth, _sourceHeight, PeepMouth, prone ? 87 : 45, prone ? 39 : 51);
                _dozeMouthPixels[2] = PatchDozeMouth(_bodyPixels, _sourceWidth, _sourceHeight, YawnMouth, prone ? 86 : 45, prone ? 37 : 48);
            }
            _faceWeights = CreateFaceWeights();
        }

        private static byte[] PatchDozeMouth(byte[] basePixels, int width, int height, BitmapSource patchSprite, int left, int top)
        {
            byte[] result = (byte[])basePixels.Clone();
            int stride = width * 4;
            int pw = patchSprite.PixelWidth, ph = patchSprite.PixelHeight, pStride = pw * 4;
            byte[] patch = new byte[pStride * ph];
            new FormatConvertedBitmap(patchSprite, PixelFormats.Pbgra32, null, 0).CopyPixels(patch, pStride, 0);
            for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    int tx = left + x, ty = top + y;
                    if (tx < 2 || ty < 2 || tx + 2 >= width || ty + 2 >= height) continue;
                    int dst = ty * stride + tx * 4;
                    if (basePixels[dst + 3] < 240) continue;
                    int src = y * pStride + x * 4;
                    int alpha = patch[src + 3];
                    if (alpha == 0) continue;
                    int bodyAlpha = basePixels[dst + 3];
                    for (int c = 0; c < 3; c++)
                        result[dst + c] = (byte)Math.Min(bodyAlpha,
                            (patch[src + c] * bodyAlpha + result[dst + c] * (255 - alpha) + 127) / 255);
                }
            return result;
        }

        public Vector Offset(Point cursor, Rect bodyBounds)
        {
            if (_drawn != null) return _drawn.Offset(cursor, bodyBounds);
            double midX = (_eyePositions[0] + _eyePositions[_eyePositions.Length - 1] + EyeSize) / 2.0;
            Point center = new Point(bodyBounds.X + midX * bodyBounds.Width / 100,
                bodyBounds.Y + (_eyeHeightRatio + (EyeSize / 2.0 - BodyLift) / _sourceHeight) * bodyBounds.Height);
            Vector delta = cursor - center;
            double divisor = Math.Max(120, delta.Length);
            return new Vector(Math.Max(-2, Math.Min(2, Math.Round(delta.X / divisor * 2))),
                Math.Max(-2, Math.Min(2, Math.Round(delta.Y / divisor * 2))));
        }

        public void Draw(DrawingContext dc, Rect bodyBounds, Vector head)
        {
            Draw(dc, bodyBounds, head, 0, 0);
        }

        public void Draw(DrawingContext dc, Rect bodyBounds, Vector head, int blinkFrame, int mouth)
        {
            Draw(dc, bodyBounds, head, blinkFrame, 0, 1, mouth, 0);
        }

        public void Draw(DrawingContext dc, Rect bodyBounds, Vector head, int blinkFrame, double drowsiness, double eyeScale, int mouth)
        {
            Draw(dc, bodyBounds, head, blinkFrame, drowsiness, eyeScale, mouth, 0);
        }

        public void Draw(DrawingContext dc, Rect bodyBounds, Vector head, int blinkFrame, double drowsiness, double eyeScale, int mouth, int dozeMouth)
        {
            if (_drawn != null)
            {
                _drawn.Draw(dc, bodyBounds, head, blinkFrame, drowsiness, eyeScale, mouth);
                return;
            }
            BitmapSource headImage;
            if (dozeMouth > 0 && dozeMouth <= 2)
            {
                if (head != _dozeDirection)
                {
                    Array.Clear(_dozeHeadFrames, 0, _dozeHeadFrames.Length);
                    _dozeDirection = head;
                }
                if (_dozeHeadFrames[dozeMouth] == null)
                    _dozeHeadFrames[dozeMouth] = CreateHeadFrame(head, _dozeMouthPixels[dozeMouth]);
                headImage = _dozeHeadFrames[dozeMouth];
            }
            else
            {
                mouth = Math.Max(0, Math.Min(3, mouth));
                if (head != _headDirection)
                {
                    Array.Clear(_headFrames, 0, _headFrames.Length);
                    _headDirection = head;
                }
                if (_headFrames[mouth] == null) _headFrames[mouth] = CreateHeadFrame(head, _mouth.Pixels[mouth]);
                headImage = _headFrames[mouth];
            }
            double sx = bodyBounds.Width / _sourceWidth;
            double sy = bodyBounds.Height / _sourceHeight;
            dc.DrawImage(headImage, new Rect(bodyBounds.X - Padding * sx, bodyBounds.Y - (Padding + BodyLift) * sy,
                (_sourceWidth + Padding * 2) * sx, (_sourceHeight + Padding * 2 + BodyLift) * sy));
            DrawEyes(dc, bodyBounds, head, blinkFrame, drowsiness, eyeScale);
        }

        public void DrawEyes(DrawingContext dc, Rect bounds, Vector head)
        {
            DrawEyes(dc, bounds, head, 0);
        }

        public void DrawEyes(DrawingContext dc, Rect bounds, Vector head, int blinkFrame)
        {
            DrawEyes(dc, bounds, head, blinkFrame, 0, 1);
        }

        private void DrawEyes(DrawingContext dc, Rect bounds, Vector head, int blinkFrame, double drowsiness, double eyeScale)
        {
            if (_drawn != null) { _drawn.DrawEyes(dc, bounds, head, blinkFrame, drowsiness, eyeScale); return; }
            BitmapSource eye = drowsiness > 0 ? _sleepEyes[IdleSleepAnimation.FrameIndex(drowsiness)]
                : _eyes[Math.Max(0, Math.Min(_eyes.Length - 1, blinkFrame))];
            foreach (int x in _eyePositions) dc.DrawImage(eye, EyeBounds(x, bounds, head, eyeScale));
        }

        private Rect EyeBounds(int x, Rect bounds, Vector head, double eyeScale)
        {
            double px = x * _sourceWidth / 100.0;
            double ey = _eyeHeightRatio * _sourceHeight;
            double es = EyeSize * _sourceWidth / 100.0;
            double angle = head.X * TurnRadians;
            Point center = DeformFace(new Point(px + es / 2.0, ey + es / 2.0),
                head, Math.Cos(angle), Math.Sin(angle));
            double tilt = head.X * HeadTiltRadians;
            center = TiltHead(center, head, Math.Cos(tilt), Math.Sin(tilt));
            double sx = bounds.Width / _sourceWidth;
            double sy = bounds.Height / _sourceHeight;
            Rect eye = new Rect(bounds.X + Math.Round(center.X - es / 2.0) * sx,
                bounds.Y + (Math.Round(center.Y - es / 2.0) - BodyLift) * sy, es * sx, es * sy);
            eye.Inflate(eye.Width * (eyeScale - 1) / 2, eye.Height * (eyeScale - 1) / 2);
            return eye;
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
            double cx = _faceCenterX, cy = _sourceHeight * (40.0 / 104.0);
            double rx = _sourceWidth * 0.35, ry = _sourceHeight * (42.0 / 104.0);
            for (int y = 0; y < _sourceHeight; y++)
                for (int x = 0; x < _sourceWidth; x++)
                {
                    double dx = (x + 0.5 - cx) / rx;
                    double dy = (y + 0.5 - cy) / ry;
                    double radius = Math.Sqrt(dx * dx + dy * dy);
                    int i = y * _sourceWidth + x;
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
            double cx = _faceCenterX, cy = _sourceHeight * (40.0 / 104.0);
            double x = point.X - cx, y = point.Y - cy;
            double dx = x * cosine - y * sine - x + direction.X * FacePull;
            double dy = x * sine + y * cosine - y + direction.Y * FacePull;
            return point + new Vector(dx, dy) * weight;
        }

        private Point TiltHead(Point point, Vector direction, double cosine, double sine)
        {
            double neckY = _sourceHeight * (88.0 / 104.0);
            double pivotY = _sourceHeight * (76.0 / 104.0);
            double cx = _sourceWidth * 0.50;
            double weight = SmoothWeight((neckY - point.Y) / 32.0);
            double x = point.X - cx, y = point.Y - pivotY;
            double dx = x * cosine - y * sine - x;
            double dy = x * sine + y * cosine - y + direction.Y;
            return point + new Vector(dx, dy) * weight;
        }

        private BitmapSource CreateHeadFrame(Vector direction, byte[] body)
        {
            int width = _sourceWidth + Padding * 2;
            int height = _sourceHeight + Padding * 2;
            byte[] face = CreateFacePixels(direction, body);
            byte[] pixels = new byte[face.Length];
            double angle = direction.X * HeadTiltRadians;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            double neckY = _sourceHeight * (88.0 / 104.0);
            for (int row = 0; row < height; row++)
            {
                double y = row - Padding + 0.5;
                if (y >= neckY)
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
                    originalY = row - BodyLift * SmoothWeight((originalY - (Padding + 80)) / Math.Max(1.0, _sourceHeight - 80.0));
                int sourceY = Math.Max(0, Math.Min(height - 1, (int)Math.Round(originalY)));
                Buffer.BlockCopy(pixels, sourceY * width * 4, extended, row * width * 4, width * 4);
            }
            return extended;
        }

        private byte[] CreateFacePixels(Vector direction, byte[] body)
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
                    Buffer.BlockCopy(body, source, pixels, target, 4);
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
                    char pixel = pattern[row][col];
                    if (pixel != '.')
                    {
                        Color shade = pixel == '*' ? Color.FromRgb(255, 240, 213) : color;
                        eye[dst] = shade.B;
                        eye[dst + 1] = shade.G;
                        eye[dst + 2] = shade.R;
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

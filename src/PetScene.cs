using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WheekKeys
{
    internal sealed class PetScene : FrameworkElement
    {
        private const double SettleTime = 285;
        private const double BubbleTime = 395;
        private static readonly BitmapImage Body = Sprite("guinea-pig.png");
        private static readonly ImageSource Paw = CreatePaw();
        private static readonly BitmapImage MouseSprite = Sprite("mouse.png");
        private static readonly SolidColorBrush Paper = Paint(255, 243, 216);
        private static readonly SolidColorBrush Umber = Paint(111, 73, 47);
        private static readonly SolidColorBrush Amber = Paint(218, 149, 84);
        private static readonly Pen InkLine = new Pen(Umber, 1.55);
        private static readonly Geometry MouseCable = Geometry.Parse("M 14,241 C 15,226 3,225 6,217 C 11,203 28,221 44,210");
        private static readonly Dictionary<string, FormattedText> Labels = new Dictionary<string, FormattedText>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer animation;
        private readonly Stroke left = new Stroke(new Point(143, 214));
        private readonly Stroke right = new Stroke(new Point(253, 214));
        private bool showKeyLabels = true;

        public PetScene()
        {
            Width = 400;
            Height = 308;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            animation = new DispatcherTimer(DispatcherPriority.Render);
            animation.Interval = TimeSpan.FromMilliseconds(24);
            animation.Tick += delegate
            {
                double now = clock.Elapsed.TotalMilliseconds;
                left.Expire(now);
                right.Expire(now);
                if (!left.Active && !right.Active) animation.Stop();
                InvalidateVisual();
            };
            Unloaded += delegate { animation.Stop(); };
        }

        public bool ShowKeyLabels
        {
            get { return showKeyLabels; }
            set { showKeyLabels = value; InvalidateVisual(); }
        }

        public void PressKey(int scan)
        {
            bool onRight = KeyboardArt.IsRight(scan);
            Stroke hand = onRight ? right : left;
            hand.Start(clock.Elapsed.TotalMilliseconds, KeyboardArt.Target(scan, onRight), scan, false, false);
            Wake();
        }

        public void ClickMouse(bool rightButton)
        {
            left.Start(clock.Elapsed.TotalMilliseconds, new Point(rightButton ? 22 : 10, 249), 0, true, rightButton);
            Wake();
        }

        private void Wake()
        {
            if (!animation.IsEnabled) animation.Start();
            InvalidateVisual();
        }

        private sealed class Stroke
        {
            public readonly Point Rest;
            public Point Target, StartPoint;
            public double Started;
            public int Scan;
            public bool Active, Mouse, RightButton;

            public Stroke(Point rest) { Rest = Target = StartPoint = rest; }

            public void Start(double now, Point target, int scan, bool mouse, bool rightButton)
            {
                StartPoint = Position(now);
                Started = now;
                Target = target;
                Scan = scan;
                Mouse = mouse;
                RightButton = rightButton;
                Active = true;
            }

            public Point Position(double now)
            {
                if (!Active) return Rest;
                double age = Math.Max(0, now - Started);
                if (age < 52)
                {
                    double t = age / 52;
                    Point p = StartPoint + (Target - StartPoint) * Smooth(t);
                    p.Y -= 9 * Math.Sin(t * Math.PI);
                    return p;
                }
                if (age < 102) return Target;
                if (age >= SettleTime) return Rest;
                double back = (age - 102) / (SettleTime - 102);
                Point result = Target + (Rest - Target) * Smooth(back);
                result.Y -= Math.Sin(back * Math.PI) * 5;
                return result;
            }

            public double Contact(double now)
            {
                if (!Active) return 0;
                double age = now - Started;
                if (age < 35 || age > 155) return 0;
                return Math.Min(1, Math.Min((age - 35) / 17, (155 - age) / 53));
            }

            public void Expire(double now)
            {
                if (!Active || now - Started < BubbleTime) return;
                Active = false;
                Scan = 0;
                Target = StartPoint = Rest;
                Mouse = RightButton = false;
            }
        }

        private static double Smooth(double t) { return t * t * (3 - 2 * t); }

        internal static BitmapImage Sprite(string name)
        {
            using (Stream data = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (data == null) throw new FileNotFoundException("缺少桌宠图片", name);
                BitmapImage sprite = new BitmapImage();
                sprite.BeginInit();
                sprite.CacheOption = BitmapCacheOption.OnLoad;
                sprite.StreamSource = data;
                sprite.EndInit();
                sprite.Freeze();
                return sprite;
            }
        }

        private static ImageSource CreatePaw()
        {
            var mask = new LinearGradientBrush();
            mask.StartPoint = new Point(0, 0);
            mask.EndPoint = new Point(0, 1);
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            mask.GradientStops.Add(new GradientStop(Colors.White, 0.27));
            var layer = new DrawingGroup();
            layer.OpacityMask = mask;
            using (DrawingContext dc = layer.Open())
                dc.DrawImage(Sprite("paw.png"), new Rect(0, 0, 34, 60));
            var image = new DrawingImage(layer);
            image.Freeze();
            return image;
        }

        private static SolidColorBrush Paint(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            Point lp = left.Position(now);
            Point rp = right.Position(now);
            double l = left.Contact(now), r = right.Contact(now);
            double lean = ((lp.X - left.Rest.X) + (rp.X - right.Rest.X)) * 0.107;
            double dip = Math.Max(l, r) * 3.4;
            dc.PushTransform(new TranslateTransform(9, 0));
            dc.DrawImage(Body, new Rect(72 + lean, 12 + dip, 251, 209 - dip * 0.4));
            DrawMouse(dc, left.Mouse ? l : 0, left.RightButton);
            KeyboardArt.Draw(dc, left.Scan, left.Mouse ? 0 : l, right.Scan, r);
            DrawPaw(dc, lp, left.Rest, false, lean, dip);
            DrawPaw(dc, rp, right.Rest, true, lean, dip);
            if (showKeyLabels)
            {
                DrawBubble(dc, left, now, false);
                DrawBubble(dc, right, now, true);
            }
            dc.Pop();
        }

        private static void DrawPaw(DrawingContext dc, Point tip, Point rest, bool onRight, double lean, double dip)
        {
            // Keep the shoulder inside the chest while the toe follows the struck key.
            double shoulderX = rest.X + (onRight ? -8 : 8) + (tip.X - rest.X) * 0.23 + lean;
            Point shoulder = new Point(shoulderX, 165 + dip + (tip.Y - rest.Y) * 0.22);
            Vector reach = tip - shoulder;
            double angle = -Math.Atan2(reach.X, reach.Y) * 180 / Math.PI;
            double width = 34 - Math.Min(4, Math.Max(0, reach.Length - 50) * 0.10);
            dc.PushTransform(new TranslateTransform(shoulder.X, shoulder.Y));
            dc.PushTransform(new RotateTransform(angle));
            if (onRight) dc.PushTransform(new ScaleTransform(-1, 1));
            dc.DrawImage(Paw, new Rect(-width / 2, 0, width, reach.Length + 2.5));
            if (onRight) dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private static void DrawMouse(DrawingContext dc, double press, bool rightButton)
        {
            dc.DrawGeometry(null, InkLine, MouseCable);
            dc.DrawImage(MouseSprite, new Rect(-2, 236 + press, 34, 48 - press));
            if (press > 0)
            {
                dc.PushOpacity(press * 0.38);
                dc.DrawEllipse(Amber, null, new Point(rightButton ? 22 : 9, 248), 4.5, 6.2);
                dc.Pop();
            }
        }

        private static void DrawBubble(DrawingContext dc, Stroke stroke, double now, bool onRight)
        {
            if (!stroke.Active || stroke.Mouse) return;
            string label = KeyboardArt.Label(stroke.Scan);
            if (String.IsNullOrEmpty(label)) return;
            double age = now - stroke.Started;
            if (age < 22 || age >= BubbleTime) return;
            double t = (age - 22) / (BubbleTime - 22);
            double opacity = Math.Min(1, Math.Min(t * 9, (1 - t) * 3.8));
            FormattedText text;
            if (!Labels.TryGetValue(label, out text))
            {
                var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 16, Umber, 1.0);
                Labels.Add(label, text);
            }
            double w = Math.Max(29, text.Width + 13);
            double x = onRight ? Math.Max(300, stroke.Target.X + 6) : Math.Min(90, stroke.Target.X - 6);
            double y = 173 - 34 * t;
            dc.PushOpacity(opacity);
            dc.PushTransform(new RotateTransform(onRight ? 7 : -7, x, y));
            dc.DrawRoundedRectangle(Amber, InkLine, new Rect(x - w / 2, y - 11, w, 25), 5, 5);
            dc.DrawRoundedRectangle(Paper, InkLine, new Rect(x - w / 2, y - 14, w, 24), 5, 5);
            dc.DrawText(text, new Point(x - text.Width / 2, y - 2 - text.Height / 2));
            dc.Pop();
            dc.Pop();
        }
    }
}

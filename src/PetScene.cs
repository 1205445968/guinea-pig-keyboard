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
        private PetSkin skin;
        private BitmapImage body;
        private ImageSource paw;
        private BitmapSource[] eyes;
        private static readonly BitmapImage MouseSprite = Sprite("mouse.png");
        private static readonly SolidColorBrush Paper = Paint(255, 243, 216);
        private static readonly SolidColorBrush Umber = Paint(111, 73, 47);
        private static readonly SolidColorBrush Amber = Paint(218, 149, 84);
        private static readonly Pen InkLine = new Pen(Umber, 1.55);
        private static readonly Dictionary<string, FormattedText> Labels = new Dictionary<string, FormattedText>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer animation;
        private readonly DispatcherTimer blinkTimer = new DispatcherTimer();
        private readonly Random random = new Random();
        private double blinkStarted = double.NegativeInfinity;
        private double lastLook = double.NegativeInfinity;
        private Vector lookMotion;
        private int lookFrame;
        // These names refer to the viewer's side. The guinea pig faces the viewer.
        private readonly Stroke left = new Stroke(new Point(156, 214));
        private readonly Stroke right = new Stroke(new Point(263, 214));
        private Point mousePosition = new Point(31, 243);
        private Point mouseDestination = new Point(31, 243);
        private double lastFrame;
        private double lastMouseUse = double.NegativeInfinity;
        private double lastClick = double.NegativeInfinity;
        private double mouseGrip;
        private bool rightButton;
        private bool showKeyLabels = true;

        public PetScene()
        {
            Width = 400;
            Height = 308;
            Skin = PetSkin.All[0];
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            animation = new DispatcherTimer(DispatcherPriority.Render);
            animation.Interval = TimeSpan.FromMilliseconds(24);
            animation.Tick += delegate
            {
                double now = clock.Elapsed.TotalMilliseconds;
                left.Expire(now);
                right.Expire(now);
                AdvanceMouse(now);
                if (now - lastLook > 420) lookFrame = 0;
                if (lookFrame == 0 && BlinkFrame(now - blinkStarted) == 0
                    && !left.Active && !right.Active && mouseGrip == 0 && mousePosition == mouseDestination
                    && now - lastMouseUse > 250) animation.Stop();
                InvalidateVisual();
            };
            blinkTimer.Tick += delegate
            {
                blinkStarted = clock.Elapsed.TotalMilliseconds;
                ScheduleBlink();
                Wake();
            };
            Loaded += delegate { ScheduleBlink(); };
            Unloaded += delegate { blinkTimer.Stop(); animation.Stop(); };
        }

        public PetSkin Skin
        {
            get { return skin; }
            set
            {
                if (value == null || value == skin) return;
                BitmapImage newBody = Sprite(value.BodyResource);
                ImageSource newPaw = CreatePaw(value.PawResource);
                BitmapSource[] newEyes = EyeFrames(value.Id);
                body = newBody;
                paw = newPaw;
                eyes = newEyes;
                skin = value;
                InvalidateVisual();
            }
        }

        public bool ShowKeyLabels
        {
            get { return showKeyLabels; }
            set { showKeyLabels = value; InvalidateVisual(); }
        }

        public void PressKey(int scan)
        {
            bool onRight = KeyboardArt.IsRight(scan);
            Stroke hand = onRight ? left : right;
            double now = clock.Elapsed.TotalMilliseconds;
            Point start = hand.Position(now);
            if (onRight)
            {
                start = MouseHand(start, now);
                mouseGrip = 0;
                lastMouseUse = double.NegativeInfinity;
                lastClick = double.NegativeInfinity;
            }
            hand.Start(now, start, KeyboardArt.Target(scan, onRight), scan);
            Wake();
        }

        public void MoveMouse(double dx, double dy)
        {
            if (dx == 0 && dy == 0) return;
            double now = clock.Elapsed.TotalMilliseconds;
            if (now - lastLook > 160) lookMotion = new Vector();
            lookMotion = lookMotion * 0.70 + new Vector(Math.Max(-40, Math.Min(40, dx)), Math.Max(-40, Math.Min(40, dy)));
            if (lookMotion.LengthSquared >= 3)
            {
                bool vertical = Math.Abs(lookMotion.Y) > Math.Abs(lookMotion.X) * 0.42;
                bool horizontal = Math.Abs(lookMotion.X) > Math.Abs(lookMotion.Y) * 0.42;
                if (!vertical) lookFrame = lookMotion.X < 0 ? 1 : 2;
                else if (!horizontal) lookFrame = lookMotion.Y < 0 ? 3 : 4;
                else if (lookMotion.Y < 0) lookFrame = lookMotion.X < 0 ? 5 : 6;
                else lookFrame = lookMotion.X < 0 ? 7 : 8;
            }
            lastLook = now;
            mouseDestination.X = Math.Max(10, Math.Min(46, mouseDestination.X + dx * 0.12));
            mouseDestination.Y = Math.Max(210, Math.Min(270, mouseDestination.Y + dy * 0.12));
            lastMouseUse = now;
            if (!animation.IsEnabled) Wake();
        }

        public void ClickMouse(bool isRightButton)
        {
            rightButton = isRightButton;
            lastMouseUse = lastClick = clock.Elapsed.TotalMilliseconds;
            Wake();
        }

        private void Wake()
        {
            if (!animation.IsEnabled)
            {
                lastFrame = clock.Elapsed.TotalMilliseconds;
                animation.Start();
            }
            InvalidateVisual();
        }

        private void ScheduleBlink()
        {
            blinkTimer.Stop();
            blinkTimer.Interval = TimeSpan.FromMilliseconds(random.Next(3000, 6001));
            blinkTimer.Start();
        }

        private static int BlinkFrame(double age)
        {
            if (age < 0 || age >= 200) return 0;
            if (age < 40 || age >= 160) return 9;
            if (age < 80 || age >= 120) return 10;
            return 11;
        }

        private static BitmapSource[] EyeFrames(string id)
        {
            var sheet = Sprite(id + "-eyes.png");
            int w = sheet.PixelWidth / 4, h = sheet.PixelHeight / 3;
            var frames = new BitmapSource[12];
            for (int i = 1; i < frames.Length; i++)
            {
                frames[i] = new CroppedBitmap(sheet, new Int32Rect(i % 4 * w, i / 4 * h, w, h));
                frames[i].Freeze();
            }
            return frames;
        }

        private void AdvanceMouse(double now)
        {
            double elapsed = Math.Max(0, now - lastFrame);
            lastFrame = now;
            mousePosition += (mouseDestination - mousePosition) * (1 - Math.Exp(-elapsed / 32));
            if ((mouseDestination - mousePosition).LengthSquared < 0.01) mousePosition = mouseDestination;

            bool tapping = left.Active && now - left.Started < 130 && left.Started > lastClick;
            double gripTarget = !tapping && now - lastMouseUse <= 250 ? 1 : 0;
            double ease = 1 - Math.Exp(-elapsed / (gripTarget > mouseGrip ? 30 : 53));
            mouseGrip += (gripTarget - mouseGrip) * ease;
            if (Math.Abs(gripTarget - mouseGrip) < 0.004) mouseGrip = gripTarget;
        }

        private Point MouseButtonPoint()
        {
            return mousePosition + new Vector(rightButton ? -6 : 6, 11);
        }

        private Point MouseHand(Point keyboardTip, double now)
        {
            double press = Contact(now - lastClick);
            Point palm = mousePosition + new Vector(3, -2);
            Point target = palm + (MouseButtonPoint() - palm) * press;
            return keyboardTip + (target - keyboardTip) * mouseGrip;
        }

        private sealed class Stroke
        {
            public readonly Point Rest;
            public Point Target, StartPoint;
            public double Started;
            public int Scan;
            public bool Active;

            public Stroke(Point rest) { Rest = Target = StartPoint = rest; }

            public void Start(double now, Point start, Point target, int scan)
            {
                StartPoint = start;
                Started = now;
                Target = target;
                Scan = scan;
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
                return Active ? PetScene.Contact(now - Started) : 0;
            }

            public void Expire(double now)
            {
                if (!Active || now - Started < BubbleTime) return;
                Active = false;
                Scan = 0;
                Target = StartPoint = Rest;
            }
        }

        private static double Smooth(double t) { return t * t * (3 - 2 * t); }

        private static double Contact(double age)
        {
            if (age < 35 || age > 155) return 0;
            return Math.Min(1, Math.Min((age - 35) / 17, (155 - age) / 53));
        }

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

        private static ImageSource CreatePaw(string resource)
        {
            var mask = new LinearGradientBrush();
            mask.StartPoint = new Point(0, 0);
            mask.EndPoint = new Point(0, 1);
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            mask.GradientStops.Add(new GradientStop(Colors.White, 0.27));
            var layer = new DrawingGroup();
            layer.OpacityMask = mask;
            using (DrawingContext dc = layer.Open())
                dc.DrawImage(Sprite(resource), new Rect(0, 0, 34, 60));
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
            Point lp = MouseHand(left.Position(now), now);
            Point rp = right.Position(now);
            double l = left.Contact(now), r = right.Contact(now);
            double lean = ((lp.X - left.Rest.X) + (rp.X - right.Rest.X)) * 0.107;
            double click = Contact(now - lastClick);
            double dip = Math.Max(click, Math.Max(l, r)) * 3.4;
            dc.PushTransform(new TranslateTransform(9, 0));
            Rect bodyRect = new Rect(84 + lean, 12 + dip, 251, 209 - dip * 0.4);
            dc.DrawImage(body, bodyRect);
            int frame = BlinkFrame(now - blinkStarted);
            if (frame == 0) frame = lookFrame;
            if (frame != 0)
                dc.DrawImage(eyes[frame], new Rect(bodyRect.X + bodyRect.Width * 0.16, bodyRect.Y + bodyRect.Height * 0.19,
                    bodyRect.Width * 0.68, bodyRect.Height * 0.27));
            DrawMouse(dc, click);
            KeyboardArt.Draw(dc, right.Scan, r, left.Scan, l);
            DrawPaw(dc, lp, left.Rest, false, lean, dip);
            DrawPaw(dc, rp, right.Rest, true, lean, dip);
            if (showKeyLabels)
            {
                DrawBubble(dc, left, now, false);
                DrawBubble(dc, right, now, true);
            }
            dc.Pop();
        }

        private void DrawPaw(DrawingContext dc, Point tip, Point rest, bool onRight, double lean, double dip)
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
            dc.DrawImage(paw, new Rect(-width / 2, 0, width, reach.Length + 2.5));
            if (onRight) dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private void DrawMouse(DrawingContext dc, double press)
        {
            dc.PushTransform(new RotateTransform(180, mousePosition.X, mousePosition.Y));
            dc.DrawImage(MouseSprite, new Rect(mousePosition.X - 17, mousePosition.Y - 24 + press, 34, 48 - press));
            dc.Pop();
            if (press > 0)
            {
                dc.PushOpacity(press * 0.38);
                dc.DrawEllipse(Amber, null, MouseButtonPoint(), 4.5, 6.2);
                dc.Pop();
            }
        }

        private static void DrawBubble(DrawingContext dc, Stroke stroke, double now, bool onRight)
        {
            if (!stroke.Active) return;
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

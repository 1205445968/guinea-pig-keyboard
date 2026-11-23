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
        private BitmapImage paw;
        private Brush fur;
        private BitmapSource[] eyes;
        private static readonly BitmapSource[] MouseSprites = MouseFrames();
        private static readonly Pen LimbOutline = new Pen(Paint(81, 58, 39), 1.05);
        private static readonly SolidColorBrush Paper = Paint(255, 243, 216);
        private static readonly SolidColorBrush Umber = Paint(78, 53, 30);
        private static readonly SolidColorBrush Amber = Paint(229, 188, 97);
        private static readonly int[] MirroredGaze = { 0, 2, 1, 3, 4, 6, 5, 8, 7 };
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
        private readonly Stroke left = new Stroke(new Point(172, 215));
        private readonly Stroke right = new Stroke(new Point(215, 213));
        private Point mousePosition = new Point(74, 234);
        private Point mouseDestination = new Point(74, 234);
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
                BitmapImage newPaw = Sprite(value.PawResource);
                BitmapSource[] newEyes = EyeFrames(value.Id);
                fur = value.Id == "teddy" ? Paint(168, 105, 70)
                    : value.Id == "silver-agouti" ? Paint(161, 144, 134) : Paint(250, 228, 203);
                paw = newPaw;
                body = newBody;
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
            Pose pose = CurrentPose(now);
            Point start = onRight ? pose.LeftTip : pose.RightTip;
            Point target = KeyboardArt.Target(scan, onRight);
            Stroke other = onRight ? right : left;
            double when = now;
            if (other.Active && other.Started <= now && Math.Abs(target.X - other.Target.X) > 140)
                when = Math.Max(now, other.Started + 130);
            lastMouseUse = lastClick = double.NegativeInfinity;
            hand.Start(when, start, target, scan);
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
            mouseDestination.X = Math.Max(65, Math.Min(82, mouseDestination.X + dx * 0.055));
            mouseDestination.Y = Math.Max(226, Math.Min(240, mouseDestination.Y + dy * 0.055));
            lastMouseUse = now;
            if (!animation.IsEnabled) Wake();
        }

        public void ClickMouse(bool isRightButton)
        {
            rightButton = isRightButton;
            lastMouseUse = clock.Elapsed.TotalMilliseconds;
            lastClick = lastMouseUse + (1 - mouseGrip) * 110;
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
            for (int i = 0; i < frames.Length; i++)
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
            bool holding = now - lastMouseUse < 520;
            bool typing = double.IsNegativeInfinity(lastMouseUse);
            mouseGrip = holding ? Math.Min(1, mouseGrip + elapsed / 145)
                : Math.Max(0, mouseGrip - elapsed / (typing ? 55 : 195));
            double traction = Smooth(Math.Max(0, (mouseGrip - 0.64) / 0.36));
            mousePosition += (mouseDestination - mousePosition) * (1 - Math.Exp(-elapsed / 48)) * traction;
            if ((mouseDestination - mousePosition).LengthSquared < 0.01) mousePosition = mouseDestination;
            if (!holding && mouseGrip == 0) mouseDestination = mousePosition;
        }

        private static BitmapSource[] MouseFrames()
        {
            var sheet = Sprite("mouse.png");
            var frames = new BitmapSource[3];
            int w = sheet.PixelWidth / frames.Length;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = new CroppedBitmap(sheet, new Int32Rect(i * w, 0, w, sheet.PixelHeight));
                frames[i].Freeze();
            }
            return frames;
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

            public double RestWeight(double now)
            {
                if (!Active) return 1;
                double age = Math.Max(0, now - Started);
                return Smooth(Math.Max(0, Math.Min(1, (age - 102) / (SettleTime - 102))));
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

        private static SolidColorBrush Paint(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private struct Pose
        {
            public Rect Body;
            public Point LeftTip, RightTip;
        }

        private Pose CurrentPose(double now)
        {
            Point lp = left.Position(now), rp = right.Position(now);
            double grip = Smooth(mouseGrip);
            double lean = Math.Max(-78, Math.Min(115, ((lp.X - left.Rest.X) + (rp.X - right.Rest.X)) * 0.84));
            lean += (mousePosition.X - 8 - 135 - lean) * grip;
            double reachY = Math.Max(0, Math.Max(lp.Y - left.Rest.Y, rp.Y - right.Rest.Y));
            double dip = Math.Min(19, reachY * 0.42) * (1 - grip) + (12 + (mousePosition.Y - 226) * 0.45) * grip;
            lp += new Vector(lean, dip * 0.68) * left.RestWeight(now);
            rp += new Vector(lean, dip * 0.68) * right.RestWeight(now);

            // 空着的前爪撑在键盘后沿，另一只绕过机身落到鼠标上。
            Point brace = new Point(153, 227);
            rp += (brace - rp) * (grip * right.RestWeight(now));
            double press = Contact(now - lastClick);
            Point mouseTip = mousePosition + new Vector(5.5 + (rightButton ? -11 : -1) * press,
                                                       2.5 + (rightButton ? -1 : 3.2) * press);
            lp += (mouseTip - lp) * grip;
            lp.Y -= Math.Sin(grip * Math.PI) * 5;
            return new Pose { Body = new Rect(135 + lean, 123 + dip, 129, 85), LeftTip = lp, RightTip = rp };
        }

        protected override void OnRender(DrawingContext dc)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            Pose pose = CurrentPose(now);
            double l = left.Contact(now), r = right.Contact(now);
            dc.PushTransform(new TranslateTransform(9, 0));
            KeyboardArt.Draw(dc, right.Scan, r, left.Scan, l);
            double press = Contact(now - lastClick) * Smooth(mouseGrip);
            int mouseFrame = press > 0.32 ? (rightButton ? 2 : 1) : 0;
            dc.DrawImage(MouseSprites[mouseFrame], new Rect(mousePosition.X - 20, mousePosition.Y - 18, 40, 36));

            DrawPaw(dc, pose.RightTip, pose.Body, true);
            DrawPaw(dc, pose.LeftTip, pose.Body, false);
            Rect b = pose.Body;
            dc.PushTransform(new ScaleTransform(-1, 1, b.X + b.Width / 2, 0));
            dc.DrawImage(body, b);
            int frame = BlinkFrame(now - blinkStarted);
            if (frame == 0) frame = Math.Max(l, r) > 0.45 ? 10 : MirroredGaze[lookFrame];
            dc.DrawImage(eyes[frame], new Rect(b.X + b.Width * 0.58, b.Y + b.Height * 0.25, b.Width * 0.22, b.Height * 0.30));
            dc.Pop();
            if (showKeyLabels)
            {
                DrawBubble(dc, left, now, false, b);
                DrawBubble(dc, right, now, true, b);
            }
            dc.Pop();
        }

        private void DrawPaw(DrawingContext dc, Point tip, Rect chest, bool far)
        {
            double grip = far ? 0 : Smooth(mouseGrip);
            double anchorX = chest.X + (far ? 75 : 36);
            Point shoulder = new Point(anchorX + Math.Max(-13, Math.Min(13, (tip.X - anchorX) * 0.30)), chest.Y + 63);
            Vector reach = tip - shoulder;
            double angle = Math.Max(-0.58, Math.Min(0.58, -Math.Atan2(reach.X, reach.Y)));
            angle += (0.52 - angle) * grip;
            Vector down = new Vector(-Math.Sin(angle), Math.Cos(angle));
            Point joint = tip - down * 6.8;
            Vector axis = joint - shoulder;
            double length = axis.Length;
            if (length > 1)
            {
                Vector normal = new Vector(axis.Y / length, -axis.X / length);
                Point bend = shoulder + axis * 0.55 + new Vector(far ? 2.6 : -3.2, -2.0);
                var arm = new StreamGeometry();
                using (StreamGeometryContext path = arm.Open())
                {
                    path.BeginFigure(shoulder - normal * 6.2, true, true);
                    path.QuadraticBezierTo(bend - normal * 5.5, joint - normal * 4.5, true, true);
                    path.LineTo(joint + normal * 4.5, true, true);
                    path.QuadraticBezierTo(bend + normal * 5.5, shoulder + normal * 6.2, true, true);
                }
                dc.DrawGeometry(fur, LimbOutline, arm);
            }
            dc.PushTransform(new TranslateTransform(tip.X, tip.Y));
            dc.PushTransform(new RotateTransform(angle * 180 / Math.PI));
            if (far) dc.PushTransform(new ScaleTransform(-1, 1));
            dc.DrawImage(paw, new Rect(-8.5 + 1.1 * grip, -15 + 2 * grip, 17 - 2.2 * grip, 15.4 - 2 * grip));
            if (far) dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        private static void DrawBubble(DrawingContext dc, Stroke stroke, double now, bool onRight, Rect bodyRect)
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
            if (x + w / 2 > bodyRect.Left - 5 && x - w / 2 < bodyRect.Right + 5)
                y = Math.Min(y, bodyRect.Top - 18);
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

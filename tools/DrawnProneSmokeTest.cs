using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class DrawnProneSmokeTest
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly List<object> Scenes = new List<object>();
    private static object Field(object o, string name) { return o.GetType().GetField(name, Fields).GetValue(o); }
    private static void Set(object o, string name, object value) { o.GetType().GetField(name, Fields).SetValue(o, value); }
    private static double Now(object o) { return ((Stopwatch)Field(o, "clock")).Elapsed.TotalMilliseconds; }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
        byte[] pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
    private static double Outside(Point p, Rect body) { return Math.Max(0, Math.Max(body.Left - p.X, p.X - body.Right)); }
    private static Point[] Tips(object pose, bool four)
    {
        return four ? new[] { (Point)Field(pose, "LeftTip"), (Point)Field(pose, "RightTip"), (Point)Field(pose, "RearLeftTip"), (Point)Field(pose, "RearRightTip") }
            : new[] { (Point)Field(pose, "LeftTip"), (Point)Field(pose, "RightTip") };
    }
    private static void CheckPaws(object pose, string label, bool four)
    {
        Rect body = (Rect)Field(pose, "Body");
        foreach (Point tip in Tips(pose, four))
            Check(Outside(tip, body) <= 35, label + " long paw: body=" + body + ", tip=" + tip);
    }
    private static void CheckTucked(object pose, string label)
    {
        Rect body = (Rect)Field(pose, "Body");
        foreach (Point tip in Tips(pose, true))
            Check(tip.Y <= body.Bottom - 2, label + " paw must tuck to body bottom: body=" + body + ", tip=" + tip);
    }
    private static object Scene(Type type, object skin)
    {
        object scene = Activator.CreateInstance(type, new object[] { false });
        Scenes.Add(scene); type.GetProperty("Skin").SetValue(scene, skin, null); return scene;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        try
        {
            Assembly app = Assembly.LoadFile(Path.GetFullPath(args[0]));
            Type type = app.GetType("WheekKeys.PixelPetScene", true), skins = app.GetType("WheekKeys.PetSkin", true);
            MethodInfo pose = type.GetMethod("CurrentDrawnPose", Fields);
            var sizes = new Dictionary<string, Size> { {"prone-tricolor", new Size(492,286)}, {"prone-black-white", new Size(492,286)}, {"prone-golden-white", new Size(492,286)}, {"prone-cream", new Size(492,286)}, {"prone-californian", new Size(492,286)}, {"prone-teddy", new Size(492,286)}, {"prone-silver-agouti", new Size(492,286)}, {"prone-longhair", new Size(492,286)} };
            foreach (string id in new[] { "prone-tricolor", "prone-black-white", "prone-golden-white", "prone-cream", "prone-californian", "prone-teddy", "prone-silver-agouti", "prone-longhair" })
            {
                object skin = skins.GetMethod("FromId").Invoke(null, new object[] { id });
                object probe = Scene(type, skin);
                bool compact = (bool)Field(Field(probe, "drawnProne"), "Compact");
                object eyes = Field(probe, "Eyes");
                BitmapSource body = (BitmapSource)eyes.GetType().GetProperty("Body").GetValue(eyes, null);
                BitmapSource paw = (BitmapSource)Field(probe, "Paw");
                Check(body.PixelWidth == sizes[id].Width && body.PixelHeight == sizes[id].Height, id + " original body resolution");
                Check(paw.PixelWidth == 111 && paw.PixelHeight == 111, id + " original paw resolution");
                if (compact) CheckTucked(pose.Invoke(probe, new object[] { Now(probe) }), id + " idle");
                string native = id.Substring("prone-".Length);
                MethodInfo load = type.GetMethod("LoadSprite", BindingFlags.Static | BindingFlags.NonPublic);
                Check(Pixels(body).SequenceEqual(Pixels((BitmapSource)load.Invoke(null, new object[] { "drawn-" + native + "-body.png" }))), id + " exact body resource");
                double min = double.MaxValue, max = double.MinValue;
                var usedPaws = new HashSet<int>();
                foreach (int scan in new[] { 1, 11, 29, 30, 38, 44, 54, 57, 0x11d })
                {
                    object scene = Scene(type, skin);
                    type.GetMethod("PressKey").Invoke(scene, new object[] { scan });
                    double now = Now(scene);
                    object[] paws = compact ? ((Array)Field(scene, "compactPaws")).Cast<object>().ToArray()
                        : new[] { Field(scene, "left"), Field(scene, "right") };
                    foreach (object stroke in paws)
                    {
                        if ((bool)Field(stroke, "Active")) Set(stroke, "Started", now - 70);
                    }
                    object p = pose.Invoke(scene, new object[] { now });
                    if (compact)
                    {
                        Rect compactBody = (Rect)Field(p, "Body");
                        Point[] tips = Tips(p, true);
                        for (int i = 0; i < paws.Length; i++)
                        {
                            if ((bool)Field(paws[i], "Active"))
                            {
                                usedPaws.Add(i);
                                Check((tips[i] - (Point)Field(paws[i], "Target")).Length < 0.01,
                                    id + " scan " + scan + " paw " + i + " reaches key");
                                Set(paws[i], "Started", now - 300);
                            }
                            else Check(tips[i].Y <= compactBody.Bottom - 2, id + " inactive paw " + i + " stays tucked");
                        }
                        CheckTucked(pose.Invoke(scene, new object[] { now }), id + " scan " + scan + " return");
                    }
                    Rect bounds = (Rect)Field(p, "Body"); min = Math.Min(min, bounds.X); max = Math.Max(max, bounds.X);
                    CheckPaws(p, id + " scan " + scan, compact);
                }
                if (compact)
                {
                    Check(usedPaws.SetEquals(new[] { 1, 2, 3 }), id + " rear forepaw and both rear paws press keys");
                    object chord = Scene(type, skin);
                    foreach (int scan in new[] { 11, 1, 0x11d, 29 }) type.GetMethod("PressKey").Invoke(chord, new object[] { scan });
                    object[] chordPaws = ((Array)Field(chord, "compactPaws")).Cast<object>().ToArray();
                    Check(!(bool)Field(chordPaws[0], "Active") && (bool)Field(chordPaws[1], "Active")
                        && (bool)Field(chordPaws[2], "Active") && (bool)Field(chordPaws[3], "Active"),
                        id + " rear forepaw and both rear paws retain key strokes");
                }
                Check(max - min >= 100, id + " body must follow keys across keyboard; span=" + (max - min));
                object mouse = Scene(type, skin);
                Set(mouse, "mouseGrip", 1.0); Set(mouse, "lastMouseUse", Now(mouse));
                Set(mouse, "mousePosition", new Point(31, 243));
                object m = pose.Invoke(mouse, new object[] { Now(mouse) });
                Check(((Rect)Field(m, "Body")).X < 60, id + " body moves beside mouse");
                CheckPaws(m, id + " mouse", compact);
                if (compact)
                {
                    Point leftTip = (Point)Field(m, "LeftTip");
                    Point rightTip = (Point)Field(m, "RightTip");
                    Check(rightTip.X - leftTip.X >= 12 && rightTip.X - leftTip.X <= 20, id + " front paws stay adjacent without overlapping");
                    object art = Field(mouse, "drawnProne");
                    Point idleTip = (Point)art.GetType().GetMethod("RestTip", new[] { typeof(Rect), typeof(int) })
                        .Invoke(art, new object[] { (Rect)Field(m, "Body"), 1 });
                    Check(Math.Abs(rightTip.Y - idleTip.Y) < 0.001, id + " support paw matches its idle height");
                    Check(leftTip.Y - rightTip.Y >= 3, id + " support paw is shorter than mouse paw");
                }
                Console.WriteLine("PASS: " + id + " original art, keyboard travel=" + (max - min).ToString("F1") + ", short paws and mouse pose" + (compact ? ", far forepaw and both rear paws press and retract" : ""));
            }
            MethodInfo custom = skins.GetMethod("CreateCustom", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo portrait = type.GetMethod("Portrait", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string coat in new[] { "californian", "teddy", "silver-agouti", "longhair" })
            {
                object derived = custom.Invoke(null, new object[] { "smoke-" + coat, "Smoke " + coat, "prone-" + coat });
                object scene = Scene(type, derived);
                Check(portrait.Invoke(null, new[] { derived }) != null && Field(scene, "Paw") != null, "custom " + coat + " scene and portrait load");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetBaseException().Message); return 1; }
        finally
        {
            foreach (object scene in Scenes)
                foreach (string name in new[] { "animation", "gaze" }) ((DispatcherTimer)Field(scene, name)).Stop();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }
}

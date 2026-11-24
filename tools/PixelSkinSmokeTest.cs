using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Media;

internal static class PixelSkinSmokeTest
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: PixelSkinSmokeTest.exe WheekKeys.exe"); return 2; }
        string exe = Path.GetFullPath(args[0]);
        string profile = Path.Combine(Path.GetTempPath(), "wheek-014-ui-check-" + Guid.NewGuid().ToString("N") + ".txt");
        Assembly assembly = Assembly.LoadFile(exe);
        Type windowType = assembly.GetType("WheekKeys.PetWindow", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        object owner = Activator.CreateInstance(windowType, true);
        Window settings = null;
        try
        {
            FieldInfo pathField = windowType.GetField("settingsPath", BindingFlags.Instance | BindingFlags.NonPublic);
            pathField.SetValue(owner, profile);
            File.WriteAllLines(profile, new[] { "111", "222", "330", "False", "True", "pixel-tricolor", "False", "12345", "6789", "测试豚鼠" });
            MethodInfo restore = windowType.GetMethod("RestoreSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            restore.Invoke(owner, null);
            Check(Convert.ToDouble(windowType.GetProperty("Left").GetValue(owner, null)) == 111, "left");
            Check(Convert.ToDouble(windowType.GetProperty("Top").GetValue(owner, null)) == 222, "top");
            Check(Convert.ToDouble(windowType.GetProperty("PetWidth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 330, "width");
            Check(!(bool)windowType.GetProperty("LabelsVisible", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null), "labels");
            Check((bool)windowType.GetProperty("MouseClickThrough", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null), "through");
            Check(Convert.ToInt64(windowType.GetProperty("KeyCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 12345, "key count");
            Check(Convert.ToInt64(windowType.GetProperty("ClickCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null)) == 6789, "click count");
            Check((string)windowType.GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null) == "测试豚鼠", "nickname");

            FieldInfo ready = windowType.GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic);
            ready.SetValue(owner, true);
            settings = (Window)Activator.CreateInstance(assembly.GetType("WheekKeys.PetSettings", true), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { owner }, null);
            FieldInfo buttonsField = settings.GetType().GetField("skinButtons", BindingFlags.Instance | BindingFlags.NonPublic);
            IDictionary buttons = (IDictionary)buttonsField.GetValue(settings);
            Check(buttons.Count == 20, "skin button count");
            foreach (DictionaryEntry entry in buttons)
            {
                Button button = (Button)entry.Value;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                object active = windowType.GetProperty("ActiveSkin", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(owner, null);
                string activeId = (string)skinType.GetField("Id").GetValue(active);
                Check(activeId == (string)entry.Key, "clicked skin " + entry.Key);
                string[] lines = File.ReadAllLines(profile);
                Check(lines.Length >= 10 && lines[5] == activeId, "saved skin " + entry.Key);
                Check(lines[0] == "111" && lines[1] == "222" && lines[2] == "330" && lines[3] == "False" && lines[4] == "True" && lines[6] == "False" && lines[7] == "12345" && lines[8] == "6789" && lines[9] == "测试豚鼠", "preserved profile " + entry.Key);
            }
            MethodInfo fromId = skinType.GetMethod("FromId", BindingFlags.Static | BindingFlags.Public);
            object oldAlias = fromId.Invoke(null, new object[] { "longhair-buff" });
            Check((string)skinType.GetField("Id").GetValue(oldAlias) == "pixel-longhair-buff", "old alias");
            object fallback = fromId.Invoke(null, new object[] { "unknown-skin-id" });
            Check((string)skinType.GetField("Id").GetValue(fallback) == "tricolor", "unknown fallback");
            CheckRenderLifecycle(assembly);
            CheckNetworkCompatibility(assembly);
            Console.WriteLine("PASS: settings roundtrips, 20 skin buttons, render lifecycle, legacy aliases, peer names and counters");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            if (settings != null) settings.Close();
            FieldInfo ready = windowType.GetField("ready", BindingFlags.Instance | BindingFlags.NonPublic);
            ready.SetValue(owner, false);
            ((Window)owner).Close();
            if (File.Exists(profile)) File.Delete(profile);
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Check failed: " + label);
    }

    private static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }

    private static void CheckRenderLifecycle(Assembly assembly)
    {
        Type sceneType = assembly.GetType("WheekKeys.PetScene", true);
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        var scene = (FrameworkElement)Activator.CreateInstance(sceneType);
        var options = new HwndSourceParameters("Pixel skin lifecycle test")
            { WindowStyle = unchecked((int)0x80000000), Width = 400, Height = 308 };
        using (var host = new HwndSource(options))
        {
            host.RootVisual = scene;
            scene.Measure(new Size(400, 308));
            scene.Arrange(new Rect(0, 0, 400, 308));
            scene.UpdateLayout();
            Pump();
            object pixel = skinType.GetMethod("FromId").Invoke(null, new object[] { "pixel-longhair-tricolor" });
            sceneType.GetProperty("Skin").SetValue(scene, pixel, null);
            Pump();
            object child = Field(scene, "pixelScene");
            Check(child != null && ((FrameworkElement)child).IsLoaded, "pixel child attached");
            Check(((DispatcherTimer)Field(child, "gaze")).IsEnabled, "pixel gaze started");
            Check(!((DispatcherTimer)Field(scene, "blinkTimer")).IsEnabled, "native blink paused");
            sceneType.GetMethod("PressKey").Invoke(scene, new object[] { 47 });
            object left = Field(child, "left"), right = Field(child, "right");
            Check((bool)left.GetType().GetField("Active").GetValue(left)
                || (bool)right.GetType().GetField("Active").GetValue(right), "pixel key forwarding");
            sceneType.GetMethod("MoveMouse").Invoke(scene, new object[] { 90.0, -30.0 });
            Check((Point)Field(child, "mouseDestination") != new Point(31, 243), "pixel mouse forwarding");
            sceneType.GetMethod("ClickMouse").Invoke(scene, new object[] { true });
            Check((bool)Field(child, "rightButton"), "pixel click forwarding");
            sceneType.GetProperty("ShowKeyLabels").SetValue(scene, false, null);
            Check(!(bool)child.GetType().GetProperty("ShowKeyLabels").GetValue(child, null), "label forwarding");
            object native = skinType.GetMethod("FromId").Invoke(null, new object[] { "tricolor" });
            sceneType.GetProperty("Skin").SetValue(scene, native, null);
            Pump();
            Check(Field(scene, "pixelScene") == null, "pixel child detached");
            Check(!((DispatcherTimer)Field(child, "gaze")).IsEnabled
                && !((DispatcherTimer)Field(child, "animation")).IsEnabled, "detached timers stopped");
            host.RootVisual = null;
            Pump();
        }
    }

    private static void CheckNetworkCompatibility(Assembly assembly)
    {
        Type skinType = assembly.GetType("WheekKeys.PetSkin", true);
        Type peerType = assembly.GetType("WheekKeys.LocalPeers", true);
        object peer = Activator.CreateInstance(peerType);
        try
        {
            MethodInfo read = peerType.GetMethod("ReadPeer", BindingFlags.Instance | BindingFlags.NonPublic);
            string session = "1234567890abcdef1234567890abcdef";
            string[] nativeIds = { "tricolor", "californian", "gold-white", "teddy", "silver-agouti", "longhair" };
            foreach (object skin in (Array)skinType.GetField("All").GetValue(null))
            {
                string id = (string)skinType.GetField("NetworkId").GetValue(skin);
                Check(Array.IndexOf(nativeIds, id) >= 0, "legacy-compatible wire skin " + id);
                read.Invoke(peer, new object[] { "WK1|" + session + "|" + id + "|12345|6789", 10.0 });
            }
            IDictionary members = (IDictionary)Field(peer, "peers");
            Check(members.Count == 1, "peer recognized");
            object member = members[session];
            Check((long)member.GetType().GetField("Keys").GetValue(member) == 12345
                && (long)member.GetType().GetField("Clicks").GetValue(member) == 6789, "peer counters");
            string encoded = (string)assembly.GetType("WheekKeys.PetName").GetMethod("Encode").Invoke(null, new object[] { "测试豚鼠" });
            read.Invoke(peer, new object[] { "WN1|" + session + "|" + encoded, 11.0 });
            Check((string)member.GetType().GetField("Nickname").GetValue(member) == "测试豚鼠", "peer nickname");
            Check(!(bool)peerType.GetProperty("Enabled").GetValue(peer, null), "no real network started");
        }
        finally { ((IDisposable)peer).Dispose(); }
    }
}

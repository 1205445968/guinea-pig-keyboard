using System;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

[assembly: AssemblyProduct("WheekKeys")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
[assembly: AssemblyVersion("0.1.4.0")]
[assembly: AssemblyInformationalVersion("0.1.4")]

namespace WheekKeys
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\WheekKeys.DesktopPet", out created))
            {
                if (!created) return;
                try
                {
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    app.Run(new PetWindow());
                }
                catch (Exception ex)
                {
                    MessageBox.Show("桌宠启动失败：\n" + ex.Message, "豚鼠键盘", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    internal sealed class PetWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int WsExNoActivate = 0x08000000;
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        private readonly PixelPetScene scene;
        private readonly Forms.NotifyIcon tray;
        private readonly string settingsPath;
        private readonly LocalPeers peers = new LocalPeers();
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer networkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly FriendDesktop friends;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long keyCount, clickCount;
        private string nickname = "";
        private bool countsDirty;
        private double nextSave;
        private bool hideForSogou = false;
        private DesktopInput input;
        private bool clickThrough;
        private bool ready;
        private PetSettings settingsWindow;
        private Drawing.Icon ownedIcon;

        public PetWindow()
        {
            Title = "豚鼠键盘";
            Width = 310;
            Height = Width * 308.0 / 400.0;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            scene = new PixelPetScene();
            scene.ShowKeyLabels = true;
            Viewbox view = new Viewbox();
            view.Width = double.NaN;
            view.Height = double.NaN;
            view.Stretch = Stretch.Uniform;
            view.Child = scene;
            Content = view;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheekKeys", "settings.txt");
            RestoreSettings();
            friends = new FriendDesktop(this, view);
            tray = new Forms.NotifyIcon();
            tray.Text = "豚鼠键盘";
            try { ownedIcon = Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); }
            catch { }
            tray.Icon = ownedIcon ?? Drawing.SystemIcons.Application;
            try
            {
                Icon = Imaging.CreateBitmapSourceFromHIcon(tray.Icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            catch { }
            tray.ContextMenuStrip = BuildTrayMenu();
            tray.Visible = true;
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate { ready = true; timer.Start(); networkTimer.Start(); };
            networkTimer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                peers.Tick(now, scene.Skin, keyCount, clickCount, nickname);
                friends.Update(peers, now, clickThrough, scene.ShowKeyLabels);
            };
            timer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                UpdateVisibility();
                if (settingsWindow != null) settingsWindow.RefreshActivity();
                if (countsDirty && now >= nextSave)
                {
                    SaveSettings();
                    nextSave = now + 5;
                }
            };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch (InvalidOperationException) { }
                    SaveSettings();
                }
            };
            MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                Point screen = PointToScreen(e.GetPosition(this));
                OpenSettings(new Drawing.Point((int)screen.X, (int)screen.Y));
                e.Handled = true;
            };
            Closed += delegate
            {
                timer.Stop();
                networkTimer.Stop();
                friends.Dispose();
                peers.Dispose();
                SaveSettings();
                if (settingsWindow != null) settingsWindow.Close();
                if (input != null) input.Dispose();
                tray.Visible = false;
                tray.Dispose();
                if (ownedIcon != null) ownedIcon.Dispose();
            };
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            HwndSource source = (HwndSource)PresentationSource.FromVisual(this);
            ApplyWindowStyles(source.Handle);
            EnsureVisible();
            input = new DesktopInput(source, OnKey, OnClick,
                delegate(double dx, double dy) { if (IsVisible) scene.MoveMouse(dx, dy); },
                delegate(int delta) { if (IsVisible) scene.ScrollMouse(delta); });
        }

        private void OnKey(int scan, bool firstPress)
        {
            if (firstPress && keyCount < long.MaxValue) { keyCount++; countsDirty = true; }
            peers.RecordKey(scan, clock.Elapsed.TotalSeconds);
            if (IsVisible) scene.PressKey(scan);
        }

        private void OnClick(bool right)
        {
            if (clickCount < long.MaxValue) { clickCount++; countsDirty = true; }
            peers.RecordClick(right, clock.Elapsed.TotalSeconds);
            if (IsVisible) scene.ClickMouse(right);
        }

        private void UpdateVisibility()
        {
            bool hide = hideForSogou && SogouInput.IsActive();
            if (hide && IsVisible) Hide();
            else if (!hide && !IsVisible) Show();
        }

        private void ApplyWindowStyles(IntPtr handle)
        {
            int style = GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow;
            if (clickThrough) style |= WsExTransparent; else style &= ~WsExTransparent;
            SetWindowLong(handle, GwlExStyle, style);
        }

        private void OpenSettings(Drawing.Point screenPoint)
        {
            if (settingsWindow != null && settingsWindow.IsVisible)
            {
                settingsWindow.Activate();
                return;
            }

            settingsWindow = new PetSettings(this);
            Drawing.Rectangle work = Forms.Screen.FromPoint(screenPoint).WorkingArea;
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            Matrix fromPixels = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
            Point cursor = fromPixels.Transform(new Point(screenPoint.X, screenPoint.Y));
            Point start = fromPixels.Transform(new Point(work.Left, work.Top));
            Point end = fromPixels.Transform(new Point(work.Right, work.Bottom));
            settingsWindow.Height = Math.Min(settingsWindow.Height, end.Y - start.Y);
            double left = cursor.X + 10;
            double top = cursor.Y + 10;
            if (left + settingsWindow.Width > end.X) left = cursor.X - settingsWindow.Width - 10;
            if (top + settingsWindow.Height > end.Y) top = cursor.Y - settingsWindow.Height - 10;
            settingsWindow.PlaceAt(
                Math.Max(start.X, Math.Min(left, Math.Max(start.X, end.X - settingsWindow.Width))),
                Math.Max(start.Y, Math.Min(top, Math.Max(start.Y, end.Y - settingsWindow.Height))));
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        private void OpenSettingsAtCursor()
        {
            Drawing.Point cursor = Forms.Cursor.Position;
            OpenSettings(cursor);
        }

        internal void SettingsClosed(PetSettings window)
        {
            if (ReferenceEquals(settingsWindow, window)) settingsWindow = null;
        }

        internal PetSkin ActiveSkin { get { return scene.Skin; } }
        internal bool LabelsVisible { get { return scene.ShowKeyLabels; } }
        internal bool MouseClickThrough { get { return clickThrough; } }
        internal double PetWidth { get { return Width; } }
        internal bool HideForSogou { get { return hideForSogou; } }
        internal LocalPeers Network { get { return peers; } }
        internal long KeyCount { get { return keyCount; } }
        internal long ClickCount { get { return clickCount; } }
        internal string Nickname { get { return nickname; } }

        internal bool TrySetNickname(string input, out string error)
        {
            string normalized;
            if (!PetName.TryNormalize(input, out normalized))
            {
                error = "名字最多 16 个字符，不能包含控制字符";
                return false;
            }
            string previous = nickname;
            nickname = normalized;
            if (!SaveSettings())
            {
                nickname = previous;
                error = "保存名字失败，请检查本地设置文件";
                return false;
            }
            error = null;
            peers.RefreshState();
            return true;
        }

        internal void SetHideForSogou(bool value)
        {
            hideForSogou = value;
            UpdateVisibility();
            SaveSettings();
        }

        internal void SetNetworking(bool value)
        {
            if (value)
            {
                peers.Start();
                peers.Tick(clock.Elapsed.TotalSeconds, scene.Skin, keyCount, clickCount, nickname);
            }
            else peers.Dispose();
        }

        internal void SetClickThrough(bool value)
        {
            clickThrough = value;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) ApplyWindowStyles(handle);
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetLabels(bool value)
        {
            scene.ShowKeyLabels = value;
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetSkin(PetSkin skin)
        {
            scene.Skin = skin;
            peers.RefreshState();
            RefreshTrayMenu();
            SaveSettings();
        }

        internal void SetWidth(double width)
        {
            Width = Math.Max(180, Math.Min(600, width));
            Height = Width * 308.0 / 400.0;
            EnsureVisible();
            SaveSettings();
        }

        private sealed class MenuAction
        {
            public readonly string Label;
            public readonly Action Action;
            public MenuAction(string label, Action action) { Label = label; Action = action; }
        }

        private MenuAction[] MenuActions()
        {
            return new MenuAction[] {
                new MenuAction("放大", delegate { SetWidth(Width + 40); }),
                new MenuAction("缩小", delegate { SetWidth(Width - 40); }),
                new MenuAction(scene.ShowKeyLabels ? "隐藏按键气泡" : "显示按键气泡", delegate { SetLabels(!scene.ShowKeyLabels); }),
                new MenuAction(clickThrough ? "关闭鼠标穿透" : "开启鼠标穿透（可从托盘关闭）", delegate { SetClickThrough(!clickThrough); }),
                new MenuAction("回到右下角", MoveToCorner),
                new MenuAction("退出", Close)
            };
        }

        private Forms.ContextMenuStrip BuildTrayMenu()
        {
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("桌宠设置…", null, delegate { OpenSettingsAtCursor(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            Forms.ToolStripMenuItem skins = new Forms.ToolStripMenuItem("切换外观");
            foreach (PetSkin skin in PetSkin.All)
            {
                PetSkin currentSkin = skin;
                Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(currentSkin.Name);
                item.Checked = scene.Skin.Id == currentSkin.Id;
                item.Click += delegate { SetSkin(currentSkin); };
                skins.DropDownItems.Add(item);
            }
            menu.Items.Add(skins);
            foreach (MenuAction entry in MenuActions())
            {
                MenuAction current = entry;
                menu.Items.Add(current.Label, null, delegate { current.Action(); });
            }
            return menu;
        }

        private void RefreshTrayMenu()
        {
            Forms.ContextMenuStrip old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = BuildTrayMenu();
            if (old != null) old.Dispose();
        }

        internal void MoveToCorner()
        {
            Rect area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - Width - 12);
            Top = Math.Max(area.Top, area.Bottom - Height - 12);
            SaveSettings();
        }

        private void EnsureVisible()
        {
            // Use the monitor work area containing the window center, expressed in WPF units.
            try
            {
                HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
                Matrix toPixels = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
                Matrix fromPixels = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
                Point center = toPixels.Transform(new Point(Left + Width / 2, Top + Height / 2));
                Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point((int)center.X, (int)center.Y));
                Drawing.Rectangle work = screen.WorkingArea;
                Point start = fromPixels.Transform(new Point(work.Left, work.Top));
                Point end = fromPixels.Transform(new Point(work.Right, work.Bottom));
                Left = Math.Max(start.X, Math.Min(Left, Math.Max(start.X, end.X - Width)));
                Top = Math.Max(start.Y, Math.Min(Top, Math.Max(start.Y, end.Y - Height)));
            }
            catch { MoveToCorner(); }
        }

        private void RestoreSettings()
        {
            MoveToCorner();
            try
            {
                if (!File.Exists(settingsPath)) return;
                string[] values = File.ReadAllLines(settingsPath);
                double left, top, width;
                bool labels, through;
                if (values.Length < 5 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left) ||
                    !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top) ||
                    !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out width) ||
                    !bool.TryParse(values[3], out labels) || !bool.TryParse(values[4], out through) ||
                    double.IsNaN(left) || double.IsInfinity(left) || double.IsNaN(top) || double.IsInfinity(top) ||
                    double.IsNaN(width) || double.IsInfinity(width)) return;
                Width = Math.Max(180, Math.Min(600, width));
                Height = Width * 308.0 / 400.0;
                Left = left;
                Top = top;
                scene.ShowKeyLabels = labels;
                clickThrough = through;
                scene.Skin = PetSkin.FromId(values.Length > 5 ? values[5] : null);
                bool hide;
                if (values.Length > 6 && bool.TryParse(values[6], out hide)) hideForSogou = hide;
                if (values.Length > 7) LocalPeers.TryCount(values[7], out keyCount);
                if (values.Length > 8) LocalPeers.TryCount(values[8], out clickCount);
                string loadedName;
                if (values.Length > 9 && PetName.TryNormalize(values[9], out loadedName) && loadedName == values[9])
                    nickname = loadedName;
            }
            catch { MoveToCorner(); }
        }

        private bool SaveSettings()
        {
            if (!ready) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                string pending = settingsPath + ".tmp";
                File.WriteAllLines(pending, new string[] {
                    Left.ToString("R", CultureInfo.InvariantCulture), Top.ToString("R", CultureInfo.InvariantCulture),
                    Width.ToString("R", CultureInfo.InvariantCulture), scene.ShowKeyLabels.ToString(), clickThrough.ToString(), scene.Skin.Id,
                    hideForSogou.ToString(), keyCount.ToString(CultureInfo.InvariantCulture), clickCount.ToString(CultureInfo.InvariantCulture), nickname });
                if (File.Exists(settingsPath)) File.Replace(pending, settingsPath, null);
                else File.Move(pending, settingsPath);
                countsDirty = false;
                return true;
            }
            catch { return false; }
        }
    }
}

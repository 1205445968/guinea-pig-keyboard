using System;
using System.Globalization;
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
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

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

        private readonly PetScene scene;
        private readonly Forms.NotifyIcon tray;
        private readonly string settingsPath;
        private DesktopInput input;
        private bool clickThrough;
        private bool ready;
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
            scene = new PetScene();
            scene.ShowKeyLabels = true;
            Viewbox view = new Viewbox();
            view.Width = double.NaN;
            view.Height = double.NaN;
            view.Stretch = Stretch.Uniform;
            view.Child = scene;
            Content = view;
            settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WheekKeys", "settings.txt");
            RestoreSettings();
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
            Loaded += delegate { ready = true; };
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
                ContextMenu menu = BuildWindowMenu();
                menu.PlacementTarget = this;
                menu.IsOpen = true;
                e.Handled = true;
            };
            Closed += delegate
            {
                SaveSettings();
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
            input = new DesktopInput(source, delegate(int scan) { scene.PressKey(scan); }, delegate(bool right) { scene.ClickMouse(right); });
        }

        private void ApplyWindowStyles(IntPtr handle)
        {
            int style = GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow;
            if (clickThrough) style |= WsExTransparent; else style &= ~WsExTransparent;
            SetWindowLong(handle, GwlExStyle, style);
        }

        private void SetClickThrough(bool value)
        {
            clickThrough = value;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) ApplyWindowStyles(handle);
            RefreshTrayMenu();
            SaveSettings();
        }

        private void SetLabels(bool value)
        {
            scene.ShowKeyLabels = value;
            RefreshTrayMenu();
            SaveSettings();
        }

        private void SetWidth(double width)
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

        private ContextMenu BuildWindowMenu()
        {
            ContextMenu menu = new ContextMenu();
            foreach (MenuAction entry in MenuActions())
            {
                MenuAction current = entry;
                MenuItem item = new MenuItem();
                item.Header = current.Label;
                item.Click += delegate { current.Action(); };
                menu.Items.Add(item);
            }
            return menu;
        }

        private Forms.ContextMenuStrip BuildTrayMenu()
        {
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
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

        private void MoveToCorner()
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
            }
            catch { MoveToCorner(); }
        }

        private void SaveSettings()
        {
            if (!ready) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllLines(settingsPath, new string[] {
                    Left.ToString("R", CultureInfo.InvariantCulture), Top.ToString("R", CultureInfo.InvariantCulture),
                    Width.ToString("R", CultureInfo.InvariantCulture), scene.ShowKeyLabels.ToString(), clickThrough.ToString() });
            }
            catch { }
        }
    }
}

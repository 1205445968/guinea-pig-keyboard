using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace WheekKeys
{
    internal sealed class FriendDesktop : IDisposable
    {
        private readonly Window owner;
        private readonly FrameworkElement viewport;
        private readonly Dictionary<string, FriendWindow> windows = new Dictionary<string, FriendWindow>();
        private bool disposed;

        public FriendDesktop(Window owner) : this(owner, null) { }

        public FriendDesktop(Window owner, FrameworkElement viewport)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            this.owner = owner;
            this.viewport = viewport;
            owner.IsVisibleChanged += OwnerVisibilityChanged;
        }

        public void Update(LocalPeers network, double now, bool clickThrough, bool showLabels)
        {
            if (disposed) return;
            foreach (string id in windows.Keys.Where(x => network == null || !network.Members.ContainsKey(x)).ToArray())
            { windows[id].Close(); windows.Remove(id); }
            if (network == null) return;
            foreach (var pair in network.Members)
            {
                FriendWindow view;
                if (windows.TryGetValue(pair.Key, out view) && view.IsClosed) windows.Remove(pair.Key);
                if (!windows.TryGetValue(pair.Key, out view))
                {
                    int slot = 0;
                    while (windows.Values.Any(w => w.Slot == slot)) slot++;
                    windows.Add(pair.Key, view = new FriendWindow(owner, viewport, slot, clickThrough));
                }
                view.Update(pair.Value, now, clickThrough, showLabels);
                view.SetOwnerVisible(owner.IsVisible);
            }
        }

        private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        { foreach (FriendWindow view in windows.Values) view.SetOwnerVisible(owner.IsVisible); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.IsVisibleChanged -= OwnerVisibilityChanged;
            foreach (FriendWindow view in windows.Values) view.Close();
            windows.Clear();
        }

        private sealed class FriendWindow
        {
            private const int ExStyle = -20, Transparent = 0x20, Tool = 0x80, NoActivate = 0x08000000;
            private const double LabelHeadroom = 14;
            [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="GetWindowLong")] private static extern int GetWindowLong(IntPtr hwnd, int index);
            [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint="SetWindowLong")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
            private readonly Window window;
            private readonly FrameworkElement viewport;
            private readonly Viewbox box;
            private readonly PixelPetScene scene;
            private readonly TextBlock label;
            private readonly TranslateTransform labelPosition = new TranslateTransform();
            private bool through;
            public readonly int Slot;
            public bool IsClosed { get; private set; }

            public FriendWindow(Window owner, FrameworkElement viewport, int slot, bool clickThrough)
            {
                this.viewport = viewport;
                Slot = slot;
                through = clickThrough;
                scene = new PixelPetScene();
                scene.ShowKeyLabels = true;
                box = new Viewbox { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, Child = scene };
                label = new TextBlock { Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(190, 50, 38, 26)),
                    FontSize = 12, Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center, RenderTransform = labelPosition };
                Grid panel = new Grid { Children = { box, label } };
                window = new Window { Owner = owner, Width = Math.Max(180, owner.Width), Height = Math.Max(150, owner.Width * 308.0 / 400.0 + LabelHeadroom),
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true,
                    ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Content = panel };
                scene.HeadPositionChanged += PositionLabel;
                label.SizeChanged += delegate { PositionLabel(); };
                MatchSize();
                window.SourceInitialized += delegate { ApplyStyles(); };
                window.Closed += delegate { IsClosed = true; };
                window.MouseLeftButtonDown += delegate(object s, System.Windows.Input.MouseButtonEventArgs e)
                {
                    if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
                    try { window.DragMove(); } catch (InvalidOperationException) { }
                    Clamp(WorkArea(window));
                    e.Handled = true;
                };
                PlaceNear(owner, slot);
            }

            private static Rect WorkArea(Window reference)
            {
                HwndSource source = PresentationSource.FromVisual(reference) as HwndSource;
                Matrix toPixels = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
                Matrix fromPixels = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
                Point center = toPixels.Transform(new Point(reference.Left + reference.Width / 2, reference.Top + reference.Height / 2));
                var area = Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y)).WorkingArea;
                return new Rect(fromPixels.Transform(new Point(area.Left, area.Top)), fromPixels.Transform(new Point(area.Right, area.Bottom)));
            }

            private void PlaceNear(Window owner, int slot)
            {
                Rect area = WorkArea(owner);
                double step = window.Width + 12;
                double left = owner.Left - step * (slot + 1), top = owner.Top - LabelHeadroom;
                if (left < area.Left) left = owner.Left + step * (slot + 1);
                if (left + window.Width > area.Right)
                {
                    int columns = Math.Max(1, (int)(area.Width / step));
                    left = area.Left + (slot % columns) * step;
                    top = owner.Top - (slot / columns + 1) * (window.Height + 10);
                }
                window.Left = left; window.Top = top;
                Clamp(area);
            }

            private void Clamp(Rect area)
            {
                window.Left = Math.Max(area.Left, Math.Min(window.Left, Math.Max(area.Left, area.Right - window.Width)));
                window.Top = Math.Max(area.Top, Math.Min(window.Top, Math.Max(area.Top, area.Bottom - window.Height)));
            }

            private void ApplyStyles()
            {
                HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
                if (source == null) return;
                int style = GetWindowLong(source.Handle, ExStyle) | Tool | NoActivate;
                SetWindowLong(source.Handle, ExStyle, through ? style | Transparent : style & ~Transparent);
            }

            public void Update(LocalPeers.Peer peer, double now, bool clickThrough, bool showLabels)
            {
                scene.Skin = peer.Skin;
                if (scene.ShowKeyLabels != showLabels) scene.ShowKeyLabels = showLabels;
                if (MatchSize()) Clamp(WorkArea(window));
                int count = 0; LocalPeers.PeerAction action;
                while (count++ < 2 && peer.TryTakeAction(now, out action))
                    if (window.Owner.IsVisible)
                    {
                        if (action.Kind == 'k') scene.PressKey(action.Scan);
                        else if (action.Kind == 'l' || action.Kind == 'r') scene.ClickMouse(action.Kind == 'r');
                    }
                string name = String.IsNullOrEmpty(peer.Nickname) ? peer.Skin.Name : peer.Nickname;
                label.Text = name;
                if (!peer.SupportsActivity) label.Text += " · 旧版 · 仅外观";
                else if (!peer.SharingActivity) label.Text += " · 已暂停动作";
                label.ToolTip = name + " · " + peer.Skin.Name;
                window.Title = "联机伙伴 · " + name;
                if (through != clickThrough) { through = clickThrough; ApplyStyles(); }
            }
            private bool MatchSize()
            {
                double width = viewport != null && viewport.ActualWidth > 0 ? viewport.ActualWidth : window.Owner.Width;
                double height = viewport != null && viewport.ActualHeight > 0 ? viewport.ActualHeight : width * 308.0 / 400.0;
                if (box.Width == width && box.Height == height) return false;
                box.Width = window.Width = width;
                box.Height = height;
                window.Height = height + LabelHeadroom;
                PositionLabel();
                return true;
            }

            private void PositionLabel()
            {
                double scale = Math.Min(box.Width / PixelPetScene.SceneWidth, box.Height / PixelPetScene.SceneHeight);
                Point head = scene.HeadTop;
                double x = (head.X - PixelPetScene.SceneWidth / 2) * scale;
                double maxX = Math.Max(0, (box.Width - label.ActualWidth) / 2);
                double sceneTop = LabelHeadroom + (box.Height - PixelPetScene.SceneHeight * scale) / 2;
                labelPosition.X = Math.Max(-maxX, Math.Min(maxX, x));
                labelPosition.Y = Math.Max(0, sceneTop + head.Y * scale - label.ActualHeight - 4);
            }

            public void SetOwnerVisible(bool visible) { if (visible && !window.IsVisible) window.Show(); else if (!visible && window.IsVisible) window.Hide(); }
            public void Close() { if (!IsClosed) window.Close(); }
        }
    }
}

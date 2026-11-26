using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WheekKeys
{
    internal sealed class WindowPlacement
    {
        private const int WmDpiChanged = 0x02E0, WmSettingChange = 0x001A, WmCancelMode = 0x001F, SpiSetWorkArea = 0x002F;
        private const double TaskbarSnap = 22, EdgeSnap = 14;
        private enum Edge { Left, Top, Right, Bottom }
        private readonly Window window;
        private readonly Action placed;
        private readonly HwndSourceHook messageHook;
        private HwndSource source;
        private Point? dragStart;
        private bool moving;
        private DispatcherOperation pendingPlacement;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        internal static void EnablePerMonitorDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
            catch (EntryPointNotFoundException) { }
            try { SetProcessDpiAwareness(2); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        internal bool IsDockedToTaskbar { get; set; }

        internal WindowPlacement(Window window, Action placed, Func<bool> doubleClick)
        {
            this.window = window;
            this.placed = placed;
            messageHook = WndProc;
            window.SourceInitialized += SourceInitialized;
            window.Closed += Closed;
            window.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2 && doubleClick != null && doubleClick())
                {
                    CancelDrag();
                    e.Handled = true;
                }
                else if (window.CaptureMouse()) dragStart = e.GetPosition(window);
            };
            window.MouseLeftButtonUp += delegate { CancelDrag(); };
            window.LostMouseCapture += delegate { dragStart = null; };
            window.MouseMove += Drag;
        }

        internal void StopDrag()
        {
            CancelDrag();
            if (moving && source != null && !source.IsDisposed)
                PostMessage(source.Handle, WmCancelMode, IntPtr.Zero, IntPtr.Zero);
        }

        private void CancelDrag()
        {
            if (!dragStart.HasValue) return;
            dragStart = null;
            if (window.IsMouseCaptured) window.ReleaseMouseCapture();
        }

        private void Drag(object sender, MouseEventArgs e)
        {
            if (!dragStart.HasValue || moving) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }
            Vector delta = e.GetPosition(window) - dragStart.Value;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            CancelDrag();
            moving = true;
            try
            {
                try { window.DragMove(); }
                catch (InvalidOperationException) { return; }
                Apply(true);
                e.Handled = true;
            }
            finally { moving = false; }
        }

        private void SourceInitialized(object sender, EventArgs e)
        {
            source = PresentationSource.FromVisual(window) as HwndSource;
            if (source == null) return;
            source.AddHook(messageHook);
            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
            Apply(false);
        }

        private void Closed(object sender, EventArgs e)
        {
            if (source == null) return;
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            if (pendingPlacement != null) pendingPlacement.Abort();
            if (!source.IsDisposed) source.RemoveHook(messageHook);
            source = null;
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (!window.Dispatcher.HasShutdownStarted)
                window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(QueuePlacement));
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmDpiChanged || (message == WmSettingChange && wParam.ToInt64() == SpiSetWorkArea))
                QueuePlacement();
            return IntPtr.Zero;
        }

        private void QueuePlacement()
        {
            if (source == null || pendingPlacement != null) return;
            pendingPlacement = window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                pendingPlacement = null;
                if (!moving) Apply(false);
            }));
        }

        internal void Apply(bool snap)
        {
            if (source == null || source.IsDisposed) return;
            Rect bounds, workArea;
            GetAreas(window, out bounds, out workArea);
            Apply(bounds, workArea, snap);
        }

        internal void Resize(double width, double height)
        {
            Rect bounds, workArea;
            GetAreas(window, out bounds, out workArea);
            window.Width = width;
            window.Height = height;
            if (source != null && !source.IsDisposed) Apply(bounds, workArea, false);
        }

        private void Apply(Rect bounds, Rect workArea, bool snap)
        {
            Rect original = new Rect(Finite(window.Left), Finite(window.Top), window.Width, window.Height);
            bool docked;
            Point target = Calculate(original, bounds, workArea, snap, IsDockedToTaskbar, out docked);
            bool changed = target != original.Location || docked != IsDockedToTaskbar;
            IsDockedToTaskbar = docked;
            if (window.Left != target.X) window.Left = target.X;
            if (window.Top != target.Y) window.Top = target.Y;
            if ((changed || snap) && placed != null) placed();
        }

        internal static Rect WorkArea(Window window)
        {
            Rect bounds, workArea;
            GetAreas(window, out bounds, out workArea);
            return workArea;
        }

        private static void GetAreas(Window window, out Rect bounds, out Rect workArea)
        {
            HwndSource hwnd = PresentationSource.FromVisual(window) as HwndSource;
            var target = hwnd == null ? null : hwnd.CompositionTarget;
            Matrix toDevice = target == null ? Matrix.Identity : target.TransformToDevice;
            Matrix fromDevice = target == null ? Matrix.Identity : target.TransformFromDevice;
            Point center = toDevice.Transform(new Point(Finite(window.Left) + window.Width / 2, Finite(window.Top) + window.Height / 2));
            Forms.Screen screen = Forms.Screen.FromPoint(new Drawing.Point(Pixel(center.X), Pixel(center.Y)));
            bounds = DipRect(screen.Bounds, fromDevice);
            workArea = DipRect(screen.WorkingArea, fromDevice);
        }

        private static int Pixel(double value)
        {
            return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(Finite(value))));
        }

        private static double Finite(double value) { return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value; }

        private static Rect DipRect(Drawing.Rectangle rect, Matrix fromDevice)
        {
            return new Rect(fromDevice.Transform(new Point(rect.Left, rect.Top)),
                fromDevice.Transform(new Point(rect.Right, rect.Bottom)));
        }

        internal static Point Calculate(Rect window, Rect screen, Rect work, bool snap, bool wasDocked, out bool docked)
        {
            double[] inset = { work.Left - screen.Left, work.Top - screen.Top, screen.Right - work.Right, screen.Bottom - work.Bottom };
            double[] distance = { window.Left - work.Left, window.Top - work.Top, work.Right - window.Right, work.Bottom - window.Bottom };
            Edge taskbar = Edge.Bottom;
            for (int i = 0; i < (int)Edge.Bottom; i++)
                if (inset[i] > inset[(int)taskbar]) taskbar = (Edge)i;

            docked = snap ? Math.Abs(distance[(int)taskbar]) <= TaskbarSnap : wasDocked;
            double left = window.Left, top = window.Top;
            if (docked && taskbar == Edge.Left) left = work.Left;
            else if (docked && taskbar == Edge.Right) left = work.Right - window.Width;
            else if (snap)
            {
                if (taskbar != Edge.Left && Math.Abs(distance[(int)Edge.Left]) <= EdgeSnap) left = work.Left;
                else if (taskbar != Edge.Right && Math.Abs(distance[(int)Edge.Right]) <= EdgeSnap) left = work.Right - window.Width;
            }
            if (docked && taskbar == Edge.Top) top = work.Top;
            else if (docked && taskbar == Edge.Bottom) top = work.Bottom - window.Height;
            else if (snap)
            {
                if (taskbar != Edge.Top && Math.Abs(distance[(int)Edge.Top]) <= EdgeSnap) top = work.Top;
                else if (taskbar != Edge.Bottom && Math.Abs(distance[(int)Edge.Bottom]) <= EdgeSnap) top = work.Bottom - window.Height;
            }
            return new Point(Math.Max(work.Left, Math.Min(left, work.Right - window.Width)),
                Math.Max(work.Top, Math.Min(top, work.Bottom - window.Height)));
        }
    }
}

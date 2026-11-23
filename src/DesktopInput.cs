using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WheekKeys
{
    internal sealed class DesktopInput : IDisposable
    {
        private const int WmInput = 0x00FF;
        private const uint RidInput = 0x10000003;
        private const uint RidevRemove = 0x00000001;
        private const uint RidevInputSink = 0x00000100;
        private const uint RimTypeMouse = 0;
        private const uint RimTypeKeyboard = 1;
        private const ushort RiKeyBreak = 0x0001;
        private const ushort RiKeyE0 = 0x0002;
        private const ushort RiKeyE1 = 0x0004;
        private const ushort RiMouseLeftButtonDown = 0x0001;
        private const ushort RiMouseRightButtonDown = 0x0004;
        private const ushort MouseMoveAbsolute = 0x0001;
        private const ushort MouseVirtualDesktop = 0x0002;

        private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(RawInputHeader));
        private static readonly uint InputSize = (uint)Marshal.SizeOf(typeof(RawInput));

        private readonly HwndSource source;
        private readonly Action<int> keyPressed;
        private readonly Action<bool> mousePressed;
        private readonly Action<double, double> mouseMoved;
        private readonly Dictionary<IntPtr, AbsolutePosition> absolutePositions = new Dictionary<IntPtr, AbsolutePosition>();
        private readonly HwndSourceHook hook;
        private bool disposed;

        public DesktopInput(HwndSource source, Action<int> keyPressed, Action<bool> mousePressed, Action<double, double> mouseMoved)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (keyPressed == null) throw new ArgumentNullException("keyPressed");
            if (mousePressed == null) throw new ArgumentNullException("mousePressed");
            if (mouseMoved == null) throw new ArgumentNullException("mouseMoved");
            if (source.IsDisposed || source.Handle == IntPtr.Zero)
                throw new ArgumentException("A live window source is required.", "source");

            this.source = source;
            this.keyPressed = keyPressed;
            this.mousePressed = mousePressed;
            this.mouseMoved = mouseMoved;
            hook = WndProc;
            source.AddHook(hook);
            RawInputDevice[] devices = CreateDevices(RidevInputSink, source.Handle);
            if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice))))
            {
                int error = Marshal.GetLastWin32Error();
                source.RemoveHook(hook);
                throw new Win32Exception(error);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RawInputDevice[] devices = CreateDevices(RidevRemove, IntPtr.Zero);
            bool removed = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
            int error = removed ? 0 : Marshal.GetLastWin32Error();
            if (!source.IsDisposed) source.RemoveHook(hook);
            if (!removed) throw new Win32Exception(error);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmInput || disposed) return IntPtr.Zero;

            uint size = InputSize;
            RawInput input;
            uint received = GetRawInputData(lParam, RidInput, out input, ref size, HeaderSize);
            if (received == UInt32.MaxValue || received < HeaderSize) return IntPtr.Zero;
            if (input.Header.Type == RimTypeKeyboard && received >= HeaderSize + 16)
            {
                RawKeyboard keyboard = input.Data.Keyboard;
                if ((keyboard.Flags & RiKeyBreak) == 0 && keyboard.MakeCode != 0 &&
                    keyboard.MakeCode != 0xFF && keyboard.VKey != 0xFF)
                {
                    int scan = keyboard.MakeCode;
                    if ((keyboard.Flags & (RiKeyE0 | RiKeyE1)) != 0) scan |= 0x100;
                    keyPressed(scan);
                }
            }
            else if (input.Header.Type == RimTypeMouse && received >= HeaderSize + 24)
            {
                RawMouse mouse = input.Data.Mouse;
                ProcessMouse(input.Header.Device, mouse);
                ushort buttons = mouse.ButtonFlags;
                if ((buttons & RiMouseLeftButtonDown) != 0) mousePressed(false);
                if ((buttons & RiMouseRightButtonDown) != 0) mousePressed(true);
            }
            return IntPtr.Zero;
        }

        private void ProcessMouse(IntPtr device, RawMouse mouse)
        {
            if ((mouse.Flags & MouseMoveAbsolute) == 0)
            {
                absolutePositions.Remove(device);
                if (mouse.LastX != 0 || mouse.LastY != 0) mouseMoved(mouse.LastX, mouse.LastY);
                return;
            }

            bool virtualDesktop = (mouse.Flags & MouseVirtualDesktop) != 0;
            int left = virtualDesktop ? GetSystemMetrics(76) : 0;
            int top = virtualDesktop ? GetSystemMetrics(77) : 0;
            int width = GetSystemMetrics(virtualDesktop ? 78 : 0);
            int height = GetSystemMetrics(virtualDesktop ? 79 : 1);
            if (width <= 0 || height <= 0) { absolutePositions.Remove(device); return; }
            AbsolutePosition previous;
            AbsolutePosition current = new AbsolutePosition { X = mouse.LastX, Y = mouse.LastY, Left = left, Top = top, Width = width, Height = height, VirtualDesktop = virtualDesktop };
            if (absolutePositions.TryGetValue(device, out previous) && previous.Left == left && previous.Top == top && previous.Width == width && previous.Height == height && previous.VirtualDesktop == virtualDesktop)
            {
                double dx = ((double)current.X - previous.X) * (width - 1) / 65535.0;
                double dy = ((double)current.Y - previous.Y) * (height - 1) / 65535.0;
                if (dx != 0 || dy != 0) mouseMoved(dx, dy);
            }
            absolutePositions[device] = current;
        }

        private struct AbsolutePosition
        {
            public int X, Y, Left, Top, Width, Height;
            public bool VirtualDesktop;
        }

        private static RawInputDevice[] CreateDevices(uint flags, IntPtr target)
        {
            return new[]
            {
                new RawInputDevice { UsagePage = 1, Usage = 6, Flags = flags, Target = target },
                new RawInputDevice { UsagePage = 1, Usage = 2, Flags = flags, Target = target }
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInput
        {
            public RawInputHeader Header;
            public RawInputData Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct RawInputData
        {
            [FieldOffset(0)] public RawKeyboard Keyboard;
            [FieldOffset(0)] public RawMouse Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawKeyboard
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct RawMouse
        {
            [FieldOffset(0)] public ushort Flags;
            [FieldOffset(4)] public ushort ButtonFlags;
            [FieldOffset(12)] public int LastX;
            [FieldOffset(16)] public int LastY;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RawInput data, ref uint size, uint headerSize);
    }
}

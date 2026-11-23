using System;
using System.ComponentModel;
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

        private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(RawInputHeader));
        private static readonly uint InputSize = (uint)Marshal.SizeOf(typeof(RawInput));

        private readonly HwndSource source;
        private readonly Action<int> keyPressed;
        private readonly Action<bool> mousePressed;
        private readonly HwndSourceHook hook;
        private bool disposed;

        public DesktopInput(HwndSource source, Action<int> keyPressed, Action<bool> mousePressed)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (keyPressed == null) throw new ArgumentNullException("keyPressed");
            if (mousePressed == null) throw new ArgumentNullException("mousePressed");
            if (source.IsDisposed || source.Handle == IntPtr.Zero)
                throw new ArgumentException("A live window source is required.", "source");

            this.source = source;
            this.keyPressed = keyPressed;
            this.mousePressed = mousePressed;
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
                ushort buttons = input.Data.Mouse.ButtonFlags;
                if ((buttons & RiMouseLeftButtonDown) != 0) mousePressed(false);
                if ((buttons & RiMouseRightButtonDown) != 0) mousePressed(true);
            }
            return IntPtr.Zero;
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
            [FieldOffset(4)] public ushort ButtonFlags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RawInput data, ref uint size, uint headerSize);
    }
}

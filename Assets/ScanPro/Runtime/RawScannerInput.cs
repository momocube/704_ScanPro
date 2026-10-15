using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScanPro704
{
    public sealed class RawScannerInput : IDisposable
    {
        public readonly ConcurrentQueue<ScannerFrame> Frames = new ConcurrentQueue<ScannerFrame>();
        public string Status { get; private set; } = "尚未啟動";
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        Thread thread;
        IntPtr window;
        WindowProc procedure;
        readonly uint processId = (uint)Process.GetCurrentProcess().Id;
        readonly ScannerFramer framer = new ScannerFramer();
        volatile bool stopping;
        public RawScannerInput()
        {
            framer.Frame += frame => Frames.Enqueue(frame);
            thread = new Thread(Run) { IsBackground = true, Name = "704 Scanner Raw Input" };
            thread.Start();
        }
        void Run()
        {
            var className = "ScanPro704.Scanner." + Guid.NewGuid().ToString("N");
            try
            {
                procedure = HandleMessage;
                var klass = new WindowClass { procedure = procedure, instance = GetModuleHandle(null), name = className };
                if (RegisterClass(ref klass) == 0) throw new InvalidOperationException("RegisterClass " + Marshal.GetLastWin32Error());
                window = CreateWindowEx(0, className, "704 scanner ingress", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, klass.instance, IntPtr.Zero);
                if (window == IntPtr.Zero) throw new InvalidOperationException("CreateWindow " + Marshal.GetLastWin32Error());
                var devices = new[] { new RawDevice { page = 1, usage = 6, flags = 0x100, target = window } };
                if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawDevice>())) throw new InvalidOperationException("RegisterRawInputDevices " + Marshal.GetLastWin32Error());
                Status = "USB 鍵盤分流已啟動（僅本遊戲／Unity 視窗在前景時）";
                if (stopping) PostMessage(window, 0x10, UIntPtr.Zero, IntPtr.Zero);
                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
            }
            catch (Exception e) { Status = "掃碼輸入失敗：" + e.Message; }
            finally
            {
                RegisterRawInputDevices(new[] { new RawDevice { page = 1, usage = 6, flags = 1 } }, 1, (uint)Marshal.SizeOf<RawDevice>());
                if (window != IntPtr.Zero) DestroyWindow(window);
                UnregisterClass(className, GetModuleHandle(null)); window = IntPtr.Zero;
            }
        }
        IntPtr HandleMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
        {
            if (message == 0x10) { DestroyWindow(hwnd); return IntPtr.Zero; }
            if (message == 2) { PostQuitMessage(0); return IntPtr.Zero; }
            if (message == 0xff)
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
                if (foreground != processId) { framer.Reset(); return DefWindowProc(hwnd, message, wParam, lParam); }
                uint size = 0;
                var headerSize = (uint)Marshal.SizeOf<RawHeader>();
                GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref size, headerSize);
                if (size >= headerSize + Marshal.SizeOf<RawKeyboard>())
                {
                    var data = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (GetRawInputData(lParam, 0x10000003, data, ref size, headerSize) == size)
                        {
                            var header = Marshal.PtrToStructure<RawHeader>(data);
                            var keyboard = Marshal.PtrToStructure<RawKeyboard>(IntPtr.Add(data, (int)headerSize));
                            if (header.type == 1 && (keyboard.flags & 1) == 0)
                            {
                                var c = Character(keyboard.virtualKey);
                                if (c != '\0') framer.Push(header.device.ToString("X"), c, Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(data); }
                }
            }
            return DefWindowProc(hwnd, message, wParam, lParam);
        }
        static char Character(ushort key)
        {
            if (key >= 65 && key <= 90) return (char)key;
            if (key >= 48 && key <= 57) return (char)key;
            if (key >= 96 && key <= 105) return (char)('0' + key - 96);
            if (key == 191 || key == 111) return '/';
            if (key == 13) return '\n';
            if (key == 8) return '\b';
            return '\0';
        }
        public void Dispose()
        {
            stopping = true;
            if (window != IntPtr.Zero) PostMessage(window, 0x10, UIntPtr.Zero, IntPtr.Zero);
            if (thread != null && thread.IsAlive) thread.Join(1000);
            thread = null;
        }
        delegate IntPtr WindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WindowClass
        { public uint style; public WindowProc procedure; public int classExtra, windowExtra; public IntPtr instance, icon, cursor, background; public string menu, name; }
        [StructLayout(LayoutKind.Sequential)] struct RawDevice { public ushort page, usage; public uint flags; public IntPtr target; }
        [StructLayout(LayoutKind.Sequential)] struct RawHeader { public uint type, size; public IntPtr device, wParam; }
        [StructLayout(LayoutKind.Sequential)] struct RawKeyboard { public ushort makeCode, flags, reserved, virtualKey; public uint message, extra; }
        [StructLayout(LayoutKind.Sequential)] struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct Message { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public Point point; public uint privateData; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClass(ref WindowClass klass);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClass(string name, IntPtr instance);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowEx(uint exStyle, string klass, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices([In] RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetMessage(out Message message, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
#else
        public RawScannerInput() { Status = "此平台不支援 Windows Raw Input；可使用模擬掃碼。"; }
        public void Dispose() { }
#endif
    }
}

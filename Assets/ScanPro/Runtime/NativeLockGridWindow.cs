#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScanPro704
{
    /// <summary>
    /// Owns the lock-grid HWND and message pump on a dedicated thread. It only receives immutable
    /// snapshots and returns cell clicks; Unity objects and game state stay on the Unity thread.
    /// </summary>
    public sealed class NativeLockGridWindow : IDisposable
    {
        public struct Cell
        {
            public readonly int X;
            public readonly int Y;
            public Cell(int x, int y) { X = x; Y = y; }
        }

        public sealed class GridSnapshot
        {
            public readonly int Width;
            public readonly int Height;
            public readonly bool[] Allowed;
            public readonly bool[] Locked;

            public GridSnapshot(int width, int height, bool[] allowed, bool[] locked)
            {
                Width = Math.Max(1, width);
                Height = Math.Max(1, height);
                var length = Width * Height;
                Allowed = allowed != null && allowed.Length == length ? allowed : new bool[length];
                Locked = locked != null && locked.Length == length ? locked : new bool[length];
            }
        }

        const int GwlStyle = -16;
        const long WsChild = 0x40000000L;
        const long WsPopup = unchecked((long)0x80000000);
        const long WsVisible = 0x10000000L;
        const long WsCaption = 0x00C00000L;
        const long WsThickFrame = 0x00040000L;
        const uint WsOverlappedWindow = 0x00CF0000;
        const uint WmPaint = 0x000F;
        const uint WmSize = 0x0005;
        const uint WmClose = 0x0010;
        const uint WmDestroy = 0x0002;
        const uint WmEraseBkgnd = 0x0014;
        const uint WmGetMinMaxInfo = 0x0024;
        const uint WmLButtonDown = 0x0201;
        const uint WmAppRefresh = 0x8001;
        const uint WmAppStop = 0x8002;
        const int SwHide = 0;
        const int SwShow = 5;
        const int SwRestore = 9;
        const uint MonitorDefaultToNearest = 2;
        static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new IntPtr(-4);
        const int MkLButton = 1;
        const int SwpNoSize = 0x0001;
        const int SwpNoMove = 0x0002;
        const int SwpNoZOrder = 0x0004;
        const int SwpNoActivate = 0x0010;
        const int SwpFrameChanged = 0x0020;
        const uint Srccopy = 0x00CC0020;
        const uint DtSingleLine = 0x20;
        const uint DtVCenter = 0x04;
        const uint DtLeft = 0x00;
        const int Transparent = 1;
        const int DefaultCharSet = 1;
        const int CleartypeQuality = 5;
        const int FontNormal = 400;
        const string WindowClass = "ScanPro704.RuntimeLockGridWindow";

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WindowClassEx
        {
            public uint Size;
            public uint Style;
            public WindowProcedure Procedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string MenuName;
            public string ClassName;
            public IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativePoint
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PaintStruct
        {
            public IntPtr DeviceContext;
            public int Erase;
            public NativeRect Paint;
            public int Restore;
            public int IncrementalUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Message
        {
            public IntPtr Window;
            public uint Id;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public NativePoint Point;
            public uint Private;
        }

        static readonly WindowProcedure Procedure = WindowProc;
        static NativeLockGridWindow activeWindow;

        readonly ConcurrentQueue<Cell> clicks = new ConcurrentQueue<Cell>();
        readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        Thread thread;
        IntPtr window;
        IntPtr operatorWindow;
        GridSnapshot snapshot;
        string startupError;
        int disposed;
        IntPtr backBufferMemory, backBufferBitmap, backBufferOriginalBitmap;
        int backBufferWidth, backBufferHeight;
        bool backBufferDirty = true;
        long redrawMessageCount, bufferRenderCount;

        public IntPtr Handle => Interlocked.CompareExchange(ref window, IntPtr.Zero, IntPtr.Zero);
        public IntPtr OperatorWindowHandle => operatorWindow;
        public bool IsReady => Handle != IntPtr.Zero && IsWindow(Handle);
        public bool HasResizableTopLevelStyle => HasMovableResizableStyle(Handle);
        public long RedrawMessageCount => Interlocked.Read(ref redrawMessageCount);
        public long BufferRenderCount => Interlocked.Read(ref bufferRenderCount);

        public void StartWindow(IntPtr controlWindowHandle)
        {
            if (thread != null) throw new InvalidOperationException("Lock-grid window has already been started.");
            if (controlWindowHandle == IntPtr.Zero) throw new InvalidOperationException("Unity operator window handle is unavailable.");
            MakeOperatorWindowResizable(controlWindowHandle);
            operatorWindow = controlWindowHandle;
            activeWindow = this;
            thread = new Thread(WindowThreadMain) { IsBackground = true, Name = "ScanPro704 Lock Grid Window" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.Wait(5000)) throw new TimeoutException("Timed out while creating the lock-grid window.");
            if (!string.IsNullOrEmpty(startupError)) throw new InvalidOperationException(startupError);
            if (!IsReady) throw new InvalidOperationException("Windows did not create the lock-grid window.");
        }

        public bool Publish(GridSnapshot next)
        {
            if (next == null) return false;
            var current = Volatile.Read(ref snapshot);
            if (SnapshotsMatch(current, next)) return false;
            Interlocked.Exchange(ref snapshot, next);
            var handle = Handle;
            if (handle != IntPtr.Zero && PostMessage(handle, WmAppRefresh, IntPtr.Zero, IntPtr.Zero))
                Interlocked.Increment(ref redrawMessageCount);
            return true;
        }

        static bool SnapshotsMatch(GridSnapshot left, GridSnapshot right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Width != right.Width || left.Height != right.Height) return false;
            for (var i = 0; i < left.Allowed.Length; i++)
                if (left.Allowed[i] != right.Allowed[i] || left.Locked[i] != right.Locked[i]) return false;
            return true;
        }

        public bool TryTakeClick(out Cell cell) => clicks.TryDequeue(out cell);

        public bool SnapshotShowsLock(int x, int y)
        {
            var current = Volatile.Read(ref snapshot);
            if (current == null || x < 0 || y < 0 || x >= current.Width || y >= current.Height) return false;
            return current.Locked[y * current.Width + x];
        }

        public void Show()
        {
            var handle = Handle;
            if (handle == IntPtr.Zero) return;
            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }

        public void Hide()
        {
            var handle = Handle;
            if (handle != IntPtr.Zero) ShowWindow(handle, SwHide);
        }

        public bool PostCellClickForTesting(int x, int y)
        {
            var handle = Handle;
            var current = Volatile.Read(ref snapshot);
            if (handle == IntPtr.Zero || current == null || x < 0 || y < 0 || x >= current.Width || y >= current.Height)
                return false;
            if (!GetClientRect(handle, out var client)) return false;
            var board = CalculateBoardRect(client.Right - client.Left, client.Bottom - client.Top, current.Width, current.Height);
            var cellWidth = board.Width / (double)current.Width;
            var cellHeight = board.Height / (double)current.Height;
            var clickX = board.Left + (int)Math.Floor((x + .5) * cellWidth);
            var clickY = board.Top + (int)Math.Floor((y + .5) * cellHeight);
            return PostMessage(handle, WmLButtonDown, new IntPtr(MkLButton), MakeLParam(clickX, clickY));
        }

        public static bool HasMovableResizableStyle(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !IsWindow(handle)) return false;
            var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
            return (style & WsChild) == 0 && (style & WsCaption) != 0 && (style & WsThickFrame) != 0;
        }

        static void MakeOperatorWindowResizable(IntPtr handle)
        {
            var style = GetWindowLongPtr(handle, GwlStyle).ToInt64();
            var updated = (style & ~(WsChild | WsPopup)) | WsOverlappedWindow | WsVisible;
            if (updated != style) SetWindowLongPtr(handle, GwlStyle, new IntPtr(updated));
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            if (!HasMovableResizableStyle(handle))
                throw new InvalidOperationException("Unity operator window could not be changed to a movable, resizable top-level window.");
        }

        /// <summary>Hide the system pointer only while it is over a non-primary display.</summary>
        public static bool IsCursorOnDifferentDisplayThanMainWindow(IntPtr main)
        {
            if (main == IntPtr.Zero || !GetCursorPos(out var point)) return false;
            var mainMonitor = MonitorFromWindow(main, MonitorDefaultToNearest);
            var cursorMonitor = MonitorFromPoint(point, MonitorDefaultToNearest);
            return mainMonitor != IntPtr.Zero && cursorMonitor != IntPtr.Zero && mainMonitor != cursorMonitor;
        }

        void WindowThreadMain()
        {
            try
            {
                if (SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2) == IntPtr.Zero)
                    throw new InvalidOperationException("SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2) failed: " + Marshal.GetLastWin32Error());

                var instance = GetModuleHandle(null);
                var windowClass = new WindowClassEx
                {
                    Size = (uint)Marshal.SizeOf(typeof(WindowClassEx)),
                    Procedure = Procedure,
                    Instance = instance,
                    Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)),
                    ClassName = WindowClass
                };
                if (RegisterClassEx(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
                    throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());

                GetInitialBounds(operatorWindow, out var x, out var y, out var width, out var height);
                var handle = CreateWindowEx(0, WindowClass, "704 掃碼 Pro｜觸碰鎖格",
                    WsOverlappedWindow | 0x10000000, x, y, width, height,
                    IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

                Interlocked.Exchange(ref window, handle);
                ShowWindow(handle, SwShow);
                UpdateWindow(handle);
                ready.Set();
                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                Interlocked.Exchange(ref window, IntPtr.Zero);
                UnregisterClass(WindowClass, instance);
            }
            catch (Exception exception)
            {
                startupError = exception.ToString();
                ready.Set();
            }
        }

        static void GetInitialBounds(IntPtr main, out int x, out int y, out int width, out int height)
        {
            x = 960; y = 32; width = 920; height = 900;
            if (main == IntPtr.Zero || !GetWindowRect(main, out var mainRect)) return;
            var monitor = MonitorFromWindow(main, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
            var workWidth = Math.Max(1, info.Work.Right - info.Work.Left);
            var workHeight = Math.Max(1, info.Work.Bottom - info.Work.Top);
            width = Math.Max(720, Math.Min(1120, workWidth / 2));
            height = Math.Max(560, Math.Min(1120, workHeight - 64));
            x = mainRect.Right + 16;
            y = Math.Max(info.Work.Top + 16, mainRect.Top);
            if (x + width > info.Work.Right - 8)
            {
                x = Math.Max(info.Work.Left + 8, info.Work.Right - width - 8);
                y = mainRect.Bottom + 16;
            }
            if (y + height > info.Work.Bottom - 8)
                y = Math.Max(info.Work.Top + 8, info.Work.Bottom - height - 8);
        }

        static IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
        {
            var owner = Volatile.Read(ref activeWindow);
            if (owner == null) return DefWindowProc(handle, message, wParam, lParam);
            switch (message)
            {
                case WmPaint:
                    owner.Paint(handle);
                    return IntPtr.Zero;
                case WmEraseBkgnd:
                    return new IntPtr(1);
                case WmGetMinMaxInfo:
                    var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    limits.MinTrackSize.X = 720;
                    limits.MinTrackSize.Y = 560;
                    Marshal.StructureToPtr(limits, lParam, false);
                    return IntPtr.Zero;
                case WmLButtonDown:
                    owner.OnCellClick(handle, SignedLowWord(lParam), SignedHighWord(lParam));
                    return IntPtr.Zero;
                case WmAppRefresh:
                    owner.backBufferDirty = true;
                    InvalidateRect(handle, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case WmSize:
                    owner.backBufferDirty = true;
                    InvalidateRect(handle, IntPtr.Zero, false);
                    return IntPtr.Zero;
                case WmClose:
                    ShowWindow(handle, SwHide);
                    return IntPtr.Zero;
                case WmAppStop:
                    DestroyWindow(handle);
                    return IntPtr.Zero;
                case WmDestroy:
                    owner.ReleaseBackBuffer();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                default:
                    return DefWindowProc(handle, message, wParam, lParam);
            }
        }

        void OnCellClick(IntPtr handle, int x, int y)
        {
            var current = Volatile.Read(ref snapshot);
            if (current == null || !GetClientRect(handle, out var client)) return;
            var board = CalculateBoardRect(client.Right - client.Left, client.Bottom - client.Top, current.Width, current.Height);
            if (x < board.Left || x >= board.Right || y < board.Top || y >= board.Bottom) return;
            var cellX = (int)((x - board.Left) * (double)current.Width / board.Width);
            var cellY = (int)((y - board.Top) * (double)current.Height / board.Height);
            if (cellX >= 0 && cellY >= 0 && cellX < current.Width && cellY < current.Height && current.Allowed[cellY * current.Width + cellX])
                clicks.Enqueue(new Cell(cellX, cellY));
        }

        void Paint(IntPtr handle)
        {
            var paint = BeginPaint(handle, out var state);
            if (paint == IntPtr.Zero) return;
            try
            {
                if (!GetClientRect(handle, out var client)) return;
                var width = Math.Max(1, client.Right - client.Left);
                var height = Math.Max(1, client.Bottom - client.Top);
                var hasBuffer = EnsureBackBuffer(paint, width, height);
                if (!hasBuffer || backBufferDirty)
                {
                    RenderGrid(hasBuffer ? backBufferMemory : paint, ref client);
                    if (hasBuffer)
                    {
                        backBufferDirty = false;
                        Interlocked.Increment(ref bufferRenderCount);
                    }
                }
                if (hasBuffer) BitBlt(paint, 0, 0, width, height, backBufferMemory, 0, 0, Srccopy);
            }
            finally
            {
                EndPaint(handle, ref state);
            }
        }

        bool EnsureBackBuffer(IntPtr reference, int width, int height)
        {
            if (backBufferMemory != IntPtr.Zero && backBufferBitmap != IntPtr.Zero &&
                backBufferWidth == width && backBufferHeight == height) return true;
            ReleaseBackBuffer();
            backBufferMemory = CreateCompatibleDC(reference);
            if (backBufferMemory == IntPtr.Zero) return false;
            backBufferBitmap = CreateCompatibleBitmap(reference, width, height);
            if (backBufferBitmap == IntPtr.Zero)
            {
                ReleaseBackBuffer();
                return false;
            }
            backBufferOriginalBitmap = SelectObject(backBufferMemory, backBufferBitmap);
            if (backBufferOriginalBitmap == IntPtr.Zero || backBufferOriginalBitmap == new IntPtr(-1))
            {
                backBufferOriginalBitmap = IntPtr.Zero;
                ReleaseBackBuffer();
                return false;
            }
            backBufferWidth = width;
            backBufferHeight = height;
            backBufferDirty = true;
            return true;
        }

        void ReleaseBackBuffer()
        {
            if (backBufferMemory != IntPtr.Zero && backBufferOriginalBitmap != IntPtr.Zero)
                SelectObject(backBufferMemory, backBufferOriginalBitmap);
            if (backBufferBitmap != IntPtr.Zero) DeleteObject(backBufferBitmap);
            if (backBufferMemory != IntPtr.Zero) DeleteDC(backBufferMemory);
            backBufferMemory = IntPtr.Zero;
            backBufferBitmap = IntPtr.Zero;
            backBufferOriginalBitmap = IntPtr.Zero;
            backBufferWidth = 0;
            backBufferHeight = 0;
            backBufferDirty = true;
        }

        void RenderGrid(IntPtr drawing, ref NativeRect client)
        {
                var current = Volatile.Read(ref snapshot);
                var background = CreateSolidBrush(Rgb(18, 24, 32));
                var freeBrush = CreateSolidBrush(Rgb(37, 83, 106));
                var lockedBrush = CreateSolidBrush(Rgb(226, 69, 57));
                var blockedBrush = CreateSolidBrush(Rgb(9, 12, 17));
                try
                {
                    FillRect(drawing, ref client, background);
                    SetBkMode(drawing, Transparent);
                    DrawText(drawing, "觸碰鎖格", 20, 22, Math.Max(1, client.Right - 40), 34, 24, true);
                    DrawText(drawing, "紅色：已鎖定　藍綠色：可互動　深色：場域固定禁區", 20, 58,
                        Math.Max(1, client.Right - 40), 26, 16, false);
                    var lockedCount = 0;
                    if (current != null)
                        for (var i = 0; i < current.Locked.Length; i++) if (current.Locked[i]) lockedCount++;
                    DrawText(drawing, $"{(current == null ? 0 : current.Width)} × {(current == null ? 0 : current.Height)} 格　｜　已鎖定 {lockedCount} 格　｜　點擊格子切換鎖定", 20, 86,
                        Math.Max(1, client.Right - 40), 25, 15, false);

                    if (current != null)
                    {
                        var board = CalculateBoardRect(client.Right - client.Left, client.Bottom - client.Top, current.Width, current.Height);
                        var cellWidth = board.Width / (double)current.Width;
                        var cellHeight = board.Height / (double)current.Height;
                        for (var row = 0; row < current.Height; row++)
                        for (var column = 0; column < current.Width; column++)
                        {
                            var index = row * current.Width + column;
                            var cell = new NativeRect
                            {
                                Left = board.Left + (int)Math.Floor(column * cellWidth),
                                Top = board.Top + (int)Math.Floor(row * cellHeight),
                                Right = board.Left + (int)Math.Floor((column + 1) * cellWidth) - 1,
                                Bottom = board.Top + (int)Math.Floor((row + 1) * cellHeight) - 1
                            };
                            var brush = !current.Allowed[index] ? blockedBrush : current.Locked[index] ? lockedBrush : freeBrush;
                            FillRect(drawing, ref cell, brush);
                        }
                        var oldPen = SelectObject(drawing, CreatePen(0, 1, Rgb(14, 22, 29)));
                        for (var column = 0; column <= current.Width; column++)
                        {
                            var px = board.Left + (int)Math.Round(column * cellWidth);
                            MoveToEx(drawing, px, board.Top, IntPtr.Zero);
                            LineTo(drawing, px, board.Bottom);
                        }
                        for (var row = 0; row <= current.Height; row++)
                        {
                            var py = board.Top + (int)Math.Round(row * cellHeight);
                            MoveToEx(drawing, board.Left, py, IntPtr.Zero);
                            LineTo(drawing, board.Right, py);
                        }
                        var pen = SelectObject(drawing, oldPen);
                        DeleteObject(pen);
                    }
                    DrawText(drawing, "鎖定位置的觸碰不會進入遊戲判定；關閉此視窗後可由後台控制視窗重新開啟。", 20,
                        client.Bottom - 36, Math.Max(1, client.Right - 40), 24, 14, false);
                }
                finally
                {
                    DeleteObject(background);
                    DeleteObject(freeBrush);
                    DeleteObject(lockedBrush);
                    DeleteObject(blockedBrush);
                }
        }

        static void DrawText(IntPtr deviceContext, string text, int x, int y, int width, int height, int fontSize, bool bold)
        {
            var font = CreateFont(-fontSize, 0, 0, 0, bold ? 700 : FontNormal, 0, 0, 0, DefaultCharSet,
                0, 0, CleartypeQuality, 0, "Microsoft JhengHei UI");
            var previous = SelectObject(deviceContext, font);
            SetTextColor(deviceContext, Rgb(224, 232, 239));
            var rect = new NativeRect { Left = x, Top = y, Right = x + width, Bottom = y + height };
            DrawTextW(deviceContext, text, text.Length, ref rect, DtLeft | DtVCenter | DtSingleLine);
            SelectObject(deviceContext, previous);
            DeleteObject(font);
        }

        static NativeRect CalculateBoardRect(int clientWidth, int clientHeight, int columns, int rows)
        {
            const int margin = 20;
            const int header = 124;
            const int footer = 46;
            var availableWidth = Math.Max(1, clientWidth - margin * 2);
            var availableHeight = Math.Max(1, clientHeight - header - footer);
            var scale = Math.Min(availableWidth / (double)Math.Max(1, columns), availableHeight / (double)Math.Max(1, rows));
            var width = Math.Max(1, (int)Math.Floor(columns * scale));
            var height = Math.Max(1, (int)Math.Floor(rows * scale));
            var left = margin + (availableWidth - width) / 2;
            var top = header + (availableHeight - height) / 2;
            return new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height };
        }

        static IntPtr MakeLParam(int x, int y)
        {
            var value = ((long)(ushort)y << 16) | (ushort)x;
            return new IntPtr(value);
        }

        static int SignedLowWord(IntPtr value) => unchecked((short)(value.ToInt64() & 0xffff));
        static int SignedHighWord(IntPtr value) => unchecked((short)((value.ToInt64() >> 16) & 0xffff));
        static uint Rgb(byte red, byte green, byte blue) => (uint)(red | (green << 8) | (blue << 16));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            var handle = Handle;
            if (handle != IntPtr.Zero) PostMessage(handle, WmAppStop, IntPtr.Zero, IntPtr.Zero);
            if (thread != null && thread.IsAlive) thread.Join(3000);
            if (ReferenceEquals(Volatile.Read(ref activeWindow), this)) activeWindow = null;
            ready.Dispose();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string moduleName);
        [DllImport("user32.dll", EntryPoint = "SetThreadDpiAwarenessContext", SetLastError = true)]
        static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WindowClassEx windowClass);
        [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)] static extern bool UnregisterClass(string className, IntPtr instance);
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode)] static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll", EntryPoint = "TranslateMessage")] static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll")] static extern void PostQuitMessage(int exitCode);
        [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode)] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "ShowWindow")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "UpdateWindow")] static extern bool UpdateWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "DestroyWindow")] static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "InvalidateRect")] static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);
        [DllImport("user32.dll", EntryPoint = "BeginPaint")] static extern IntPtr BeginPaint(IntPtr window, out PaintStruct paint);
        [DllImport("user32.dll", EntryPoint = "EndPaint")] static extern bool EndPaint(IntPtr window, ref PaintStruct paint);
        [DllImport("user32.dll", EntryPoint = "GetClientRect")] static extern bool GetClientRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowRect")] static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll", EntryPoint = "IsWindow")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowPos")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode)] static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);
        [DllImport("user32.dll", EntryPoint = "FillRect")] static extern int FillRect(IntPtr deviceContext, ref NativeRect rect, IntPtr brush);
        [DllImport("gdi32.dll", EntryPoint = "CreateSolidBrush")] static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll", EntryPoint = "CreatePen")] static extern IntPtr CreatePen(int style, int width, uint color);
        [DllImport("gdi32.dll", EntryPoint = "SelectObject")] static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr obj);
        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")] static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);
        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")] static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);
        [DllImport("gdi32.dll", EntryPoint = "BitBlt")] static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
            IntPtr source, int sourceX, int sourceY, uint rasterOperation);
        [DllImport("gdi32.dll", EntryPoint = "DeleteDC")] static extern bool DeleteDC(IntPtr deviceContext);
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll", EntryPoint = "MoveToEx")] static extern bool MoveToEx(IntPtr deviceContext, int x, int y, IntPtr previousPoint);
        [DllImport("gdi32.dll", EntryPoint = "LineTo")] static extern bool LineTo(IntPtr deviceContext, int x, int y);
        [DllImport("gdi32.dll", EntryPoint = "SetBkMode")] static extern int SetBkMode(IntPtr deviceContext, int mode);
        [DllImport("gdi32.dll", EntryPoint = "SetTextColor")] static extern uint SetTextColor(IntPtr deviceContext, uint color);
        [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr deviceContext, string text, int length, ref NativeRect rect, uint format);
        [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight,
            uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
            uint quality, uint pitchAndFamily, string faceName);
    }
}
#endif

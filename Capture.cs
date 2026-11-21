using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace Translater
{
    internal sealed class SelectionMonitor : IDisposable
    {
        public event Action<string, SelectionContext> Selected;
        public event Action<Point> SelectionStarted;
        private readonly Control dispatcher = new Control();
        private readonly Native.MouseHook callback;
        private readonly SemaphoreSlim captures = new SemaphoreSlim(1);
        private readonly IntPtr dispatcherHandle;
        private IntPtr hook;
        private volatile bool enabled = true;
        private bool disposed;
        private int revision;
        private IntPtr dragWindow, previousWindow;
        private Point down, previousDown;
        private uint previousTime;
        private bool doubleClick;

        public SelectionMonitor()
        {
            dispatcherHandle = dispatcher.Handle;
            callback = OnMouse;
            hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value;
                Interlocked.Increment(ref revision);
                dragWindow = previousWindow = IntPtr.Zero;
            }
        }

        public static bool IsZoteroForeground()
        {
            return Native.IsZoteroWindow(Native.GetForegroundWindow());
        }

        private IntPtr OnMouse(int code, IntPtr message, IntPtr info)
        {
            int kind = message.ToInt32();
            if (code >= 0 && enabled && (kind == 0x201 || kind == 0x202))
            {
                var mouse = (Native.MouseInfo)Marshal.PtrToStructure(info, typeof(Native.MouseInfo));
                if (!disposed)
                {
                    var window = Native.GetAncestor(Native.WindowFromPoint(mouse.point), 2);
                    var foreground = Native.GetForegroundWindow();
                    // Invalidate an older capture as soon as another press arrives.
                    int ticket = kind == 0x201 ? Interlocked.Increment(ref revision) : revision;
                    dispatcher.BeginInvoke((Action)delegate
                    {
                        if (!disposed && enabled)
                            HandleMouse(kind, mouse, window, foreground, ticket);
                    });
                }
            }
            return Native.CallNextHookEx(hook, code, message, info);
        }

        private void HandleMouse(int kind, Native.MouseInfo mouse, IntPtr window, IntPtr foreground, int ticket)
        {
            if (kind == 0x201)
            {
                dragWindow = IntPtr.Zero;
                if (window == IntPtr.Zero || !Native.IsZoteroWindow(window))
                {
                    previousWindow = IntPtr.Zero;
                    return;
                }
                down = mouse.point;
                var area = SystemInformation.DoubleClickSize;
                doubleClick = previousWindow == window && unchecked(mouse.time - previousTime) <= Native.GetDoubleClickTime()
                    && Math.Abs(down.X - previousDown.X) <= area.Width / 2
                    && Math.Abs(down.Y - previousDown.Y) <= area.Height / 2;
                dragWindow = previousWindow = window;
                previousDown = down;
                previousTime = mouse.time;
                var started = SelectionStarted;
                if (started != null) started(down);
                return;
            }
            bool sameWindow = dragWindow != IntPtr.Zero && dragWindow == window && window == foreground;
            dragWindow = IntPtr.Zero;
            if (!sameWindow || ticket != revision) return;
            var distance = SystemInformation.DragSize;
            bool dragged = Math.Abs(mouse.point.X - down.X) >= Math.Max(2, distance.Width / 2)
                || Math.Abs(mouse.point.Y - down.Y) >= Math.Max(2, distance.Height / 2);
            if (dragged) previousWindow = IntPtr.Zero;
            if (dragged || doubleClick) CaptureAsync(down, mouse.point, window, ticket);
        }

        public void CancelPending()
        {
            Interlocked.Increment(ref revision);
        }

        private bool StillCurrent(IntPtr window, int ticket)
        {
            return enabled && Volatile.Read(ref revision) == ticket && Native.GetForegroundWindow() == window;
        }

        private async void CaptureAsync(Point start, Point end, IntPtr window, int ticket)
        {
            await Task.Delay(60);
            if (!StillCurrent(window, ticket)) return;
            await captures.WaitAsync();
            try
            {
                if (!StillCurrent(window, ticket)) return;
                var context = await Task.Run(delegate {
                    var result = new SelectionContext { Window = window, Anchor = end };
                    InspectPoint(start, end, result);
                    if (!result.IsReader && start != end) InspectPoint(end, end, result);
                    return result;
                });
                if (!context.IsReader || !StillCurrent(window, ticket)) return;
                string text = await Task.Run(delegate {
                    return ClipboardCapture.Copy(window, dispatcherHandle,
                        delegate { return StillCurrent(window, ticket); });
                });
                if (!StillCurrent(window, ticket) || String.IsNullOrWhiteSpace(text)) return;
                var handler = Selected;
                if (handler != null) handler(text.Trim(), context);
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            catch (InvalidOperationException) { }
            finally { captures.Release(); }
        }

        private static void InspectPoint(Point point, Point end, SelectionContext result)
        {
            AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            var elements = new List<AutomationElement>();
            bool reader = false;
            for (int depth = 0; depth < 40 && element != null; depth++)
            {
                var current = element.Current;
                var type = current.ControlType;
                // Gecko's PDF text layer contains Edit controls, even in read-only PDFs.
                if (current.IsPassword || type == ControlType.DataItem || type == ControlType.TreeItem
                    || type == ControlType.Button || type == ControlType.MenuItem) break;
                if (current.AutomationId == "viewerContainer" || (type == ControlType.Document
                    && current.Name == "PDF.js viewer")) { reader = true; result.ReaderBounds = current.BoundingRectangle; }
                if (type == ControlType.Window) break;
                elements.Add(element);
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            if (!reader) return;
            result.IsReader = true;
            foreach (var item in elements)
            {
                object pattern;
                if (!item.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) continue;
                try
                {
                    var textPattern = (TextPattern)pattern;
                    if (textPattern.SupportedTextSelection == SupportedTextSelection.None) continue;
                    foreach (var range in textPattern.GetSelection())
                    {
                        object size = range.GetAttributeValue(TextPattern.FontSizeAttribute);
                        if (size is double) result.FontPoints = (double)size;
                        foreach (var rect in range.GetBoundingRectangles())
                        {
                            var margin = rect;
                            margin.Inflate(12, 10);
                            if (!margin.Contains(point.X, point.Y) && !margin.Contains(end.X, end.Y)) continue;
                            if (rect.Height > 0 && rect.Height < 200)
                                result.LineHeight = rect.Height;
                            return;
                        }
                    }
                }
                catch (InvalidOperationException) { }
                catch (COMException) { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Enabled = false;
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            dispatcher.Dispose();
        }
    }

    internal sealed class SelectionContext
    {
        public IntPtr Window;
        public Point Anchor;
        public bool IsReader;
        public System.Windows.Rect ReaderBounds;
        public double LineHeight;
        public double FontPoints;
    }

    internal static class ClipboardCapture
    {
        public static string Copy(IntPtr window, IntPtr owner, Func<bool> stillCurrent)
        {
            if (!stillCurrent()) return null;
            uint sequence = Native.GetClipboardSequenceNumber();
            if (!stillCurrent() || !Native.CopyShortcut()) return null;
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 1200 && stillCurrent())
            {
                Thread.Sleep(15);
                if (Native.GetClipboardSequenceNumber() == sequence || !Native.OpenClipboard(owner)) continue;
                try
                {
                    if (!stillCurrent() || Native.GetClipboardSequenceNumber() == sequence
                        || !Native.SameProcess(Native.GetClipboardOwner(), window)) return null;
                    return ReadUnicode();
                }
                finally { Native.CloseClipboard(); }
            }
            return null;
        }

        private static string ReadUnicode()
        {
            var handle = Native.GetClipboardData(13);
            if (handle == IntPtr.Zero) return null;
            ulong length = Native.GlobalSize(handle).ToUInt64() / 2;
            if (length == 0 || length > 2 * 1024 * 1024) return null;
            var pointer = Native.GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;
            try
            {
                string text = Marshal.PtrToStringUni(pointer, (int)length);
                int terminator = text.IndexOf('\0');
                return terminator < 0 ? text : text.Substring(0, terminator);
            }
            finally { Native.GlobalUnlock(handle); }
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInfo
        {
            public Point point;
            public uint mouseData, flags, time;
            public UIntPtr extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort key, scan;
            public uint flags, time;
            public UIntPtr extra;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputPayload
        {
            [FieldOffset(0)] public KeyboardInput keyboard;
            [FieldOffset(0)] public MouseInfo mouse; // Pads the union to MOUSEINPUT size on x86/x64.
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public uint type;
            public InputPayload payload;
        }

        internal static bool CopyShortcut()
        {
            foreach (int key in new int[] { 1, 2, 0x10, 0x11, 0x12, 0x5b, 0x5c, 0x43 })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
            var inputs = new Input[4];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i].type = 1;
                inputs[i].payload.keyboard.key = (ushort)(i == 0 || i == 3 ? 0x11 : 0x43);
                inputs[i].payload.keyboard.flags = i >= 2 ? 2u : 0u;
            }
            return SendInput(4, inputs, Marshal.SizeOf(typeof(Input))) == 4;
        }

        internal static bool SameProcess(IntPtr left, IntPtr right)
        {
            uint a, b;
            return GetWindowThreadProcessId(left, out a) != 0 && GetWindowThreadProcessId(right, out b) != 0 && a == b;
        }

        internal static bool IsZoteroWindow(IntPtr window)
        {
            uint pid;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out pid) == 0) return false;
            try
            {
                using (var process = Process.GetProcessById((int)pid))
                    return String.Equals(process.ProcessName, "zotero", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return false; }
        }

        internal delegate IntPtr MouseHook(int code, IntPtr message, IntPtr info);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, MouseHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] internal static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] internal static extern bool CloseClipboard();
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardData(uint format);
        [DllImport("kernel32.dll")] internal static extern UIntPtr GlobalSize(IntPtr data);
        [DllImport("kernel32.dll")] internal static extern IntPtr GlobalLock(IntPtr data);
        [DllImport("kernel32.dll")] internal static extern bool GlobalUnlock(IntPtr data);
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Translater
{
    internal sealed class TranslationPopup : IDisposable
    {
        private readonly Border paper;
        private readonly TextBox body;
        private readonly ContextMenu menu;
        private readonly MenuItem copy, retry;
        private readonly StringBuilder translation = new StringBuilder();
        private HwndSource source;
        private SelectionContext context;
        private Drawing.Rectangle bounds, original;
        private Drawing.Point dragStart, ownerPosition;
        private bool placed, dragging, waiting, failed;
        private int edges;
        private double scale = 1;
        public bool Visible { get; private set; }
        public bool ContainsFocus
        {
            get { return source != null && (Native.GetForegroundWindow() == source.Handle || menu.IsOpen); }
        }
        public event EventHandler Dismissed;
        public event EventHandler RetryRequested;

        public TranslationPopup()
        {
            body = new TextBox {
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(37,47,41)),
                Padding = new Thickness(0), Margin = new Thickness(22,18,22,18),
                VerticalAlignment = VerticalAlignment.Top,
                FontFamily = new FontFamily(new Uri(AppDomain.CurrentDomain.BaseDirectory),
                    "./fonts/#Ubuntu, ./fonts/#Noto Sans CJK SC, Microsoft YaHei UI")
            };
            paper = new Border {
                Background = new SolidColorBrush(Color.FromArgb(225,250,248,241)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(136,150,154,151)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Child = body, SnapsToDevicePixels = true, UseLayoutRounding = true
            };
            menu = new ContextMenu();
            copy = new MenuItem { Header = "复制译文" };
            copy.Click += delegate {
                try { Forms.Clipboard.SetDataObject(translation.ToString(), true, 3, 30); }
                catch (ExternalException) { body.Text = translation + "\n\n复制失败，请重试。"; }
            };
            retry = new MenuItem { Header = "重试" };
            retry.Click += delegate { if (RetryRequested != null) RetryRequested(this, EventArgs.Empty); };
            var close = new MenuItem { Header = "关闭" };
            close.Click += delegate { Dismiss(); };
            menu.Items.Add(copy); menu.Items.Add(retry); menu.Items.Add(close);
            paper.ContextMenu = menu;
            paper.SizeChanged += delegate { body.MaxHeight = Math.Max(1, paper.ActualHeight - 38); };
            body.ContextMenu = menu;
            menu.Opened += delegate { copy.IsEnabled = translation.Length > 0; retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed; };
            paper.PreviewMouseLeftButtonDown += Down;
            paper.PreviewMouseMove += Move;
            paper.PreviewMouseLeftButtonUp += delegate { if (dragging) paper.ReleaseMouseCapture(); };
            paper.LostMouseCapture += delegate { dragging = false; };
            paper.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
            };
        }

        public void BeginTranslation(SelectionContext selection)
        {
            context = selection;
            RECT owner;
            if (!GetWindowRect(context.Window, out owner)) return;
            ownerPosition = new Drawing.Point(owner.Left, owner.Top);
            scale = Math.Max(1, GetDpiForWindow(context.Window)) / 96.0;
            if (source == null)
            {
                var options = new HwndSourceParameters("划译译文") {
                    WindowStyle = unchecked((int)0x80000000),
                    ExtendedWindowStyle = 0x08000000 | 0x80 | 0x8,
                    Width = 520, Height = 280, UsesPerPixelTransparency = true
                };
                source = new HwndSource(options);
                source.RootVisual = paper;
                source.AddHook(WindowMessage);
            }
            SetWindowLongPtr(source.Handle, -8, context.Window);
            var area = AvailableArea();
            if (!placed)
            {
                int width = (int)(520 * scale), height = (int)(280 * scale);
                bounds = new Drawing.Rectangle(area.Left + (area.Width-width)/2,
                    area.Top + (int)(area.Height * .62) - height/2, width, height);
                placed = true;
            }
            double fontSize = context.LineHeight > 0 ? context.LineHeight / scale * .8 :
                context.FontPoints > 0 ? context.FontPoints * 96 / 72 : 17;
            body.FontSize = Math.Max(14, Math.Min(26, fontSize));
            translation.Clear(); waiting = true; failed = false;
            body.Text = "正在翻译…";
            body.ScrollToHome();
            Clamp(area);
            Position();
            ShowWindow(source.Handle, 4);
            Visible = true;
        }

        public void AppendTranslation(string delta)
        {
            if (!Visible || failed || String.IsNullOrEmpty(delta)) return;
            translation.Append(delta);
            if (waiting) { body.Clear(); waiting = false; }
            double offset = body.VerticalOffset;
            body.AppendText(delta);
            body.ScrollToVerticalOffset(offset);
        }
        public void FinishTranslation()
        {
            if (Visible && waiting) ShowFailure("没有收到译文，请右键重试。");
        }
        public void ShowFailure(string message)
        {
            if (!Visible) return;
            failed = true; waiting = false;
            body.Text = message + "\n\n右键可重试。";
        }
        public void Dismiss()
        {
            if (!Visible) return;
            menu.IsOpen = false;
            paper.ReleaseMouseCapture();
            ShowWindow(source.Handle, 0);
            Visible = false;
            if (Dismissed != null) Dismissed(this, EventArgs.Empty);
        }

        private Drawing.Rectangle AvailableArea()
        {
            RECT r;
            if (!GetWindowRect(context.Window, out r)) return Forms.Screen.FromPoint(context.Anchor).WorkingArea;
            var owner = Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
            var area = Drawing.Rectangle.Intersect(owner, Forms.Screen.FromHandle(context.Window).WorkingArea);
            if (!context.ReaderBounds.IsEmpty && context.ReaderBounds.Width > 0)
            {
                var reader = context.ReaderBounds;
                var adjusted = new Drawing.Rectangle((int)reader.X + r.Left-ownerPosition.X,
                    (int)reader.Y + r.Top-ownerPosition.Y, (int)reader.Width, (int)reader.Height);
                var intersection = Drawing.Rectangle.Intersect(area, adjusted);
                if (intersection.Width > 100 && intersection.Height > 80) area = intersection;
            }
            return area;
        }
        private void Clamp(Drawing.Rectangle area)
        {
            if (area.Width < 1 || area.Height < 1) return;
            bounds.Width = Math.Min(area.Width, Math.Max((int)(260*scale),bounds.Width));
            bounds.Height = Math.Min(area.Height, Math.Max((int)(120*scale),bounds.Height));
            bounds.X = Math.Max(area.Left,Math.Min(bounds.X,area.Right-bounds.Width));
            bounds.Y = Math.Max(area.Top,Math.Min(bounds.Y,area.Bottom-bounds.Height));
        }
        public void ConstrainToOwner()
        {
            if (!Visible || dragging) return;
            var before = bounds;
            Clamp(AvailableArea());
            if (before != bounds) Position();
        }
        private void Position()
        {
            SetWindowPos(source.Handle, new IntPtr(-1), bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10);
        }
        private int Hit(Point p)
        {
            int sides = p.X < 7 ? 1 : p.X > paper.ActualWidth-7 ? 2 : 0;
            return sides | (p.Y < 7 ? 4 : p.Y > paper.ActualHeight-7 ? 8 : 0);
        }
        private void Down(object sender, MouseButtonEventArgs e)
        {
            edges = Hit(e.GetPosition(paper));
            if (edges == 0 && e.OriginalSource != paper) return;
            original = bounds; dragStart = Forms.Cursor.Position;
            dragging = paper.CaptureMouse(); e.Handled = dragging;
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (!dragging)
            {
                int hit = Hit(e.GetPosition(paper));
                paper.Cursor = hit == 0 ? null : hit == 1 || hit == 2 ? Cursors.SizeWE :
                    hit == 4 || hit == 8 ? Cursors.SizeNS : hit == 5 || hit == 10 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                return;
            }
            var cursor = Forms.Cursor.Position;
            int dx=cursor.X-dragStart.X, dy=cursor.Y-dragStart.Y;
            int left=original.Left, top=original.Top, right=original.Right, bottom=original.Bottom;
            if (edges == 0) { left+=dx; right+=dx; top+=dy; bottom+=dy; }
            else
            {
                if ((edges&1)!=0) left=Math.Min(left+dx,right-(int)(260*scale));
                if ((edges&2)!=0) right=Math.Max(right+dx,left+(int)(260*scale));
                if ((edges&4)!=0) top=Math.Min(top+dy,bottom-(int)(120*scale));
                if ((edges&8)!=0) bottom=Math.Max(bottom+dy,top+(int)(120*scale));
            }
            bounds=Drawing.Rectangle.FromLTRB(left,top,right,bottom);
            Clamp(AvailableArea()); Position(); e.Handled=true;
        }
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x21) { handled=true; return new IntPtr(3); }
            return IntPtr.Zero;
        }
        public void Dispose()
        {
            Dismiss();
            if (source != null) source.Dispose();
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    }
}

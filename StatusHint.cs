using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Translater
{
    internal sealed class StatusHint : Form
    {
        private readonly Label caption;
        public StatusHint()
        {
            Text = "翻译已开启";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            BackColor = Color.White;
            Opacity = 0.98;
            caption = new Label { Text = "翻译已开启", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.Black,
                BackColor = Color.White, Font = AppFonts.Chinese(10) };
            Controls.Add(caption);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x80000;
                return p;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
            base.WndProc(ref m);
        }
        public void UpdateVisibility(bool enabled)
        {
            if (!enabled) { if (Visible) Hide(); return; }
            IntPtr window = GetForegroundWindow();
            RECT bounds;
            if (!GetWindowRect(window, out bounds)) { Hide(); return; }
            var work = Screen.FromHandle(window).WorkingArea;
            var rect = Rectangle.Intersect(Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), work);
            float scale = GetDpiForWindow(window) / 96f;
            int width = (int)(108 * scale), height = (int)(30 * scale), gap = (int)(16 * scale);
            if (rect.Width < width || rect.Height < height) { Hide(); return; }
            Bounds = new Rectangle(Math.Max(rect.Left, rect.Right - width - gap),
                Math.Max(rect.Top, rect.Bottom - height - gap), width, height);
            if (!Visible) Show();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) caption.Font.Dispose();
            base.Dispose(disposing);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT bounds);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    }
}

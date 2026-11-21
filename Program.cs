using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("划译")]
[assembly: AssemblyDescription("Zotero 桌面划词翻译")]
[assembly: AssemblyProduct("划译")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace Translater
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.SystemDefault;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (var mutex = new Mutex(true, @"Local\Translater.Huayi.Owner", out first))
            using (var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Translater.Huayi.Settings"))
            {
                if (!first)
                {
                    reopen.Set();
                    return;
                }
                try
                {
                    using (var app = new TrayApp(reopen)) Application.Run(app);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("启动划译时遇到问题。\n" + ex.Message, "划译",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly SelectionMonitor monitor;
        private readonly TranslationPopup popup = new TranslationPopup();
        private readonly StatusHint hint = new StatusHint();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem pause;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly EventWaitHandle reopen;
        private readonly Icon icon;
        private SettingsForm settings;
        private CancellationTokenSource active;
        private string key;
        private string lastText;
        private SelectionContext anchor;
        private bool lastEscape;
        private bool stopping;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public TrayApp(EventWaitHandle reopen)
        {
            this.reopen = reopen;
            dispatcher.CreateControl();
            icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            monitor = new SelectionMonitor();
            monitor.Enabled = false;
            monitor.SelectionStarted += delegate { popup.Dismiss(); };
            monitor.Selected += OnSelection;
            popup.Dismissed += delegate { CancelTranslation(); };
            popup.RetryRequested += delegate { if (lastText != null) Translate(lastText, anchor); };
            pause = new ToolStripMenuItem("暂停划词翻译", null, TogglePause);
            menu.Font = AppFonts.Chinese(10);
            menu.Renderer = new ToolStripSystemRenderer();
            menu.Items.Add(new ToolStripMenuItem("翻译设置…", null, delegate { ShowSettings(null); }));
            menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出划译", null, delegate { ExitThread(); }));
            tray = new NotifyIcon { Icon = icon, Text = "划译 · Zotero 划词翻译", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(null); };
            string error = null;
            try { key = Credentials.Read(); }
            catch (Exception ex) { error = ex.Message; }
            UpdateMonitor();
            timer.Interval = 90;
            timer.Tick += Tick;
            timer.Start();
            if (String.IsNullOrEmpty(key))
                dispatcher.BeginInvoke((Action)delegate { ShowSettings(error); });
        }

        private void Tick(object sender, EventArgs e)
        {
            if (reopen.WaitOne(0)) ShowSettings(null);
            UpdateMonitor();
            bool escape = (GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0;
            if (escape && !lastEscape) monitor.CancelPending();
            if (popup.Visible && ((escape && !lastEscape) ||
                (!SelectionMonitor.IsZoteroForeground() && !popup.ContainsFocus)))
                popup.Dismiss();
            popup.ConstrainToOwner();
            lastEscape = escape;
        }

        private void TogglePause(object sender, EventArgs e)
        {
            pause.Checked = !pause.Checked;
            pause.Text = pause.Checked ? "继续划词翻译" : "暂停划词翻译";
            if (pause.Checked) popup.Dismiss();
            UpdateMonitor();
        }

        private void UpdateMonitor()
        {
            monitor.Enabled = !stopping && settings == null && !pause.Checked && !String.IsNullOrEmpty(key)
                && SelectionMonitor.IsZoteroForeground();
            hint.UpdateVisibility(monitor.Enabled);
            tray.Text = String.IsNullOrEmpty(key) ? "划译 · 双击配置 DeepSeek" :
                pause.Checked ? "划译 · 已暂停" : "划译 · Zotero 划词翻译";
        }

        private void ShowSettings(string error)
        {
            if (settings != null)
            {
                if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
                settings.Activate();
                return;
            }
            popup.Dismiss();
            settings = new SettingsForm(key, error, icon);
            settings.KeySaved += delegate(string saved) { key = saved; };
            settings.FormClosed += delegate { settings = null; UpdateMonitor(); };
            UpdateMonitor();
            settings.Show();
        }

        private void OnSelection(string text, SelectionContext point)
        {
            if (!monitor.Enabled || !SelectionMonitor.IsZoteroForeground() || String.IsNullOrWhiteSpace(text)) return;
            int latin = 0, han = 0;
            foreach (char c in text)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) latin++;
                else if (c >= '\u3400' && c <= '\u9fff') han++;
            }
            if (latin > 0 && latin > han) Translate(text.Trim(), point);
        }

        private async void Translate(string text, SelectionContext point)
        {
            CancelTranslation();
            var request = new CancellationTokenSource();
            active = request;
            lastText = text;
            anchor = point;
            popup.BeginTranslation(point);
            // Queue deltas and completion in the same order on the window thread.
            Action<Action> post = action => {
                if (request.IsCancellationRequested) return;
                try
                {
                    dispatcher.BeginInvoke((Action)delegate {
                        if (active == request && !stopping) action();
                    });
                }
                catch (InvalidOperationException) { /* Window closed while streaming. */ }
            };
            try
            {
                await DeepSeek.TranslateAsync(key, text,
                    delta => post(delegate { popup.AppendTranslation(delta); }), request.Token).ConfigureAwait(false);
                post(popup.FinishTranslation);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { post(delegate { popup.ShowFailure(ex.Message); }); }
            finally
            {
                // UI owns active; dispose after queued delta and completion callbacks.
                try
                {
                    dispatcher.BeginInvoke((Action)delegate {
                        if (active == request) active = null;
                        request.Dispose();
                    });
                }
                catch (InvalidOperationException) { request.Dispose(); }
            }
        }

        private void CancelTranslation()
        {
            var previous = active;
            active = null;
            if (previous != null) previous.Cancel();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !stopping)
            {
                stopping = true;
                timer.Dispose();
                monitor.Dispose();
                CancelTranslation();
                if (settings != null) settings.Dispose();
                popup.Dispose();
                hint.Dispose();
                tray.Visible = false;
                tray.Dispose();
                menu.Dispose();
                icon.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

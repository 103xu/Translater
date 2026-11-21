using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Translater
{
    internal sealed class SettingsForm : Form
    {
        private static readonly Color Forest = Color.Black;
        private static readonly Color Ink = Color.Black;
        private static readonly Color Muted = Color.Black;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private readonly TextBox secret = new TextBox();
        private readonly CheckBox startup = new CheckBox();
        private readonly Label status = new Label();
        private readonly Button test = new Button();
        private readonly Button save = new Button();
        private CancellationTokenSource testing;
        private string savedKey;
        public event Action<string> KeySaved;

        public SettingsForm(string existing, string error, Icon icon)
        {
            Text = "划译 · 翻译设置";
            Icon = icon;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = AppFonts.Chinese(10);
            ForeColor = Ink;
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 230);
            savedKey = existing;

            AddLabel("DeepSeek API Key", 24, 22, 250, 26, AppFonts.Latin(11), Ink);
            var keyLink = new LinkLabel { Text = "获取密钥 ↗", Bounds = new Rectangle(291, 22, 105, 26),
                TextAlign = ContentAlignment.MiddleRight, LinkColor = Forest, ActiveLinkColor = Forest,
                LinkBehavior = LinkBehavior.HoverUnderline };
            keyLink.LinkClicked += delegate {
                try { Process.Start(new ProcessStartInfo("https://platform.deepseek.com/api_keys") { UseShellExecute = true }); }
                catch { SetStatus("请在浏览器中打开 platform.deepseek.com/api_keys。", true); }
            };
            Controls.Add(keyLink);
            secret.SetBounds(24, 58, 372, 30);
            secret.Font = AppFonts.Latin(11);
            secret.UseSystemPasswordChar = true;
            secret.AccessibleName = "DeepSeek API Key";
            secret.Text = existing ?? "";
            Controls.Add(secret);
            startup.Text = "登录 Windows 时启动";
            startup.FlatStyle = FlatStyle.Flat;
            startup.FlatAppearance.CheckedBackColor = Color.White;
            startup.SetBounds(24, 101, 300, 29);
            startup.Checked = IsStartup();
            Controls.Add(startup);
            status.SetBounds(24, 233, 372, 65);
            status.ForeColor = Muted;
            Controls.Add(status);
            SetStatus(error, error != null);
            var footer = new Panel { Bounds = new Rectangle(0, 150, 420, 64), BackColor = Color.White };
            Controls.Add(footer);
            StyleButton(test, "测试翻译", 24, 10, 103, false);
            StyleButton(save, "保存到托盘", 263, 10, 133, true);
            footer.Controls.Add(test);
            footer.Controls.Add(save);
            test.Click += TestConnection;
            save.Click += Save;
            secret.TextChanged += delegate {
                if (testing != null) testing.Cancel();
                test.Enabled = secret.Text.Trim().Length != 0 && testing == null;
                SetStatus("", false);
            };
            test.Enabled = !String.IsNullOrEmpty(existing);
            AcceptButton = save;
            FormClosed += delegate { if (testing != null) testing.Cancel(); };
        }

        private void AddLabel(string text, int x, int y, int width, int height, Font font, Color color)
        {
            Controls.Add(new Label { Text = text, Bounds = new Rectangle(x,y,width,height),
                Font = font, ForeColor = color, TextAlign = ContentAlignment.MiddleLeft });
        }

        private static void StyleButton(Button button, string text, int x, int y, int width, bool primary)
        {
            button.Text = text;
            button.SetBounds(x, y, width, 39);
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? Forest : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.FlatAppearance.BorderColor = primary ? Forest : Color.Black;
            button.Cursor = Cursors.Hand;
        }

        private bool IsStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue("HuayiTranslater") != null;
        }

        private void Save(object sender, EventArgs e)
        {
            try
            {
                string value = secret.Text.Trim();
                if (value.Length == 0 && String.IsNullOrEmpty(savedKey)) { SetStatus("请先填写 DeepSeek API Key。", true); return; }
                if (value != savedKey)
                {
                    if (value.Length == 0) Credentials.Delete(); else Credentials.Save(value);
                    savedKey = value;
                    if (KeySaved != null) KeySaved(value);
                }
                if (startup.Checked || IsStartup())
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (startup.Checked) key.SetValue("HuayiTranslater", "\"" + Application.ExecutablePath + "\"");
                        else key.DeleteValue("HuayiTranslater", false);
                    }
                }
                Close();
            }
            catch (Exception ex) { SetStatus("保存失败。" + ex.Message, true); }
        }

        private async void TestConnection(object sender, EventArgs e)
        {
            var source = new CancellationTokenSource();
            testing = source;
            test.Enabled = false;
            SetStatus("正在翻译测试短句 “The sample is ready.”", false);
            var result = new System.Text.StringBuilder();
            try
            {
                await DeepSeek.TranslateAsync(secret.Text.Trim(), "The sample is ready.", delta => result.Append(delta), source.Token);
                if (!IsDisposed && !source.IsCancellationRequested) SetStatus("连接成功 · " + result.ToString().Trim(), false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed && !source.IsCancellationRequested) SetStatus(ex.Message, true); }
            finally
            {
                testing = null;
                if (!IsDisposed) test.Enabled = secret.Text.Trim().Length != 0;
                source.Dispose();
            }
        }

        private void SetStatus(string message, bool error)
        {
            status.Text = message;
            status.ForeColor = Color.Black;
            bool showStatus = !String.IsNullOrEmpty(message);
            status.Visible = showStatus;
            ClientSize = new Size(ClientSize.Width, (int)Math.Round((showStatus ? 310 : 230) * DeviceDpi / 96f));
        }
    }
}

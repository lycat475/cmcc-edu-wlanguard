using System;
using System.Drawing;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>验证码/短信码输入框。返回 null 表示用户取消。</summary>
    internal sealed class InputDialog : Form
    {
        private readonly TextBox _txt;
        public string Value { get; private set; }

        private InputDialog(string title, string hint, bool showImage, byte[] image, bool isSms)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            Font = new Font("微软雅黑", 9f);
            ClientSize = new Size(showImage ? 400 : 380, showImage ? 270 : 176);
            ShowInTaskbar = true;

            int y = 16;

            var lblHint = new Label { Location = new Point(16, y), Size = new Size(348, 22), Text = hint };
            Controls.Add(lblHint);
            y += 24;

            if (isSms)
            {
                var lblValid = new Label
                {
                    Location = new Point(16, y), Size = new Size(348, 20),
                    Text = "（15分钟内有效）", ForeColor = Color.DimGray
                };
                Controls.Add(lblValid);
                y += 22;
            }

            if (showImage && image != null)
            {
                var pic = new PictureBox
                {
                    Location = new Point(16, y), Size = new Size(360, 70),
                    BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.CenterImage
                };
                try
                {
                    using (var ms = new System.IO.MemoryStream(image))
                        pic.Image = Image.FromStream(ms);
                }
                catch (Exception ex) { AppLog.WriteErrorLog("验证码图片解码失败: " + ex.Message); }
                Controls.Add(pic);
                y += 78;
            }

            _txt = new TextBox
            {
                Location = new Point(showImage ? 120 : 110, y), Size = new Size(160, 30),
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Consolas", 14f),
                MaxLength = isSms ? 8 : 6
            };
            Controls.Add(_txt);
            y += 46;

            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(showImage ? 120 : 110, y), Size = new Size(78, 30) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(showImage ? 206 : 196, y), Size = new Size(78, 30) };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (s, e) => _txt.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (DialogResult == DialogResult.OK) Value = _txt.Text.Trim();
            base.OnFormClosed(e);
        }

        public static string Ask(string title, string hint, bool isSms, bool showImage = false, byte[] image = null)
        {
            using (var dlg = new InputDialog(title, hint, showImage, image, isSms))
            {
                return dlg.ShowDialog() == DialogResult.OK ? dlg.Value : null;
            }
        }
    }

    /// <summary>把 ILoginPrompter 的调用挪到 UI 线程上执行。</summary>
    internal sealed class FormPrompter : ILoginPrompter
    {
        private readonly Form _owner;

        public FormPrompter(Form owner) { _owner = owner; }

        public string AskCode(string hint, bool isSms)
        {
            return (string)_owner.Invoke(new Func<string>(() =>
                InputDialog.Ask(isSms ? "输入短信验证码" : "输入图形验证码",
                                string.IsNullOrWhiteSpace(hint) ? (isSms ? "请输入手机收到的短信验证码" : "请输入图片中的验证码") : hint,
                                isSms)));
        }

        public string AskCaptcha(byte[] imageBytes)
        {
            return (string)_owner.Invoke(new Func<string>(() =>
                InputDialog.Ask("输入图形验证码", "请输入图片中的验证码", false, true, imageBytes)));
        }
    }
}

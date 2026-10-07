using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NotepadFileLocator
{
    internal sealed class HelpWindow : Form
    {
        private readonly Image appImage;
        private readonly List<Font> ownedFonts = new List<Font>();

        internal HelpWindow(Icon appIcon)
        {
            SuspendLayout();
            Text = "使用方法";
            Icon = appIcon;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 408);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(255, 252, 248);
            ForeColor = Color.FromArgb(43, 37, 30);
            Font = MakeFont("Microsoft YaHei UI", 10F, FontStyle.Regular);

            appImage = appIcon.ToBitmap();
            Controls.Add(new PictureBox { Image = appImage, SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(28, 28), Size = new Size(44, 44), TabStop = false });
            AddText(this, "NotepadFileLocator", 88, 28, 364, 28, 14F, FontStyle.Bold, ForeColor);
            AddText(this, "记事本文件定位工具", 88, 58, 364, 24, 9F, FontStyle.Regular, Color.FromArgb(120, 106, 90));
            AddText(this, "快速找到当前文件", 28, 106, 424, 38, 20F, FontStyle.Bold, ForeColor);
            AddText(this, "在记事本中选中标签，然后快速按两次 Esc。", 28, 151, 424, 28, 10F, FontStyle.Regular, ForeColor);

            var shortcut = new Panel { Location = new Point(28, 191), Size = new Size(424, 76),
                BackColor = Color.FromArgb(255, 237, 211) };
            var key = new Label { Text = "Esc", TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(16, 15), Size = new Size(66, 46), BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, Font = MakeFont("Segoe UI", 16F, FontStyle.Bold),
                AccessibleName = "Esc 键" };
            shortcut.Controls.Add(key);
            AddText(shortcut, "× 2", 92, 23, 60, 32, 16F, FontStyle.Regular, Color.FromArgb(169, 77, 13));
            AddText(shortcut, "定位并选中文件", 165, 15, 247, 27, 11F, FontStyle.Bold, ForeColor);
            AddText(shortcut, "资源管理器直接在前台显示", 165, 44, 247, 24, 9F, FontStyle.Regular, Color.FromArgb(120, 82, 45));
            Controls.Add(shortcut);

            AddText(this, "新文档请先保存，再使用快捷键。", 28, 286, 424, 26, 10F, FontStyle.Regular, ForeColor);
            AddText(this, "托盘右键：暂停 · 开机启动 · 退出", 28, 315, 424, 26, 9F, FontStyle.Regular, Color.FromArgb(120, 106, 90));
            var close = new Button { Text = "知道了", DialogResult = DialogResult.OK,
                Location = new Point(340, 356), Size = new Size(112, 36), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(187, 78, 8), ForeColor = Color.White,
                UseVisualStyleBackColor = false, Cursor = Cursors.Hand, TabIndex = 0 };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 62, 4);
            close.FlatAppearance.MouseDownBackColor = Color.FromArgb(133, 49, 0);
            close.Click += delegate { Close(); };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            ResumeLayout(false);
        }

        private Font MakeFont(string family, float size, FontStyle style)
        {
            var font = new Font(family, size, style);
            ownedFonts.Add(font);
            return font;
        }

        private void AddText(Control parent, string text, int x, int y, int width, int height,
            float size, FontStyle style, Color color)
        {
            parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                Font = MakeFont("Microsoft YaHei UI", size, style), ForeColor = color, BackColor = Color.Transparent,
                UseMnemonic = false, TabStop = false });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                if (appImage != null) appImage.Dispose();
                foreach (Font font in ownedFonts) font.Dispose();
                ownedFonts.Clear();
            }
        }
    }
}

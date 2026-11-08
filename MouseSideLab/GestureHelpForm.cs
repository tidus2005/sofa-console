using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseSideLab
{
    public sealed class GestureHelpForm : Form
    {
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        public const int DurationMs = 5000;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly Stopwatch clock = new Stopwatch();
        private bool volumeEnabled = true, sofaEnabled = true;
        public GestureHelpForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            // This is a read-only overlay; disabling activation also prevents WinForms
            // from moving focus between forms in the same UI thread on first show.
            Enabled = false;
            Opacity = 0.98;
            StartPosition = FormStartPosition.Manual; DoubleBuffered = true;
            BackColor = Color.FromArgb(19, 28, 46);
            timer.Tick += delegate { if (clock.ElapsedMilliseconds >= DurationMs) Dismiss(); else Invalidate(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // WM_MOUSEACTIVATE: MA_NOACTIVATE
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; } // WM_NCHITTEST: HTTRANSPARENT
            base.WndProc(ref m);
        }
        public bool IsDisplayed { get { return IsHandleCreated && IsWindowVisible(Handle); } }
        public void Dismiss()
        {
            timer.Stop(); clock.Stop();
            if (IsHandleCreated) SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x97);
        }
        public void Display(bool volume = true, bool sofa = true)
        {
            IntPtr previousForeground = Hotkeys.GetForegroundWindow();
            // Create the initially hidden window before positioning it on a DPI-scaled
            // display; handle creation inside the native show call can change focus.
            IntPtr window = Handle;
            volumeEnabled = volume; sofaEnabled = sofa;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = Math.Min(2200, (int)(area.Width * .82));
            int height = (int)(width * 650.0 / 1120);
            if (height > area.Height * .88) { height = (int)(area.Height * .88); width = (int)(height * 1120.0 / 650); }
            ClientSize = new Size(width, height);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            clock.Restart();
            // Native show with SWP_NOACTIVATE avoids Form.Show's same-thread focus path.
            SetWindowPos(window, new IntPtr(-1), Left, Top, Width, Height, 0x50);
            // First handle creation can activate a same-thread Form despite NOACTIVATE.
            // Restore only when this overlay itself took focus, never over another app.
            if (previousForeground != IntPtr.Zero && previousForeground != window && Hotkeys.GetForegroundWindow() == window)
                Hotkeys.SetForegroundWindow(previousForeground);
            timer.Start(); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(ClientSize.Width / 1120F, ClientSize.Height / 650F);
            Draw(g, "鼠标快捷操作", 42, 29, 1030, 54, 36, Color.White, true);
            Draw(g, "前键靠近鼠标头部 · 后键靠近手掌 · 前键三击 Esc / 后键三击 Enter", 44, 88, 1040, 36, 22, Color.FromArgb(162, 181, 210));
            using (var brush = new SolidBrush(Color.FromArgb(30, 44, 67)))
            { g.FillRectangle(brush, 32, 145, 520, 395); g.FillRectangle(brush, 568, 145, 520, 395); }
            Draw(g, "前侧键", 52, 158, 470, 42, 28, Color.FromArgb(104, 180, 255), true);
            Draw(g, "后侧键", 588, 158, 470, 42, 28, Color.FromArgb(104, 224, 190), true);
            string off = sofaEnabled ? "" : "（已关闭）";
            Row(g, 52, 214, "单击", "Codex / 返回原程序" + off);
            Row(g, 52, 276, "双击", "删除 Delete" + off);
            Row(g, 52, 338, "双击 · 第二下按住", "每 0.5 秒退格，松开即停" + off);
            Row(g, 52, 400, "按住 + 滚轮上下", "调节音量" + (volumeEnabled ? "" : "（已关闭）"));
            Row(g, 52, 462, "按住 + 按下滚轮", "静音 / 恢复声音" + off);
            Row(g, 588, 214, "单击", "宠物显示 / 隐藏" + off);
            Row(g, 588, 276, "双击", "撤销 Ctrl+Z" + off);
            Row(g, 588, 338, "按住 + 滚轮上下", "切换软件窗口，松开确认" + off);
            Row(g, 588, 400, "按住 + 按下滚轮", "桌面 / 恢复原窗口" + off);
            Row(g, 588, 462, "连按三下", "确认 / 回车 Enter" + off);
            Draw(g, "双侧键同时按住 3 秒：打开这张说明", 44, 559, 1020, 38, 25, Color.White, true);
            int remaining = Math.Max(0, (int)Math.Ceiling((DurationMs - clock.ElapsedMilliseconds) / 1000.0));
            Draw(g, "说明 " + remaining + " 秒后自动关闭 · 普通左右键照常使用", 44, 604, 1020, 32, 21, Color.FromArgb(162, 181, 210));
        }
        private static void Row(Graphics g, int x, int y, string gesture, string action)
        {
            Draw(g, gesture, x, y, 480, 28, 20, Color.FromArgb(162, 181, 210));
            Draw(g, action, x, y + 27, 480, 32, 24, Color.White, true);
        }
        private static void Draw(Graphics g, string text, int x, int y, int width, int height, float size, Color color, bool bold = false)
        {
            using (var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
                g.DrawString(text, font, brush, new RectangleF(x, y, width, height));
        }
        protected override void Dispose(bool disposing)
        { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}

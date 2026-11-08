using System;
using System.Drawing;
using System.Windows.Forms;

namespace MouseSideLab
{
    internal sealed class CommandPanel : Form
    {
        private static readonly string[,] Commands = {
            { "语音输入", "Win+H" }, { "屏幕键盘", "Win+Ctrl+O" }, { "播放 / 暂停", "PlayPause" }, { "静音 / 恢复", "Mute" },
            { "音量减小", "VolumeDown" }, { "音量增大", "VolumeUp" }, { "浏览器全屏", "F11" }, { "播放器全屏*", "Alt+Enter" },
            { "复制", "Ctrl+C" }, { "粘贴", "Ctrl+V" }, { "全选", "Ctrl+A" }, { "撤销", "Ctrl+Z" },
            { "确认 / 发送", "Enter" }, { "取消 / 返回", "Esc" }, { "切换窗口", "Win+Tab" }, { "显示桌面", "Win+D" },
            { "新建标签页", "Ctrl+T" }, { "关闭标签页", "Ctrl+W" }, { "恢复标签页", "Ctrl+Shift+T" }, { "定位地址栏", "Ctrl+L" },
            { "页面放大", "Ctrl+Plus" }, { "页面缩小", "Ctrl+Minus" }, { "区域截图", "Win+Shift+S" }, { "暂停映射 / 游戏", "pause" }
        };
        private readonly IntPtr target;
        private readonly bool execute;
        private readonly Action<string, string, bool, IntPtr> perform;
        public CommandPanel(bool allowExecution, IntPtr previousWindow, Action<string, string, bool, IntPtr> handler)
        {
            SuspendLayout();
            execute = allowExecution; target = previousWindow; perform = handler;
            Text = "沙发操作面板"; TopMost = true; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(900, 540); MinimumSize = new Size(850, 530);
            BackColor = Color.FromArgb(243, 246, 250); Padding = new Padding(18);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.Controls.Add(new Label { Text = allowExecution ? "选择一个动作 · 将作用于打开面板前的窗口" : "测试模式 · 点按只显示说明，不执行快捷键", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 6 };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            for (int i = 0; i < 6; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6));
            for (int i = 0; i < Commands.GetLength(0); i++)
            {
                string name = Commands[i, 0], shortcut = Commands[i, 1];
                var button = new Button { Text = name + "\n" + shortcut, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(5), Cursor = Cursors.Hand };
                button.Click += delegate { Close(); perform(name, shortcut, execute, target); };
                grid.Controls.Add(button, i % 4, i / 4);
            }
            root.Controls.Add(grid, 0, 1);
            root.Controls.Add(new Label { Text = "* 播放器全屏取决于软件。关闭面板可取消；游戏时暂停映射，手柄操作不受影响。", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(98, 113, 132) }, 0, 2);
            Controls.Add(root);
            AutoScaleDimensions = new SizeF(96F, 96F); ResumeLayout(true);
        }
    }
}

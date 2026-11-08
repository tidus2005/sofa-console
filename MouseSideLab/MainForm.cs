using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MouseSideLab
{
    public sealed class Settings
    {
        public int ClickMs = 300, HoldMs = 650, ChordMs = 150;
        public bool SwapButtons = false, SuppressNavigation = true, AlwaysOnTop = true;
        public bool DriverCompatibility = true;
        public bool VolumeWheelEnabled = true;
        public bool SofaGesturesEnabled = true;
        public bool ShowGestureToasts = false;
        public bool ShowSoundToasts = true;
        public bool SoundHintsEnabled { get { return ShowGestureToasts || ShowSoundToasts; } }
        public int SofaBindingsVersion;
        public Dictionary<string, string> Descriptions = new Dictionary<string, string>();
        public Dictionary<string, string> Shortcuts = new Dictionary<string, string>();
        public static readonly string[] Codes = { "front.single", "front.double", "front.triple", "front.hold", "back.single", "back.double", "back.triple", "back.hold", "both.chord", "front.left", "front.right", "front.middle", "front.wheelup", "front.wheeldown", "back.left", "back.right", "back.middle", "back.wheelup", "back.wheeldown", "both.hold" };
        private static readonly string[] DefaultDescriptions = { "打开 Codex / 返回之前的程序", "删除（Delete）", "取消 / 退出（Esc）", "显示前键功能提示", "整个宠物浮窗：显示 / 隐藏", "撤销（Ctrl+Z）", "确认 / 回车（Enter）", "显示后键功能提示", "继续同时按住 3 秒查看使用说明", "复制选中内容", "粘贴", "静音 / 恢复", "增大音量", "减小音量", "确认 / 发送（Enter）", "取消 / 退出（Esc）", "显示桌面 / 恢复窗口", "选择上一个软件窗口，松开确认", "选择下一个软件窗口，松开确认", "显示按键使用说明，5 秒后关闭" };
        private static readonly string[] DefaultShortcuts = { "CodexToggle", "Delete", "Esc", "hint", "PetToggle", "Ctrl+Z", "Enter", "hint", "hint", "Ctrl+C", "Ctrl+V", "Mute", "VolumeUp", "VolumeDown", "Enter", "Esc", "Win+D", "WindowPrevious", "WindowNext", "ShowHelp" };
        public void Validate()
        {
            if (ClickMs < 100 || ClickMs > 800 || HoldMs < 300 || HoldMs > 3000 || ChordMs < 20 || ChordMs > 300 || ChordMs >= HoldMs)
                throw new InvalidDataException("时间参数不在允许范围内");
            if (Descriptions == null) Descriptions = new Dictionary<string, string>();
            if (Shortcuts == null) Shortcuts = new Dictionary<string, string>();
            if (SofaBindingsVersion < 1)
            {
                Shortcuts["back.triple"] = "Win+D"; Descriptions["back.triple"] = "显示桌面 / 恢复原窗口";
                Shortcuts["back.wheelup"] = "WindowPrevious"; Descriptions["back.wheelup"] = "选择上一个软件窗口，松开确认";
                Shortcuts["back.wheeldown"] = "WindowNext"; Descriptions["back.wheeldown"] = "选择下一个软件窗口，松开确认";
                Shortcuts["both.chord"] = "hint"; Descriptions["both.chord"] = "继续同时按住 3 秒查看使用说明";
                SofaBindingsVersion = 1;
            }
            if (SofaBindingsVersion < 2)
            {
                Shortcuts["front.double"] = "CodexToggle"; Descriptions["front.double"] = "打开 Codex / 返回之前的程序";
                Shortcuts["back.middle"] = "Win+D"; Descriptions["back.middle"] = "显示桌面 / 恢复窗口";
                SofaBindingsVersion = 2;
            }
            if (SofaBindingsVersion < 3)
            {
                Shortcuts["front.single"] = "CodexToggle"; Descriptions["front.single"] = "打开 Codex / 返回之前的程序";
                Shortcuts["front.double"] = ""; Descriptions["front.double"] = "未分配（Codex 已改为单击）";
                Shortcuts["back.single"] = "Win+Alt+P"; Descriptions["back.single"] = "整个宠物浮窗：显示 / 隐藏";
                SofaBindingsVersion = 3;
            }
            if (SofaBindingsVersion < 4)
            {
                Shortcuts["back.single"] = "PetToggle";
                Descriptions["back.single"] = "整个宠物浮窗：显示 / 隐藏";
                SofaBindingsVersion = 4;
            }
            if (SofaBindingsVersion < 5)
            {
                Shortcuts["front.double"] = "Delete"; Descriptions["front.double"] = "删除（Delete）";
                Shortcuts["back.double"] = "Ctrl+Z"; Descriptions["back.double"] = "撤销（Ctrl+Z）";
                SofaBindingsVersion = 5;
            }
            if (SofaBindingsVersion < 6)
            {
                Shortcuts["both.hold"] = "ShowHelp"; Descriptions["both.hold"] = "显示按键使用说明，5 秒后关闭";
                Descriptions["both.chord"] = "继续同时按住 3 秒查看使用说明";
                SofaBindingsVersion = 6;
            }
            if (SofaBindingsVersion < 7)
            {
                Shortcuts["front.triple"] = "Esc"; Descriptions["front.triple"] = "取消 / 退出（Esc）";
                Shortcuts["back.triple"] = "Enter"; Descriptions["back.triple"] = "确认 / 回车（Enter）";
                SofaBindingsVersion = 7;
            }
            for (int i = 0; i < Codes.Length; i++)
            {
                string code = Codes[i];
                if (!Descriptions.ContainsKey(code)) Descriptions[code] = DefaultDescriptions[i];
                if (!Shortcuts.ContainsKey(code)) Shortcuts[code] = DefaultShortcuts[i];
                Hotkeys.Parse(Shortcuts[code]);
            }
        }
    }
    internal sealed class DemoStep { public long Time; public int Button; public bool Down; public string Kind; }

    public sealed class MainForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(27, 40, 61), Muted = Color.FromArgb(98, 113, 132), Blue = Color.FromArgb(34, 88, 224);
        private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplaySceneSwitcher", "mouse");
        private readonly JsonCodec json = new JsonCodec();
        private readonly bool startInput;
        private string configError;
        private Settings config;
        private NativeMouse mouse;
        private NativeMouse demoInput;
        private readonly bool[] sideInjected = new bool[2];
        private GestureEngine engine, demoEngine;
        private readonly DoubleHoldBackspace backspaceRepeat = new DoubleHoldBackspace();
        private readonly GestureHelpForm gestureHelp = new GestureHelpForm();
        private int backspaceCount;
        private bool listening = true, demoRunning, closing, loaded, previousListening;
        private int generation;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Dictionary<string, Label> cards = new Dictionary<string, Label>();
        private readonly Dictionary<string, int> realCounts = new Dictionary<string, int>(), demoCounts = new Dictionary<string, int>();
        private readonly List<GestureEvent> records = new List<GestureEvent>();
        private readonly BlockingCollection<GestureEvent> actionQueue = new BlockingCollection<GestureEvent>(128);
        private readonly Label status = new Label(), heroTitle = new Label(), heroDetail = new Label(), coverage = new Label(), rawStatus = new Label();
        private readonly Label frontState = new Label(), backState = new Label();
        private readonly ProgressBar frontBar = new ProgressBar(), backBar = new ProgressBar();
        private readonly ListView history = new ListView();
        private readonly ListView rawHistory = new ListView();
        private readonly InputDiagnostics inputDiagnostics = new InputDiagnostics();
        private readonly InputDiagnostics forwardedDiagnostics = new InputDiagnostics();
        private readonly Label compatibility = new Label();
        private readonly Button pause = new Button(), demo = new Button(), clear = new Button();
        private readonly CheckBox suppress = new CheckBox(), swap = new CheckBox(), pin = new CheckBox(), popup = new CheckBox();
        private readonly CheckBox execute = new CheckBox();
        private readonly CheckBox driverCompatibility = new CheckBox();
        private readonly CheckBox volumeWheel = new CheckBox();
        private readonly CheckBox soundPopup = new CheckBox();
        private readonly CheckBox sofaGestures = new CheckBox();
        private readonly WindowCycle windowCycle = new WindowCycle();
        private readonly AppToggle codexToggle = new AppToggle(new DisplaySceneSwitcher.CodexWindows());
        private readonly PetToggle petToggle = new PetToggle(new DisplaySceneSwitcher.PetWindows());
        public event Action ToggleDisplayRequested;
        public event Action InputStatusChanged;
        public bool IsPaused { get { return !listening; } }
        public bool GestureHintsEnabled { get { return config.ShowGestureToasts; } }
        public string InputSummary { get { return !listening ? "鼠标映射已暂停" : config.SofaGesturesEnabled ? "沙发手势已开启" : config.VolumeWheelEnabled ? "前侧键 + 滚轮：音量已开启" : "鼠标测试模式"; } }
        public void SetPaused(bool paused) { listening = !paused; ResetInput(); }
        private readonly ListView bindings = new ListView();
        private CommandPanel panel;
        private readonly NumericUpDown clickMs = new NumericUpDown(), holdMs = new NumericUpDown(), chordMs = new NumericUpDown();
        private readonly Stopwatch demoClock = new Stopwatch();
        private readonly List<DemoStep> demoSteps = new List<DemoStep>();
        private int demoIndex;
        private long demoEnd;
        private Toast toast;
        private string sessionLog;
        public static string TitleFor(string code)
        {
            if (code == "both.chord") return "双键 · 同时按下";
            if (code == "both.hold") return "双键 · 按住 3 秒";
            string[] pieces = code.Split('.');
            string key = pieces[0] == "front" ? "前侧键" : "后侧键";
            string kind;
            switch (pieces[1])
            {
                case "single": kind = "单击"; break; case "double": kind = "双击"; break;
                case "triple": kind = "三击"; break; case "hold": kind = "长按提示"; break;
                case "left": kind = "+ 左键"; break; case "right": kind = "+ 右键"; break;
                case "middle": kind = "+ 按滚轮"; break; case "wheelup": kind = "+ 滚轮向上"; break;
                default: kind = "+ 滚轮向下"; break;
            }
            return key + " · " + kind;
        }
        public MainForm(bool enableInput = true, string diagnosticFolder = null)
        {
            if (diagnosticFolder != null)
            {
                if (enableInput) throw new ArgumentException("诊断目录只用于不连接系统输入的实例");
                folder = diagnosticFolder;
            }
            startInput = enableInput;
            SuspendLayout();
            try
            {
                string path = Path.Combine(folder, "settings.json");
                config = File.Exists(path) ? json.Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8)) : new Settings();
                if (config == null) throw new InvalidDataException("配置文件为空");
                config.Validate();
            }
            catch (Exception ex) { config = new Settings(); config.Validate(); configError = "配置读取失败，已用默认值：" + ex.Message; }
            foreach (string code in Settings.Codes) { realCounts[code] = 0; demoCounts[code] = 0; }
            Text = "鼠标与音量 · MX Anywhere 3";
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = Ink; BackColor = Color.FromArgb(243, 246, 250);
            // Embedded page shares the host's pixel layout; avoid scaling it again on high-DPI TVs.
            AutoScaleMode = AutoScaleMode.None;
            AutoScroll = true;
            ClientSize = new Size(1110, 790); MinimumSize = new Size(1000, 780);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = config.AlwaysOnTop;
            BuildLayout();
            ResumeLayout(true);
            toast = new Toast();
            Shown += delegate { EnsureStarted(); };
            timer.Interval = 15; timer.Tick += Tick;
            var worker = new Thread(delegate()
            {
                foreach (GestureEvent e in actionQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        bool volumeAction = e.Shortcut == "VolumeUp" || e.Shortcut == "VolumeDown" || e.Shortcut == "Mute";
                        if (!volumeAction && e.Shortcut != "Win+D" && e.TargetWindow != IntPtr.Zero && Hotkeys.GetForegroundWindow() != e.TargetWindow)
                            throw new InvalidOperationException("目标窗口已变化，本次动作已取消");
                        if (volumeAction)
                        {
                            var result = e.Shortcut == "Mute" ? DisplaySceneSwitcher.AudioVolume.ToggleMuteDefault() : DisplaySceneSwitcher.AudioVolume.AdjustDefault(e.Shortcut == "VolumeUp" ? 2 : -2);
                            if (!closing && IsHandleCreated) BeginInvoke((Action)delegate
                            {
                                string feedback = e.Shortcut == "Mute" ? (result.Muted ? "已静音" : "已恢复声音") + " · 音量 " + result.Percent + "%" : "音量 " + result.Percent + "%" + (result.Muted ? "（静音中）" : "");
                                heroTitle.Text = feedback; heroDetail.Text = "当前系统声音输出 · 前键 + 滚轮调音量 / 按滚轮静音";
                                if (config.SoundHintsEnabled) toast.Display(feedback, "前键 + 按滚轮：静音 / 恢复");
                            });
                        }
                        else Hotkeys.Send(e.Shortcut);
                        LogAction(e, "sent", null);
                        DeveloperActions.Handle(e);
                    }
                    catch (Exception ex)
                    {
                        LogAction(e, "failed", ex.Message);
                        if (!closing && IsHandleCreated)
                            try { BeginInvoke((Action)delegate { Notice("自定义动作失败：" + ex.Message); }); }
                            catch (InvalidOperationException) { }
                    }
                }
            });
            worker.IsBackground = true; worker.Name = "Custom gesture actions"; worker.Start();
            FormClosing += delegate
            {
                closing = true; timer.Stop();
                codexToggle.CancelPending(); petToggle.CancelPending();
                FinishWindowCycle();
                if (hardwareChordReader != null) hardwareChordReader.Dispose();
                if (mouse != null) mouse.Dispose();
                if (demoInput != null) demoInput.Dispose();
                actionQueue.CompleteAdding(); toast.Close(); gestureHelp.Dispose(); backspaceRepeat.Cancel();
                if (panel != null) panel.Close();
            };
        }
        public void EnsureStarted()
        {
            if (loaded || !startInput) return;
            try
            {
                IntPtr handle = Handle;
                Directory.CreateDirectory(Path.Combine(folder, "logs"));
                mouse = new NativeMouse(); mouse.Start(); loaded = true; ResetInput(); timer.Start();
                StartHardwareChordReader();
                File.AppendAllText(Path.Combine(folder, "logs", "lifecycle.jsonl"), json.Serialize(new { Timestamp = DateTimeOffset.Now.ToString("o"), Event = "started", Version = "2.4.6", RawInputAvailable = mouse.RawInputAvailable, SofaGesturesEnabled = config.SofaGesturesEnabled, DriverCompatibility = config.DriverCompatibility, VolumeWheelEnabled = config.VolumeWheelEnabled }) + Environment.NewLine, new UTF8Encoding(false));
                if (configError != null) Notice(configError); SaveSettings();
            }
            catch (Exception ex)
            {
                mouse?.Dispose(); mouse = null; listening = false;
                status.Text = "监听启动失败"; status.ForeColor = Color.Firebrick;
                heroTitle.Text = "未能连接鼠标事件"; heroDetail.Text = ex.Message;
                pause.Enabled = false; demo.Enabled = false;
            }
        }
        private DisplaySceneSwitcher.LogitechChordReader hardwareChordReader;
        private void StartHardwareChordReader()
        {
            NativeMouse input = mouse;
            hardwareChordReader = new DisplaySceneSwitcher.LogitechChordReader(
                (both, any) => input.RecordHardwareChord(both, any, input.Now),
                () => input.HardwareChordDisconnected(input.Now),
                message => {
                    try { File.AppendAllText(Path.Combine(folder, "logs", "hid-chord.jsonl"),
                        json.Serialize(new { Timestamp = DateTimeOffset.Now.ToString("o"), Message = message }) + Environment.NewLine, new UTF8Encoding(false)); }
                    catch (IOException) { }
                });
        }
        internal object DiagnoseHelpPipeline(bool hardware = false)
        {
            if (startInput) throw new InvalidOperationException("仅允许隔离诊断实例");
            mouse = new NativeMouse(); loaded = true; ResetInput();
            long began = mouse.Now;
            mouse.ProcessSide(1, true, 1, began);
            bool second = false, released = false, observed = false, early = false;
            long shownAt = -1, hiddenAt = -1;
            try
            {
                while (mouse.Now - began < 8600)
                {
                    long elapsed = mouse.Now - began;
                    if (!second && elapsed >= 120)
                    {
                        if (hardware) mouse.RecordHardwareChord(true, true, mouse.Now);
                        mouse.ProcessSide(2, true, 1, mouse.Now); second = true;
                    }
                    if (!released && elapsed >= 3600)
                    {
                        if (hardware) mouse.RecordHardwareChord(false, false, mouse.Now);
                        mouse.ProcessSide(1, false, 1, mouse.Now); mouse.ProcessSide(2, false, 1, mouse.Now); released = true;
                    }
                    Tick(this, EventArgs.Empty); Application.DoEvents();
                    if (gestureHelp.IsDisplayed)
                    { if (shownAt < 0) shownAt = elapsed; observed = true; if (elapsed < 3000) early = true; }
                    else if (observed && hiddenAt < 0) hiddenAt = elapsed;
                    Thread.Sleep(10);
                }
                if (early || !observed || shownAt < 3100 || hiddenAt - shownAt < 4900 || hiddenAt - shownAt > 5300 || realCounts["both.hold"] != 1)
                    throw new InvalidOperationException("双键到帮助窗口全流程失败: shown=" + shownAt + ", hidden=" + hiddenAt);
                return new { Passed = true, ShownAtMs = shownAt, HiddenAtMs = hiddenAt, HelpCount = realCounts["both.hold"],
                    HardwareReportReplay = hardware, Path = "NativeMouse -> MainForm.OnGesture -> GestureHelpForm", OsMouseInputInjected = false };
            }
            finally { mouse.Dispose(); mouse = null; gestureHelp.Dismiss(); }
        }
        private Label TextLabel(string text, float size, Color color, bool bold)
        { return new Label { Text = text, AutoSize = false, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }; }
        private Button MakeButton(string text, int width)
        {
            return new Button { Text = text, Width = Math.Max(width, 120), Height = 40, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink, Margin = new Padding(0, 0, 9, 0), Cursor = Cursors.Hand };
        }
        private void CopyButton(Button to, string text, int width)
        { to.Text = text; to.Width = Math.Max(width, 120); to.Height = 40; to.FlatStyle = FlatStyle.Flat; to.BackColor = Color.White; to.Margin = new Padding(0, 0, 9, 0); to.Cursor = Cursors.Hand; }
        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(22, 12, 22, 12), ColumnCount = 1, RowCount = 11 };
            int[] heights = { 75, 42, 42, 42, 115, 60, 190, 32, 32, 300, 32 };
            for (int i = 0; i < heights.Length; i++) root.RowStyles.Add(new RowStyle(i >= 1 && i <= 3 ? SizeType.AutoSize : SizeType.Absolute, heights[i]));
            Controls.Add(root);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 76)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            header.Controls.Add(TextLabel("鼠标与音量", 16, Ink, true), 0, 0);
            header.Controls.Add(TextLabel("20 种基础手势 + 连续退格", 9, Muted, false), 0, 1);
            status.Text = "正在连接…"; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleRight; status.Font = new Font(Font, FontStyle.Bold); status.ForeColor = Blue;
            header.Controls.Add(status, 1, 0); header.SetRowSpan(status, 2); root.Controls.Add(header, 0, 0);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            CopyButton(pause, "暂停监听", 108); pause.BackColor = Blue; pause.ForeColor = Color.White;
            pause.Click += delegate { listening = !listening; ResetInput(); };
            CopyButton(demo, "自测 20 种手势", 165); demo.Click += delegate { StartDemo(); };
            CopyButton(clear, "重置测试", 100); clear.Click += delegate { ClearResults(); };
            Button export = MakeButton("导出记录", 100); export.Click += delegate { Export(); };
            actions.Controls.AddRange(new Control[] { pause, demo, clear, export });
            Button openPanel = MakeButton("操作面板", 100);
            openPanel.Click += delegate { ShowPanel(Hotkeys.GetForegroundWindow()); }; actions.Controls.Add(openPanel);
            execute.Text = "启用实际操作"; execute.AutoSize = true; execute.Margin = new Padding(5, 7, 5, 0);
            execute.CheckedChanged += delegate
            {
                if (execute.Checked) suppress.Checked = true;
                ResetInput();
                heroTitle.Text = execute.Checked ? "全部实际操作已开启" : InputSummary;
                heroDetail.Text = execute.Checked ? "先点击要操作的软件，再按侧键。游戏时可点“暂停监听”。" : "音量与沙发手势由下方独立开关控制；其他手势显示测试说明。";
            };
            actions.Controls.Add(execute);
            pin.Text = "窗口置顶"; pin.AutoSize = true; pin.Checked = config.AlwaysOnTop; pin.Margin = new Padding(8, 7, 8, 0);
            pin.CheckedChanged += delegate { config.AlwaysOnTop = pin.Checked; TopMost = pin.Checked; SaveSettings(); };
            popup.Text = "全部操作提示"; popup.AutoSize = true; popup.Checked = config.ShowGestureToasts; popup.Margin = new Padding(8, 7, 0, 0);
            popup.CheckedChanged += delegate {
                config.ShowGestureToasts = popup.Checked;
                if (!popup.Checked && toast != null) toast.Hide();
                SaveSettings();
            };
            actions.Controls.Add(popup); root.Controls.Add(actions, 0, 1);

            var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            AddTiming(settings, "连击间隔", clickMs, 100, 800, config.ClickMs);
            AddTiming(settings, "长按时长", holdMs, 300, 3000, config.HoldMs);
            chordMs.Minimum = 20; chordMs.Maximum = 300; chordMs.Value = config.ChordMs;
            suppress.Text = "接管侧键"; suppress.AutoSize = true; suppress.Checked = config.SuppressNavigation; suppress.Margin = new Padding(12, 6, 0, 0);
            swap.Text = "交换前后键"; swap.AutoSize = true; swap.Checked = config.SwapButtons; swap.Margin = new Padding(12, 6, 0, 0);
            settings.Controls.Add(suppress); settings.Controls.Add(swap); root.Controls.Add(settings, 0, 2);
            driverCompatibility.Text = "兼容驱动转发"; driverCompatibility.AutoSize = true; driverCompatibility.Checked = config.DriverCompatibility;
            driverCompatibility.Margin = new Padding(12, 6, 0, 0); settings.Controls.Add(driverCompatibility);
            clickMs.ValueChanged += SettingsChanged; holdMs.ValueChanged += SettingsChanged; chordMs.ValueChanged += SettingsChanged;
            suppress.CheckedChanged += SettingsChanged; swap.CheckedChanged += SettingsChanged;
            driverCompatibility.CheckedChanged += SettingsChanged;
            var volumeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            volumeWheel.Text = "前侧键 + 滚轮调音量（直接生效）"; volumeWheel.AutoSize = true; volumeWheel.Checked = config.VolumeWheelEnabled;
            volumeWheel.Margin = new Padding(0, 6, 16, 0); volumeWheel.ForeColor = Blue;
            volumeRow.Controls.Add(volumeWheel);
            volumeRow.Controls.Add(new Label { Text = "向上 +2% · 向下 −2% · 松开恢复页面滚动", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
            soundPopup.Text = "声音提醒"; soundPopup.AutoSize = true; soundPopup.Checked = config.ShowSoundToasts;
            soundPopup.Margin = new Padding(16, 6, 0, 0); volumeRow.Controls.Add(soundPopup);
            soundPopup.CheckedChanged += delegate {
                config.ShowSoundToasts = soundPopup.Checked;
                if (!config.SoundHintsEnabled && toast != null) toast.Hide();
                SaveSettings();
            };
            root.Controls.Add(volumeRow, 0, 3);
            volumeWheel.CheckedChanged += delegate { config.VolumeWheelEnabled = volumeWheel.Checked; if (volumeWheel.Checked) suppress.Checked = true; ResetInput(); SaveSettings(); };
            volumeRow.SetFlowBreak(volumeRow.Controls[volumeRow.Controls.Count - 1], true);
            sofaGestures.Text = "Codex / 宠物 / 静音 / 窗口 / 桌面 / 连续退格 / 使用说明（直接生效）"; sofaGestures.AutoSize = true;
            sofaGestures.Checked = config.SofaGesturesEnabled; sofaGestures.ForeColor = Blue; sofaGestures.Margin = new Padding(0, 6, 16, 0);
            volumeRow.Controls.Add(sofaGestures);
            sofaGestures.CheckedChanged += delegate { config.SofaGesturesEnabled = sofaGestures.Checked; if (sofaGestures.Checked) suppress.Checked = true; ResetInput(); SaveSettings(); };

            var hero = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(229, 237, 255), Padding = new Padding(20, 8, 20, 8), ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 9) };
            hero.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); hero.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            heroTitle.Text = config.SofaGesturesEnabled ? "前键切 Codex · 后键开关宠物" : config.VolumeWheelEnabled ? "按住前侧键，滚动调音量" : "按一下鼠标侧键，开始测试"; heroTitle.Dock = DockStyle.Fill; heroTitle.Font = new Font(Font.FontFamily, 18, FontStyle.Bold); heroTitle.ForeColor = Blue; heroTitle.TextAlign = ContentAlignment.MiddleLeft;
            heroDetail.Text = "前键+按轮：静音；后键+按轮：桌面；后键+滚动：窗口；双键3秒：使用说明。"; heroDetail.Dock = DockStyle.Fill; heroDetail.TextAlign = ContentAlignment.MiddleLeft;
            hero.Controls.Add(heroTitle, 0, 0); hero.Controls.Add(heroDetail, 0, 1); root.Controls.Add(hero, 0, 4);

            var states = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
            states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); states.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            states.RowStyles.Add(new RowStyle(SizeType.Percent, 70)); states.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            frontState.Dock = backState.Dock = DockStyle.Fill; frontState.TextAlign = backState.TextAlign = ContentAlignment.MiddleLeft;
            frontState.Font = backState.Font = new Font(Font, FontStyle.Bold);
            frontBar.Dock = backBar.Dock = DockStyle.Fill; frontBar.Maximum = backBar.Maximum = 100;
            states.Controls.Add(frontState, 0, 0); states.Controls.Add(backState, 1, 0); states.Controls.Add(frontBar, 0, 1); states.Controls.Add(backBar, 1, 1); root.Controls.Add(states, 0, 5);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Margin = Padding.Empty };
            for (int i = 0; i < 5; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < 9; i++)
            {
                string code = i == 8 ? "both.hold" : Settings.Codes[i];
                var card = new Label { Dock = DockStyle.Fill, BackColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, 8, 8), Font = new Font(Font.FontFamily, 9F), AccessibleName = TitleFor(code) };
                cards.Add(code, card);
                if (i == 8) { grid.Controls.Add(card, 4, 0); grid.SetRowSpan(card, 2); }
                else grid.Controls.Add(card, i % 4, i / 4);
            }
            root.Controls.Add(grid, 0, 6);
            coverage.Dock = DockStyle.Fill; coverage.TextAlign = ContentAlignment.MiddleLeft; coverage.Font = new Font(Font, FontStyle.Bold); root.Controls.Add(coverage, 0, 7);
            rawStatus.Text = "原始输入：等待侧键按下 / 松开"; rawStatus.Dock = DockStyle.Fill; rawStatus.ForeColor = Muted; rawStatus.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(rawStatus, 0, 8);
            history.Dock = DockStyle.Fill; history.View = View.Details; history.FullRowSelect = true; history.GridLines = false; history.BorderStyle = BorderStyle.None; history.HideSelection = false;
            history.Columns.Add("时间", 105); history.Columns.Add("来源", 68); history.Columns.Add("识别结果", 160); history.Columns.Add("事件代码", 130); history.Columns.Add("测试说明", 500);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var logPage = new TabPage("事件记录"); logPage.Controls.Add(history); tabs.TabPages.Add(logPage);
            var mapPage = new TabPage("全部功能分配 / 组合键测试");
            bindings.Dock = DockStyle.Fill; bindings.View = View.Details; bindings.FullRowSelect = true; bindings.BorderStyle = BorderStyle.None;
            bindings.Columns.Add("手势", 205); bindings.Columns.Add("分配功能", 295); bindings.Columns.Add("快捷键", 165); bindings.Columns.Add("接收 / 自测次数", 180);
            foreach (string code in Settings.Codes)
            {
                var row = new ListViewItem(TitleFor(code)) { Tag = code };
                row.SubItems.Add(config.Descriptions[code]); row.SubItems.Add(config.Shortcuts[code]); row.SubItems.Add("0 / 0"); bindings.Items.Add(row);
            }
            var repeatRow = new ListViewItem("前侧键 · 双击后按住");
            repeatRow.SubItems.Add("每 500 毫秒退格，松开即停"); repeatRow.SubItems.Add("Backspace"); repeatRow.SubItems.Add("0 次退格"); bindings.Items.Add(repeatRow);
            mapPage.Controls.Add(bindings); tabs.TabPages.Add(mapPage); root.Controls.Add(tabs, 0, 9);
            var rawPage = new TabPage("原始按下 / 松开记录");
            rawHistory.Dock = DockStyle.Fill; rawHistory.View = View.Details; rawHistory.FullRowSelect = true; rawHistory.BorderStyle = BorderStyle.None;
            rawHistory.Columns.Add("时间", 125); rawHistory.Columns.Add("侧键 / 来源", 195); rawHistory.Columns.Add("原始事件", 120); rawHistory.Columns.Add("系统上报按住时长", 180); rawHistory.Columns.Add("说明", 450);
            rawPage.Controls.Add(rawHistory); tabs.TabPages.Add(rawPage);
            var helpPage = new TabPage("长按 / 双键排查");
            var help = new Label { Dock = DockStyle.Fill, Padding = new Padding(14), AutoSize = false, Text =
                "MX Anywhere 3 的横向滚动功能会让侧键等到松开才发送点击。\r\n" +
                "请打开 Logi Options+ → MX Anywhere 3 → 指向和滚动 / Point & Scroll → 横向滚动 / Horizontal Scroll，关闭前后两键的横向滚动。\r\n" +
                "侧键本身保持标准前进 / 后退，不要另设成键盘快捷键。然后回到此工具，按住任一侧键 2 秒。\r\n" +
                "正常时，按住中的数字会持续增长；长按提示应在 650 ms 左右出现。\r\n" +
                "双键测试：按住一个侧键不放，再按下另一个，两个键有重叠即可；任一键松开就退出组合状态。\r\n" +
                "1.3：测试模式不拦截左右键、滚轮；本工具及操作面板始终可点击。普通组合按住达 5 秒自动释放；连续退格由实体按键状态校验。\r\n" +
                "1.2 默认兼容驱动转发：软件转发事件也能识别，但会标明来源，不能当作已确认的实体鼠标输入。\r\n" +
                "自测会覆盖接收处理、计时和屏幕提示，不向系统发送模拟点击，不验证鼠标固件或蓝牙链路。\r\n" +
                "若仍只在松开时产生事件，请查看“原始按下 / 松开记录”；0–5 ms 的短脉冲无法反推出真正按住多久。" };
            helpPage.Controls.Add(help); tabs.TabPages.Add(helpPage);
            compatibility.Dock = DockStyle.Fill; compatibility.ForeColor = Muted; compatibility.TextAlign = ContentAlignment.MiddleLeft;
            compatibility.Text = "普通左右键照常使用；连续退格松开即停；双键按住 3 秒查看使用说明。";
            root.Controls.Add(compatibility, 0, 10);
            UpdateCards();
        }
        private void AddTiming(FlowLayoutPanel row, string title, NumericUpDown n, int min, int max, int value)
        {
            row.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 6, 5, 0) });
            n.Minimum = min; n.Maximum = max; n.Value = value; n.Increment = 25; n.Width = 90; n.Margin = new Padding(0, 3, 3, 0);
            row.Controls.Add(n); row.Controls.Add(new Label { Text = "ms", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 6, 12, 0) });
        }
        private void SettingsChanged(object sender, EventArgs e)
        {
            config.ClickMs = (int)clickMs.Value; config.HoldMs = (int)holdMs.Value;
            if (chordMs.Value >= holdMs.Value) chordMs.Value = holdMs.Value - 1;
            config.ChordMs = (int)chordMs.Value; config.SuppressNavigation = suppress.Checked; config.SwapButtons = swap.Checked;
            config.DriverCompatibility = driverCompatibility.Checked;
            if (!suppress.Checked) { execute.Checked = false; volumeWheel.Checked = false; sofaGestures.Checked = false; }
            ResetInput(); SaveSettings();
        }
        private void SaveSettings()
        {
            if (!loaded) return;
            try { File.WriteAllText(Path.Combine(folder, "settings.json"), json.Serialize(config), new UTF8Encoding(false)); }
            catch (Exception ex) { Notice("设置未保存：" + ex.Message); }
        }
        private void ResetInput()
        {
            codexToggle.CancelPending(); petToggle.CancelPending();
            backspaceRepeat.Cancel(); gestureHelp.Dismiss();
            FinishWindowCycle();
            if (mouse == null || !loaded) return;
            generation = mouse.Configure(listening && !demoRunning, config.SuppressNavigation, config.DriverCompatibility, execute.Checked && !demoRunning, config.VolumeWheelEnabled && !demoRunning ? FrontButton : 0, config.SofaGesturesEnabled && !demoRunning ? FrontButton : 0, config.SofaGesturesEnabled && !demoRunning ? 3 - FrontButton : 0, config.SofaGesturesEnabled && !demoRunning ? 3 - FrontButton : 0);
            inputDiagnostics.Reset(); forwardedDiagnostics.Reset();
            engine = new GestureEngine(config.ClickMs, config.HoldMs, config.ChordMs);
            engine.Recognized += delegate(Gesture g) { OnGesture(g, false); };
            pause.Text = listening ? "暂停监听" : "开始监听";
            status.Text = demoRunning ? "●  模拟演示中" : listening ? (execute.Checked ? "●  全部操作开启" : config.SofaGesturesEnabled ? "●  沙发手势已开启" : config.VolumeWheelEnabled ? "●  音量控制开启" : "●  测试模式 · 正在监听") : "○  已暂停 · 侧键正常使用";
            status.ForeColor = listening ? Blue : Muted;
            UpdateStates();
            InputStatusChanged?.Invoke();
        }
        private void Tick(object sender, EventArgs e)
        {
            if (mouse == null) return;
            codexToggle.ObserveForeground();
            long now;
            MousePacket[] batch = mouse.Drain(out now);
            foreach (MousePacket observed in batch) TrackRawInput(observed);
            if (!demoRunning && listening)
            {
                foreach (MousePacket p in batch)
                {
                    if (p.Generation != generation || p.ObservedOnly) continue;
                    if (p.Kind == "reset") { FinishWindowCycle(); backspaceRepeat.Cancel(); mouse.SetRepeatHold(0, 0); engine.Reset(); Notice(p.ResetReason); continue; }
                    if (p.Kind == "hardwarechordhold")
                    { OnGesture(new Gesture(0, "chordhold", p.TimeMs, GestureEngine.ChordHoldMs), false, true); continue; }
                    if (p.Kind == null && ((!p.Down && p.Button == 3 - FrontButton) || (p.Down && p.Button == FrontButton))) FinishWindowCycle();
                    if (p.Kind == null && p.Down) sideInjected[p.Button - 1] = p.Injected;
                    // Release and modifier packets must cancel before GestureEngine.Advance,
                    // which otherwise could turn a delayed release into a double-click Delete.
                    backspaceRepeat.Input(engine, FrontButton, p, Hotkeys.GetForegroundWindow(), config.SofaGesturesEnabled);
                    mouse.SetRepeatHold(backspaceRepeat.Armed ? FrontButton : 0, backspaceRepeat.StartedAt);
                }
                RepeatDecision repeat = backspaceRepeat.Advance(engine, FrontButton, now, Hotkeys.GetForegroundWindow(), mouse.IsRepeatHeld(FrontButton, backspaceRepeat.StartedAt));
                if (repeat == RepeatDecision.Cancelled) mouse.SetRepeatHold(0, 0);
                if (repeat == RepeatDecision.Backspace)
                {
                    try
                    {
                        // Bounded synchronous key pair: never queue trailing deletes after release.
                        if (Hotkeys.GetForegroundWindow() == backspaceRepeat.Target && mouse.IsRepeatHeld(FrontButton, backspaceRepeat.StartedAt))
                        { Hotkeys.Send("Backspace"); RecordBackspace(now); }
                        else { backspaceRepeat.Cancel(); mouse.SetRepeatHold(0, 0); }
                    }
                    catch (Exception ex) { backspaceRepeat.Cancel(); mouse.SetRepeatHold(0, 0); Notice("连续退格已停止：" + ex.Message); }
                }
                engine.Advance(now);
            }
            if (demoRunning)
            {
                long demoNow = demoClock.ElapsedMilliseconds;
                while (demoIndex < demoSteps.Count && demoSteps[demoIndex].Time <= demoNow)
                {
                    DemoStep step = demoSteps[demoIndex++];
                    if (step.Kind == null)
                    {
                        demoInput.ProcessSide(step.Button, step.Down, 1, step.Time);
                        MousePacket replay;
                        while (demoInput.TryRead(out replay))
                            if (!replay.ObservedOnly)
                            {
                                if (replay.Kind == "reset") demoEngine.Reset();
                                else demoEngine.Input(replay.Button, replay.Down, replay.TimeMs);
                            }
                    }
                    else demoEngine.Combo(step.Button, step.Kind, step.Time);
                }
                demoEngine.Advance(demoNow);
                if (demoNow >= demoEnd)
                {
                    demoRunning = false; demoClock.Stop(); SetDemoControls(true);
                    listening = previousListening; ResetInput();
                    heroTitle.Text = "输入处理自测完成 · " + demoCounts.Count(x => x.Value > 0) + " / 20";
                    heroDetail.Text = "已测试驱动事件处理、手势计时和提示；实体鼠标、蓝牙及驱动上报仍需实际输入验证。";
                    rawStatus.Text = "原始输入：等待真实鼠标侧键按下 / 松开";
                }
            }
            UpdateStates();
        }
        private int FrontButton { get { return config.SwapButtons ? 1 : 2; } }
        private void RecordBackspace(long now)
        {
            backspaceCount++;
            heroTitle.Text = "前键双击按住 · 连续退格";
            heroDetail.Text = "每 500 毫秒发送 Backspace；松开即停。累计 " + backspaceCount + " 次。";
            var e = new GestureEvent { Timestamp = DateTimeOffset.Now.ToString("o"), Code = "front.doublehold", Title = "前侧键 · 双击后按住", Description = "连续退格（500 ms）", Shortcut = "Backspace", ExecuteShortcut = true, PhysicalButton = FrontButton, HeldMs = now - backspaceRepeat.StartedAt, TargetWindow = backspaceRepeat.Target, Source = sideInjected[FrontButton - 1] ? "forwarded" : "non-injected" };
            records.Add(e); if (records.Count > 5000) records.RemoveAt(0);
            var row = new ListViewItem(DateTime.Now.ToString("HH:mm:ss.fff"));
            row.SubItems.Add("接收"); row.SubItems.Add(e.Title); row.SubItems.Add(e.Code); row.SubItems.Add(e.Description);
            history.Items.Insert(0, row); if (history.Items.Count > 300) history.Items.RemoveAt(300);
            UpdateCards();
            try
            {
                if (sessionLog == null) sessionLog = Path.Combine(folder, "logs", "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
                File.AppendAllText(sessionLog, json.Serialize(e) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) { Notice("记录写入失败：" + ex.Message); }
        }
        private void TrackRawInput(MousePacket p)
        {
            InputDiagnostics diagnostics = p.Injected ? forwardedDiagnostics : inputDiagnostics;
            long duration = p.Kind == null ? diagnostics.Record(p.Button, p.Down, p.TimeMs) : -1;
            string type = p.Kind ?? (p.Down ? "按下" : "松开");
            string detail = p.Kind == "reset" ? p.ResetReason : duration < 0 ? "" : duration <= 5 ? "短脉冲；若实际长按，检查横向滚动" : "已收到持续按住状态";
            detail = (p.ObservedOnly ? "仅记录（过滤 / 重复 / 暂停）  " : "已接收  ") + detail;
            string source = p.Kind == "reset" ? "状态保护" : p.Injected ? "软件转发" : "非注入输入";
            rawStatus.Text = "原始输入：XBUTTON" + p.Button + "  " + type + (duration < 0 ? "" : "  ·  系统上报按住 " + duration + " ms");
            var row = new ListViewItem(DateTime.Now.ToString("HH:mm:ss.fff"));
            row.SubItems.Add("XBUTTON" + p.Button + " / " + source); row.SubItems.Add(type); row.SubItems.Add(duration < 0 ? "—" : duration + " ms"); row.SubItems.Add(detail);
            rawHistory.Items.Insert(0, row); if (rawHistory.Items.Count > 200) rawHistory.Items.RemoveAt(200);
            if (p.Kind == "reset")
            {
                inputDiagnostics.Reset(); forwardedDiagnostics.Reset();
                compatibility.Text = p.ResetReason; compatibility.ForeColor = Blue;
            }
            else if ((p.Button == 1 || p.Button == 2) && diagnostics.SuspectedReleaseOnly(p.Button))
            {
                compatibility.ForeColor = Color.Firebrick;
                compatibility.Text = "检测到连续极短点击：若你实际长按，请在 Logi Options+ 中关闭侧键横向滚动；这不是长按阈值过高。";
            }
            else if (duration >= 80)
            {
                compatibility.ForeColor = Color.FromArgb(25, 112, 72);
                compatibility.Text = "已收到正常按住时长（" + duration + " ms）。双键测试可先按住一个，再按另一个。";
            }
            try
            {
                string path = Path.Combine(folder, "logs", "raw-input.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024) File.WriteAllText(path, "", new UTF8Encoding(false));
                File.AppendAllText(path, json.Serialize(new { Timestamp = DateTimeOffset.Now.ToString("o"), Button = p.Button, Event = type, TimeMs = p.TimeMs, DurationMs = duration, Source = source, ObservedOnly = p.ObservedOnly, ResetReason = p.ResetReason }) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) { Notice("原始事件记录未保存：" + ex.Message); }
        }
        private string CodeFor(Gesture g)
        { return g.Button == 0 ? (g.Kind == "chordhold" ? "both.hold" : "both.chord") : (g.Button == FrontButton ? "front." : "back.") + g.Kind; }
        private void OnGesture(Gesture g, bool isDemo, bool hardware = false)
        {
            string code = CodeFor(g), title = TitleFor(code), desc;
            if (!config.Descriptions.TryGetValue(code, out desc)) desc = title;
            string shortcut = config.Shortcuts[code];
            bool volumeAction = config.VolumeWheelEnabled && (code == "front.wheelup" || code == "front.wheeldown");
            if (volumeAction) shortcut = code == "front.wheelup" ? "VolumeUp" : "VolumeDown";
            string sofaShortcut = config.SofaGesturesEnabled ? SofaBindings.Shortcut(code) : null;
            if (sofaShortcut != null) shortcut = sofaShortcut;
            bool forwarded = g.Button == 0 ? sideInjected[0] || sideInjected[1] : sideInjected[g.Button - 1];
            var e = new GestureEvent { Timestamp = DateTimeOffset.Now.ToString("o"), Code = code, Title = title, Description = desc, Source = isDemo ? "demo" : hardware ? "hid++" : forwarded ? "forwarded" : "non-injected", PhysicalButton = g.Button, HeldMs = g.HeldMs, Shortcut = shortcut, ExecuteShortcut = !isDemo && (execute.Checked || volumeAction || sofaShortcut != null), TargetWindow = Hotkeys.GetForegroundWindow() };
            if (isDemo) demoCounts[code]++; else realCounts[code]++;
            records.Add(e); if (records.Count > 5000) records.RemoveAt(0);
            heroTitle.Text = (isDemo ? "[模拟] " : "") + title;
            heroDetail.Text = desc + "  [" + shortcut + "]" + (g.Kind == "hold" ? "  ·  长按仅显示提示，不执行额外快捷键" : g.Kind == "chord" ? "  ·  全部松开后可再次触发" : e.ExecuteShortcut ? "  ·  已请求实际操作" : "  ·  测试预览");
            var item = new ListViewItem(DateTime.Now.ToString("HH:mm:ss.fff"));
            item.SubItems.Add(isDemo ? "自测" : forwarded ? "转发" : "系统"); item.SubItems.Add(title); item.SubItems.Add(code); item.SubItems.Add(desc);
            item.ForeColor = isDemo ? Color.FromArgb(158, 99, 20) : Ink;
            history.Items.Insert(0, item); if (history.Items.Count > 300) history.Items.RemoveAt(300);
            UpdateCards();
            try
            {
                if (sessionLog == null)
                {
                    Directory.CreateDirectory(Path.Combine(folder, "logs"));
                    sessionLog = Path.Combine(folder, "logs", "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
                }
                File.AppendAllText(sessionLog, json.Serialize(e) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) { Notice("记录写入失败：" + ex.Message); }
            if (shortcut == "panel" && !isDemo) ShowPanel(e.TargetWindow);
            else if (e.ExecuteShortcut && (shortcut == "WindowPrevious" || shortcut == "WindowNext"))
            {
                try { windowCycle.Step(shortcut == "WindowPrevious"); LogAction(e, "sent", null); }
                catch (Exception ex) { LogAction(e, "failed", ex.Message); Notice("窗口切换失败：" + ex.Message); }
            }
            else if (e.ExecuteShortcut && shortcut == "ShowHelp")
            {
                FinishWindowCycle(); gestureHelp.Display(config.VolumeWheelEnabled, config.SofaGesturesEnabled);
            }
            else if (e.ExecuteShortcut && shortcut == "DisplayToggle")
            {
                FinishWindowCycle();
                if (panel != null && !panel.IsDisposed) panel.Close();
                int requestGeneration = generation;
                BeginInvoke((Action)delegate { if (listening && !demoRunning && generation == requestGeneration) ToggleDisplayRequested?.Invoke(); });
            }
            else if (e.ExecuteShortcut && shortcut == "CodexToggle")
            {
                FinishWindowCycle();
                _ = ToggleCodex();
            }
            else if (e.ExecuteShortcut && shortcut == "PetToggle")
            {
                FinishWindowCycle();
                _ = TogglePet();
            }
            else if (e.ExecuteShortcut && shortcut != "hint" && !string.IsNullOrWhiteSpace(shortcut))
            {
                if (shortcut == "Win+D") FinishWindowCycle();
                QueueAction(e);
            }
            if (popup.Checked && shortcut != "ShowHelp")
            {
                if (code == "both.chord") toast.Display("双键已按住", "继续同时按住 3 秒：查看使用说明（显示 5 秒）");
                else if (shortcut == "hint")
                    toast.Display(g.Button == FrontButton ? "前键 · 声音与媒体" : "后键 · 窗口与桌面", g.Button == FrontButton ? "单击 Codex / 返回 · 按滚轮静音 · 滚动调音量" : "单击开关宠物 · 按滚轮显示桌面 · 滚动选窗口");
                else if ((!volumeAction && shortcut != "Mute" && shortcut != "WindowPrevious" && shortcut != "WindowNext" && shortcut != "CodexToggle" && shortcut != "PetToggle") || isDemo) toast.Display((isDemo ? "模拟 · " : e.ExecuteShortcut ? "" : "测试 · ") + title, desc);
            }
        }
        private void UpdateStates()
        {
            GestureEngine current = demoRunning ? demoEngine : engine;
            if (current == null) return;
            long now = demoRunning ? demoClock.ElapsedMilliseconds : mouse.Now;
            UpdateState(frontState, frontBar, current, FrontButton, "前侧键", now);
            UpdateState(backState, backBar, current, 3 - FrontButton, "后侧键", now);
        }
        private void UpdateState(Label label, ProgressBar bar, GestureEngine current, int button, string name, long now)
        {
            bool down = current.IsDown(button); long held = current.HeldFor(button, now);
            label.Text = name + "  /  XBUTTON" + button + "     " + (down ? "按住中  " + held + " ms" : "已松开");
            label.ForeColor = down ? Blue : Muted;
            bool bothDown = current.IsDown(1) && current.IsDown(2);
            if (bothDown) held = Math.Min(current.HeldFor(1, now), current.HeldFor(2, now));
            bar.Value = (int)Math.Min(100, held * 100 / (bothDown ? GestureEngine.ChordHoldMs : config.HoldMs));
        }
        private void UpdateCards()
        {
            foreach (string code in Settings.Codes)
            {
                if (!cards.ContainsKey(code)) continue;
                Label c = cards[code]; int real = realCounts[code], sim = demoCounts[code];
                string cardDescription = code == "front.single" ? "Codex / 返回" : code == "back.single" ? "宠物显示 / 隐藏" : code == "back.triple" ? "确认 / 回车 Enter" : code == "both.hold" ? "使用说明 · 显示 5 秒" : config.Descriptions[code];
                c.Text = TitleFor(code) + "\n" + cardDescription + "\n" + (real > 0 ? "接收 ✓ " + real : "接收 0") + "  /  自测 " + sim;
                c.BackColor = real > 0 ? Color.FromArgb(219, 245, 232) : sim > 0 ? Color.FromArgb(255, 242, 216) : Color.White;
                c.ForeColor = real > 0 ? Color.FromArgb(25, 112, 72) : Ink;
            }
            foreach (ListViewItem row in bindings.Items)
            {
                if (row.Tag == null) { row.SubItems[3].Text = backspaceCount + " 次退格"; continue; }
                string code = (string)row.Tag; row.SubItems[3].Text = realCounts[code] + " / " + demoCounts[code];
                row.BackColor = realCounts[code] > 0 ? Color.FromArgb(219, 245, 232) : demoCounts[code] > 0 ? Color.FromArgb(255, 242, 216) : Color.White;
            }
            coverage.Text = "接收已测  " + realCounts.Count(x => x.Value > 0) + " / 20       自测已测  " + demoCounts.Count(x => x.Value > 0) + " / 20       绿色为系统接收（含驱动转发），黄色为程序自测";
        }
        private void StartDemo()
        {
            if (panel != null && !panel.IsDisposed) panel.Close();
            previousListening = listening; demoRunning = true; ResetInput(); SetDemoControls(false);
            demoSteps.Clear(); demoIndex = 0; long t = 120;
            if (demoInput != null) demoInput.Dispose();
            demoInput = new NativeMouse(); demoInput.Configure(true, true, true);
            demoEngine = new GestureEngine(config.ClickMs, config.HoldMs, config.ChordMs);
            demoEngine.Recognized += delegate(Gesture g) { OnGesture(g, true); };
            foreach (int b in new[] { FrontButton, 3 - FrontButton })
            {
                for (int count = 1; count <= 3; count++)
                {
                    for (int i = 0; i < count; i++)
                    { demoSteps.Add(new DemoStep { Time = t, Button = b, Down = true }); t += 45; demoSteps.Add(new DemoStep { Time = t, Button = b, Down = false }); t += Math.Min(90, config.ClickMs / 2); }
                    t += config.ClickMs + 250;
                }
                demoSteps.Add(new DemoStep { Time = t, Button = b, Down = true }); t += config.HoldMs + 150;
                demoSteps.Add(new DemoStep { Time = t, Button = b, Down = false }); t += 420;
                foreach (string kind in new[] { "left", "right", "middle", "wheelup", "wheeldown" })
                {
                    demoSteps.Add(new DemoStep { Time = t, Button = b, Down = true }); t += 50;
                    demoSteps.Add(new DemoStep { Time = t, Button = b, Kind = kind }); t += 50;
                    demoSteps.Add(new DemoStep { Time = t, Button = b, Down = false }); t += 480;
                }
            }
            demoSteps.Add(new DemoStep { Time = t, Button = 1, Down = true }); t += Math.Max(10, config.ChordMs / 2);
            demoSteps.Add(new DemoStep { Time = t, Button = 2, Down = true }); t += GestureEngine.ChordHoldMs + 100;
            demoSteps.Add(new DemoStep { Time = t, Button = 1, Down = false }); t += 40;
            demoSteps.Add(new DemoStep { Time = t, Button = 2, Down = false }); demoEnd = t + 650;
            rawStatus.Text = "程序自测：20 种手势经过输入处理层，不发送系统点击，实际动作已暂停"; demoClock.Restart();
        }
        private void SetDemoControls(bool enabled)
        { foreach (Control c in new Control[] { pause, demo, clear, swap, suppress, driverCompatibility, volumeWheel, sofaGestures, clickMs, holdMs, chordMs, execute }) c.Enabled = enabled; }
        private void ClearResults()
        {
            backspaceCount = 0;
            foreach (string code in Settings.Codes) { realCounts[code] = 0; demoCounts[code] = 0; }
            history.Items.Clear(); rawHistory.Items.Clear(); records.Clear(); sessionLog = null; ResetInput(); UpdateCards();
            heroTitle.Text = "测试已重置 · 按侧键开始"; heroDetail.Text = "20 种手势独立统计。日志文件保留在 logs 文件夹。";
            rawStatus.Text = "原始输入：等待侧键按下 / 松开"; toast.Hide();
        }
        private void Export()
        {
            try
            {
                string dir = Path.Combine(folder, "logs"); Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "export-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                File.WriteAllText(path, json.Serialize(new { ExportedAt = DateTimeOffset.Now.ToString("o"), Settings = config, PhysicalCounts = realCounts, DemoCounts = demoCounts, Events = records }), new UTF8Encoding(false));
                heroDetail.Text = "已导出：" + path;
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Notice("导出失败：" + ex.Message); }
        }
        private void Notice(string message) { heroDetail.Text = message; }
        private async System.Threading.Tasks.Task ToggleCodex()
        {
            string feedback = await codexToggle.Toggle();
            if (closing) return;
            heroTitle.Text = feedback; heroDetail.Text = "前键单击：Codex 与切换前的具体窗口往返";
            if (popup.Checked) toast.Display(feedback, "再次单击前侧键可返回");
        }
        private void FinishWindowCycle()
        {
            try { windowCycle.Finish(); }
            catch (Exception ex) { Notice("窗口切换结束失败：" + ex.Message); }
        }
        private async System.Threading.Tasks.Task TogglePet()
        {
            string feedback = await petToggle.Toggle();
            if (closing) return;
            heroTitle.Text = feedback; heroDetail.Text = "后键单击：整个宠物浮窗显示 / 消失";
            if (popup.Checked) toast.Display(feedback, "再次单击后侧键可切换");
            try
            {
                File.AppendAllText(Path.Combine(folder, "logs", "pet-toggle.jsonl"), json.Serialize(new {
                    Timestamp = DateTimeOffset.Now.ToString("o"), Result = feedback, Version = "2.4.6"
                }) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) { Notice("宠物结果记录失败：" + ex.Message); }
        }
        private void QueueAction(GestureEvent e)
        {
            if (e.TargetWindow == Handle && !e.Shortcut.StartsWith("Win+") && e.Shortcut != "PlayPause" && e.Shortcut != "Mute" && !e.Shortcut.StartsWith("Volume"))
            { Notice("请先点击需要操作的软件，再使用这个手势。"); return; }
            if (!actionQueue.TryAdd(e)) Notice("动作队列已满，本次动作未执行");
        }
        private readonly object actionLogGate = new object();
        private void LogAction(GestureEvent e, string result, string error)
        {
            try { lock (actionLogGate) {
                Directory.CreateDirectory(Path.Combine(folder, "logs"));
                File.AppendAllText(Path.Combine(folder, "logs", "action-results.jsonl"), json.Serialize(new {
                    Timestamp = DateTimeOffset.Now.ToString("o"), Code = e.Code, Shortcut = e.Shortcut,
                    Result = result, Error = error, TargetWindow = e.TargetWindow.ToInt64(), Version = "2.4.6"
                }) + Environment.NewLine, new UTF8Encoding(false));
            } } catch (IOException) { }
        }
        private void ShowPanel(IntPtr target)
        {
            if (panel != null && !panel.IsDisposed) { panel.Activate(); return; }
            panel = new CommandPanel(execute.Checked && !demoRunning, target, delegate(string name, string shortcut, bool run, IntPtr previous)
            {
                if (shortcut == "pause") { listening = false; ResetInput(); heroTitle.Text = "游戏模式 · 鼠标映射已暂停"; return; }
                heroTitle.Text = name; heroDetail.Text = run ? "操作面板：" + shortcut : "测试预览：" + shortcut + "，未执行快捷键";
                if (!run) { if (popup.Checked) toast.Display("测试 · " + name, shortcut); return; }
                if (!Hotkeys.SetForegroundWindow(previous)) { Notice("无法恢复目标窗口，请先点击目标软件再使用手势。"); return; }
                QueueAction(new GestureEvent { Code = "panel." + shortcut, Title = name, Shortcut = shortcut, Source = "physical", ExecuteShortcut = true, TargetWindow = previous });
            });
            panel.Show();
        }
    }
    internal sealed class Toast : Form
    {
        private readonly Label title = new Label(), detail = new Label();
        private readonly System.Windows.Forms.Timer hide = new System.Windows.Forms.Timer();
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20; return p; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; }
            base.WndProc(ref m);
        }
        public Toast()
        {
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            Opacity = 0.98;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(470, 110);
            BackColor = Color.FromArgb(25, 39, 64); ForeColor = Color.White; Padding = new Padding(18, 9, 18, 9);
            title.Dock = DockStyle.Top; title.Height = 46; title.Font = new Font("Microsoft YaHei UI", 20, FontStyle.Bold);
            detail.Dock = DockStyle.Fill; detail.Font = new Font("Microsoft YaHei UI", 10); detail.ForeColor = Color.FromArgb(195, 214, 245);
            Controls.Add(detail); Controls.Add(title);
            hide.Interval = 1800; hide.Tick += delegate { Hide(); hide.Stop(); };
            AutoScaleDimensions = new SizeF(96F, 96F); ResumeLayout(true);
        }
        public void Display(string name, string description)
        {
            title.Text = name; detail.Text = description;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 22, area.Bottom - Height - 22);
            Show(); hide.Stop(); hide.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing) hide.Dispose(); base.Dispose(disposing); }
    }
}

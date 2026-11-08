using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DisplaySceneSwitcher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--hid-chord-probe"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).First();
            try { Store.Write(output, LogitechChordReader.Probe()); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--help-pipeline-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).First();
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
                using var form = new MouseSideLab.MainForm(false, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "help-test-profile"));
                Store.Write(output, form.DiagnoseHelpPipeline(args.Contains("--hardware")));
                form.Close();
            }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--chord-input-capture"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).First();
            try { ChordInputDiagnostics.Capture(output); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--pet-direct-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "pet-direct-test.json");
            try { PetDirectDiagnostics.Run(output).GetAwaiter().GetResult(); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--sound-toast-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "sound-toast-test.json");
            try { MouseGestureDiagnostics.SoundToast(output); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--mouse-gesture-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "mouse-gesture-test.json");
            try { MouseGestureDiagnostics.Run(output); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--watchdog")) { Recovery.Watchdog(args.SkipWhile(a => a != "--watchdog").Skip(1).First()); return; }
        if (args.Contains("--pet-diagnose"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "pet-diagnostics.json");
            try { PetWindows.Diagnose(output); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--codex-diagnose"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "codex-diagnostics.json");
            try { CodexWindows.Diagnose(output); }
            catch (Exception ex) { Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--sofa-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "sofa-test.json");
            try { SofaDiagnostics.Test(output); }
            catch (Exception ex) { Store.Log(ex.ToString()); Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--volume-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "volume-test.json");
            try { MouseVolumeDiagnostics.Test(output); }
            catch (Exception ex) { Store.Log(ex.ToString()); Store.Write(output + ".error.json", new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--audio-diagnose") || args.Contains("--audio-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "audio-diagnostics.json");
            try { if (args.Contains("--audio-test")) AudioDiagnostics.Test(output); else AudioDiagnostics.Write(output); }
            catch (Exception ex) { Store.Write(output, new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--integration-test"))
        {
            try { Diagnostics.IntegrationTest(); } catch (Exception ex) { Store.Write(Path.Combine(Store.Root, "integration-error.json"), new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--diagnose") || args.Contains("--self-test"))
        {
            string output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Store.Root, "diagnostics.json");
            try { Diagnostics.Write(output, args.Contains("--self-test")); } catch (Exception ex) { Store.Write(output, new { Error = ex.ToString() }); Environment.ExitCode = 1; }
            return;
        }
        using var single = new Mutex(true, @"Local\DisplaySceneSwitcher.Main", out bool first);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DisplaySceneSwitcher.Show");
        if (!first) { showEvent.Set(); return; }
        using var mouseSingle = new Mutex(true, @"Local\MouseSideLab-20261107", out bool mouseAvailable);
        if (!mouseAvailable) { MessageBox.Show("请先关闭独立版鼠标侧键实验室，再启动二合一控制台，避免两个程序重复接管鼠标。", "沙发控制台"); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--render-preview"))
        {
            Directory.CreateDirectory(Path.Combine(Store.Root, "preview"));
            var settings = Settings.Load();
            using var main = new MainForm(false, false); Render(main, "main"); main.ShowMousePanel(); Render(main, "mouse");
            using var preferences = new SettingsForm(settings); Render(preferences, "settings");
            using var confirmation = new ConfirmationForm("电视主屏 · 电脑扩展", "SONY TV *30 · 3840×2160 / 120 Hz · HDR 已开\r\nDELL U2720Q · 3840×2160 / 60 Hz · HDR 已关"); Render(confirmation, "confirmation");
            return;
        }
        Application.ThreadException += (_, e) => { Store.Log(e.Exception.ToString()); MessageBox.Show(e.Exception.Message, "显示场景切换器", MessageBoxButtons.OK, MessageBoxIcon.Error); };
        try
        {
            using var main = new MainForm(args.Contains("--tray"));
            var wait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => { try { main.BeginInvoke(main.ShowPanel); } catch { } }, null, -1, false);
            try { Application.Run(main); } finally { wait.Unregister(null); }
        }
        catch (Exception ex) { Store.Log(ex.ToString()); MessageBox.Show(ex.Message, "显示场景切换器启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    static void Render(Form form, string name)
    {
        form.Show(); Application.DoEvents(); form.PerformLayout();
        using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(Store.Root, "preview", name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        form.Hide();
    }
}
public static class Diagnostics
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception("自检失败：" + message); }
    public static void Write(string output, bool selfTest)
    {
        var sizes = new Dictionary<string, int> { ["LUID"] = Marshal.SizeOf<Luid>(), ["Path"] = Marshal.SizeOf<DisplayPath>(), ["Mode"] = Marshal.SizeOf<ModeInfo>(), ["Header"] = Marshal.SizeOf<InfoHeader>(), ["TargetName"] = Marshal.SizeOf<TargetName>(), ["Color2"] = Marshal.SizeOf<ColorInfo2>(), ["DevMode"] = Marshal.SizeOf<DevMode>() };
        if (selfTest)
        {
            Assert(sizes["LUID"] == 8 && sizes["Path"] == 72 && sizes["Mode"] == 64 && sizes["Header"] == 20 && sizes["TargetName"] == 420 && sizes["Color2"] == 36 && sizes["DevMode"] == 220, "Windows API 结构大小");
        }
        var settings = Settings.Load(); var monitors = Native.Monitors(); var q = Native.Query();
        var tests = new List<object>(); bool allValid = true;
        foreach (Scene scene in Enum.GetValues<Scene>())
        {
            try { var plan = Engine.Plan(settings, scene); tests.Add(new { Scene = scene.ToString(), Valid = true, Paths = plan.Length }); }
            catch (Exception ex) { allValid = false; tests.Add(new { Scene = scene.ToString(), Valid = false, Error = ex.Message }); }
        }
        var snapshot = Snapshot.Capture(); var decodedPaths = Native.Decode<DisplayPath>(snapshot.Paths); var decodedModes = Native.Decode<ModeInfo>(snapshot.Modes);
        Assert(Native.Encode(decodedPaths) == snapshot.Paths && Native.Encode(decodedModes) == snapshot.Modes, "显示快照二进制往返");
        int currentValidation = Native.SetDisplayConfig((uint)q.Paths.Length, q.Paths, (uint)q.Modes.Length, q.Modes, Native.Validate | Native.Supplied);
        Native.Check(currentValidation, "验证当前布局快照");
        var modeReports = monitors.Select(m => new { Device = m.Name, m.Key, m.GdiName, m.Active, m.Width, m.Height, m.Hz, m.X, m.Y, m.NativeWidth, m.NativeHeight, m.Hdr, Modes = m.Active ? Native.Modes(m.GdiName).Select(dm => new { dm.Width, dm.Height, Hz = dm.Frequency }).ToArray() : [], BestAtNative = Best(m) }).ToList();
        var raw = Native.Query(true).Paths.Select(p => new { Source = Native.SourceKey(p), Target = Native.TargetKey(p), p.Flags, SourceIndex = p.Source.ModeIndex, TargetIndex = p.Target.ModeIndex, p.Target.Available, p.Target.Rotation, p.Target.Scaling, p.Target.OutputTechnology, HzN = p.Target.Refresh.Numerator, HzD = p.Target.Refresh.Denominator, p.Target.ScanLine }).ToArray();
        Store.Write(output, new { Version = "2.4.5", CheckedAt = DateTime.Now, ReadOnly = true, Sizes = sizes, CurrentLayoutValidated = currentValidation == 0, SnapshotRoundTrip = true, SceneValidation = tests, Settings = settings, Monitors = modeReports, Paths = raw, PlaybackDevices = AudioSystem.Devices(), DefaultPlayback = AudioDefaults.Capture() });
        Store.Log("只读自检完成：" + output);
        if (selfTest && !allValid) Environment.ExitCode = 1;
    }
    static string Best(MonitorDevice m)
    {
        if (!m.Active) return "启用后检测";
        try { var mode = Native.ChooseMode(m, new()); return $"{mode.Width}×{mode.Height} @ {mode.Frequency} Hz"; } catch (Exception ex) { return ex.Message; }
    }
    public static void IntegrationTest()
    {
        var settings = Settings.Load(); var baseline = Snapshot.Capture(); var results = new List<object>();
        Store.Write(Path.Combine(Store.Root, "test-baseline.json"), baseline);
        try
        {
            foreach (Scene scene in new[] { Scene.ComputerPrimary, Scene.TvPrimary, Scene.Clone, Scene.TvOnly, Scene.ComputerOnly })
            {
                Recovery.Arm();
                try
                {
                    string result = Engine.ApplyScene(settings, scene);
                    results.Add(new { Scene = scene.ToString(), Passed = true, Result = result });
                    Store.Write(Path.Combine(Store.Root, "integration-test.json"), results);
                    if (scene == Scene.ComputerPrimary)
                    {
                        Write(Path.Combine(Store.Root, "tv-connected-diagnostics.json"), true);
                        var tv = Engine.Bound(Native.Monitors(), settings.TvKey, "电视"); bool originalHdr = tv.Hdr.Enabled;
                        Native.SetHdr(tv, !originalHdr); Native.SetHdr(Engine.Bound(Native.Monitors(), settings.TvKey, "电视"), originalHdr);
                        results.Add(new { Scene = "TvHdrRoundTrip", Passed = true, Result = "电视 HDR 开关往返，电脑 HDR 状态保持" });
                    }
                }
                finally { Recovery.Revert(); }
            }
            // Exercise the independent recovery process on a real primary-screen change.
            Recovery.Arm(); Engine.ApplyScene(settings, Scene.TvPrimary);
            Recovery.Locked(() => { var pending = Store.Read<PendingChange>(Store.PendingPath); pending.ExpiresUtc = DateTime.UtcNow.AddMilliseconds(500); Store.Write(Store.PendingPath, pending); return true; });
            for (int i = 0; i < 100 && File.Exists(Store.PendingPath); i++) Thread.Sleep(100);
            Assert(!File.Exists(Store.PendingPath), "独立恢复进程倒计时");
            var actual = Native.Monitors().Where(m => m.Active).ToList();
            Assert(actual.Count == baseline.Monitors.Count && actual.All(a => baseline.Monitors.Any(b => b.Key == a.Key && b.X == a.X && b.Y == a.Y && b.Width == a.Width && b.Height == a.Height && Math.Abs(b.Hz - a.Hz) < 1.1 && (!b.Hdr.Known || a.Hdr.Enabled == b.Hdr.Enabled))), "自动恢复后的显示状态");
            results.Add(new { Scene = "IndependentWatchdog", Passed = true, Result = "倒计时结束自动恢复原布局、刷新率与 HDR" });
        }
        finally
        {
            if (File.Exists(Store.PendingPath)) Recovery.Revert();
            baseline.Restore();
            Store.Write(Path.Combine(Store.Root, "integration-test.json"), results);
        }
    }
}
public sealed class MainForm : Form
{
    readonly NotifyIcon tray;
    readonly Label status = new(), detail = new();
    readonly FlowLayoutPanel actions = new();
    readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 4000 };
    readonly bool startHidden;
    readonly TabControl pages = new() { Dock = DockStyle.Fill };
    readonly MouseSideLab.MainForm mouseControl;
    readonly Label mouseSummary = new() { AutoSize = true, ForeColor = Color.FromArgb(30, 110, 75), Margin = new Padding(8, 10, 0, 0) };
    Settings settings;
    bool busy, quitting;
    readonly List<int> registeredKeys = [];
    readonly Dictionary<Scene, string> sceneNames = new() { [Scene.ComputerOnly] = "仅电脑屏幕", [Scene.TvOnly] = "仅电视", [Scene.Clone] = "电脑与电视同步", [Scene.ComputerPrimary] = "电脑主屏 · 电视扩展", [Scene.TvPrimary] = "电视主屏 · 电脑扩展" };
    public MainForm(bool hidden, bool enableMouse = true)
    {
        settings = Settings.Load(); startHidden = hidden;
        Text = "沙发控制台 · 电脑电视与鼠标"; Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1180, 900); MinimumSize = new Size(1040, 820); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(246, 248, 252); Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "switcher.ico"));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 22, 26, 18), ColumnCount = 1, RowCount = 8 };
        root.RowStyles.Add(new(SizeType.Absolute, 42)); root.RowStyles.Add(new(SizeType.Absolute, 38)); root.RowStyles.Add(new(SizeType.Absolute, 105)); root.RowStyles.Add(new(SizeType.Absolute, 240)); root.RowStyles.Add(new(SizeType.Absolute, 56)); root.RowStyles.Add(new(SizeType.Absolute, 56)); root.RowStyles.Add(new(SizeType.Absolute, 88)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "电脑 ↔ 电视，一键切换", Font = new Font(Font, FontStyle.Bold), ForeColor = Color.FromArgb(28, 46, 75), Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
        status.Dock = DockStyle.Fill; status.ForeColor = Color.FromArgb(45, 101, 169); root.Controls.Add(status, 0, 1);
        detail.Dock = DockStyle.Fill; detail.Font = new Font(Font.FontFamily, 9); detail.AutoEllipsis = true; root.Controls.Add(detail, 0, 2);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        grid.ColumnStyles.Add(new(SizeType.Percent, 50)); grid.ColumnStyles.Add(new(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++) grid.RowStyles.Add(new(SizeType.Percent, 33.33f));
        foreach (var pair in sceneNames)
        {
            var scene = pair.Key; var b = MakeButton(pair.Value, async () => await SwitchScene(scene));
            b.AutoSize = false; b.Dock = DockStyle.Fill; b.Margin = new Padding(3, 4, 9, 7); grid.Controls.Add(b, ((int)scene - 1) % 2, ((int)scene - 1) / 2);
        }
        var restore = MakeButton("恢复上次画面与声音", async () => await RestorePrevious()); restore.Dock = DockStyle.Fill; restore.Margin = new Padding(3, 4, 9, 7); grid.Controls.Add(restore, 1, 2); root.Controls.Add(grid, 0, 3);
        var hdrPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        hdrPanel.Controls.Add(MakeButton("电视 HDR 开", async () => await SetTvHdr(true))); hdrPanel.Controls.Add(MakeButton("电视 HDR 关", async () => await SetTvHdr(false))); hdrPanel.Controls.Add(MakeButton("仅电视 + HDR", async () => await SwitchScene(Scene.TvOnly, "on"))); hdrPanel.Controls.Add(MakeButton("仅电视 + SDR", async () => await SwitchScene(Scene.TvOnly, "off"))); root.Controls.Add(hdrPanel, 0, 4);
        var audioPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        audioPanel.Controls.Add(MakeButton("声音到电脑音响", async () => await SetPlayback(false))); audioPanel.Controls.Add(MakeButton("声音到电视", async () => await SetPlayback(true))); audioPanel.Controls.Add(MakeButton("Windows 声音设置", () => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }))); root.Controls.Add(audioPanel, 0, 5);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "切换后 20 秒内点击“保留”；超时会一起恢复画面和声音。\r\nCtrl+Alt+1～5 切场景；Ctrl+Alt+H 切电视 HDR；Ctrl+Alt+0 恢复。\r\n双侧键按住 3 秒：使用说明显示 5 秒；关闭窗口后继续驻留托盘。", Font = new Font(Font.FontFamily, 9), ForeColor = Color.DimGray }, 0, 6);
        actions.Dock = DockStyle.Fill;
        actions.Controls.Add(MakeButton("设备与偏好", Configure)); actions.Controls.Add(MakeButton("刷新状态", RefreshStatus)); actions.Controls.Add(MakeButton("鼠标与音量", ShowMousePanel)); actions.Controls.Add(MakeButton("收起到托盘", Hide)); actions.Controls.Add(mouseSummary); root.Controls.Add(actions, 0, 7);
        var displayPage = new TabPage("电脑 / 电视 / 声音"); displayPage.Controls.Add(root); pages.TabPages.Add(displayPage);
        var mousePage = new TabPage("鼠标 / 音量"); pages.TabPages.Add(mousePage); Controls.Add(pages);
        mouseControl = new MouseSideLab.MainForm(enableMouse) { TopLevel = false, TopMost = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill, MinimumSize = Size.Empty };
        mousePage.Controls.Add(mouseControl); mouseControl.Show();
        mouseControl.InputStatusChanged += () => mouseSummary.Text = mouseControl.InputSummary;
        mouseControl.ToggleDisplayRequested += async () => await ToggleExclusiveDisplay();
        mouseSummary.Text = mouseControl.InputSummary;
        tray = new NotifyIcon { Icon = Icon, Text = "沙发控制台 · 显示与鼠标", Visible = true }; tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPanel(); }; tray.DoubleClick += (_, _) => ShowPanel(); BuildTray();
        refreshTimer.Tick += (_, _) => { if (!busy && Visible) RefreshStatus(); }; refreshTimer.Start();
        Shown += (_, _) =>
        {
            mouseControl.EnsureStarted();
            RefreshStatus();
            if (startHidden) Hide();
            if (File.Exists(Store.PendingPath)) _ = RecoverStale();
        };
        FormClosing += (_, e) => { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { mouseControl.Close(); refreshTimer.Dispose(); foreach (int id in registeredKeys) Native.UnregisterHotKey(Handle, id); tray.Visible = false; tray.Dispose(); };
        _ = Handle;
        for (int i = 0; i <= 5; i++) if (Native.RegisterHotKey(Handle, i, 0x4003, (uint)Keys.D0 + (uint)i)) registeredKeys.Add(i); else Store.Log("快捷键占用：Ctrl+Alt+" + i);
        if (Native.RegisterHotKey(Handle, 8, 0x4003, (uint)Keys.H)) registeredKeys.Add(8); else Store.Log("快捷键占用：Ctrl+Alt+H");
    }
    Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(125, 40), Padding = new Padding(8, 3, 8, 3), BackColor = Color.White, FlatStyle = FlatStyle.Flat, Margin = new Padding(3, 3, 8, 3), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Color.FromArgb(211, 221, 234); button.Click += (_, _) => { if (!busy) action(); }; return button;
    }
    void BuildTray()
    {
        var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add("打开切换面板", null, (_, _) => ShowPanel()); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("鼠标与音量", null, (_, _) => ShowMousePanel());
        menu.Items.Add("暂停鼠标映射", null, (_, _) => mouseControl.SetPaused(!mouseControl.IsPaused));
        menu.Items.Add(new ToolStripSeparator());
        foreach (var item in sceneNames) { var scene = item.Key; menu.Items.Add(item.Value, null, async (_, _) => { if (!busy) await SwitchScene(scene); }); }
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("仅电视 + HDR", null, async (_, _) => { if (!busy) await SwitchScene(Scene.TvOnly, "on"); }); menu.Items.Add("仅电视 + SDR", null, async (_, _) => { if (!busy) await SwitchScene(Scene.TvOnly, "off"); });
        menu.Items.Add("电视 HDR 开", null, async (_, _) => { if (!busy) await SetTvHdr(true); }); menu.Items.Add("电视 HDR 关", null, async (_, _) => { if (!busy) await SetTvHdr(false); });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("声音到电脑音响", null, async (_, _) => { if (!busy) await SetPlayback(false); }); menu.Items.Add("声音到电视", null, async (_, _) => { if (!busy) await SetPlayback(true); });
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("设备与偏好", null, (_, _) => { if (!busy) Configure(); }); menu.Items.Add("恢复上一次设置", null, async (_, _) => { if (!busy) await RestorePrevious(); });
        menu.Items.Add("开机启动", null, (_, _) => { if (!busy) ToggleStartup(); }); menu.Items.Add("退出", null, async (_, _) => { if (busy) return; if (File.Exists(Store.PendingPath)) { try { await Task.Run(Recovery.Revert); } catch (Exception ex) { MessageBox.Show(ex.Message); return; } } quitting = true; Close(); });
        menu.Opening += (_, _) => { foreach (ToolStripItem item in menu.Items) if (item is ToolStripMenuItem entry) { entry.Enabled = !busy; if (entry.Text == "开机启动") entry.Checked = StartupEnabled(); if (entry.Text == "暂停鼠标映射") entry.Checked = mouseControl.IsPaused; } };
        tray.ContextMenuStrip = menu;
    }
    public void ShowPanel()
    {
        Show(); WindowState = FormWindowState.Normal;
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(Bounds))) CenterOnPrimary();
        Activate(); if (!busy) RefreshStatus();
    }
    public void ShowMousePanel() { ShowPanel(); pages.SelectedIndex = 1; }
    void CenterOnPrimary() { var area = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea; Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2)); }
    void RefreshStatus()
    {
        try
        {
            var list = Native.Monitors();
            status.Text = "已连接 " + list.Count + " 台设备 · " + list.Count(m => m.Active) + " 台正在显示";
            detail.Text = string.Join("\r\n", list.Select(m => (m.Key == settings.TvKey ? "电视：" : m.Key == settings.ComputerKey ? "电脑：" : "其他：") + m + (m.Active && m.X == 0 && m.Y == 0 ? " · 主屏/同步" : "") + " · " + m.Hdr)) + "\r\n声音：" + AudioSystem.DefaultName();
        }
        catch (Exception ex) { status.Text = ex.Message; Store.Log(ex.ToString()); }
    }
    async Task SwitchScene(Scene scene, string? hdr = null, bool showNotification = true) => await Change(sceneNames[scene] + (hdr == "on" ? " + HDR" : hdr == "off" ? " + SDR" : ""), () => Engine.ApplyScene(settings, scene, hdr), showNotification);
    async Task ToggleExclusiveDisplay()
    {
        if (busy) return;
        try
        {
            var monitors = Native.Monitors();
            bool televisionActive = Engine.Bound(monitors, settings.TvKey, "电视").Active;
            bool computerActive = Engine.Bound(monitors, settings.ComputerKey, "电脑屏幕").Active;
            await SwitchScene(Engine.ExclusiveToggleTarget(televisionActive, computerActive), showNotification: mouseControl.GestureHintsEnabled);
        }
        catch (Exception ex) { Store.Log(ex.ToString()); if (mouseControl.GestureHintsEnabled) tray.ShowBalloonTip(4000, Text, "双键切屏失败：" + ex.Message, ToolTipIcon.Warning); }
    }
    async Task SetTvHdr(bool enabled) => await Change(enabled ? "电视 HDR 开" : "电视 HDR 关", () => { var tv = Engine.Bound(Native.Monitors(), settings.TvKey, "电视"); Native.SetHdr(tv, enabled, true); return "电视 HDR 已" + (enabled ? "开启" : "关闭"); });
    async Task SetPlayback(bool television) => await Change(television ? "声音到电视" : "声音到电脑音响", () => "声音：" + AudioSystem.SwitchTo(television ? settings.TvAudioId : settings.ComputerAudioId, settings.SwitchCommunicationAudio));
    async Task RestorePrevious()
    {
        if (!File.Exists(Store.PreviousPath)) { MessageBox.Show(this, "还没有切换记录。首次切换后会自动保存。", Text); return; }
        var previous = Store.Read<Snapshot>(Store.PreviousPath);
        await Change("恢复上次画面与声音", () => { previous.Restore(true); return "已恢复上次画面与声音"; });
    }
    async Task Change(string title, Func<string> operation, bool showNotification = true)
    {
        if (busy) return; busy = true; status.Text = "正在切换：" + title; UseWaitCursor = true;
        bool mouseWasPaused = mouseControl.IsPaused; mouseControl.SetPaused(true);
        try
        {
            string summary = await Task.Run(() => { Recovery.Arm(); try { string s = operation(); Recovery.Countdown(); return s; } catch { Recovery.Revert(); throw; } });
            UseWaitCursor = false; CenterOnPrimary();
            using var confirmation = new ConfirmationForm(title, summary);
            var result = confirmation.ShowDialog();
            if (result == DialogResult.OK)
            {
                bool accepted = await Task.Run(Recovery.Accept);
                if (showNotification) tray.ShowBalloonTip(accepted ? 3000 : 4000, Text, accepted ? "已保留：" + title : "已超时并恢复，请重新切换。", ToolTipIcon.Info);
            }
            else await Task.Run(Recovery.Revert);
        }
        catch (Exception ex) { Store.Log(ex.ToString()); MessageBox.Show(this, ex.Message + (File.Exists(Store.PendingPath) ? "\r\n恢复任务仍在等待处理，请按 Ctrl+Alt+0 重试。" : "\r\n已恢复切换前设置，或切换尚未开始。"), "切换未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; UseWaitCursor = false; mouseControl.SetPaused(mouseWasPaused); RefreshStatus(); }
    }
    async Task RecoverStale()
    {
        busy = true;
        try { await Task.Run(Recovery.Revert); } catch (Exception ex) { MessageBox.Show("恢复上次未确认设置失败：" + ex.Message); }
        finally { busy = false; RefreshStatus(); }
    }
    void Configure()
    {
        try { using var form = new SettingsForm(settings); if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Value; Store.Write(Store.SettingsPath, settings); RefreshStatus(); } }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
    }
    void OpenHelp() => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "使用说明.txt")) { UseShellExecute = true });
    static string StartupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "电脑电视切换器.lnk");
    static bool StartupEnabled() => File.Exists(StartupPath);
    void ToggleStartup()
    {
        try
        {
            if (StartupEnabled()) File.Delete(StartupPath);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StartupPath)!);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                dynamic shortcut = shell.CreateShortcut(StartupPath);
                try { shortcut.TargetPath = Environment.ProcessPath!; shortcut.Arguments = "--tray"; shortcut.WorkingDirectory = AppContext.BaseDirectory; shortcut.IconLocation = Path.Combine(AppContext.BaseDirectory, "switcher.ico"); shortcut.Save(); }
                finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
            }
            tray.ShowBalloonTip(2000, Text, StartupEnabled() ? "已开启登录后自动驻留托盘" : "已关闭开机启动", ToolTipIcon.Info);
        }
        catch (Exception ex) { MessageBox.Show(this, "无法更改开机启动：" + ex.Message, Text); }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            int id = m.WParam.ToInt32();
            if (id == 0) { if (File.Exists(Store.PendingPath)) _ = Task.Run(Recovery.Revert); }
            else if (!busy && id == 8) { try { var tv = Engine.Bound(Native.Monitors(), settings.TvKey, "电视"); _ = SetTvHdr(!tv.Hdr.Enabled); } catch (Exception ex) { tray.ShowBalloonTip(3000, Text, ex.Message, ToolTipIcon.Warning); } }
            else if (!busy && id is >= 1 and <= 5) _ = SwitchScene((Scene)id);
        }
        base.WndProc(ref m);
    }
}
public sealed class ConfirmationForm : Form
{
    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    public ConfirmationForm(string title, string summary)
    {
        Text = "保留这次画面和声音设置？"; Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(670, 295);
        StartPosition = FormStartPosition.CenterScreen; TopMost = true; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var text = new Label { Text = title + "\r\n\r\n" + summary, Dock = DockStyle.Top, Height = 185, Padding = new Padding(22, 18, 18, 8), AutoEllipsis = true };
        var countdown = new Label { Dock = DockStyle.Top, Height = 38, Padding = new Padding(22, 0, 0, 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        var keep = new Button { Text = "保留 (Enter)", DialogResult = DialogResult.OK, AutoSize = true, Height = 36 }; var revert = new Button { Text = "恢复 (Esc)", DialogResult = DialogResult.Cancel, AutoSize = true, Height = 36 };
        buttons.Controls.Add(keep); buttons.Controls.Add(revert); Controls.Add(countdown); Controls.Add(text); Controls.Add(buttons); AcceptButton = keep; CancelButton = revert;
        timer.Tick += (_, _) =>
        {
            if (!File.Exists(Store.PendingPath)) { DialogResult = DialogResult.Cancel; Close(); return; }
            try { int seconds = Math.Max(0, (int)Math.Ceiling((Store.Read<PendingChange>(Store.PendingPath).ExpiresUtc - DateTime.UtcNow).TotalSeconds)); countdown.Text = $"还有 {seconds} 秒；未确认将自动恢复。"; if (seconds == 0) { DialogResult = DialogResult.Cancel; Close(); } } catch (IOException) { }
        };
        Shown += (_, _) => timer.Start(); FormClosed += (_, _) => timer.Dispose();
    }
}

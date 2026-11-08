using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseSideLab;

namespace DisplaySceneSwitcher;

internal static class MouseGestureDiagnostics
{
    public static void SoundToast(string output)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        var json = new JsonCodec();
        var old = json.Deserialize<MouseSideLab.Settings>("{\"ShowGestureToasts\":false}"); old.Validate();
        Assert(old.ShowSoundToasts && old.SoundHintsEnabled && !old.ShowGestureToasts, "旧配置默认只开启声音提醒");
        var saved = json.Deserialize<MouseSideLab.Settings>(json.Serialize(old));
        Assert(saved.ShowSoundToasts && !saved.ShowGestureToasts, "声音提醒保存");
        saved.ShowSoundToasts = false;
        Assert(!json.Deserialize<MouseSideLab.Settings>(json.Serialize(saved)).SoundHintsEnabled, "声音提醒关闭保存");
        using var host = new Form { Text = "声音提醒测试", ShowInTaskbar = false };
        host.Show(); host.Hide(); Application.DoEvents();
        IntPtr foreground = Hotkeys.GetForegroundWindow();
        using var toast = new Toast();
        toast.Display("声音提醒预览", "调节音量、静音 / 恢复时显示"); Application.DoEvents();
        Assert(toast.Visible && Hotkeys.GetForegroundWindow() == foreground, "声音提醒显示且不抢焦点");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string preview = Path.Combine(Path.GetDirectoryName(output)!, "sound-toast.png");
        using (var bitmap = new Bitmap(toast.Width, toast.Height))
        { toast.DrawToBitmap(bitmap, toast.ClientRectangle); bitmap.Save(preview); }
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 2100) { Application.DoEvents(); Thread.Sleep(10); }
        Assert(!toast.Visible, "声音提醒自动关闭");
        Store.Write(output, new { Version = "2.4.2", Passed = true, SoundOnly = true, SettingRoundTrip = true,
            ReminderDoesNotActivate = true, AutoHides = true, SystemAudioChanged = false, Preview = preview });
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    // Explicit test command. Only this process's disposable editor receives test keys.
    public static void Run(string output)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var config = new MouseSideLab.Settings { SofaBindingsVersion = 5, ShowGestureToasts = false };
        config.Shortcuts["both.hold"] = "DisplayToggle"; config.Validate();
        Assert(config.SofaBindingsVersion == 7 && config.Shortcuts["both.hold"] == "ShowHelp" && !config.ShowGestureToasts, "配置迁移");
        using var form = new Form { Text = "连续退格自动测试 · 临时文本", ClientSize = new Size(660, 150), StartPosition = FormStartPosition.CenterScreen };
        using var editor = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 24), Text = "ABCDEFGHIJ", ImeMode = ImeMode.Disable };
        form.Controls.Add(editor); form.Show(); form.Activate(); editor.Focus(); editor.Select(editor.TextLength, 0);
        var received = new List<string>();
        editor.KeyDown += (_, e) => received.Add("down:" + e.KeyData);
        editor.KeyUp += (_, e) => received.Add("up:" + e.KeyData);
        form.Deactivate += (_, _) => received.Add("deactivated:" + Hotkeys.GetForegroundWindow());
        Application.DoEvents();
        uint currentThread = GetCurrentThreadId(), foregroundThread = GetWindowThreadProcessId(Hotkeys.GetForegroundWindow(), out _);
        bool attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try { ShowWindow(form.Handle, 5); BringWindowToTop(form.Handle); Hotkeys.SetForegroundWindow(form.Handle); editor.Focus(); }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
        Application.DoEvents();
        Assert(Hotkeys.GetForegroundWindow() == form.Handle && editor.Focused, "测试编辑器未获得焦点，未发送任何按键");
        void SendToEditor(string keys) {
            Assert(Hotkeys.GetForegroundWindow() == form.Handle && editor.Focused, "测试窗口失去焦点，取消 " + keys);
            Hotkeys.Send(keys); Pump(150);
        }
        editor.Select(0, 0); SendToEditor("Delete");
        Assert(editor.Text == "BCDEFGHIJ", "Delete 必须实际删除临时文本的首字符");
        SendToEditor("Ctrl+Z");
        Assert(editor.Text == "ABCDEFGHIJ", "Ctrl+Z 必须实际恢复删除的字符: text=" + editor.Text + ", received=" + string.Join(";", received));
        int escapes = 0, enters = 0;
        editor.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) escapes++; if (e.KeyCode == Keys.Enter) enters++; };
        SendToEditor("Esc"); SendToEditor("Enter");
        Assert(escapes == 1 && enters == 1, "Esc / Enter 必须各到达一次: Esc=" + escapes + ", Enter=" + enters + ", foreground=" + Hotkeys.GetForegroundWindow() + ", expected=" + form.Handle);
        editor.Text = "ABCDEFGHIJ"; editor.Select(editor.TextLength, 0);
        var engine = new GestureEngine(300, 650, 150);
        var repeat = new DoubleHoldBackspace();
        var gestures = new List<string>(); engine.Recognized += g => gestures.Add(g.Kind);
        foreach (var p in new[] {
            new MousePacket { Button = 2, Down = true, TimeMs = 0 },
            new MousePacket { Button = 2, Down = false, TimeMs = 40 },
            new MousePacket { Button = 2, Down = true, TimeMs = 160 } })
            repeat.Input(engine, 2, p, form.Handle, true);
        var watch = Stopwatch.StartNew();
        var sentAt = new List<long>(); bool released = false;
        while (watch.ElapsedMilliseconds < 1800)
        {
            Application.DoEvents();
            long now = watch.ElapsedMilliseconds + 160;
            RepeatDecision decision = repeat.Advance(engine, 2, now, Hotkeys.GetForegroundWindow(), !released);
            Assert(decision != RepeatDecision.Cancelled, "测试过程中焦点已变化，已停止发送");
            if (decision == RepeatDecision.Backspace)
            {
                Assert(Hotkeys.GetForegroundWindow() == form.Handle && editor.Focused, "焦点保护");
                Hotkeys.Send("Backspace"); sentAt.Add(now);
                Application.DoEvents();
                if (sentAt.Count == 2)
                { repeat.Input(engine, 2, new MousePacket { Button = 2, Down = false, TimeMs = now + 1 }, form.Handle, true); released = true; }
            }
            engine.Advance(now); Thread.Sleep(5);
        }
        Assert(sentAt.Count == 2 && editor.Text == "ABCDEFGH" && gestures.Count == 0, "退格内容或松开后互斥");
        Assert(sentAt[1] - sentAt[0] >= 500 && sentAt[1] - sentAt[0] < 650, "退格间隔");
        using var help = new GestureHelpForm();
        IntPtr foreground = Hotkeys.GetForegroundWindow();
        watch.Restart(); help.Display(); Application.DoEvents();
        Assert(help.IsDisplayed && Hotkeys.GetForegroundWindow() == foreground, "说明不抢焦点: visible=" + help.IsDisplayed + ", before=" + foreground + ", after=" + Hotkeys.GetForegroundWindow() + ", help=" + help.Handle);
        string preview = Path.Combine(Path.GetDirectoryName(output)!, "gesture-help.png");
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        using (var bitmap = new Bitmap(help.Width, help.Height))
        { help.DrawToBitmap(bitmap, help.ClientRectangle); bitmap.Save(preview); }
        while (watch.ElapsedMilliseconds < 4600) { Application.DoEvents(); Thread.Sleep(10); }
        Assert(help.IsDisplayed, "说明提前消失");
        while (watch.ElapsedMilliseconds < 5400) { Application.DoEvents(); Thread.Sleep(10); }
        Assert(!help.IsDisplayed, "说明未在 5 秒后关闭");
        using var other = new Form { Text = "窗口切换测试 · 临时窗口 B", Size = new Size(400, 200) };
        other.Show(); other.Activate(); Pump(300);
        Assert(Hotkeys.GetForegroundWindow() == other.Handle, "测试窗口 B 未获得焦点，停止切换测试");
        form.Activate(); editor.Focus(); Pump(300);
        Assert(Hotkeys.GetForegroundWindow() == form.Handle, "窗口切换测试起点必须是临时窗口 A");
        using (var cycle = new WindowCycle()) { cycle.Step(false); Pump(150); cycle.Finish(); Pump(250); }
        Assert(Hotkeys.GetForegroundWindow() == other.Handle, "Alt+Tab 必须切换到临时窗口 B: actual=" + Hotkeys.GetForegroundWindow() + ", expected=" + other.Handle);
        using (var cycle = new WindowCycle()) { cycle.Step(false); Pump(150); cycle.Finish(); Pump(250); }
        Assert(Hotkeys.GetForegroundWindow() == form.Handle && !Hotkeys.IsPressed(0x12), "Alt+Tab 必须返回窗口 A，且松开 Alt");
        Store.Write(output, new { Version = "2.4.6", Passed = true, TestTarget = "Own disposable editor and two test windows only", HardwareMouseSimulated = true,
            DeletePassed = true, UndoPassed = true, EscapeCount = escapes, EnterCount = enters, WindowCycleRoundTrip = true, AltReleased = true,
            BackspaceSentAtMs = sentAt, ResultText = editor.Text, NoLateDeletes = true, NoExtraClickGestures = true,
            HelpVisibleAt4600Ms = true, HelpHiddenAt5400Ms = true, HelpDoesNotActivate = true,
            MigrationPassed = true, ToastsRemainOff = true, ReceivedEvents = received, Preview = preview });
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Pump(int milliseconds)
    { var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(5); } }
}

using System.Diagnostics;
using System.Text.Json;

namespace DisplaySceneSwitcher;

public sealed class ModePreference
{
    public uint Width { get; set; } public uint Height { get; set; } public uint Hz { get; set; }
}
public sealed class Settings
{
    public string ComputerKey { get; set; } = "";
    public string TvKey { get; set; } = "";
    public ModePreference ComputerMode { get; set; } = new();
    public ModePreference TvMode { get; set; } = new();
    public string TvHdr { get; set; } = "keep";
    public bool TvOnRight { get; set; } = true;
    public int AudioRoutingVersion { get; set; }
    public bool AudioFollowsScene { get; set; } = true;
    public string ComputerAudioId { get; set; } = "";
    public string TvAudioId { get; set; } = "";
    public string CloneAudio { get; set; } = "computer";
    public bool SwitchCommunicationAudio { get; set; } = true;
    public static Settings Load()
    {
        if (File.Exists(Store.SettingsPath))
        {
            var saved = Store.Read<Settings>(Store.SettingsPath);
            if (AudioSystem.Migrate(saved)) Store.Write(Store.SettingsPath, saved);
            return saved;
        }
        var monitors = Native.Monitors();
        var tv = monitors.FirstOrDefault(m => m.Name.Contains("SONY", StringComparison.OrdinalIgnoreCase) || m.Key.Contains("SNY", StringComparison.OrdinalIgnoreCase));
        var pc = monitors.FirstOrDefault(m => m.Key != tv?.Key);
        var settings = new Settings { ComputerKey = pc?.Key ?? "", TvKey = tv?.Key ?? "", TvOnRight = tv == null || pc == null || tv.X >= pc.X };
        if (pc is { Active: true }) settings.ComputerMode = new() { Width = pc.Width, Height = pc.Height, Hz = (uint)Math.Round(pc.Hz) };
        AudioSystem.Migrate(settings); Store.Write(Store.SettingsPath, settings); return settings;
    }
}
public static class Store
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplaySceneSwitcher");
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public static string PendingPath => Path.Combine(Root, "pending.json");
    public static string PreviousPath => Path.Combine(Root, "previous.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); File.Move(temp, path, true);
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new Exception("无法读取配置：" + path);
    public static void Log(string message)
    {
        try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "switcher.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n"); } catch { }
    }
}
public sealed class Snapshot
{
    public string Paths { get; set; } = ""; public string Modes { get; set; } = "";
    public DateTime CapturedUtc { get; set; }
    public List<MonitorDevice> Monitors { get; set; } = [];
    public AudioDefaults? Audio { get; set; }
    public static Snapshot Capture()
    {
        var q = Native.Query();
        return new() { Paths = Native.Encode(q.Paths), Modes = Native.Encode(q.Modes), Monitors = Native.Monitors().Where(m => m.Active).ToList(), Audio = AudioDefaults.Capture(), CapturedUtc = DateTime.UtcNow };
    }
    public void Restore(bool transactional = false)
    {
        Exception? displayError = null;
        try { RestoreDisplay(transactional); } catch (Exception ex) { displayError = ex; }
        try { Audio?.Restore(transactional); }
        catch (Exception ex)
        {
            Store.Log("恢复声音失败：" + ex);
            if (displayError != null) throw new AggregateException("显示和声音恢复未完成。", displayError, ex);
            throw;
        }
        if (displayError != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(displayError).Throw();
    }
    void RestoreDisplay(bool transactional)
    {
        var paths = Native.Decode<DisplayPath>(Paths); var modes = Native.Decode<ModeInfo>(Modes);
        int Set(uint flags) => transactional ? Recovery.Mutate(() => Native.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags)) : Native.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, flags);
        int result = Set(Native.Apply | Native.Supplied | Native.Save);
        if (result != 0) result = Set(Native.Apply | Native.Supplied | Native.Save | Native.Allow);
        Native.Check(result, "恢复显示布局");
        Thread.Sleep(650);
        var current = Native.Monitors();
        foreach (var saved in Monitors.Where(m => m.Hdr.Known && m.Hdr.Supported))
        {
            var m = current.FirstOrDefault(m => m.Key == saved.Key && m.Active);
            if (m == null) continue;
            try { Native.SetHdr(m, saved.Hdr.Enabled, transactional); } catch (Exception ex) { Store.Log("恢复 HDR：" + ex.Message); throw; }
        }
        Store.Log("已恢复显示快照 " + CapturedUtc.ToString("O"));
    }
}
public enum Scene { ComputerOnly = 1, TvOnly = 2, Clone = 3, ComputerPrimary = 4, TvPrimary = 5 }
public static class Engine
{
    public static Scene ExclusiveToggleTarget(bool televisionActive, bool computerActive) => televisionActive && !computerActive ? Scene.ComputerOnly : Scene.TvOnly;
    public static MonitorDevice Bound(List<MonitorDevice> monitors, string key, string label) => monitors.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"找不到{label}。请打开电视/检查连接，或在“设备与偏好”重新选择设备。");
    public static DisplayPath[] Plan(Settings settings, Scene scene)
    {
        var monitors = Native.Monitors();
        var required = new List<MonitorDevice>();
        if (scene != Scene.TvOnly) required.Add(Bound(monitors, settings.ComputerKey, "电脑屏幕"));
        if (scene != Scene.ComputerOnly) required.Add(Bound(monitors, settings.TvKey, "电视"));
        if (required.Count == 2 && required[0].Key == required[1].Key) throw new Exception("电脑屏幕和电视必须选择不同设备。");
        var candidates = required.Select(m => m.Candidates.OrderByDescending(p => (p.Flags & 1) != 0).ToArray()).ToArray();
        var combinations = new List<DisplayPath[]>();
        if (candidates.Length == 1) combinations.AddRange(candidates[0].Select(p => new[] { p }));
        else foreach (var a in candidates[0]) foreach (var b in candidates[1])
        {
            bool same = Native.SourceKey(a) == Native.SourceKey(b);
            if ((scene == Scene.Clone) == same) combinations.Add([a, b]);
        }
        int last = 1610;
        foreach (var paths in combinations)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                paths[i].Flags = 1; paths[i].Source.ModeIndex = Native.Invalid; paths[i].Target.ModeIndex = Native.Invalid;
                paths[i].Target.Refresh = new Rational(); paths[i].Target.ScanLine = 0;
            }
            last = Native.SetDisplayConfig((uint)paths.Length, paths, 0, null, Native.Validate | Native.Supplied | Native.Allow);
            if (last == 0) return paths;
        }
        throw new Exception($"此场景未通过 Windows 显示驱动验证（{last}）。");
    }
    public static void SetPrimary(Settings settings, Scene scene)
    {
        var q = Native.Query();
        var monitors = Native.Monitors();
        var pc = Bound(monitors, settings.ComputerKey, "电脑屏幕"); var tv = Bound(monitors, settings.TvKey, "电视");
        var primary = scene == Scene.TvPrimary ? tv : pc;
        var secondary = scene == Scene.TvPrimary ? pc : tv;
        bool secondaryRight = secondary.Key == tv.Key ? settings.TvOnRight : !settings.TvOnRight;
        foreach (var p in q.Paths)
        {
            if (p.Source.ModeIndex >= q.Modes.Length) throw new Exception("显示驱动未返回有效的桌面布局。");
            int index = (int)p.Source.ModeIndex;
            if (q.Modes[index].Type != 1) throw new Exception("桌面布局类型无效。");
            bool isPrimary = Native.TargetKey(p) == Native.TargetKey(primary.Path);
            var mode = q.Modes[index];
            mode.Source.Position = new PointL { X = isPrimary ? 0 : secondaryRight ? (int)primary.Width : -(int)secondary.Width, Y = 0 };
            q.Modes[index] = mode;
        }
        Native.Check(Native.SetDisplayConfig((uint)q.Paths.Length, q.Paths, (uint)q.Modes.Length, q.Modes, Native.Validate | Native.Supplied), "验证主屏布局");
        Native.Check(Recovery.Mutate(() => Native.SetDisplayConfig((uint)q.Paths.Length, q.Paths, (uint)q.Modes.Length, q.Modes, Native.Apply | Native.Supplied | Native.Save)), "切换主屏幕");
        Thread.Sleep(350);
    }
    static List<MonitorDevice> WaitForScene(Settings settings, Scene scene)
    {
        for (int i = 0; i < 30; i++)
        {
            var current = Native.Monitors(); var active = current.Where(m => m.Active).ToList();
            bool pc = active.Any(m => m.Key == settings.ComputerKey), tv = active.Any(m => m.Key == settings.TvKey);
            bool ok = scene == Scene.ComputerOnly ? pc && !tv && active.Count == 1 : scene == Scene.TvOnly ? tv && !pc && active.Count == 1 : pc && tv && active.Count == 2;
            if (ok) return current; Thread.Sleep(250);
        }
        throw new Exception("等待设备切换超时，已请求恢复。");
    }
    public static string ApplyScene(Settings settings, Scene scene, string? hdrOverride = null)
    {
        var plan = Plan(settings, scene);
        Native.Check(Recovery.Mutate(() => Native.SetDisplayConfig((uint)plan.Length, plan, 0, null, Native.Apply | Native.Supplied | Native.Save | Native.Allow)), "切换显示场景");
        var monitors = WaitForScene(settings, scene); Thread.Sleep(500);
        if (scene != Scene.Clone)
        {
            if (scene != Scene.TvOnly) Native.ApplyMode(Bound(monitors, settings.ComputerKey, "电脑屏幕"), settings.ComputerMode, true);
            monitors = Native.Monitors();
            if (scene != Scene.ComputerOnly) Native.ApplyMode(Bound(monitors, settings.TvKey, "电视"), settings.TvMode, true);
            if (scene is Scene.ComputerPrimary or Scene.TvPrimary) SetPrimary(settings, scene);
        }
        monitors = Native.Monitors();
        string hdr = hdrOverride ?? settings.TvHdr;
        if (scene != Scene.ComputerOnly && hdr != "keep") Native.SetHdr(Bound(monitors, settings.TvKey, "电视"), hdr == "on", true);
        monitors = WaitForScene(settings, scene);
        if (scene is Scene.ComputerPrimary or Scene.TvPrimary)
        {
            var primary = Bound(monitors, scene == Scene.TvPrimary ? settings.TvKey : settings.ComputerKey, "主屏幕");
            if (primary.X != 0 || primary.Y != 0) throw new Exception("主屏幕位置未达到预期。");
        }
        if (scene == Scene.Clone && Native.Query().Paths.Select(Native.SourceKey).Distinct().Count() != 1) throw new Exception("驱动未切换到同步显示。");
        if (scene != Scene.Clone && scene != Scene.ComputerOnly)
        {
            var tv = Bound(monitors, settings.TvKey, "电视"); var expected = Native.ChooseMode(tv, settings.TvMode);
            if (tv.Width != expected.Width || tv.Height != expected.Height || Math.Abs(tv.Hz - expected.Frequency) > 1.1) throw new Exception("电视分辨率或刷新率未达到预期，已请求恢复。");
        }
        string audio = AudioSystem.ApplyScene(settings, scene);
        Store.Log("场景成功：" + scene + " HDR=" + hdr + " " + audio);
        return string.Join("\r\n", monitors.Where(m => m.Active).Select(m => m + " · " + m.Hdr)) + "\r\n" + audio;
    }
}
public sealed class PendingChange
{
    public string Id { get; set; } = "";
    public Snapshot Before { get; set; } = new();
    public DateTime ExpiresUtc { get; set; }
    public int OwnerPid { get; set; }
    public long OwnerStartTicks { get; set; }
}
public static class Recovery
{
    static readonly string LockName = @"Local\DisplaySceneSwitcher.Recovery";
    public static T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, LockName);
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        try { return action(); } finally { mutex.ReleaseMutex(); }
    }
    public static int Mutate(Func<int> operation) => Locked(() =>
    {
        if (!File.Exists(Store.PendingPath)) throw new Exception("切换已取消或已由恢复进程还原。");
        if (DateTime.UtcNow >= Store.Read<PendingChange>(Store.PendingPath).ExpiresUtc) throw new Exception("显示切换超时，正在恢复原设置。");
        return operation();
    });
    public static void Arm()
    {
        if (File.Exists(Store.PendingPath)) throw new Exception("上一次切换尚未确认，请先保留或恢复。");
        var before = Snapshot.Capture(); Store.Write(Store.PreviousPath, before);
        using var process = Process.GetCurrentProcess();
        string id = Guid.NewGuid().ToString("N");
        string armedPath = Store.PendingPath + ".armed." + id;
        Store.Write(Store.PendingPath, new PendingChange { Id = id, Before = before, ExpiresUtc = DateTime.UtcNow.AddSeconds(55), OwnerPid = process.Id, OwnerStartTicks = process.StartTime.ToUniversalTime().Ticks });
        File.Delete(Store.PendingPath + ".result");
        try
        {
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--watchdog " + id) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (child == null) throw new Exception("无法启动显示恢复进程。");
            for (int i = 0; i < 50; i++) { if (File.Exists(armedPath)) return; if (child.HasExited) break; Thread.Sleep(100); }
            throw new Exception("显示恢复进程未就绪，已取消切换。");
        }
        catch { File.Delete(Store.PendingPath); throw; }
    }
    public static void Countdown() => Locked(() => { var p = Store.Read<PendingChange>(Store.PendingPath); p.ExpiresUtc = DateTime.UtcNow.AddSeconds(20); Store.Write(Store.PendingPath, p); return true; });
    public static bool Accept() => Locked(() => { if (!File.Exists(Store.PendingPath)) return false; if (DateTime.UtcNow >= Store.Read<PendingChange>(Store.PendingPath).ExpiresUtc) { Revert(); return false; } File.Delete(Store.PendingPath); Store.Log("用户保留显示设置"); return true; });
    public static void Revert() => Revert(null);
    public static void Revert(string? expectedId)
    {
        Locked(() =>
        {
            if (!File.Exists(Store.PendingPath)) return true;
            var pending = Store.Read<PendingChange>(Store.PendingPath);
            if (expectedId != null && pending.Id != expectedId) return true;
            pending.Before.Restore(); File.Delete(Store.PendingPath);
            File.WriteAllText(Store.PendingPath + ".result", "已恢复切换前的显示和声音设置。"); return true;
        });
    }
    public static void Watchdog(string id)
    {
        string armedPath = Store.PendingPath + ".armed." + id;
        Directory.CreateDirectory(Store.Root); File.WriteAllText(armedPath, Environment.ProcessId.ToString());
        try
        {
            while (File.Exists(Store.PendingPath))
            {
                PendingChange pending;
                try { pending = Store.Read<PendingChange>(Store.PendingPath); } catch (IOException) { Thread.Sleep(200); continue; }
                if (pending.Id != id) return;
                bool ownerAlive;
                try { using var owner = Process.GetProcessById(pending.OwnerPid); ownerAlive = !owner.HasExited && owner.StartTime.ToUniversalTime().Ticks == pending.OwnerStartTicks; } catch { ownerAlive = false; }
                if (!ownerAlive || DateTime.UtcNow >= pending.ExpiresUtc)
                {
                    try { Revert(id); }
                    catch (Exception ex) { Store.Log("自动恢复失败：" + ex); File.WriteAllText(Store.PendingPath + ".result", "自动恢复失败：" + ex.Message); }
                    return;
                }
                Thread.Sleep(250);
            }
        }
        finally { File.Delete(armedPath); }
    }
}

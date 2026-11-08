using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MouseSideLab;

namespace DisplaySceneSwitcher;

public sealed class CodexWindows : IAppWindowHost
{
    // Verified against this computer's installed package manifest, independent of its versioned install path.
    public const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
    public const string ApplicationId = PackageFamily + "!App";
    delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder name);
    IntPtr observed;
    WindowBookmark? cachedForeground;
    internal static bool BelongsToCodex(Process process)
    {
        try
        {
            uint size = 256; var family = new StringBuilder((int)size);
            return GetPackageFamilyName(process.Handle, ref size, family) == 0 && family.ToString().Equals(PackageFamily, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    static WindowBookmark? Capture(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle)) return null;
        handle = GetAncestor(handle, 2); // Preserve an active dialog's root, rather than substituting its owner.
        GetWindowThreadProcessId(handle, out uint id);
        if (id == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)id);
            var name = new StringBuilder(128); GetClassName(handle, name, name.Capacity);
            return new WindowBookmark { Handle = handle, ProcessId = (int)id, ProcessStarted = process.StartTime.ToUniversalTime().Ticks,
                IsCodex = BelongsToCodex(process), IsDesktop = name.ToString() is "Progman" or "WorkerW" };
        }
        catch { return null; }
    }
    public WindowBookmark? Foreground()
    {
        var handle = Hotkeys.GetForegroundWindow();
        if (handle != observed || cachedForeground == null) { observed = handle; cachedForeground = Capture(handle); }
        return cachedForeground;
    }
    public bool IsValid(WindowBookmark? window)
    {
        if (window == null) return false;
        var actual = Capture(window.Handle);
        return actual != null && window.SameWindow(actual);
    }
    public WindowBookmark? FindCodex(WindowBookmark? preferred)
    {
        if (preferred != null && preferred.IsCodex && IsValid(preferred)) return preferred;
        WindowBookmark? found = null;
        EnumWindows((window, _) => {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero || (GetWindowLong(window, -20) & 0x80) != 0) return true;
            var candidate = Capture(window);
            if (candidate == null || !candidate.IsCodex) return true;
            found = candidate; return false;
        }, IntPtr.Zero);
        return found;
    }
    public void LaunchCodex()
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { "shell:AppsFolder\\" + ApplicationId } });
    }
    public async Task<bool> Activate(WindowBookmark window, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!IsValid(window)) return false;
        if (window.IsDesktop)
        {
            Hotkeys.Send("Win+D");
            await Task.Delay(120, cancellation);
            return Foreground()?.IsDesktop == true;
        }
        if (IsIconic(window.Handle)) { ShowWindowAsync(window.Handle, 9); await Task.Delay(60, cancellation); }
        cancellation.ThrowIfCancellationRequested();
        SetForegroundWindow(window.Handle);
        if (GetAncestor(Hotkeys.GetForegroundWindow(), 2) == window.Handle) return true;
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = GetWindowThreadProcessId(Hotkeys.GetForegroundWindow(), out _);
        bool attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try { BringWindowToTop(window.Handle); SetForegroundWindow(window.Handle); }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
        await Task.Delay(60, cancellation);
        return GetAncestor(Hotkeys.GetForegroundWindow(), 2) == window.Handle;
    }
    public static void Diagnose(string output)
    {
        var initialForeground = Hotkeys.GetForegroundWindow();
        var host = new CodexWindows();
        var target = host.FindCodex(null);
        var configPath = Path.Combine(Store.Root, "mouse", "settings.json");
        var config = new MouseSideLab.JsonCodec().Deserialize<MouseSideLab.Settings>(File.ReadAllText(configPath));
        config.Validate();
        bool migrated = config.SofaBindingsVersion == 6 && config.Shortcuts["both.hold"] == "ShowHelp" && config.Shortcuts["front.single"] == "CodexToggle" && config.Shortcuts["front.double"] == "Delete" && config.Shortcuts["back.double"] == "Ctrl+Z" && config.Shortcuts["back.single"] == "PetToggle" && config.Shortcuts["back.middle"] == "Win+D";
        Store.Write(output, new { Version = "2.4.2", ReadOnly = true, ApplicationId, CodexWindowFound = target != null,
            TargetIsCodex = target?.IsCodex, TargetProcessId = target?.ProcessId, WindowValid = host.IsValid(target),
            ConfigurationMigrationPassed = migrated, ForegroundUnchanged = Hotkeys.GetForegroundWindow() == initialForeground, ApplicationLaunched = false });
        if (target == null || !host.IsValid(target) || !migrated) throw new Exception("Codex 窗口识别或配置验证失败");
    }
}

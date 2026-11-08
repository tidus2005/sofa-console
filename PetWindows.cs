using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MouseSideLab;

namespace DisplaySceneSwitcher;

public sealed class PetWindows : IPetWindowHost
{
    delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    public PetWindowSnapshot[] Snapshot()
    {
        var windows = new List<PetWindowSnapshot>();
        // Async diagnostics resume on a different thread. Read bounds in physical pixels
        // on every thread so DPI virtualization cannot masquerade as a layout change.
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
        EnumWindows((window, _) => {
            var name = new StringBuilder(128); GetClassName(window, name, name.Capacity);
            if (name.ToString() != "Chrome_WidgetWin_1") return true;
            GetWindowThreadProcessId(window, out uint id);
            try
            {
                using var process = Process.GetProcessById((int)id);
                if (!CodexWindows.BelongsToCodex(process) || !GetWindowRect(window, out var rect)) return true;
                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                windows.Add(new PetWindowSnapshot {
                    Identity = new WindowBookmark { Handle = window, ProcessId = (int)id, ProcessStarted = process.StartTime.ToUniversalTime().Ticks, IsCodex = true },
                    ClassName = name.ToString(), Title = title.ToString(), Style = unchecked((uint)GetWindowLong(window, -16)),
                    ExtendedStyle = unchecked((uint)GetWindowLong(window, -20)), Owner = GetWindow(window, 4),
                    Visible = IsWindowVisible(window), Width = rect.Right - rect.Left, Height = rect.Bottom - rect.Top
                });
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        }
        finally { if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi); }
        var snapshots = windows.ToArray();
        WindowDocumentRoute.Resolve(snapshots);
        return snapshots;
    }
    public bool RequestVisibility(WindowBookmark window, bool visible)
    {
        // Validate the HWND together with PID/start time immediately before acting.
        // A stale/reused handle or another visible overlay must never select the main app.
        var candidates = Snapshot().Where(w => w.IsPetCandidate).ToArray();
        var target = candidates.SingleOrDefault(w => w.Identity.SameWindow(window));
        if (target == null || candidates.Any(w => w.Visible && !w.Identity.SameWindow(window))) return false;
        return ShowWindowAsync(window.Handle, visible ? 8 : 0); // SW_SHOWNA / SW_HIDE, not WM_CLOSE
    }
    public static void Diagnose(string output)
    {
        var before = Hotkeys.GetForegroundWindow();
        var snapshots = new PetWindows().Snapshot();
        Store.Write(output, new {
            Version = "2.4.3", ReadOnly = true, ForegroundUnchanged = before == Hotkeys.GetForegroundWindow(),
            VisibleOverlayCount = snapshots.Count(w => w.IsPetCandidate && w.Visible),
            Windows = snapshots.Select(w => new { Handle = w.Identity.Handle.ToInt64(), w.Identity.ProcessId, w.Visible, w.Title,
                Candidate = w.IsPetCandidate, w.DocumentUrl, Style = w.Style.ToString("X8"), ExtendedStyle = w.ExtendedStyle.ToString("X8"), w.Width, w.Height })
        });
    }
}

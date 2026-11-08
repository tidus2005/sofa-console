using System.Diagnostics;
using MouseSideLab;

namespace DisplaySceneSwitcher;

internal static class PetDirectDiagnostics
{
    public static async Task Run(string output)
    {
        var host = new PetWindows();
        var before = host.Snapshot();
        var candidates = before.Where(w => w.IsPetCandidate).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("测试需要一个唯一的现有宠物窗口；未操作任何窗口");
        var target = candidates[0];
        IntPtr foreground = Hotkeys.GetForegroundWindow();
        var toggle = new PetToggle(host);
        var stages = new List<object>();
        bool restoreRequested = false;
        try
        {
            for (int step = 0; step < 6; step++)
            {
                string result = await toggle.Toggle();
                bool expected = step % 2 == 0 ? !target.Visible : target.Visible;
                var watch = Stopwatch.StartNew();
                var sizes = new HashSet<string>();
                while (watch.ElapsedMilliseconds < 1500)
                {
                    var now = host.Snapshot();
                    var pet = now.SingleOrDefault(w => w.IsPetCandidate && w.Identity.SameWindow(target.Identity));
                    if (pet == null || pet.Visible != expected) throw new InvalidOperationException("宠物窗口显示状态不稳定：" + result);
                    sizes.Add(pet.Width + "x" + pet.Height);
                    if (pet.Width != target.Width || pet.Height != target.Height)
                        throw new InvalidOperationException("宠物窗口物理尺寸变化，需要检查输入框状态");
                    foreach (var other in before.Where(w => !w.IsPetCandidate))
                    {
                        var current = now.SingleOrDefault(w => w.Identity.SameWindow(other.Identity));
                        if (current == null || current.Visible != other.Visible) throw new InvalidOperationException("其他 Codex 窗口状态发生变化");
                    }
                    if (foreground != target.Identity.Handle && Hotkeys.GetForegroundWindow() != foreground)
                        throw new InvalidOperationException("测试期间焦点发生变化");
                    await Task.Delay(100);
                }
                stages.Add(new { Result = result, Visible = expected, SameHandle = true, ObservedSizes = sizes, StableForMs = watch.ElapsedMilliseconds });
            }
        }
        finally
        {
            var current = host.Snapshot().SingleOrDefault(w => w.IsPetCandidate && w.Identity.SameWindow(target.Identity));
            if (current != null && current.Visible != target.Visible)
            { restoreRequested = host.RequestVisibility(target.Identity, target.Visible); await Task.Delay(600); }
        }
        var restored = host.Snapshot().SingleOrDefault(w => w.IsPetCandidate && w.Identity.SameWindow(target.Identity));
        if (restored == null || restored.Visible != target.Visible) throw new InvalidOperationException("原始宠物状态未恢复");
        Store.Write(output, new { Version = "2.4.3", Passed = true, target.DocumentUrl, Control = "ShowWindowAsync(SW_SHOWNA / SW_HIDE)",
            KeyboardShortcutsSent = 0, CloseMessagesSent = 0, InitialVisible = target.Visible, FinalVisible = restored.Visible,
            Handle = target.Identity.Handle.ToInt64(), target.Identity.ProcessId, target.Width, target.Height,
            ForegroundPreserved = foreground == Hotkeys.GetForegroundWindow(), OtherCodexWindowsUnchanged = true,
            RestoreRequestedAfterFailure = restoreRequested, Stages = stages });
    }
}

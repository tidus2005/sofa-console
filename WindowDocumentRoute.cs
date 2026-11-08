using System.Windows.Automation;
using MouseSideLab;

namespace DisplaySceneSwitcher;

// Read only the document URL from overlay windows; never inspect conversation content.
// A single MTA worker and a bounded wait prevent a stalled provider from accumulating work.
internal static class WindowDocumentRoute
{
    private static readonly object Gate = new();
    private static Task<(WindowBookmark Identity, string Url)[]>? pending;

    public static void Resolve(PetWindowSnapshot[] windows)
    {
        var shells = windows.Where(w => w.IsOverlayShell).ToArray();
        if (shells.Length == 0) return;
        Task<(WindowBookmark Identity, string Url)[]> query;
        lock (Gate)
        {
            // Do not use stale completed results for a potentially reused HWND.
            if (pending == null || pending.IsCompleted)
                pending = Task.Run(() => Read(shells));
            query = pending;
        }
        if (!query.Wait(750)) return; // Unknown identity fails closed, with no shortcut fallback.
        foreach (var result in query.Result)
        {
            var target = shells.SingleOrDefault(w => w.Identity.SameWindow(result.Identity));
            if (target != null) target.DocumentUrl = result.Url;
        }
    }

    private static (WindowBookmark Identity, string Url)[] Read(PetWindowSnapshot[] windows)
    {
        var results = new List<(WindowBookmark, string)>();
        foreach (var window in windows)
        {
            try
            {
                var root = AutomationElement.FromHandle(window.Identity.Handle);
                var documents = root.FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                var urls = new HashSet<string>(StringComparer.Ordinal);
                foreach (AutomationElement document in documents)
                    if (document.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                        urls.Add(((ValuePattern)pattern).Current.Value);
                // Ambiguous document identity is not accepted.
                if (urls.Count == 1) results.Add((window.Identity, urls.Single()));
            }
            catch (Exception) { /* A closed window or unavailable provider is not a pet match. */ }
        }
        return results.ToArray();
    }
}

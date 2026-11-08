using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MouseSideLab
{
    public sealed class PetWindowSnapshot
    {
        public WindowBookmark Identity;
        public string ClassName, Title, DocumentUrl;
        public uint Style, ExtendedStyle;
        public bool Visible;
        public IntPtr Owner;
        public int Width, Height;
        public bool IsOverlayShell
        {
            get
            {
                // A prefilter only: dictation has the same native signature.
                // Hidden avatar windows may temporarily lose WS_EX_LAYERED.
                const uint required = 0x00000088; // tool window + topmost
                const uint frame = 0x00CF0000; // caption, system menu, resize frame, minimize/maximize
                return Identity != null && Identity.IsCodex && ClassName == "Chrome_WidgetWin_1" &&
                    (Title == "ChatGPT" || Title == "Codex") &&
                    Owner == IntPtr.Zero && Width > 0 && Height > 0 &&
                    (ExtendedStyle & required) == required && (ExtendedStyle & 0x00040000) == 0 &&
                    (Style & (frame | 0x40000000)) == 0;
            }
        }
        public bool IsPetCandidate { get { return IsOverlayShell && IsAvatarDocument(DocumentUrl); } }
        public static bool IsAvatarDocument(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "app" || uri.Host != "-" ||
                uri.AbsolutePath != "/index.html" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !uri.IsDefaultPort) return false;
            var routes = uri.Query.TrimStart('?').Split('&').Select(part => part.Split(new[] { '=' }, 2))
                .Where(pair => Uri.UnescapeDataString(pair[0]) == "initialRoute").ToArray();
            return routes.Length == 1 && routes[0].Length == 2 &&
                Uri.UnescapeDataString(routes[0][1]) == "/avatar-overlay";
        }
    }
    public interface IPetWindowHost
    {
        PetWindowSnapshot[] Snapshot();
        bool RequestVisibility(WindowBookmark window, bool visible);
    }
    public sealed class PetToggle
    {
        private readonly IPetWindowHost host;
        private readonly Func<int, CancellationToken, Task> delay;
        private CancellationTokenSource pending;
        private WindowBookmark lastWindow;
        public bool Busy { get { return pending != null; } }
        public PetToggle(IPetWindowHost windowHost) : this(windowHost, Task.Delay) { }
        internal PetToggle(IPetWindowHost windowHost, Func<int, CancellationToken, Task> wait)
        { host = windowHost; delay = wait; }
        public void CancelPending() { if (pending != null) pending.Cancel(); }
        public async Task<string> Toggle()
        {
            if (Busy) return "宠物浮窗正在切换，请稍候";
            var request = new CancellationTokenSource(); pending = request;
            try
            {
                var candidates = host.Snapshot().Where(w => w.IsPetCandidate).ToArray();
                var visible = candidates.Where(w => w.Visible).ToArray();
                if (visible.Length > 1) return "检测到多个 Codex 悬浮窗，无法安全确定宠物窗口";
                bool hide = visible.Length == 1;
                PetWindowSnapshot target = hide ? visible[0] : candidates.FirstOrDefault(w => w.Identity.SameWindow(lastWindow));
                if (target == null && candidates.Length == 1) target = candidates[0];
                if (target == null)
                {
                    if (candidates.Length > 1) return "检测到多个隐藏悬浮窗，无法安全确定宠物窗口";
                    return "未找到宠物窗口；请先从 Codex 的宠物菜单显示一次，再使用后侧键（不模拟快捷键）";
                }
                // Retain the existing renderer and its collapsed/expanded input state.
                // No close message, launcher, shortcut or input-box toggle is used.
                if (!host.RequestVisibility(target.Identity, !hide)) return "宠物窗口已变化或请求被拒绝，请重试";
                lastWindow = target.Identity;
                int stableSamples = 0;
                for (int i = 0; i < 30; i++)
                {
                    await delay(100, request.Token); request.Token.ThrowIfCancellationRequested();
                    var now = host.Snapshot().Where(w => w.IsPetCandidate).ToArray();
                    var current = now.FirstOrDefault(w => w.Identity.SameWindow(target.Identity));
                    if (current == null) return "宠物窗口已失效；请从 Codex 的宠物菜单重新显示一次";
                    if (now.Any(w => w.Visible && !w.Identity.SameWindow(target.Identity))) return "出现其他可见悬浮窗，已停止检查";
                    stableSamples = current.Visible == !hide ? stableSamples + 1 : 0;
                    if (stableSamples >= 5) return hide ? "已隐藏整个宠物浮窗（保留窗口）" : "已显示宠物浮窗（直接窗口控制）";
                }
                return hide ? "宠物浮窗仍可见，隐藏未成功" : "宠物浮窗显示未成功，应用可能重新改变了显示状态";
            }
            catch (OperationCanceledException) { return "宠物切换检查已取消"; }
            catch (Exception ex) { return "宠物切换失败：" + ex.Message; }
            finally { if (pending == request) pending = null; request.Dispose(); }
        }
    }
}

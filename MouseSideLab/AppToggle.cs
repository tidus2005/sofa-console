using System;
using System.Threading;
using System.Threading.Tasks;

namespace MouseSideLab
{
    public sealed class WindowBookmark
    {
        public IntPtr Handle;
        public int ProcessId;
        public long ProcessStarted;
        public bool IsCodex, IsDesktop;
        public bool SameWindow(WindowBookmark other)
        { return other != null && Handle == other.Handle && ProcessId == other.ProcessId && ProcessStarted == other.ProcessStarted; }
    }
    public interface IAppWindowHost
    {
        WindowBookmark Foreground();
        WindowBookmark FindCodex(WindowBookmark preferred);
        bool IsValid(WindowBookmark window);
        Task<bool> Activate(WindowBookmark window, CancellationToken cancellation);
        void LaunchCodex();
    }
    public sealed class AppToggle
    {
        private readonly IAppWindowHost host;
        private readonly Func<int, CancellationToken, Task> delay;
        private WindowBookmark returnWindow, lastExternal, preferredCodex;
        private CancellationTokenSource pending;
        public bool Busy { get { return pending != null; } }
        public AppToggle(IAppWindowHost windowHost) : this(windowHost, Task.Delay) { }
        internal AppToggle(IAppWindowHost windowHost, Func<int, CancellationToken, Task> wait)
        { host = windowHost; delay = wait; }
        public void ObserveForeground()
        {
            var current = host.Foreground();
            if (current == null) return;
            if (current.IsCodex) preferredCodex = current;
            else lastExternal = current;
        }
        public void CancelPending() { if (pending != null) pending.Cancel(); }
        public async Task<string> Toggle()
        {
            if (Busy) return "Codex 正在切换，请稍候";
            var request = new CancellationTokenSource(); pending = request;
            try
            {
                var origin = host.Foreground();
                if (origin != null && origin.IsCodex)
                {
                    preferredCodex = origin;
                    var previous = returnWindow ?? lastExternal;
                    if (previous == null) return "尚未记住返回窗口：先从其他软件单击前键进入 Codex";
                    if (!host.IsValid(previous)) return "切换前的窗口已关闭，无法返回";
                    return await host.Activate(previous, request.Token) ? "已返回切换前的窗口" : "Windows 未能激活原窗口，请重试";
                }
                // Capture the exact window before launch; don't overwrite this bookmark with a toast or launcher.
                var destination = host.FindCodex(preferredCodex);
                if (destination == null)
                {
                    host.LaunchCodex();
                    for (int i = 0; i < 40 && destination == null; i++)
                    {
                        await delay(200, request.Token);
                        destination = host.FindCodex(preferredCodex);
                    }
                }
                request.Token.ThrowIfCancellationRequested();
                if (destination == null) return "Codex 已请求打开，但暂未找到主窗口";
                var current = host.Foreground();
                if (current != null && !current.IsCodex && (origin == null || !origin.SameWindow(current)))
                    return "你已切到其他窗口，本次不再改变焦点";
                if (!await host.Activate(destination, request.Token)) return "Windows 未能激活 Codex，请重试";
                preferredCodex = destination;
                if (origin != null) returnWindow = origin;
                else if (host.IsValid(lastExternal)) returnWindow = lastExternal;
                return "已切到 Codex；再单击前键返回";
            }
            catch (OperationCanceledException) { return "Codex 切换已取消"; }
            catch (Exception ex) { return "Codex 切换失败：" + ex.Message; }
            finally { if (pending == request) pending = null; request.Dispose(); }
        }
    }
}

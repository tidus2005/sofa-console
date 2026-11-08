using System;

namespace MouseSideLab
{
    public sealed class GestureEvent
    {
        public string Timestamp { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public int PhysicalButton { get; set; }
        public long HeldMs { get; set; }
        public string Shortcut { get; set; }
        public bool ExecuteShortcut { get; set; }
        internal IntPtr TargetWindow;
    }
    public static class DeveloperActions
    {
        // The single integration point. Runs on an ordered background worker.
        // Received OS input (including driver forwarding) may reach this function; demos never execute actions.
        // Do not access WinForms controls here; marshal back with BeginInvoke if needed.
        public static void Handle(GestureEvent e)
        {
            switch (e.Code)
            {
                case "front.single": break; // 前键单击：在这里绑定操作
                case "front.double": break; // 前键双击
                case "front.triple": break; // 前键三击
                case "front.hold": break;   // 前键长按（每次按住只触发一次）
                case "back.single": break;
                case "back.double": break;
                case "back.triple": break;
                case "back.hold": break;
                case "both.chord": break;   // 双键组合，必须全部松开才能开始下一次
                // Also available: front/back.left, .right, .middle, .wheelup, .wheeldown
            }
        }
    }
}

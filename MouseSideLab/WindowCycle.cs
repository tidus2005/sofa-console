using System;

namespace MouseSideLab
{
    // One Alt-Tab session spans all wheel notches. Releasing the side key commits the selection.
    public sealed class WindowCycle : IDisposable
    {
        private readonly Func<ushort, bool> isPressed;
        private readonly Action<ushort, bool> setKey;
        private readonly Action<string> send;
        private bool active, ownsAlt;
        public bool Active { get { return active; } }
        public WindowCycle() : this(Hotkeys.IsPressed, Hotkeys.SetKey, Hotkeys.Send) { }
        internal WindowCycle(Func<ushort, bool> pressed, Action<ushort, bool> key, Action<string> chord)
        { isPressed = pressed; setKey = key; send = chord; }
        public void Step(bool previous)
        {
            try
            {
                if (!active)
                {
                    active = true;
                    ownsAlt = !isPressed(0x12);
                    if (ownsAlt) setKey(0x12, true);
                }
                send(previous ? "Shift+Tab" : "Tab");
            }
            catch { Finish(); throw; }
        }
        public void Finish()
        {
            if (!active) return;
            try { if (ownsAlt) setKey(0x12, false); }
            finally { active = false; ownsAlt = false; }
        }
        public void Dispose() { Finish(); }
    }
    public static class SofaBindings
    {
        public static string Shortcut(string code)
        {
            switch (code)
            {
                case "front.single": return "CodexToggle";
                case "front.double": return "Delete";
                case "front.triple": return "Esc";
                case "back.single": return "PetToggle";
                case "back.double": return "Ctrl+Z";
                case "front.middle": return "Mute";
                case "back.middle": return "Win+D";
                case "back.wheelup": return "WindowPrevious";
                case "back.wheeldown": return "WindowNext";
                case "back.triple": return "Enter";
                case "both.hold": return "ShowHelp";
                default: return null;
            }
        }
    }
}

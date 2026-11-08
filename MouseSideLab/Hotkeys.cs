using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseSideLab
{
    public static class Hotkeys
    {
        private static readonly object sendGate = new object();
        [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public Keyboard Key; [FieldOffset(0)] public Mouse Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        private static ushort Key(string token)
        {
            token = token.Trim().ToUpperInvariant();
            if (token.Length == 1 && char.IsLetterOrDigit(token[0])) return token[0];
            switch (token)
            {
                case "CTRL": return 0x11; case "SHIFT": return 0x10; case "ALT": return 0x12; case "WIN": return 0x5B;
                case "ENTER": return 0x0D; case "ESC": return 0x1B; case "TAB": return 0x09; case "SPACE": return 0x20;
                case "LEFT": return 0x25; case "UP": return 0x26; case "RIGHT": return 0x27; case "DOWN": return 0x28;
                case "HOME": return 0x24; case "END": return 0x23; case "BACKSPACE": return 0x08; case "DELETE": return 0x2E;
                case "PLAYPAUSE": return 0xB3; case "MUTE": return 0xAD; case "VOLUMEDOWN": return 0xAE; case "VOLUMEUP": return 0xAF;
                case "PLUS": return 0xBB; case "MINUS": return 0xBD;
            }
            int n;
            if (token.StartsWith("F") && int.TryParse(token.Substring(1), out n) && n >= 1 && n <= 12) return (ushort)(0x6F + n);
            throw new ArgumentException("不支持的快捷键：" + token);
        }
        public static ushort[] Parse(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut) || shortcut == "hint" || shortcut == "panel" || shortcut == "WindowPrevious" || shortcut == "WindowNext" || shortcut == "DisplayToggle" || shortcut == "CodexToggle" || shortcut == "PetToggle" || shortcut == "ShowHelp") return new ushort[0];
            string[] tokens = shortcut.Split('+'); var result = new List<ushort>();
            foreach (string token in tokens) result.Add(Key(token));
            return result.ToArray();
        }
        private static Input Make(ushort vk, bool up)
        {
            bool extended = vk == 0x5B || (vk >= 0x21 && vk <= 0x28) || (vk >= 0xAD && vk <= 0xB3) || vk == 0x2E;
            return new Input { Type = 1, Data = new InputData { Key = new Keyboard { Vk = vk, Flags = (up ? 2u : 0u) | (extended ? 1u : 0u) } } };
        }
        internal static bool IsPressed(ushort key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static void SetKey(ushort key, bool down)
        {
            lock (sendGate) {
            if (SendInput(1, new[] { Make(key, !down) }, Marshal.SizeOf(typeof(Input))) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "窗口切换按键未发送");
            System.Threading.Thread.Sleep(10);
            }
        }
        public static void Send(string shortcut)
        { lock (sendGate) SendCore(shortcut); }
        private static void SendCore(string shortcut)
        {
            ushort[] keys = Parse(shortcut); if (keys.Length == 0) return;
            var inputs = new List<Input>(); var pressed = new List<ushort>();
            foreach (ushort vk in keys)
            {
                // Preserve keys the user is already holding on a physical keyboard.
                if ((GetAsyncKeyState(vk) & 0x8000) == 0) { inputs.Add(Make(vk, false)); pressed.Add(vk); }
            }
            for (int i = pressed.Count - 1; i >= 0; i--) inputs.Add(Make(pressed[i], true));
            if (inputs.Count == 0) return;
            // This machine rejects multi-event SendInput batches (zero/error 0).
            // Submit each transition once, checking its result; never replay a partial chord.
            uint sent = SendTransitions(inputs);
            if (sent != inputs.Count)
            {
                int error = Marshal.GetLastWin32Error();
                // Only release transitions accepted by Windows that remain held.
                int held = sent <= pressed.Count ? (int)sent : inputs.Count - (int)sent;
                for (int i = held - 1; i >= 0; i--) SendInput(1, new[] { Make(pressed[i], true) }, Marshal.SizeOf(typeof(Input)));
                throw new Win32Exception(error, "快捷键未完整发送：" + shortcut + "，已发送 " + sent + "/" + inputs.Count + "，错误码 " + error);
            }
        }
        private static uint SendTransitions(List<Input> inputs)
        {
            uint sent = 0;
            foreach (Input input in inputs)
            {
                if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1) break;
                sent++;
                System.Threading.Thread.Sleep(10);
            }
            return sent;
        }
    }
}

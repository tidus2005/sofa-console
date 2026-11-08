using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseSideLab
{
    // Passive evidence only. Do not disable legacy input or replace the existing hook.
    internal sealed class RawMouseWindow : Control
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        private readonly NativeMouse owner;
        private bool registered;
        public RawMouseWindow(NativeMouse owner) { this.owner = owner; }
        public void Register()
        {
            registered = RegisterRawInputDevices(new[] { new Device { Page = 1, Usage = 2, Flags = 0x2100, Target = Handle } }, 1, (uint)Marshal.SizeOf(typeof(Device)));
            owner.RawInputAvailable = registered;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0xFF)
            {
                uint bytes = 0, header = (uint)(8 + 2 * IntPtr.Size);
                if (GetRawInputData(m.LParam, 0x10000003, IntPtr.Zero, ref bytes, header) == 0 && bytes >= header + 24 && bytes < 65536)
                {
                    IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
                    try
                    {
                        uint copied = GetRawInputData(m.LParam, 0x10000003, buffer, ref bytes, header);
                        if (copied != uint.MaxValue && copied >= header + 24 && Marshal.ReadInt32(buffer) == 0)
                            owner.RecordRawButtons(Marshal.ReadIntPtr(buffer, 8), (ushort)Marshal.ReadInt16(buffer, (int)header + 4), owner.Now);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            else if (m.Msg == 0xFE && m.WParam.ToInt32() == 2) owner.RawDeviceRemoved(m.LParam);
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing)
        {
            if (registered)
            {
                RegisterRawInputDevices(new[] { new Device { Page = 1, Usage = 2, Flags = 1, Target = IntPtr.Zero } }, 1, (uint)Marshal.SizeOf(typeof(Device)));
                registered = false; owner.RawInputAvailable = false;
            }
            base.Dispose(disposing);
        }
    }
}

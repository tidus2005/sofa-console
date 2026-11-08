using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MouseSideLab
{
    public sealed class MousePacket
    {
        public int Button, Generation;
        public bool Down;
        public long TimeMs;
        public string Kind, ResetReason;
        public bool Injected, ObservedOnly;
    }
    public sealed class NativeMouse : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct HookData
        { public Point Point; public uint MouseData, Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int type, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, UIntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly object gate = new object();
        private readonly ConcurrentQueue<MousePacket> packets = new ConcurrentQueue<MousePacket>();
        private readonly bool[] physicalDown = new bool[2], swallowed = new bool[2];
        private readonly int[] startedGeneration = { -1, -1 };
        private readonly int[] activeSource = { -1, -1 };
        private readonly long[] downAt = new long[2];
        private readonly long[] lastReleaseAt = { long.MinValue, long.MinValue };
        private readonly long[] rawAfter = { long.MinValue, long.MinValue };
        private sealed class RawPress { public long At; public bool Down; }
        private readonly Dictionary<IntPtr, RawPress[]> rawDevices = new Dictionary<IntPtr, RawPress[]>();
        private int repeatButton;
        private long repeatStarted;
        public bool RawInputAvailable { get; internal set; }
        internal event Action<IntPtr, ushort, long> RawButtonsObserved;
        private readonly uint ownProcessId = (uint)Process.GetCurrentProcess().Id;
        internal const int MaxHoldMs = 5000;
        private bool chordOverlap, captureCombinations;
        private bool hardwareChordActive, hardwareChordWaitingRelease, hardwareChordFired;
        private long hardwareChordStarted;
        private int volumeWheelButton, muteMiddleButton, windowWheelButton, desktopMiddleButton;
        private readonly bool[] mainDown = new bool[3], mainSwallowed = new bool[3];
        private int wheelRemainder, wheelSide;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private HookProc callback;
        private Thread thread;
        private IntPtr hook;
        private uint threadId;
        private Exception startError;
        private bool enabled = true, suppress = true, disposed;
        private volatile bool acceptDriver = true;
        private int generation;
        public long Now { get { return clock.ElapsedMilliseconds; } }
        public int Generation { get { lock (gate) return generation; } }
        public bool TryRead(out MousePacket packet) { return packets.TryDequeue(out packet); }
        // Atomic input/time snapshot prevents an idle timer from overtaking queued input.
        public MousePacket[] Drain(out long now)
        { lock (gate) { now = Now; ExpireStaleHolds(now); var all = packets.ToArray(); MousePacket p; while (packets.TryDequeue(out p)) { } return all; } }
        public void Start()
        {
            callback = Callback;
            thread = new Thread(delegate()
            {
                try
                {
                    threadId = GetCurrentThreadId();
                    physicalDown[0] = (GetAsyncKeyState(5) & 0x8000) != 0;
                    physicalDown[1] = (GetAsyncKeyState(6) & 0x8000) != 0;
                    mainDown[0] = (GetAsyncKeyState(1) & 0x8000) != 0;
                    mainDown[1] = (GetAsyncKeyState(2) & 0x8000) != 0;
                    mainDown[2] = (GetAsyncKeyState(4) & 0x8000) != 0;
                    hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
                    if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    // Creating a hidden control ensures this thread has a message queue.
                    using (var context = new ApplicationContext())
                    using (var control = new RawMouseWindow(this))
                    {
                        IntPtr handle = control.Handle;
                        control.Register();
                        ready.Set(); Application.Run(context);
                    }
                }
                catch (Exception ex) { startError = ex; ready.Set(); }
                finally { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
            });
            thread.IsBackground = true; thread.Name = "Side button hook";
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            if (!ready.WaitOne(5000)) throw new TimeoutException("鼠标监听启动超时");
            if (startError != null) throw startError;
        }
        public int Configure(bool listen, bool block, bool driverCompatibility = true, bool captureMainButtons = false, int volumeButton = 0, int muteButton = 0, int windowButton = 0, int desktopButton = 0)
        {
            lock (gate)
            {
                enabled = listen; suppress = block; acceptDriver = driverCompatibility; generation++;
                captureCombinations = captureMainButtons; chordOverlap = false;
                hardwareChordActive = hardwareChordWaitingRelease = hardwareChordFired = false;
                repeatButton = 0; rawDevices.Clear();
                volumeWheelButton = volumeButton; muteMiddleButton = muteButton; windowWheelButton = windowButton; desktopMiddleButton = desktopButton;
                wheelRemainder = 0; wheelSide = 0;
                MousePacket p; while (packets.TryDequeue(out p)) { }
                return generation;
            }
        }
        internal void RecordRawButtons(IntPtr device, ushort flags, long time)
        {
            lock (gate)
            {
                if ((flags & 0x3C0) == 0) return;
                if (RawButtonsObserved != null) RawButtonsObserved(device, flags, time);
                RawPress[] state;
                if (!rawDevices.TryGetValue(device, out state))
                { state = new RawPress[2]; rawDevices[device] = state; }
                for (int i = 0; i < 2; i++)
                {
                    int downFlag = i == 0 ? 0x40 : 0x100, upFlag = downFlag * 2;
                    if ((flags & downFlag) != 0 && (state[i] == null || !state[i].Down)) state[i] = new RawPress { At = time, Down = true };
                    if ((flags & upFlag) != 0 && state[i] != null) state[i].Down = false;
                }
            }
        }
        internal void RecordHardwareChord(bool both, bool any, long time)
        {
            lock (gate)
            {
                if (!enabled) return;
                if (hardwareChordWaitingRelease)
                {
                    if (hardwareChordActive && !both)
                    {
                        hardwareChordActive = false;
                        CancelHeldState(time, "HID++ 双键已有一键松开");
                    }
                    if (!any) hardwareChordWaitingRelease = false;
                }
                else if (both)
                {
                    hardwareChordActive = hardwareChordWaitingRelease = true;
                    hardwareChordFired = false; hardwareChordStarted = time;
                    CancelHeldState(time, "HID++ 已确认两个侧键同时按住，开始 3 秒计时");
                }
            }
        }
        internal void HardwareChordDisconnected(long time)
        {
            lock (gate)
            {
                if (hardwareChordWaitingRelease) CancelHeldState(time, "HID++ 接口断开，取消双键计时");
                hardwareChordActive = hardwareChordWaitingRelease = hardwareChordFired = false;
            }
        }
        internal void RawDeviceRemoved(IntPtr device)
        {
            lock (gate)
            {
                RawPress[] state;
                if (rawDevices.TryGetValue(device, out state))
                    foreach (RawPress press in state) if (press != null) press.Down = false;
            }
        }
        // 0 = no unambiguous matching evidence, 1 = held, -1 = released/removed.
        private int RawHoldState(int button, long began)
        {
            int matches = 0, result = 0;
            foreach (RawPress[] state in rawDevices.Values)
            {
                RawPress press = state[button - 1];
                if (press == null || press.At <= rawAfter[button - 1] || Math.Abs(press.At - began) > 250) continue;
                matches++; result = press.Down ? 1 : -1;
            }
            return matches == 1 ? result : 0;
        }
        internal void SetRepeatHold(int button, long began)
        { lock (gate) { repeatButton = button; repeatStarted = began; } }
        internal bool IsRepeatHeld(int button, long began)
        {
            lock (gate)
            {
                return enabled && !chordOverlap && physicalDown[button - 1] && !physicalDown[2 - button]
                    && startedGeneration[button - 1] == generation && downAt[button - 1] == began
                    && RawHoldState(button, began) != -1;
            }
        }
        private IntPtr Callback(int code, IntPtr message, IntPtr data)
        {
            long msg = message.ToInt64();
            if (code >= 0 && (msg == 0x201 || msg == 0x202 || msg == 0x204 || msg == 0x205 || msg == 0x207 || msg == 0x208 || msg == 0x20A))
            {
                HookData h = (HookData)Marshal.PtrToStructure(data, typeof(HookData));
                uint foregroundProcess; GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
                if (ProcessMain((int)msg, h.Flags, (short)(h.MouseData >> 16), Now, foregroundProcess == ownProcessId)) return new IntPtr(1);
            }
            if (code >= 0 && (message.ToInt64() == 0x20B || message.ToInt64() == 0x20C))
            {
                HookData h = (HookData)Marshal.PtrToStructure(data, typeof(HookData));
                int button = (int)((h.MouseData >> 16) & 0xffff);
                if ((button == 1 || button == 2) && ProcessSide(button, msg == 0x20B, h.Flags, Now)) return new IntPtr(1);
            }
            return CallNextHookEx(hook, code, message, data);
        }
        // Test mode and our own windows must remain clickable, even if a side release is lost.
        internal bool ProcessMain(int message, uint flags, short wheelDelta, long time, bool protectedWindow = false)
        {
            lock (gate)
            {
                ExpireStaleHolds(time);
                if ((flags & 1) != 0 && !acceptDriver) return false;
                int side = 0, active = 0;
                for (int j = 0; j < 2; j++)
                    if (enabled && physicalDown[j] && startedGeneration[j] == generation) { side = j + 1; active++; }
                bool combo = active == 1 && !chordOverlap && !protectedWindow;
                bool canBlock = combo && suppress && captureCombinations;
                if (message == 0x20A)
                {
                    bool dedicatedWheel = active == 1 && !chordOverlap && (side == volumeWheelButton || side == windowWheelButton);
                    if (combo || dedicatedWheel)
                    {
                        if (wheelSide != side) { wheelRemainder = 0; wheelSide = side; }
                        wheelRemainder += wheelDelta;
                        while (Math.Abs(wheelRemainder) >= 120)
                        {
                            bool up = wheelRemainder > 0; wheelRemainder += up ? -120 : 120;
                            packets.Enqueue(new MousePacket { Button = side, Kind = up ? "wheelup" : "wheeldown", Generation = generation, TimeMs = time, Injected = (flags & 1) != 0 });
                        }
                    }
                    return canBlock || dedicatedWheel;
                }
                int i = message <= 0x202 ? 0 : message <= 0x205 ? 1 : 2;
                bool down = message == 0x201 || message == 0x204 || message == 0x207;
                bool dedicatedMiddle = i == 2 && active == 1 && !chordOverlap && (side == muteMiddleButton || side == desktopMiddleButton);
                canBlock = canBlock || dedicatedMiddle;
                if (down)
                {
                    // A fresh down with no live modifier also recovers a lost primary-button up.
                    if (!mainDown[i] || !canBlock) mainSwallowed[i] = canBlock;
                    if (!mainDown[i] && (combo || dedicatedMiddle))
                        packets.Enqueue(new MousePacket { Button = side, Kind = i == 0 ? "left" : i == 1 ? "right" : "middle", Generation = generation, TimeMs = time, Injected = (flags & 1) != 0 });
                }
                bool block = mainSwallowed[i]; mainDown[i] = down;
                if (!down) mainSwallowed[i] = false;
                return block;
            }
        }
        internal void ExpireStaleHolds(long time)
        {
            lock (gate)
            {
                if (hardwareChordActive)
                {
                    long held = time - hardwareChordStarted;
                    if (held >= MaxHoldMs)
                    {
                        hardwareChordActive = false;
                        CancelHeldState(time, "HID++ 双键已达 5 秒，退出侧键组合状态");
                    }
                    else if (!hardwareChordFired && held >= GestureEngine.ChordHoldMs)
                    {
                        hardwareChordFired = true;
                        packets.Enqueue(new MousePacket { Kind = "hardwarechordhold", Button = 0, TimeMs = time, Generation = generation });
                    }
                }
                for (int i = 0; i < 2; i++)
                    if (physicalDown[i] && startedGeneration[i] == generation && time - downAt[i] >= MaxHoldMs)
                    {
                        if (repeatButton == i + 1 && repeatStarted == downAt[i] && !chordOverlap &&
                            !physicalDown[1 - i] && RawHoldState(i + 1, downAt[i]) == 1) continue;
                        CancelHeldState(time, "侧键按住达到 5 秒，已自动释放；请松开后重新按下");
                        // Recovery prioritizes restoring normal clicks even if a primary up was lost too.
                        Array.Clear(mainDown, 0, mainDown.Length); Array.Clear(mainSwallowed, 0, mainSwallowed.Length);
                        break;
                    }
            }
        }
        private void CancelHeldState(long time, string reason)
        {
            for (int i = 0; i < 2; i++) { physicalDown[i] = false; activeSource[i] = -1; startedGeneration[i] = -1; }
            chordOverlap = false; wheelRemainder = 0; wheelSide = 0;
            repeatButton = 0;
            packets.Enqueue(new MousePacket { Kind = "reset", ResetReason = reason, TimeMs = time, Generation = generation });
        }
        // The hook and regression tests share this input path. Tests do not inject OS input.
        internal bool ProcessSide(int button, bool down, uint flags, long time)
        {
            if (button != 1 && button != 2) throw new ArgumentOutOfRangeException("button");
            int i = button - 1, source = (flags & 1) != 0 ? 1 : 0;
            lock (gate)
            {
                ExpireStaleHolds(time);
                if (hardwareChordWaitingRelease && time - hardwareChordStarted < MaxHoldMs)
                {
                    // The vendor report owns this chord. Do not turn Options+ echoes
                    // into clicks, deletes, or a second timer. Primary buttons pass through.
                    packets.Enqueue(new MousePacket { Button = button, Down = down, Generation = generation,
                        TimeMs = time, Injected = source == 1, ObservedOnly = true });
                    return enabled && suppress;
                }
                bool block = false, accepted = false;
                bool endChord = false;
                // Keep the source that began the press until it releases: duplicate driver
                // echoes must not release another source's held button prematurely.
                bool owner = physicalDown[i] && (activeSource[i] == source || activeSource[i] < 0);
                if (owner && !down)
                {
                    block = swallowed[i]; accepted = enabled && startedGeneration[i] == generation;
                    endChord = chordOverlap;
                    physicalDown[i] = false; activeSource[i] = -1; swallowed[i] = false;
                    lastReleaseAt[i] = time;
                    startedGeneration[i] = -1; wheelRemainder = 0; wheelSide = 0;
                }
                else if (!physicalDown[i] && down && (source == 0 || acceptDriver))
                {
                    physicalDown[i] = true; activeSource[i] = source;
                    downAt[i] = time;
                    rawAfter[i] = lastReleaseAt[i];
                    swallowed[i] = enabled && suppress; startedGeneration[i] = enabled ? generation : -1;
                    block = swallowed[i]; accepted = enabled;
                    if (enabled && physicalDown[1-i] && startedGeneration[1-i] == generation)
                    { chordOverlap = true; downAt[0] = downAt[1] = time; }
                }
                else if (physicalDown[i]) block = swallowed[i];
                else if (!down) { block = swallowed[i]; swallowed[i] = false; }
                packets.Enqueue(new MousePacket { Button = button, Down = down, Generation = generation,
                    TimeMs = time, Injected = source == 1, ObservedOnly = !accepted });
                // Some drivers omit one release after a chord. End the whole modifier session
                // on the first release; a fresh down is required before either side can modify clicks.
                if (endChord) CancelHeldState(time, "双键已有一键松开，已退出组合状态");
                return block;
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Configure(false, false);
            if (threadId != 0) PostThreadMessage(threadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
            if (thread != null) thread.Join(1500);
            ready.Dispose(); GC.KeepAlive(callback);
        }
    }
}

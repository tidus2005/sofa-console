using System.Runtime.InteropServices;

namespace DisplaySceneSwitcher;

// Public Windows Core Audio endpoint-volume API; the output is resolved for every adjustment.
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
public sealed record VolumeReading(string DeviceId, float Scalar, bool Muted)
{
    public int Percent => (int)Math.Round(Scalar * 100);
}
public static class AudioVolume
{
    static readonly Guid VolumeGuid = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
    static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    static VolumeReading Access(string? id, Func<float, float>? change, Func<bool, bool>? changeMute = null)
    {
        var enumerator = (IAudioEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
        IAudioDevice? device = null; object? endpoint = null;
        try
        {
            if (id == null) Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
            else Check(enumerator.GetDevice(id, out device));
            Check(device.GetId(out string deviceId));
            Guid iid = VolumeGuid; Check(device.Activate(ref iid, 23, IntPtr.Zero, out endpoint));
            var volume = (IEndpointVolume)endpoint;
            Check(volume.GetMasterVolumeLevelScalar(out float scalar));
            if (change != null)
            {
                Guid context = Guid.Empty;
                Check(volume.SetMasterVolumeLevelScalar(Math.Clamp(change(scalar), 0f, 1f), ref context));
                Check(volume.GetMasterVolumeLevelScalar(out scalar));
            }
            Check(volume.GetMute(out bool muted));
            if (changeMute != null)
            {
                Guid context = Guid.Empty;
                Check(volume.SetMute(changeMute(muted), ref context));
                Check(volume.GetMute(out muted));
            }
            return new(deviceId, scalar, muted);
        }
        finally { Release(endpoint); Release(device); Release(enumerator); }
    }
    public static VolumeReading ReadDefault() => Access(null, null);
    public static VolumeReading AdjustDefault(int percentagePoints) => Access(null, current => current + percentagePoints / 100f);
    public static VolumeReading ToggleMuteDefault() => Access(null, null, muted => !muted);
    internal static VolumeReading Restore(VolumeReading saved) => Access(saved.DeviceId, _ => saved.Scalar, _ => saved.Muted);
}

public static class MouseVolumeDiagnostics
{
    public static void Test(string output)
    {
        var baseline = AudioVolume.ReadDefault();
        var readings = new List<object>();
        var engine = new MouseSideLab.GestureEngine(300, 650, 150);
        using var input = new MouseSideLab.NativeMouse();
        input.Configure(true, true, true, false, 2);
        int operations = 0;
        engine.Recognized += gesture =>
        {
            if (gesture.Button != 2 || gesture.Kind is not ("wheelup" or "wheeldown")) throw new Exception("出现意外手势：" + gesture.Kind);
            var reading = AudioVolume.AdjustDefault(gesture.Kind == "wheelup" ? 2 : -2);
            readings.Add(new { gesture.Kind, reading.Percent, reading.Scalar, reading.Muted }); operations++;
        };
        void Pump()
        {
            while (input.TryRead(out var packet))
            {
                if (packet.ObservedOnly) continue;
                if (packet.Kind == "reset") engine.Reset();
                else if (packet.Kind == null) engine.Input(packet.Button, packet.Down, packet.TimeMs);
                else engine.Combo(packet.Button, packet.Kind, packet.TimeMs);
            }
        }
        void Assert(bool value, string error) { if (!value) throw new Exception(error); }
        bool restored = false, passed = false;
        try
        {
            input.ProcessSide(2, true, 1, 0); Pump();
            short first = baseline.Scalar > 0.97f ? (short)-120 : (short)120;
            Assert(input.ProcessMain(0x20A, 0, first, 100, true), "情境滚轮没有被接管"); Pump();
            var changed = AudioVolume.ReadDefault();
            Assert(first > 0 ? changed.Scalar > baseline.Scalar : changed.Scalar < baseline.Scalar, "系统音量没有按预期变化");
            Assert(input.ProcessMain(0x20A, 0, (short)-first, 200, true), "反向情境滚轮没有被接管"); Pump();
            input.ProcessSide(2, false, 1, 250); Pump(); engine.Advance(600);
            Assert(!input.ProcessMain(0x20A, 0, 120, 650), "松开侧键后仍拦截页面滚动"); Pump();
            Assert(!input.ProcessMain(0x201, 0, 0, 700) && !input.ProcessMain(0x202, 0, 0, 710), "普通左键被拦截");
            var after = AudioVolume.ReadDefault();
            Assert(operations == 2, "操作次数不正确");
            Assert(after.DeviceId == baseline.DeviceId && after.Muted == baseline.Muted, "默认输出或静音状态被改变");
            Assert(Math.Abs(after.Scalar - baseline.Scalar) < .005f, "音量往返结果不符");
            passed = true;
        }
        finally
        {
            var result = AudioVolume.Restore(baseline); restored = Math.Abs(result.Scalar - baseline.Scalar) < .001f;
            Store.Write(output, new { Passed = passed && restored, Restored = restored, Baseline = baseline, Readings = readings, Operations = operations, TestSource = "程序事件回放 + 实际 Windows 音量读写，不是机械按压鼠标" });
        }
    }
}

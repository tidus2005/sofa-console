using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DisplaySceneSwitcher;

[StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; public override string ToString() => $"{High:X8}:{Low:X8}"; }
[StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; public readonly double Hz => Denominator == 0 ? 0 : (double)Numerator / Denominator; }
[StructLayout(LayoutKind.Sequential)] public struct PointL { public int X, Y; }
[StructLayout(LayoutKind.Sequential)] public struct Region { public uint Width, Height; }
[StructLayout(LayoutKind.Sequential)] public struct Signal { public ulong PixelRate; public Rational HSync, VSync; public Region Active, Total; public uint VideoStandard, ScanLine; }
[StructLayout(LayoutKind.Sequential)] public struct SourceMode { public uint Width, Height, PixelFormat; public PointL Position; }
[StructLayout(LayoutKind.Explicit, Size = 64)] public struct ModeInfo
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public Luid Adapter;
    [FieldOffset(16)] public SourceMode Source;
    [FieldOffset(16)] public Signal Target;
}
[StructLayout(LayoutKind.Sequential)] public struct SourceInfo { public Luid Adapter; public uint Id, ModeIndex, Status; }
[StructLayout(LayoutKind.Sequential)] public struct TargetInfo
{
    public Luid Adapter; public uint Id, ModeIndex, OutputTechnology, Rotation, Scaling; public Rational Refresh;
    public uint ScanLine; [MarshalAs(UnmanagedType.Bool)] public bool Available; public uint Status;
}
[StructLayout(LayoutKind.Sequential)] public struct DisplayPath { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
[StructLayout(LayoutKind.Sequential)] public struct InfoHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TargetName
{
    public InfoHeader Header; public uint Flags, OutputTechnology; public ushort Manufacturer, Product; public uint Connector;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
}
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SourceName
{
    public InfoHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
}
[StructLayout(LayoutKind.Sequential)] public struct PreferredMode { public InfoHeader Header; public uint Width, Height; public Signal Signal; }
[StructLayout(LayoutKind.Sequential)] public struct ColorInfo { public InfoHeader Header; public uint Value, Encoding, Bits; }
[StructLayout(LayoutKind.Sequential)] public struct ColorInfo2 { public InfoHeader Header; public uint Value, Encoding, Bits, ActiveMode; }
[StructLayout(LayoutKind.Sequential)] public struct ColorState { public InfoHeader Header; public uint Value; }
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DevMode
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    public ushort SpecVersion, DriverVersion, Size, DriverExtra; public uint Fields;
    public int PositionX, PositionY; public uint Orientation, FixedOutput;
    public short Color, Duplex, YResolution, TTOption, Collate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
    public ushort LogPixels; public uint Bits, Width, Height, DisplayFlags, Frequency, IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    public static DevMode Create() => new() { Size = (ushort)Marshal.SizeOf<DevMode>(), DeviceName = "", FormName = "" };
}

public static class Native
{
    public const uint Invalid = 0xFFFFFFFF, Apply = 0x80, Validate = 0x40, Supplied = 0x20, Save = 0x200, Allow = 0x400;
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, [Out] ModeInfo[] modeArray, IntPtr topology);
    [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint paths, [In] DisplayPath[]? pathArray, uint modes, [In] ModeInfo[]? modeArray, uint flags);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetTargetName(ref TargetName value);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetSourceName(ref SourceName value);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetPreferred(ref PreferredMode value);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetColor(ref ColorInfo value);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int GetColor2(ref ColorInfo2 value);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] public static extern int SetColor(ref ColorState value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int index, ref DevMode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsEx(string? device, ref DevMode mode, IntPtr hwnd, uint flags, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    public static InfoHeader Header<T>(uint type, Luid adapter, uint id) => new() { Type = type, Size = (uint)Marshal.SizeOf<T>(), Adapter = adapter, Id = id };
    public static void Check(int result, string operation) { if (result != 0) throw new Win32Exception(result, $"{operation}失败（{result}）：{new Win32Exception(result).Message}"); }
    public static (DisplayPath[] Paths, ModeInfo[] Modes) Query(bool all = false)
    {
        uint flags = all ? 1u : 2u;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Check(GetDisplayConfigBufferSizes(flags, out uint pc, out uint mc), "读取显示配置大小");
            var paths = new DisplayPath[pc]; var modes = new ModeInfo[mc];
            int result = QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
            if (result == 122) continue;
            Check(result, "读取显示配置");
            return (paths.Take((int)pc).ToArray(), modes.Take((int)mc).ToArray());
        }
        throw new Exception("显示设备正在反复变化，请稍后重试。");
    }
    public static string TargetKey(DisplayPath p) => p.Target.Adapter + ":" + p.Target.Id;
    public static string SourceKey(DisplayPath p) => p.Source.Adapter + ":" + p.Source.Id;
    public static List<MonitorDevice> Monitors()
    {
        var q = Query(true); var list = new List<MonitorDevice>();
        foreach (var group in q.Paths.Where(p => p.Target.Available).GroupBy(TargetKey))
        {
            var p = group.OrderByDescending(p => (p.Flags & 1) != 0).First();
            var tn = new TargetName { Header = Header<TargetName>(2, p.Target.Adapter, p.Target.Id) };
            if (GetTargetName(ref tn) != 0) continue;
            var sn = new SourceName { Header = Header<SourceName>(1, p.Source.Adapter, p.Source.Id) }; GetSourceName(ref sn);
            var pref = new PreferredMode { Header = Header<PreferredMode>(3, p.Target.Adapter, p.Target.Id) }; GetPreferred(ref pref);
            var active = (p.Flags & 1) != 0;
            var dm = DevMode.Create(); bool hasMode = active && EnumDisplaySettings(sn.Name, -1, ref dm);
            list.Add(new MonitorDevice {
                Key = string.IsNullOrEmpty(tn.DevicePath) ? TargetKey(p) : tn.DevicePath,
                Name = string.IsNullOrEmpty(tn.Name) ? "显示设备 " + p.Target.Id : tn.Name,
                GdiName = sn.Name ?? "", Active = active, Path = p,
                NativeWidth = pref.Width, NativeHeight = pref.Height,
                Width = hasMode ? dm.Width : 0, Height = hasMode ? dm.Height : 0,
                X = hasMode ? dm.PositionX : 0, Y = hasMode ? dm.PositionY : 0,
                Hz = active ? p.Target.Refresh.Hz : 0,
                Hdr = ReadHdr(p), Candidates = group.ToArray()
            });
        }
        return list;
    }
    public static HdrStatus ReadHdr(DisplayPath p)
    {
        var c2 = new ColorInfo2 { Header = Header<ColorInfo2>(15, p.Target.Adapter, p.Target.Id) };
        int result = GetColor2(ref c2);
        if (result == 0) return new HdrStatus { Known = true, Modern = true, Supported = (c2.Value & 16) != 0, Enabled = (c2.Value & 32) != 0, Limited = (c2.Value & 8) != 0, Bits = c2.Bits };
        if (result is not (50 or 87 or 120)) return new HdrStatus();
        var c = new ColorInfo { Header = Header<ColorInfo>(9, p.Target.Adapter, p.Target.Id) };
        if (GetColor(ref c) != 0) return new HdrStatus();
        return new HdrStatus { Known = true, Supported = (c.Value & 1) != 0, Enabled = (c.Value & 2) != 0, Limited = (c.Value & 8) != 0, Bits = c.Bits };
    }
    public static void SetHdr(MonitorDevice monitor, bool enabled, bool transactional = false)
    {
        if (!monitor.Active) throw new Exception("请先启用电视，再切换电视 HDR。");
        var state = ReadHdr(monitor.Path);
        if (!state.Known) throw new Exception("当前驱动无法读取 HDR 状态。");
        if (!state.Supported && enabled) throw new Exception("当前连接模式未报告 HDR 支持，请检查电视 HDMI 输入设置和线材。");
        if (state.Enabled == enabled) return;
        if (state.Limited && enabled) throw new Exception("Windows 当前限制了 HDR，请检查显示设置。");
        var value = new ColorState { Header = Header<ColorState>(state.Modern ? 16u : 10u, monitor.Path.Target.Adapter, monitor.Path.Target.Id), Value = enabled ? 1u : 0u };
        int result = transactional ? Recovery.Mutate(() => SetColor(ref value)) : SetColor(ref value);
        Check(result, "切换电视 HDR");
        for (int i = 0; i < 15; i++) { Thread.Sleep(200); if (ReadHdr(monitor.Path).Enabled == enabled) return; }
        throw new Exception("HDR 状态未达到预期，已请求恢复。");
    }
    public static List<DevMode> Modes(string device)
    {
        var list = new List<DevMode>();
        for (int i = 0; i < 10000; i++)
        {
            var dm = DevMode.Create(); if (!EnumDisplaySettings(device, i, ref dm)) break;
            if (dm.Bits >= 32 && (dm.DisplayFlags & 2) == 0 && dm.Frequency > 1) list.Add(dm);
        }
        return list.GroupBy(m => (m.Width, m.Height, m.Frequency)).Select(g => g.First()).OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.Frequency).ToList();
    }
    public static DevMode ChooseMode(MonitorDevice monitor, ModePreference preference)
    {
        var modes = Modes(monitor.GdiName);
        uint w = preference.Width == 0 ? monitor.NativeWidth : preference.Width;
        uint h = preference.Height == 0 ? monitor.NativeHeight : preference.Height;
        var candidates = modes.Where(m => m.Width == w && m.Height == h && (preference.Hz == 0 || m.Frequency == preference.Hz)).OrderByDescending(m => m.Frequency).ToList();
        if (candidates.Count == 0) throw new Exception($"{monitor.Name} 当前连接未提供 {w}×{h}" + (preference.Hz == 0 ? " 的可用模式。" : $" @ {preference.Hz} Hz。"));
        foreach (var candidate in candidates) { var dm = candidate; if (ChangeDisplaySettingsEx(monitor.GdiName, ref dm, IntPtr.Zero, 2, IntPtr.Zero) == 0) return candidate; }
        throw new Exception($"{monitor.Name} 的候选刷新率未通过驱动验证。");
    }
    public static void ApplyMode(MonitorDevice monitor, ModePreference preference, bool transactional = false)
    {
        var dm = ChooseMode(monitor, preference);
        // Position belongs to the current layout, not the mode enumeration record.
        dm.Fields = 0x40000 | 0x80000 | 0x100000 | 0x400000 | 0x200000;
        int result = transactional ? Recovery.Mutate(() => ChangeDisplaySettingsEx(monitor.GdiName, ref dm, IntPtr.Zero, 1, IntPtr.Zero)) : ChangeDisplaySettingsEx(monitor.GdiName, ref dm, IntPtr.Zero, 1, IntPtr.Zero);
        Check(result, "设置分辨率 / 刷新率");
        Thread.Sleep(450);
    }
    public static string Encode<T>(T[] values) where T : struct
    {
        int size = Marshal.SizeOf<T>(); var bytes = new byte[values.Length * size]; var ptr = Marshal.AllocHGlobal(size);
        try { for (int i = 0; i < values.Length; i++) { Marshal.StructureToPtr(values[i], ptr, false); Marshal.Copy(ptr, bytes, i * size, size); } }
        finally { Marshal.FreeHGlobal(ptr); }
        return Convert.ToBase64String(bytes);
    }
    public static T[] Decode<T>(string value) where T : struct
    {
        var bytes = Convert.FromBase64String(value); int size = Marshal.SizeOf<T>();
        if (bytes.Length % size != 0) throw new Exception("显示快照数据损坏。");
        var values = new T[bytes.Length / size]; var ptr = Marshal.AllocHGlobal(size);
        try { for (int i = 0; i < values.Length; i++) { Marshal.Copy(bytes, i * size, ptr, size); values[i] = Marshal.PtrToStructure<T>(ptr); } }
        finally { Marshal.FreeHGlobal(ptr); }
        return values;
    }
}

public sealed class HdrStatus
{
    public bool Known { get; set; } public bool Modern { get; set; } public bool Supported { get; set; } public bool Enabled { get; set; } public bool Limited { get; set; } public uint Bits { get; set; }
    public override string ToString() => !Known ? "HDR 未知" : !Supported ? "不支持 HDR" : Enabled ? "HDR 已开" : "HDR 已关";
}
public sealed class MonitorDevice
{
    public string Key { get; set; } = ""; public string Name { get; set; } = ""; public string GdiName { get; set; } = "";
    public bool Active { get; set; } public uint NativeWidth { get; set; } public uint NativeHeight { get; set; }
    public uint Width { get; set; } public uint Height { get; set; } public int X { get; set; } public int Y { get; set; } public double Hz { get; set; }
    public HdrStatus Hdr { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public DisplayPath Path { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public DisplayPath[] Candidates { get; set; } = [];
    public override string ToString() => Name + (Active ? $" · {Width}×{Height} / {Hz:0.##} Hz" : " · 已连接，未启用");
}

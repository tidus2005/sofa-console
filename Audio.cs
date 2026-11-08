using System.Runtime.InteropServices;

namespace DisplaySceneSwitcher;

// Core Audio COM ABI. Playback policy is the Windows PolicyConfig interface,
// which is not a public SDK contract; errors are verified and rolled back.
// ABI reference: https://github.com/frgnca/AudioDeviceCmdlets/tree/master/SOURCE
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out IAudioCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IAudioDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IAudioDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IAudioDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    [PreserveSig] int OpenPropertyStore(uint access, out IAudioProperties properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[StructLayout(LayoutKind.Sequential)] struct AudioPropertyKey { public Guid Format; public uint Id; }
[StructLayout(LayoutKind.Explicit, Size = 24)] struct AudioVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Value;
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioProperties
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out AudioPropertyKey key);
    [PreserveSig] int GetValue(ref AudioPropertyKey key, out AudioVariant value);
    [PreserveSig] int SetValue(ref AudioPropertyKey key, ref AudioVariant value);
    [PreserveSig] int Commit();
}
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPlaybackPolicy
{
    // Unused vtable slots retain the native order. Only SetDefaultEndpoint is invoked.
    [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
    [PreserveSig] int GetDeviceFormat(IntPtr id, int useDefault, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(IntPtr id);
    [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpointFormat, IntPtr mixFormat);
    [PreserveSig] int GetProcessingPeriod(IntPtr id, int useDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
    [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
    [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int GetPropertyValue(IntPtr id, int fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(IntPtr id, int fxStore, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    [PreserveSig] int SetEndpointVisibility(IntPtr id, int visible);
}
public sealed class PlaybackDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public uint State { get; set; }
    public bool Active => (State & 1) != 0;
    public override string ToString() => Name + (Active ? "" : "（当前未就绪）");
}
public sealed class AudioDefaults
{
    public string? Console { get; set; }
    public string? Multimedia { get; set; }
    public string? Communications { get; set; }
    public static AudioDefaults Capture() => new() { Console = AudioSystem.DefaultId(0), Multimedia = AudioSystem.DefaultId(1), Communications = AudioSystem.DefaultId(2) };
    public void Restore(bool transactional = false)
    {
        foreach (var (id, role) in new[] { (Console, 0), (Multimedia, 1), (Communications, 2) })
            if (!string.IsNullOrEmpty(id) && AudioSystem.DefaultId(role) != id) AudioSystem.SetRole(id, role, transactional);
        foreach (var (id, role) in new[] { (Console, 0), (Multimedia, 1), (Communications, 2) })
            if (!string.IsNullOrEmpty(id)) AudioSystem.WaitForDefault(id, role);
    }
    public bool Matches(AudioDefaults other) => Console == other.Console && Multimedia == other.Multimedia && Communications == other.Communications;
}
public static class AudioSystem
{
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref AudioVariant value);
    static IAudioEnumerator CreateEnumerator() => (IAudioEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
    static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    static void Check(int result, string action)
    {
        if (result < 0) throw new Exception($"{action}失败（0x{result:X8}）：{Marshal.GetExceptionForHR(result)?.Message}");
    }
    public static List<PlaybackDevice> Devices()
    {
        var enumerator = CreateEnumerator(); IAudioCollection? devices = null; var result = new List<PlaybackDevice>();
        try
        {
            // eRender only. Microphones are never enumerated as output candidates.
            Check(enumerator.EnumAudioEndpoints(0, 15, out devices), "读取播放设备");
            Check(devices.GetCount(out uint count), "读取播放设备数量");
            for (uint i = 0; i < count; i++)
            {
                Check(devices.Item(i, out var device), "读取播放设备"); IAudioProperties? properties = null;
                try
                {
                    Check(device.GetId(out string id), "读取播放设备标识"); Check(device.GetState(out uint state), "读取播放设备状态");
                    string name = id;
                    if (device.OpenPropertyStore(0, out properties) >= 0)
                    {
                        var key = new AudioPropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                        if (properties.GetValue(ref key, out var value) >= 0)
                        {
                            try { if (value.Type == 31) name = Marshal.PtrToStringUni(value.Value) ?? id; }
                            finally { PropVariantClear(ref value); }
                        }
                    }
                    result.Add(new PlaybackDevice { Id = id, Name = name, State = state });
                }
                finally { Release(properties); Release(device); }
            }
        }
        finally { Release(devices); Release(enumerator); }
        return result.OrderByDescending(d => d.Active).ThenBy(d => d.Name).ToList();
    }
    public static string? DefaultId(int role = 1)
    {
        var enumerator = CreateEnumerator(); IAudioDevice? device = null;
        try { if (enumerator.GetDefaultAudioEndpoint(0, role, out device) < 0) return null; Check(device.GetId(out string id), "读取默认声音输出"); return id; }
        finally { Release(device); Release(enumerator); }
    }
    public static string DefaultName()
    {
        string? id = DefaultId(); return Devices().FirstOrDefault(d => d.Id == id)?.Name ?? "没有可用的默认播放设备";
    }
    static PlaybackDevice WaitForDevice(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new Exception("请先在“设备与偏好”选择电脑音响和电视音频。");
        for (int attempt = 0; attempt < 33; attempt++)
        {
            var device = Devices().FirstOrDefault(d => d.Id == id && d.Active);
            if (device != null) return device;
            if (attempt < 32) Thread.Sleep(250);
        }
        throw new Exception("所选声音输出未就绪，请检查电视 HDMI 音频连接或音响连接，并在“设备与偏好”重新选择。");
    }
    public static void SetRole(string id, int role, bool transactional)
    {
        WaitForDevice(id);
        object policyObject = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))!)!;
        try
        {
            if (policyObject is not IPlaybackPolicy policy) throw new Exception("当前 Windows 不提供可用的声音输出切换接口。");
            int Change() => policy.SetDefaultEndpoint(id, role);
            Check(transactional ? Recovery.Mutate(Change) : Change(), "切换默认声音输出");
        }
        finally { Release(policyObject); }
    }
    public static void WaitForDefault(string id, int role)
    {
        for (int i = 0; i < 20; i++) { if (DefaultId(role) == id) return; Thread.Sleep(100); }
        throw new Exception("Windows 默认声音输出没有达到预期，已请求恢复。");
    }
    public static string SwitchTo(string id, bool communications, bool transactional = true)
    {
        var device = WaitForDevice(id);
        int[] roles = communications ? [0, 1, 2] : [0, 1];
        foreach (int role in roles) if (DefaultId(role) != id) SetRole(id, role, transactional);
        foreach (int role in roles) WaitForDefault(id, role);
        Store.Log("声音输出：" + device.Name);
        return device.Name;
    }
    public static bool Migrate(Settings settings)
    {
        bool changed = settings.AudioRoutingVersion < 1;
        if (!changed && settings.ComputerAudioId.Length > 0 && settings.TvAudioId.Length > 0) return false;
        var devices = Devices();
        // Use unambiguous physical-device names, never Steam/virtual/microphone devices.
        var pcs = devices.Where(d => d.Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase) && !d.Name.Contains("Digital", StringComparison.OrdinalIgnoreCase) && !d.Name.Contains("数字")).ToList();
        var tvs = devices.Where(d => d.Name.Contains("SONY", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pcs.Any(d => d.Active)) pcs = pcs.Where(d => d.Active).ToList();
        if (tvs.Any(d => d.Active)) tvs = tvs.Where(d => d.Active).ToList();
        if (string.IsNullOrEmpty(settings.ComputerAudioId) && pcs.Count == 1) { settings.ComputerAudioId = pcs[0].Id; changed = true; }
        if (string.IsNullOrEmpty(settings.TvAudioId) && tvs.Count == 1) { settings.TvAudioId = tvs[0].Id; changed = true; }
        settings.AudioRoutingVersion = 1; return changed;
    }
    public static string? TargetForScene(Settings settings, Scene scene)
    {
        if (!settings.AudioFollowsScene) return null;
        return scene switch
        {
            Scene.TvOnly or Scene.TvPrimary => settings.TvAudioId,
            Scene.Clone when settings.CloneAudio == "keep" => null,
            Scene.Clone when settings.CloneAudio == "tv" => settings.TvAudioId,
            _ => settings.ComputerAudioId
        };
    }
    public static string ApplyScene(Settings settings, Scene scene)
    {
        string? target = TargetForScene(settings, scene);
        return target == null ? "声音保持当前输出" : "声音：" + SwitchTo(target, settings.SwitchCommunicationAudio);
    }
}

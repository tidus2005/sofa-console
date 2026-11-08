using HidSharp;

namespace DisplaySceneSwitcher;

// The verified Bluetooth MX Anywhere 3 vendor collection reports all diverted
// buttons together, even when Options+ forwards only one XBUTTON to Windows.
// This reader never changes mappings, diversion, firmware, or mouse movement.
internal sealed class LogitechChordReader : IDisposable
{
    public static object Probe()
    {
        using var ready = new ManualResetEventSlim();
        string message = "未连接";
        using var reader = new LogitechChordReader((both, any) => { }, () => { }, value =>
        { message = value; ready.Set(); });
        ready.Wait(4000);
        if (message != "HID++ 侧键直读已连接") throw new IOException(message);
        return new { Connected = true, Device = "MX Anywhere 3 Bluetooth", MappingChanges = 0, Query = "ROOT.GetFeature(0x1B04)" };
    }
    private readonly CancellationTokenSource stop = new();
    private readonly Action<bool, bool> state;
    private readonly Action disconnected;
    private readonly Action<string> status;
    private readonly Task worker;
    public LogitechChordReader(Action<bool, bool> onState, Action onDisconnected, Action<string> onStatus)
    {
        state = onState; disconnected = onDisconnected; status = onStatus;
        worker = Task.Run(Run);
    }
    private void Run()
    {
        string previousStatus = "";
        void Report(string value) { if (value != previousStatus) { previousStatus = value; status(value); } }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var devices = DeviceList.Local.GetHidDevices(0x046D, 0xB025)
                    .Where(d => d.GetMaxInputReportLength() == 20 && d.GetMaxOutputReportLength() == 20).ToArray();
                if (devices.Length != 1) throw new IOException("未找到唯一的 MX Anywhere 3 HID++ 接口");
                using var stream = devices[0].Open();
                stream.ReadTimeout = 200; stream.WriteTimeout = 500;
                // Read-only ROOT.GetFeature(0x1B04), software identifier 15.
                var request = new byte[20]; request[0] = 0x11; request[1] = 0xFF;
                request[3] = 0x0F; request[4] = 0x1B; request[5] = 0x04;
                stream.Write(request);
                byte feature = 0;
                var buffer = new byte[20]; var queryStarted = System.Diagnostics.Stopwatch.StartNew();
                while (!stop.IsCancellationRequested && queryStarted.ElapsedMilliseconds < 2000)
                {
                    try
                    {
                        int size = stream.Read(buffer);
                        if (size == 20 && buffer[0] == 0x11 && buffer[1] == 0xFF && buffer[2] == 0 && buffer[3] == 0x0F)
                        { feature = buffer[4]; break; }
                    }
                    catch (TimeoutException) { }
                }
                if (feature == 0) throw new IOException("HID++ 侧键功能查询没有有效答复");
                Report("HID++ 侧键直读已连接");
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        int size = stream.Read(buffer);
                        bool front, back;
                        if (MouseSideLab.LogitechChordReport.TryRead(buffer, size, feature, out front, out back))
                            state(front && back, front || back);
                    }
                    catch (TimeoutException) { }
                }
            }
            catch (Exception ex) { Report("HID++ 侧键直读未连接：" + ex.Message); }
            finally { disconnected(); }
            if (!stop.IsCancellationRequested) stop.Token.WaitHandle.WaitOne(2000);
        }
    }
    public void Dispose()
    {
        stop.Cancel();
        // Reads are bounded; never wait indefinitely on an unplugged device.
        if (worker.Wait(3000)) stop.Dispose();
    }
}

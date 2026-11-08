using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using MouseSideLab;

namespace DisplaySceneSwitcher;

internal static class ChordInputDiagnostics
{
    // Passive, bounded capture: no key injection, blocking, or mapped actions.
    public static void Capture(string output)
    {
        var records = new ConcurrentQueue<object>();
        using var mouse = new NativeMouse();
        mouse.RawButtonsObserved += (device, flags, time) => records.Enqueue(new {
            Type = "raw", Device = device.ToInt64(), Flags = flags.ToString("X4"), TimeMs = time });
        mouse.Configure(true, false); mouse.Start();
        using var file = new StreamWriter(output, false) { AutoFlush = true };
        file.WriteLine(JsonSerializer.Serialize(new { Type = "started", mouse.RawInputAvailable, Timestamp = DateTimeOffset.Now }));
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 60000)
        {
            foreach (var packet in mouse.Drain(out _))
                records.Enqueue(new { Type = "hook", packet.Button, packet.Down, packet.Kind, packet.Injected, packet.ObservedOnly, packet.TimeMs, packet.ResetReason });
            while (records.TryDequeue(out var record)) file.WriteLine(JsonSerializer.Serialize(record));
            Thread.Sleep(15);
        }
        file.WriteLine("{\"Type\":\"completed\"}");
    }
}

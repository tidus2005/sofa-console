namespace DisplaySceneSwitcher;

public static class SofaDiagnostics
{
    public static void Test(string output)
    {
        void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        var old = new MouseSideLab.JsonCodec().Deserialize<MouseSideLab.Settings>("{\"Shortcuts\":{\"back.triple\":\"Ctrl+Shift+T\",\"back.wheelup\":\"Ctrl+Shift+Tab\"}}");
        old.Validate();
        Assert(old.SofaGesturesEnabled && old.SofaBindingsVersion == 7 && old.Shortcuts["back.triple"] == "Enter" && old.Shortcuts["back.wheelup"] == "WindowPrevious" && old.Shortcuts["both.hold"] == "ShowHelp", "旧配置迁移失败");
        old.Validate();
        Assert(old.Shortcuts.Count == 20 && old.Descriptions.Count == 20, "手势配置数量不符");
        var toggles = new[] {
            new { TvActive = true, ComputerActive = false, Target = Engine.ExclusiveToggleTarget(true, false).ToString() },
            new { TvActive = false, ComputerActive = true, Target = Engine.ExclusiveToggleTarget(false, true).ToString() },
            new { TvActive = true, ComputerActive = true, Target = Engine.ExclusiveToggleTarget(true, true).ToString() }
        };
        Assert(toggles[0].Target == "ComputerOnly" && toggles[1].Target == "TvOnly" && toggles[2].Target == "TvOnly", "独显场景切换方向不正确");
        var baseline = AudioVolume.ReadDefault();
        var readings = new List<VolumeReading>();
        bool passed = false;
        try
        {
            var engine = new MouseSideLab.GestureEngine(300, 650, 150);
            using var input = new MouseSideLab.NativeMouse();
            input.Configure(true, true, true, false, 2, 2, 1);
            engine.Recognized += gesture => {
                Assert(gesture.Button == 2 && gesture.Kind == "middle", "静音回放出现额外手势");
                readings.Add(AudioVolume.ToggleMuteDefault());
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
            input.ProcessSide(2, true, 1, 0); Pump();
            foreach (long time in new long[] { 100, 200 })
            {
                Assert(input.ProcessMain(0x207, 0, 0, time, true), "前键加中键没有拦截按下"); Pump();
                Assert(input.ProcessMain(0x208, 0, 0, time + 10, true), "前键加中键没有配对松开"); Pump();
            }
            input.ProcessSide(2, false, 1, 250); Pump(); engine.Advance(1000);
            Assert(readings.Count == 2 && readings[0].Muted != baseline.Muted && readings[1].Muted == baseline.Muted, "静音切换没有往返");
            Assert(readings.All(r => r.DeviceId == baseline.DeviceId && Math.Abs(r.Scalar - baseline.Scalar) < .001f), "静音改变了音量或输出设备");
            passed = true;
        }
        finally
        {
            var restored = AudioVolume.Restore(baseline);
            bool restoredOk = restored.Muted == baseline.Muted && Math.Abs(restored.Scalar - baseline.Scalar) < .001f;
            Store.Write(output, new { Passed = passed && restoredOk, Restored = restoredOk, Baseline = baseline, MuteReadings = readings, DisplayTargets = toggles, ConfigurationMigrationPassed = true, DisplayChanged = false, WindowsChanged = false, TestSource = "输入事件回放 + 实际系统静音往返；窗口/桌面按键通过隔离测试，显示目标只读验证" });
            Assert(restoredOk, "音频状态恢复失败");
        }
    }
}

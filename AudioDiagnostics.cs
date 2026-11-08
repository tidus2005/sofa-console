namespace DisplaySceneSwitcher;

public static class AudioDiagnostics
{
    public static void Write(string output)
    {
        var settings = Settings.Load();
        Store.Write(output, new { Version = "1.1.0", CheckedAt = DateTime.Now, Devices = AudioSystem.Devices(), Defaults = AudioDefaults.Capture(), settings.ComputerAudioId, settings.TvAudioId, settings.AudioFollowsScene, settings.CloneAudio, settings.SwitchCommunicationAudio });
    }
    static void Assert(bool ok, string message) { if (!ok) throw new Exception("音频自检失败：" + message); }
    public static void Test(string output)
    {
        if (File.Exists(Store.PendingPath)) throw new Exception("当前有未确认的切换，暂不能测试。");
        var settings = Settings.Load(); var original = Snapshot.Capture(); var results = new List<object>();
        Store.Write(Path.Combine(Store.Root, "audio-test-baseline.json"), original);
        try
        {
            foreach (Scene scene in Enum.GetValues<Scene>())
            {
                Recovery.Arm();
                try
                {
                    string report = AudioSystem.ApplyScene(settings, scene);
                    string? expected = AudioSystem.TargetForScene(settings, scene);
                    if (expected != null)
                    {
                        var actual = AudioDefaults.Capture();
                        Assert(actual.Console == expected && actual.Multimedia == expected && (!settings.SwitchCommunicationAudio || actual.Communications == expected), "场景输出 " + scene);
                    }
                    results.Add(new { Test = scene.ToString(), Passed = true, Output = report });
                }
                finally { Recovery.Revert(); }
                Assert(original.Audio!.Matches(AudioDefaults.Capture()), "恢复场景前的三个播放角色");
                Store.Write(output, results);
            }
            Assert(AudioSystem.TargetForScene(new Settings { ComputerAudioId = "pc", TvAudioId = "tv", CloneAudio = "tv" }, Scene.Clone) == "tv", "同步场景选择电视");
            Assert(AudioSystem.TargetForScene(new Settings { CloneAudio = "keep" }, Scene.Clone) == null, "同步场景保持声音");
            Assert(AudioSystem.TargetForScene(new Settings { AudioFollowsScene = false }, Scene.TvOnly) == null, "关闭音频联动");
            results.Add(new { Test = "CloneAndDisabledPolicies", Passed = true, Output = "同步可选电视、电脑或保持；支持关闭联动" });
            Recovery.Arm();
            string alternative = original.Audio!.Multimedia == settings.TvAudioId ? settings.ComputerAudioId : settings.TvAudioId;
            AudioSystem.SwitchTo(alternative, settings.SwitchCommunicationAudio);
            Recovery.Locked(() => { var pending = Store.Read<PendingChange>(Store.PendingPath); pending.ExpiresUtc = DateTime.UtcNow.AddMilliseconds(500); Store.Write(Store.PendingPath, pending); return true; });
            for (int i = 0; i < 200 && File.Exists(Store.PendingPath); i++) Thread.Sleep(100);
            Assert(!File.Exists(Store.PendingPath), "独立进程自动恢复");
            Assert(original.Audio.Matches(AudioDefaults.Capture()), "自动恢复原声音输出");
            results.Add(new { Test = "IndependentAudioRecovery", Passed = true, Output = "独立恢复进程已恢复三个播放角色" });
            Store.Write(output, results);
        }
        finally
        {
            if (File.Exists(Store.PendingPath)) Recovery.Revert();
            original.Audio?.Restore();
            Store.Write(output, results);
        }
    }
}

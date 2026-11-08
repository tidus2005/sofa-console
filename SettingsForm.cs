namespace DisplaySceneSwitcher;

public sealed class SettingsForm : Form
{
    public Settings Value { get; private set; }
    readonly ComboBox pc = new(), tv = new(), tvMode = new(), pcMode = new(), hdr = new();
    readonly ComboBox pcAudio = new(), tvAudio = new(), cloneAudio = new();
    readonly CheckBox audioFollow = new() { Text = "切换显示场景时，自动切换声音输出", AutoSize = true };
    readonly CheckBox communication = new() { Text = "同时切换通话声音（不改麦克风）", AutoSize = true };
    readonly CheckBox tvRight = new() { Text = "扩展显示时，电视放在电脑屏幕右侧", AutoSize = true };
    readonly List<MonitorDevice> monitors;
    readonly Settings original;
    public SettingsForm(Settings settings)
    {
        original = settings; Value = settings; monitors = Native.Monitors();
        Text = "设备、画面与声音偏好"; Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(900, 750); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 13, AutoScroll = true };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 175)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 11; i++) layout.RowStyles.Add(new(SizeType.Absolute, 46)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 50));
        foreach (var combo in new[] { pc, tv, tvMode, pcMode, hdr, pcAudio, tvAudio, cloneAudio }) { combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Dock = DockStyle.Fill; }
        pc.Items.AddRange(monitors.Cast<object>().ToArray()); tv.Items.AddRange(monitors.Cast<object>().ToArray());
        pc.SelectedIndex = monitors.FindIndex(m => m.Key == settings.ComputerKey); tv.SelectedIndex = monitors.FindIndex(m => m.Key == settings.TvKey);
        hdr.Items.AddRange(new object[] { "保持电视当前 HDR 状态", "电视参与显示时自动开启 HDR", "电视参与显示时自动关闭 HDR" }); hdr.SelectedIndex = settings.TvHdr == "on" ? 1 : settings.TvHdr == "off" ? 2 : 0;
        tvRight.Checked = settings.TvOnRight;
        AddRow(layout, 0, "电脑屏幕", pc); AddRow(layout, 1, "电视", tv); AddRow(layout, 2, "电脑显示模式", pcMode); AddRow(layout, 3, "电视显示模式", tvMode); AddRow(layout, 4, "HDR 偏好", hdr); layout.Controls.Add(tvRight, 1, 5);
        audioFollow.Checked = settings.AudioFollowsScene; communication.Checked = settings.SwitchCommunicationAudio;
        var playback = AudioSystem.Devices(); FillAudio(pcAudio, playback, settings.ComputerAudioId); FillAudio(tvAudio, playback, settings.TvAudioId);
        cloneAudio.Items.AddRange(new object[] { "电脑音响", "电视音频", "保持当前声音输出" }); cloneAudio.SelectedIndex = settings.CloneAudio == "tv" ? 1 : settings.CloneAudio == "keep" ? 2 : 0;
        layout.Controls.Add(audioFollow, 1, 6); AddRow(layout, 7, "电脑音响", pcAudio); AddRow(layout, 8, "电视音频", tvAudio); AddRow(layout, 9, "同步时声音", cloneAudio); layout.Controls.Add(communication, 1, 10);
        var hint = new Label { Dock = DockStyle.Fill, Text = "扩展模式：声音跟随主屏；同步模式：按上面的选择输出。\r\n切换失败、确认超时或恢复设置时，声音也一起恢复。\r\n应用若固定了播放设备，请在应用里选择“系统默认”。\r\n电视自动选原生分辨率和驱动支持的最高刷新率；同步模式使用共同显示模式。", Font = new Font(Font.FontFamily, 9), ForeColor = Color.DimGray }; layout.Controls.Add(hint, 0, 11); layout.SetColumnSpan(hint, 2);
        var save = new Button { Text = "保存偏好", AutoSize = true, Height = 36, Anchor = AnchorStyles.Right }; save.Click += (_, _) => SaveValue(); layout.Controls.Add(save, 1, 12); Controls.Add(layout);
        FillModes(pcMode, pc.SelectedItem as MonitorDevice, settings.ComputerMode); FillModes(tvMode, tv.SelectedItem as MonitorDevice, settings.TvMode);
        pc.SelectedIndexChanged += (_, _) => FillModes(pcMode, pc.SelectedItem as MonitorDevice, original.ComputerMode); tv.SelectedIndexChanged += (_, _) => FillModes(tvMode, tv.SelectedItem as MonitorDevice, original.TvMode);
    }
    static void AddRow(TableLayoutPanel layout, int row, string name, Control control) { layout.Controls.Add(new Label { Text = name, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row); layout.Controls.Add(control, 1, row); }
    static void FillAudio(ComboBox combo, List<PlaybackDevice> devices, string selected)
    {
        combo.Items.Add(new PlaybackDevice { Name = "请选择播放设备", State = 1 }); combo.Items.AddRange(devices.Where(d => d.State != 4 || d.Id == selected).Cast<object>().ToArray());
        int index = combo.Items.Cast<PlaybackDevice>().ToList().FindIndex(d => d.Id == selected);
        if (index < 0 && selected.Length > 0) { combo.Items.Add(new PlaybackDevice { Id = selected, Name = "已保存设备（未连接，可重新选择）" }); index = combo.Items.Count - 1; }
        combo.SelectedIndex = Math.Max(0, index);
    }
    static void FillModes(ComboBox combo, MonitorDevice? monitor, ModePreference selected)
    {
        combo.Items.Clear(); combo.Items.Add(new ModeOption(new(), "原生分辨率 + 最高刷新率（自动）"));
        if (monitor?.Active == true)
            foreach (var dm in Native.Modes(monitor.GdiName)) combo.Items.Add(new ModeOption(new ModePreference { Width = dm.Width, Height = dm.Height, Hz = dm.Frequency }, $"{dm.Width} × {dm.Height}  /  {dm.Frequency} Hz"));
        int index = combo.Items.Cast<ModeOption>().ToList().FindIndex(o => o.Preference.Width == selected.Width && o.Preference.Height == selected.Height && o.Preference.Hz == selected.Hz);
        if (index < 0 && selected.Width > 0) { combo.Items.Add(new ModeOption(selected, $"已保存：{selected.Width} × {selected.Height} / {selected.Hz} Hz（启用后验证）")); index = combo.Items.Count - 1; }
        combo.SelectedIndex = Math.Max(0, index);
    }
    void SaveValue()
    {
        if (pc.SelectedItem is not MonitorDevice computer || tv.SelectedItem is not MonitorDevice television || computer.Key == television.Key) { MessageBox.Show(this, "请选择两个不同的显示设备。", Text); return; }
        var pcSound = (PlaybackDevice)pcAudio.SelectedItem!; var tvSound = (PlaybackDevice)tvAudio.SelectedItem!;
        if (audioFollow.Checked && (pcSound.Id.Length == 0 || tvSound.Id.Length == 0)) { MessageBox.Show(this, "请分别选择电脑音响和电视音频，或取消声音自动切换。", Text); return; }
        Value = new Settings { ComputerKey = computer.Key, TvKey = television.Key, ComputerMode = ((ModeOption)pcMode.SelectedItem!).Preference, TvMode = ((ModeOption)tvMode.SelectedItem!).Preference, TvHdr = hdr.SelectedIndex == 1 ? "on" : hdr.SelectedIndex == 2 ? "off" : "keep", TvOnRight = tvRight.Checked, AudioRoutingVersion = 1, AudioFollowsScene = audioFollow.Checked, ComputerAudioId = pcSound.Id, TvAudioId = tvSound.Id, CloneAudio = cloneAudio.SelectedIndex == 1 ? "tv" : cloneAudio.SelectedIndex == 2 ? "keep" : "computer", SwitchCommunicationAudio = communication.Checked };
        DialogResult = DialogResult.OK; Close();
    }
    sealed record ModeOption(ModePreference Preference, string Label) { public override string ToString() => Label; }
}

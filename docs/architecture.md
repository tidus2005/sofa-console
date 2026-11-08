# 架构

## 显示与声音

`Program.cs` 提供入口、主窗、托盘、确认窗和全局快捷键。`SettingsForm.cs` 负责屏幕、显示模式和音频偏好。

`Native.cs` 封装 Windows 显示配置 API；`Engine.cs` 负责场景规划、主屏布局、HDR、快照和恢复。恢复状态写入本地 `pending.json`，配合 watchdog 处理确认超时和程序意外退出。`Audio.cs` 负责音频设备与默认输出跟随，`AudioVolume.cs` 调整当前默认输出音量。

## 鼠标输入

```text
Windows 鼠标钩子 / Raw Input ──> NativeMouse ──> GestureEngine
                                                    │
MX Anywhere 3 HID++ ──> LogitechChordReader ───────────┤
                                                    ▼
                                            MainForm.OnGesture
                                                    │
                    ┌───────────────┬───────────────┼───────────────┐
                    ▼               ▼               ▼               ▼
                 Hotkeys       WindowCycle      AudioVolume    Codex / Pet
```

- `NativeMouse.cs`：专用线程监听，区分实体/驱动转发事件，维护按下状态、组合接管和丢失松开事件的保护。
- `GestureEngine.cs`：时间戳驱动的单击/双击/三击/按住/组合识别。
- `DoubleHoldBackspace.cs`：第二次按住启动 500 毫秒退格；松开或焦点改变后停止。
- `WindowCycle.cs`：后键按住期间维持 Alt，滚轮选择窗口，松开释放 Alt。
- `Hotkeys.cs`：逐个提交按下/松开事件，串行发送，失败只释放已成功按下且仍未释放的键，不重放删除。
- `MainForm.cs`：映射、状态界面、动作队列和日志。识别记录与发送结果分别记录。
- `DeveloperActions.cs`：为额外自定义动作保留的集成入口。

HID++ 仅补足两个侧键同时按住的状态。已确认双键时抑制重复的软件侧键转发，任一键提前松开取消计时，完整松开后允许下一次触发。断开、暂停及状态超时均有清理路径。

## Codex 窗口

`CodexWindows.cs` 和 `AppToggle.cs` 保存前一个窗口身份并切换 Codex 主窗口。

`PetWindows.cs` 先用进程包、窗口类和样式过滤候选，`WindowDocumentRoute.cs` 只对这些候选读取 UI Automation Document URL。`PetToggle.cs` 要求精确的本地 `avatar-overlay` 路由，排除 `global-dictation` 听写窗口。操作前重新校验句柄和进程身份，显示/隐藏后检查可见状态。

该路径不读取聊天正文，不调用内部 IPC，不通过快捷键切换宠物。保留已有窗口及其内部输入框状态。

## 主要边界

显示设备、音频设备、Windows 前台激活和第三方输入转发都需要实机验证。纯逻辑测试不会证明某台设备上的显示切换或目标应用行为。公开仓库保留诊断工具，但不提交个人机器的诊断输出。

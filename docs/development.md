# 开发与测试

## 环境

Windows x64、.NET 8 SDK。`RunMouseTests.ps1` 使用 Windows 自带的 .NET Framework 4.x C# 编译器，生成 `tests/mouse/GestureTests.exe`；测试程序和构建输出均被 Git 忽略。

```powershell
.\RunMouseTests.ps1
dotnet build .\DisplaySceneSwitcher.csproj -c Release
.\Build.ps1
```

测试数据 `tests/mouse/missing-release-trace.json` 仅包含用于重现松开丢失问题的按键编号、相对时间和事件类型。

## 手动集成测试

以下命令使用已构建的程序。`artifacts/` 不纳入版本控制。

```powershell
.\publish\DisplaySceneSwitcher.exe --mouse-gesture-test --output "$PWD\artifacts\keyboard-test.json"
.\publish\DisplaySceneSwitcher.exe --help-pipeline-test --hardware --output "$PWD\artifacts\help-test.json"
.\publish\DisplaySceneSwitcher.exe --hid-chord-probe --output "$PWD\artifacts\hid-probe.json"
```

WinExe 可能异步返回。脚本若要等待完成，应使用 `Start-Process -PassThru` 和 `WaitForExit`，并检查退出码及输出文件。

| 命令 | 行为 |
| --- | --- |
| `--mouse-gesture-test` | 在临时文本框实际测试删除、撤销、Esc、Enter、连续退格，显示帮助，并在临时窗口之间实际 Alt+Tab；保持桌面空闲 |
| `--help-pipeline-test --hardware` | 回放 HID++ 双键状态到帮助窗，不模拟实体鼠标；会显示帮助 |
| `--hid-chord-probe` | 对支持的鼠标执行只读功能查询，不改变映射 |
| `--chord-input-capture` | 约 60 秒被动记录鼠标输入，用于驱动转发分析 |
| `--sound-toast-test` | 显示声音提醒预览，不调整系统音量 |
| `--pet-diagnose` / `--codex-diagnose` | 枚举相关窗口元数据用于诊断 |
| `--pet-direct-test` | 实际隐藏/显示宠物并恢复状态 |
| `--sofa-test` / `--volume-test` / `--audio-test` / `--integration-test` | 涉及实际系统动作；运行前阅读对应 Diagnostics 实现，不放入无交互 CI |

诊断失败可能来自焦点被用户或其他自动化程序切走。不要为了让报告变绿而移除焦点保护，也不要把 `ExecuteShortcut=true` 或发送成功当作目标软件执行成功。

## 修改映射

当前沙发操作集中在 `MouseSideLab/WindowCycle.cs` 的 `SofaBindings`。修改时同步更新 `MainForm.cs` 中的描述、`GestureHelpForm.cs` 的屏幕帮助和 README 按键表。

增加手势时先验证单/双/三击互斥、组合后不补发单击、松开停止、断开/超时清理，以及普通左右键不会被无条件接管。

## 发布

仓库维护独立历史，不引入开发机器的上层工作区历史。构建产物、日志和本机数据不提交。GitHub Actions 仅做纯逻辑测试和 Windows 构建，交互式测试由开发者在空闲桌面执行。

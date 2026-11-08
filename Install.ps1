param([switch]$NoStartup)
$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\DisplaySceneSwitcher'
$sourceDir = Join-Path $PSScriptRoot 'publish'
if (-not (Test-Path -LiteralPath (Join-Path $sourceDir 'DisplaySceneSwitcher.exe'))) { throw '请先运行 Build.ps1。' }
$existing = Get-Process -Name DisplaySceneSwitcher -ErrorAction SilentlyContinue
if ($existing) { throw '请先在托盘菜单退出显示场景切换器，再安装更新。' }
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Get-ChildItem -LiteralPath $sourceDir -File | Copy-Item -Destination $installDir -Force
if (Test-Path -LiteralPath (Join-Path $sourceDir 'docs')) {
    Copy-Item -LiteralPath (Join-Path $sourceDir 'docs') -Destination $installDir -Recurse -Force
}
$exePath = Join-Path $installDir 'DisplaySceneSwitcher.exe'
$desktopDir = [Environment]::GetFolderPath('Desktop')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $desktopDir '电脑电视切换器.lnk'))
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = '沙发控制台：电脑电视切换、HDR、声音输出、鼠标侧键与滚轮音量'
$shortcut.IconLocation = (Join-Path $installDir 'switcher.ico')
$shortcut.Save()
if (-not $NoStartup) {
    try {
        $startupDir = [Environment]::GetFolderPath('Startup')
        if (-not (Test-Path -LiteralPath $startupDir)) { New-Item -ItemType Directory -Path $startupDir -Force | Out-Null }
        $startupShortcut = $shell.CreateShortcut((Join-Path $startupDir '电脑电视切换器.lnk'))
        $startupShortcut.TargetPath = $exePath
        $startupShortcut.Arguments = '--tray'
        $startupShortcut.WorkingDirectory = $installDir
        $startupShortcut.IconLocation = (Join-Path $installDir 'switcher.ico')
        $startupShortcut.Save()
    } catch {
        Write-Warning '系统不允许设置开机启动。程序仍可从桌面快捷方式启动并驻留托盘。'
    }
}
Start-Process -FilePath $exePath -ArgumentList '--tray' -WorkingDirectory $installDir -WindowStyle Hidden
Write-Output "已安装：$exePath"
Write-Output "桌面快捷方式：$desktopDir\电脑电视切换器.lnk"

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(35,79,143))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),3
$graphics.DrawRectangle($pen,8,12,30,22)
$graphics.DrawLine($pen,23,35,23,42)
$graphics.DrawLine($pen,16,43,30,43)
$graphics.DrawRectangle($pen,31,30,25,19)
$graphics.DrawLine($pen,36,53,52,53)
$handle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($handle)
$stream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'switcher.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $graphics.Dispose(); $pen.Dispose(); $bitmap.Dispose() }
dotnet publish .\DisplaySceneSwitcher.csproj -c Release -r win-x64 --self-contained false -o .\publish -p:PublishSingleFile=true -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -LiteralPath .\switcher.ico,.\使用说明.txt,.\README.md,.\CHANGELOG.md,.\THIRD_PARTY_NOTICES.md,.\HidSharp-LICENSE.txt -Destination .\publish -Force
Copy-Item -LiteralPath .\docs -Destination .\publish -Recurse -Force
Write-Output "编译完成：$PSScriptRoot\publish\DisplaySceneSwitcher.exe"

$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'tests\mouse'
$testBinary = Join-Path $testDirectory 'GestureTests.exe'
$sourceFiles = @('GestureEngine.cs','LogitechChordReport.cs','Hotkeys.cs','InputDiagnostics.cs','NativeMouse.cs','RawMouseWindow.cs','DoubleHoldBackspace.cs','WindowCycle.cs','AppToggle.cs','PetToggle.cs') | ForEach-Object { Join-Path $PSScriptRoot ('MouseSideLab\' + $_) }
& $compilerPath /nologo /target:exe /utf8output "/out:$testBinary" /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll $sourceFiles (Join-Path $testDirectory 'GestureTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mouse test compilation failed.' }
& $testBinary
if ($LASTEXITCODE -ne 0) { throw 'Mouse regression tests failed.' }

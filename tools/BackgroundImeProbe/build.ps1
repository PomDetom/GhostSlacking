param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../../artifacts/background-interaction-round4/ime-research'))
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ Build Tools are required.' }
$developerCommand = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$sourcePath = Join-Path $PSScriptRoot 'BackgroundImeProbe.cpp'
$executablePath = Join-Path $outputPath 'BackgroundImeProbe.exe'
$objectPath = Join-Path $outputPath 'BackgroundImeProbe.obj'
$pdbPath = Join-Path $outputPath 'BackgroundImeProbe.pdb'
$buildCommand = 'call "{0}" -no_logo -arch=x64 && cl /nologo /std:c++20 /EHsc /utf-8 /W4 /WX /MT /DWIN32_LEAN_AND_MEAN "{1}" /Fe:"{2}" /Fo:"{3}" /Fd:"{4}" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib ole32.lib oleaut32.lib uuid.lib' -f $developerCommand, $sourcePath, $executablePath, $objectPath, $pdbPath
& $env:ComSpec /d /s /c $buildCommand
if ($LASTEXITCODE -ne 0) { throw "IME research compilation failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputPath 'README.md')

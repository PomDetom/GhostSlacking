[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$source = Join-Path $root 'src/GhostSlacking.Platform/BackgroundInteraction/InputBridge.cpp'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ Build Tools are required to build the input bridge.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Install the Visual Studio Desktop development with C++ workload.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
$optimization = if ($Configuration -eq 'Release') { '/O2' } else { '/Od' }
$commandPath = Join-Path $output 'compile.cmd'
$command = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /WX /MT $optimization /LD /DUNICODE /D_UNICODE "$source" /Fo"$output\InputBridge.obj" /Fe"$output\GhostSlacking.InputBridge.dll" /link /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA /IMPLIB:"$output\InputBridge.lib" user32.lib gdi32.lib bcrypt.lib
"@
[IO.File]::WriteAllText($commandPath, $command, [Text.Encoding]::Default)
& $env:ComSpec /d /c $commandPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Input bridge compilation failed ($LASTEXITCODE)." }
Get-Item -LiteralPath (Join-Path $output 'GhostSlacking.InputBridge.dll')

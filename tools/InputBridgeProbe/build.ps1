param([switch]$NativeOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $root 'artifacts/tools/InputBridgeProbe/packaged-input'
[IO.Directory]::CreateDirectory($output) | Out-Null
& (Join-Path $root 'scripts/build-input-bridge.ps1') -OutputDirectory $output | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Input bridge compilation failed.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ Build Tools are required.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
$commandPath = Join-Path $output 'compile.cmd'
$command = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /WX /MT /DUNICODE /D_UNICODE "$PSScriptRoot\BridgeFixture.cpp" /Fo"$output\BridgeFixture.obj" /Fe"$output\BridgeFixture.exe" /link /SUBSYSTEM:CONSOLE user32.lib gdi32.lib
"@
[IO.File]::WriteAllText($commandPath, $command, [Text.Encoding]::Default)
& $env:ComSpec /d /c $commandPath
if ($LASTEXITCODE -ne 0) { throw "Probe compilation failed ($LASTEXITCODE)." }
if (-not $NativeOnly) {
    & dotnet build (Join-Path $root 'src/GhostSlacking.App/GhostSlacking.App.csproj') -c Release '-p:BaseOutputPath=bin/BridgeProbe/'
    if ($LASTEXITCODE -ne 0) { throw 'Probe app build failed.' }
    $appSource = Join-Path $root 'src/GhostSlacking.App/bin/BridgeProbe/Release/net8.0-windows10.0.19041.0/win-x64'
    $appOutput = Join-Path $output 'app'
    [IO.Directory]::CreateDirectory($appOutput) | Out-Null
    Copy-Item -Path (Join-Path $appSource '*') -Destination $appOutput -Recurse -Force
    Copy-Item -Path (Join-Path $root 'src/GhostSlacking.Watchdog/bin/BridgeProbe/Release/net8.0-windows/GhostSlacking.Watchdog.*') -Destination $appOutput -Force
}
Write-Output "Probe output: $output"

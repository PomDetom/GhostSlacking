param([switch]$NativeOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$output = Join-Path $root 'artifacts/tools/ExclusiveInputPrototype/menu-hover'
[IO.Directory]::CreateDirectory($output) | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ Build Tools are required.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
$commandPath = Join-Path $output 'compile.cmd'
$command = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /WX /MT /LD /DUNICODE /D_UNICODE "$PSScriptRoot\InputBridge.cpp" /Fo"$output\InputBridge.obj" /Fe"$output\GhostSlacking.InputPrototype.MenuHover.dll" /link /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA /IMPLIB:"$output\InputBridge.lib" user32.lib gdi32.lib bcrypt.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c++20 /EHsc /W4 /WX /MT /DUNICODE /D_UNICODE "$PSScriptRoot\BridgeFixture.cpp" /Fo"$output\BridgeFixture.obj" /Fe"$output\BridgeFixture.exe" /link /SUBSYSTEM:CONSOLE user32.lib gdi32.lib
"@
[IO.File]::WriteAllText($commandPath, $command, [Text.Encoding]::Default)
& $env:ComSpec /d /c $commandPath
if ($LASTEXITCODE -ne 0) { throw "Prototype compilation failed ($LASTEXITCODE)." }
if (-not $NativeOnly) {
    & dotnet build (Join-Path $root 'src/GhostSlacking.App/GhostSlacking.App.csproj') -c Release '-p:BaseOutputPath=bin/ExclusivePrototype/'
    if ($LASTEXITCODE -ne 0) { throw 'Prototype app build failed.' }
    $appSource = Join-Path $root 'src/GhostSlacking.App/bin/ExclusivePrototype/Release/net8.0-windows10.0.19041.0/win-x64'
    $appOutput = Join-Path $output 'app'
    [IO.Directory]::CreateDirectory($appOutput) | Out-Null
    Copy-Item -Path (Join-Path $appSource '*') -Destination $appOutput -Recurse -Force
    Copy-Item -Path (Join-Path $root 'src/GhostSlacking.Watchdog/bin/ExclusivePrototype/Release/net8.0-windows/GhostSlacking.Watchdog.*') -Destination $appOutput -Force
    Copy-Item -LiteralPath (Join-Path $output 'GhostSlacking.InputPrototype.MenuHover.dll') -Destination $appOutput -Force
}
Write-Output "Prototype output: $output"

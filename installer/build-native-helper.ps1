[CmdletBinding()]
param([switch]$Testing)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root $(if ($Testing) { 'artifacts/native-helper-tests' } else { 'artifacts/native-helper' })
New-Item -ItemType Directory -Path $output -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio C++ Build Tools are required for the native installation helper.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Install the Visual Studio Desktop development with C++ workload.' }
$vcvars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
$source = Join-Path $PSScriptRoot 'InstallerHelper.cpp'
$executable = Join-Path $output 'GhostSlacking.InstallHelper.exe'
$define = if ($Testing) { '/DINSTALLER_TESTING' } else { '' }
$commandPath = Join-Path $output 'compile.cmd'
$command = @"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /W4 /WX /MT /DUNICODE /D_UNICODE $define "$source" /Fo"$output\InstallerHelper.obj" /Fe"$executable" /link /DYNAMICBASE /NXCOMPAT /HIGHENTROPYVA msi.lib bcrypt.lib shell32.lib ole32.lib advapi32.lib
"@
[IO.File]::WriteAllText($commandPath, $command, [Text.Encoding]::Default)
& $env:ComSpec /d /c $commandPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Native helper compilation failed: $LASTEXITCODE" }
Get-Item -LiteralPath $executable

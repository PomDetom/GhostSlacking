#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$tool = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $tool '../..')).Path
$dotnet = (Get-Command dotnet).Source
& $dotnet build (Join-Path $tool 'LiquidGlassSample.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Use the repository's independent desktop so foreground applications cannot intercept
# WindowFromPoint while the sample checks the real HUD and Reveal windows.
. (Join-Path $repo 'tests/Run-ReleaseTests.ps1')
$dll = Join-Path $tool 'bin/Release/net8.0-windows10.0.19041.0/win-x64/LiquidGlassSample.dll'
$log = Join-Path $repo ('artifacts/tools/LiquidGlassSample/verify-' + [guid]::NewGuid().ToString('N') + '.log')
exit (Invoke-TestDesktopProcess -FilePath $dotnet -ArgumentList @($dll, '--verify') -WorkingDirectory $repo -LogPath $log)

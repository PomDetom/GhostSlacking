$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$app = Join-Path $root 'artifacts/tools/InputBridgeProbe/packaged-input/app/GhostSlacking.App.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Run tools/InputBridgeProbe/build.ps1 first.' }
if (Get-Process -Name 'GhostSlacking.App' -ErrorAction SilentlyContinue) {
    throw 'Restore your targets and exit the running GhostSlacking instance, then run this launcher again.'
}
Start-Process -FilePath $app -WorkingDirectory (Split-Path $app) -WindowStyle Hidden

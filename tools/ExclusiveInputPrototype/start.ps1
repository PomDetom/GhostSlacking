$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$app = Join-Path $root 'artifacts/tools/ExclusiveInputPrototype/menu-hover/app/GhostSlacking.App.exe'
if (-not (Test-Path -LiteralPath $app)) { throw 'Run tools/ExclusiveInputPrototype/build.ps1 first.' }
if (Get-Process -Name 'GhostSlacking.App' -ErrorAction SilentlyContinue) {
    throw 'Restore your targets and exit the running GhostSlacking instance, then run this launcher again.'
}
Start-Process -FilePath $app -ArgumentList '--exclusive-input-prototype' -WorkingDirectory (Split-Path $app) -WindowStyle Hidden

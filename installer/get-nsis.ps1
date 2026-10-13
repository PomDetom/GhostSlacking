$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tools = Join-Path $root 'artifacts/tools'
$compiler = Join-Path $tools 'nsis-3.11/makensis.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    $archive = Join-Path $tools 'nsis-3.11.zip'
    $archiveUri = 'https://github.com/tauri-apps/binary-releases/releases/download/nsis-3.11/nsis-3.11.zip'
    $expectedHash = 'C7D27F780DDB6CFFB4730138CD1591E841F4B7EDB155856901CDF5F214394FA1'
    $expectedSize = 2361546
    Invoke-WebRequest -Uri $archiveUri -OutFile $archive -TimeoutSec 120
    $archiveFile = Get-Item -LiteralPath $archive
    $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if ($archiveFile.Length -ne $expectedSize -or $actualHash -ne $expectedHash) {
        throw "Pinned NSIS archive verification failed (expected $expectedSize bytes / $expectedHash; received $($archiveFile.Length) bytes / $actualHash)."
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
}
$version = (& $compiler /VERSION).Trim()
if ($version -ne 'v3.11') { throw "NSIS 3.11 is required; found $version." }
$compiler

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tools = Join-Path $root 'artifacts/tools'
$compiler = Join-Path $tools 'nsis-3.11/makensis.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    $archive = Join-Path $tools 'nsis-3.11.zip'
    $expectedHash = 'C7D27F780DDB6CFFB4730138CD1591E841F4B7EDB155856901CDF5F214394FA1'
    foreach ($mirror in @('phoenixnap', 'deac-riga', 'master')) {
        try {
            Invoke-WebRequest "https://$mirror.dl.sourceforge.net/project/nsis/NSIS%203/3.11/nsis-3.11.zip" -OutFile $archive -TimeoutSec 30
            if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'NSIS archive checksum mismatch.' }
            Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
            break
        } catch { if ($mirror -eq 'master') { throw } }
    }
}
$version = (& $compiler /VERSION).Trim()
if ($version -ne 'v3.11') { throw "NSIS 3.11 is required; found $version." }
$compiler

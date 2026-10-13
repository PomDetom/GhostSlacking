[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$CoreAssemblyPath = (Join-Path $PSScriptRoot '../artifacts/installer-publish/win-x64/GhostSlacking.Core.dll'))
$ErrorActionPreference = 'Stop'
Add-Type -Path (Resolve-Path -LiteralPath $CoreAssemblyPath)
$manifestPath = Join-Path $ReleaseDirectory 'release.json'
$manifest = [GhostSlacking.Core.SignedReleaseManifest]::Verify([IO.File]::ReadAllBytes((Resolve-Path $manifestPath)),
    [IO.File]::ReadAllText((Resolve-Path "$manifestPath.sig")), [GhostSlacking.Core.SignedReleaseManifest]::PublicKey)
$installer = Join-Path $ReleaseDirectory $manifest.Installer.File
$locked = $manifest.OpenVerifiedInstaller($installer)
try {
    $checksum = (Get-Content -LiteralPath "$installer.sha256" -Raw).Trim()
    if ($checksum -ne "$($manifest.Installer.Sha256.ToLowerInvariant())  $($manifest.Installer.File)") { throw 'Checksum asset disagrees with the signed manifest.' }
    if ((Get-Item -LiteralPath $installer).VersionInfo.ProductVersion -ne $manifest.Version) { throw 'EXE product version disagrees with the signed manifest.' }
} finally { $locked.Dispose() }
Write-Output "Signed NSIS release validated: $($manifest.Version)"

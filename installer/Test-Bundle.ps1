[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath,
    [Parameter(Mandatory)][string]$DisplayVersion,
    [Parameter(Mandatory)][string]$FileVersion,
    [Parameter(Mandatory)][string]$MsiPath
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$inspection = Join-Path $repositoryRoot "artifacts\bundle-validation\$DisplayVersion"
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$wix = Join-Path $nugetRoot 'wixtoolset.sdk\5.0.2\tools\net6.0\wix.dll'
dotnet $wix burn extract $BundlePath -oba (Join-Path $inspection 'ba') -o (Join-Path $inspection 'payload')
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Burn bundle.' }
$manifest = [xml](Get-Content -LiteralPath (Join-Path $inspection 'ba\manifest.xml') -Raw)
$burn = $manifest.BurnManifest
$packages = @($burn.Chain.MsiPackage)
if ($packages.Count -ne 1 -or $packages[0].Id -ne 'GhostSlackingMsi' -or $packages[0].Vital -ne 'yes') { throw 'Bundle must contain one vital MSI.' }
if ($burn.Win64 -ne 'yes' -or $burn.Registration.Version -ne $FileVersion) { throw 'Bundle architecture/version mismatch.' }
$version = $burn.Variable | Where-Object Id -eq 'DisplayVersion'
if ($version.Value -ne $DisplayVersion) { throw 'Bundle must display the full SemVer.' }
$msiProperties = @{}
foreach ($property in $packages[0].MsiProperty) { $msiProperties[$property.Id] = $property.Value }
if ($msiProperties.ARPSYSTEMCOMPONENT -ne '1' -or $msiProperties.MSIFASTINSTALL -ne '0') { throw 'MSI must be hidden and retain restore-point policy.' }
if ($msiProperties.ContainsKey('DISABLEROLLBACK')) { throw 'MSI rollback must remain enabled.' }
if ($burn.UX.Payload.FilePath -notcontains 'coreclr.dll' -or $burn.UX.Payload.FilePath -notcontains 'PresentationFramework.dll') { throw 'Setup must be self-contained.' }
$msiPayload = $burn.Payload | Where-Object Id -eq 'GhostSlackingMsi'
$extractedMsi = Join-Path $inspection "payload\$($msiPayload.Container)\$($msiPayload.FilePath)"
if ((Get-FileHash -LiteralPath $extractedMsi -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $MsiPath -Algorithm SHA256).Hash) { throw 'Embedded MSI differs from the validated source.' }
Write-Host 'Burn bundle validation passed.' -ForegroundColor Green

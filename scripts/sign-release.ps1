[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [string]$PrivateKeyPath,
    [string]$PublicKeyPath = (Join-Path $PSScriptRoot '../src/GhostSlacking.Core/release-public-key.pem')
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Signing requires PowerShell 7 (.NET cryptography).' }
$privatePem = if ($PrivateKeyPath) { [IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyPath)) } else { $env:GHOSTSLACKING_RELEASE_SIGNING_KEY }
if ([string]::IsNullOrWhiteSpace($privatePem)) { throw 'A release signing private key is required. Set GHOSTSLACKING_RELEASE_SIGNING_KEY or -PrivateKeyPath.' }
$private = [Security.Cryptography.ECDsa]::Create()
$public = [Security.Cryptography.ECDsa]::Create()
try {
    $private.ImportFromPem($privatePem)
    $public.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $PublicKeyPath)))
    if ($private.KeySize -ne 256 -or $private.ExportSubjectPublicKeyInfoPem() -ne $public.ExportSubjectPublicKeyInfoPem()) { throw 'The signing key does not match the application public key.' }
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ManifestPath))
    $format = [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation
    $signature = $private.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, $format)
    if (-not $public.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, $format)) { throw 'Release signature verification failed.' }
    [IO.File]::WriteAllText("$ManifestPath.sig", [Convert]::ToBase64String($signature), [Text.UTF8Encoding]::new($false))
    Write-Output 'Release manifest signed and independently verified.'
} finally { $private.Dispose(); $public.Dispose(); $privatePem = $null }
